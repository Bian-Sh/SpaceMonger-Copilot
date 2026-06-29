using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceMonger.Core.Diagnostics;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Whitelist;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine : IRecommendationEngine
{
    // Send at most 100 top-level directories and 100 largest files to keep
    // the prompt well under the API's input token limit (~200K tokens).
    private const int MaxTopDirs = 100;
    private const int MaxTopFiles = 100;
    private const int MaxKnownPatternItems = 50;
    private const int MaxDuplicateGroups = 20;
    private const int MaxContentFingerprint = 30;
    private const int MaxSampleFiles = 15;
    private const int MaxDuplicateCandidates = 5_000;
    private const int MaxFingerprintFiles = 2_000;
    private const long DuplicateMinSize = 1_048_576; // 1 MB

    // Well-known OS and application temp/cache paths that are safe to match
    // by convention. These use full path segments to avoid false positives
    // on user-created folders that happen to contain "temp" or "cache".
    private static readonly string[] StandardTempPaths =
    [
        @"AppData\Local\Temp",
        @"Windows\Temp",
        @"Windows\Prefetch",
    ];

    // Directory names that are safe cleanup targets when they appear as an
    // exact folder name anywhere in the tree (build artifacts, package caches).
    private static readonly string[] SafeDirectoryNames =
    [
        ".npm", ".nuget", "node_modules", "obj", "bin",
        "__pycache__", ".gradle", "CacheStorage",
        "Code Cache", "GPUCache", "INetCache", ".cache",
        "_cacache",
    ];

    // Names that are *suggestive* of cleanup but need content inspection
    // before recommending deletion. These get a content fingerprint attached.
    private static readonly string[] AmbiguousPatterns =
    [
        "Temp", "Cache", "Logs", "Log", "tmp",
    ];

    // File extensions that indicate user-created, potentially important content.
    private static readonly HashSet<string> UserContentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".pdf", ".odt", ".ods", ".odp",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".svg", ".psd", ".ai",
        ".mp3", ".mp4", ".avi", ".mkv", ".mov", ".wav", ".flac",
        ".zip", ".rar", ".7z", ".tar", ".gz",
        ".cs", ".py", ".js", ".ts", ".java", ".cpp", ".h", ".go", ".rs",
        ".sln", ".csproj", ".json", ".xml", ".yaml", ".yml", ".toml",
        ".sql", ".db", ".sqlite", ".bak",
        ".iso", ".vhd", ".vhdx", ".vmdk",
        ".exe", ".msi",
    };

    private static readonly string[] ProtectedProgramFolders =
    [
        "Program Files",
        "Program Files (x86)"
    ];

    private static readonly string[] ProtectedUserFolders =
    [
        "Desktop",
        "Documents",
        "Pictures",
        "Music",
        "Videos"
    ];

    private readonly ILlmClient _llmClient;
    private readonly ISettingsService? _settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private readonly ILogger<RecommendationEngine> _logger;
    private readonly AsyncLocal<List<PathWhitelistEntry>?> _cleanupRecommendationWhitelist = new();

    public RecommendationEngine(ILlmClient llmClient, IDuplicateDetector duplicateDetector, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null, ILogger<RecommendationEngine>? logger = null)
    {
        _llmClient = llmClient;
        _settingsService = settingsService;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
        _logger = logger ?? NullLogger<RecommendationEngine>.Instance;
        _ = duplicateDetector;
    }

    public async Task<List<CleanupRecommendation>> AnalyzeAsync(
        ScanSession session,
        string apiKey,
        string? baseUrl,
        string? modelName,
        bool enableThinking,
        string? responseLanguage,
        CancellationToken cancellationToken,
        FileEntry? focusEntry = null)
    {
        var result = await AnalyzeWithDiagnosticsAsync(
            session, apiKey, baseUrl, modelName, enableThinking, responseLanguage, cancellationToken, focusEntry);
        return result.Recommendations;
    }

    public async Task<AnalysisResult> AnalyzeWithDiagnosticsAsync(
        ScanSession session,
        string apiKey,
        string? baseUrl,
        string? modelName,
        bool enableThinking,
        string? responseLanguage,
        CancellationToken cancellationToken,
        FileEntry? focusEntry = null)
    {
        _logger.LogInformation("Analysis started: target={TargetPath}, focus={FocusPath}, model={ModelName}, thinking={EnableThinking}", session.TargetPath, focusEntry?.Path, modelName, enableThinking);
        var result = new AnalysisResult();
        _cleanupRecommendationWhitelist.Value = _settingsService?.LoadSettings().CleanupRecommendationWhitelist;

        try
        {
            if (session.RootEntry is null)
            {
                _logger.LogWarning("Analysis skipped because scan session root is null: target={TargetPath}", session.TargetPath);
                result.Diagnostics.ParseError = "ScanSession.RootEntry is null.";
                return result;
            }

            // When a focusEntry is provided (user drilled into a folder), analyze
            // only that subtree.  Otherwise analyze the full scan root.
            var analysisRoot = focusEntry ?? session.RootEntry;
            if (IsCleanupExcluded(analysisRoot.Path))
            {
                _logger.LogWarning("Analysis scope excluded by cleanup recommendation whitelist: {Path}", analysisRoot.Path);
                result.Diagnostics.ParseError = "Analysis scope is excluded by cleanup recommendation whitelist.";
                return result;
            }

            var metadataJson = BuildCompactMetadata(session, analysisRoot);
            DebugBreakpoints.Hit("analysis-metadata-built");
            var systemPrompt = BuildSystemPrompt(responseLanguage);

            result.Diagnostics = new AnalysisDiagnostics
            {
                TargetPath = session.TargetPath,
                ScopePath = analysisRoot.Path,
                IsFocusedScope = focusEntry is not null,
                MetadataLength = metadataJson.Length,
            };

            _logger.LogInformation("Analysis metadata built: scope={ScopePath}, chars={MetadataLength}", analysisRoot.Path, metadataJson.Length);
            var response = await _llmClient.SendAnalysisAsync(systemPrompt, metadataJson, apiKey, baseUrl, modelName, enableThinking, cancellationToken);
            DebugBreakpoints.Hit("analysis-response-received");
            _logger.LogInformation("Analysis response received: chars={ResponseLength}", response.Length);
            result.Diagnostics.ResponseLength = response.Length;
            result.Diagnostics.ResponsePreview = BuildPreview(response);
            result.Diagnostics.RawResponsePath = WriteRawResponse(response);
            result.Diagnostics.ResponseEnvelopePath = AnthropicClient.LastResponseEnvelopePath;
            result.Diagnostics.StopReason = AnthropicClient.LastResponseStopReason;
            result.Diagnostics.ThinkingPath = AnthropicClient.LastResponseThinkingPath;

            var recommendations = ParseResponse(response, session.RootEntry, result.Diagnostics);
            AddUnityLibraryRecommendations(recommendations, analysisRoot);
            DebugBreakpoints.Hit("analysis-response-parsed");
            recommendations = recommendations
                .Where(r => !IsCleanupExcluded(r.TargetPath))
                .ToList();
            result.Diagnostics.ParsedRecommendationCount = recommendations.Count;
            _logger.LogInformation("Analysis response parsed: recommendations={Count}", recommendations.Count);

            if (focusEntry is null)
            {
                // Full-drive analysis: hide only truly critical system/user-data
                // structures.  Known cache/temp locations under Windows should still
                // be visible, but they are risk-adjusted below instead of shown as
                // blindly safe cleanup targets.
                var beforeFilterCount = recommendations.Count;
                recommendations = recommendations
                    .Where(r => !IsHardProtectedPath(r.TargetPath))
                    .ToList();
                result.Diagnostics.ProtectedFilteredCount = beforeFilterCount - recommendations.Count;
                _logger.LogInformation("Protected recommendation filter removed {Count} entries", result.Diagnostics.ProtectedFilteredCount);
            }

            foreach (var rec in recommendations)
            {
                ApplySystemPathRiskAdjustment(rec);
            }

            for (int i = 0; i < recommendations.Count; i++)
            {
                recommendations[i].Id = (i + 1).ToString();
            }

            result.Recommendations = recommendations;
            _logger.LogInformation("Analysis completed: finalRecommendations={Count}", recommendations.Count);
            return result;
        }
        finally
        {
            _cleanupRecommendationWhitelist.Value = null;
        }
    }

    private bool IsCleanupExcluded(string path)
    {
        return _whitelistMatcher.IsExcluded(path, _cleanupRecommendationWhitelist.Value);
    }

}

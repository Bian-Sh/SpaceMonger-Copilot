using System.Text.Json;
using System.Text.RegularExpressions;
using SpaceMonger.Core.Diagnostics;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

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

    private static readonly string[] ProtectedPathSegments =
    [
        "Windows",
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

    public RecommendationEngine(ILlmClient llmClient, IDuplicateDetector duplicateDetector)
    {
        _llmClient = llmClient;
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
        var result = new AnalysisResult();

        if (session.RootEntry is null)
        {
            result.Diagnostics.ParseError = "ScanSession.RootEntry is null.";
            return result;
        }

        // When a focusEntry is provided (user drilled into a folder), analyze
        // only that subtree.  Otherwise analyze the full scan root.
        var analysisRoot = focusEntry ?? session.RootEntry;

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

        var response = await _llmClient.SendAnalysisAsync(systemPrompt, metadataJson, apiKey, baseUrl, modelName, enableThinking, cancellationToken);
        DebugBreakpoints.Hit("analysis-response-received");
        result.Diagnostics.ResponseLength = response.Length;
        result.Diagnostics.ResponsePreview = BuildPreview(response);
        result.Diagnostics.RawResponsePath = WriteRawResponse(response);
        result.Diagnostics.ResponseEnvelopePath = AnthropicClient.LastResponseEnvelopePath;
        result.Diagnostics.StopReason = AnthropicClient.LastResponseStopReason;
        result.Diagnostics.ThinkingPath = AnthropicClient.LastResponseThinkingPath;

        var recommendations = ParseResponse(response, session.RootEntry, result.Diagnostics);
        DebugBreakpoints.Hit("analysis-response-parsed");
        result.Diagnostics.ParsedRecommendationCount = recommendations.Count;

        if (focusEntry is not null)
        {
            // User explicitly drilled into this folder and requested analysis -
            // don't silently discard results. Instead, bump any "Safe" rating
            // to "ReviewFirst" for paths under protected system directories so
            // the user still gets a warning before deleting system-adjacent files.
            foreach (var rec in recommendations)
            {
                if (IsProtectedPath(rec.TargetPath) && rec.SafetyRating == SafetyRating.Safe)
                {
                    rec.SafetyRating = SafetyRating.ReviewFirst;
                    rec.Explanation += " (This item is under a system directory - review carefully before deleting.)";
                }
            }
        }
        else
        {
            // Full-drive analysis: filter out protected paths entirely to avoid
            // recommending OS/system files in broad recommendations.
            var beforeFilterCount = recommendations.Count;
            recommendations = recommendations
                .Where(r => !IsProtectedPath(r.TargetPath))
                .ToList();
            result.Diagnostics.ProtectedFilteredCount = beforeFilterCount - recommendations.Count;
        }

        for (int i = 0; i < recommendations.Count; i++)
        {
            recommendations[i].Id = (i + 1).ToString();
        }

        result.Recommendations = recommendations;
        return result;
    }

}

using System.Text.Json;
using System.Text.RegularExpressions;
using SpaceMonger.Core.Diagnostics;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Analysis;

public class RecommendationEngine : IRecommendationEngine
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
            recommendations[i].Id = $"REC-{(i + 1):D3}";
        }

        result.Recommendations = recommendations;
        return result;
    }

    private static string BuildCompactMetadata(ScanSession session, FileEntry analysisRoot)
    {
        // Collect all entries in a single pass from the analysis root
        var allFiles = new List<FileEntry>();
        var allDirs = new List<FileEntry>();
        CollectEntries(analysisRoot, allFiles, allDirs);

        // Top files by size
        var topFiles = allFiles
            .OrderByDescending(f => f.Size)
            .Take(MaxTopFiles)
            .Select(f => $"{f.Path}|{f.Size}")
            .ToList();

        // Known cleanup pattern directories — now with content fingerprints
        // so the AI can distinguish real temp folders from user working folders.
        var topDirSet = allDirs
            .Where(d => d.Depth >= 1)
            .OrderByDescending(d => d.Size)
            .Take(MaxTopDirs)
            .ToHashSet();

        var patternDirs = allDirs
            .Where(d => !topDirSet.Contains(d) && MatchesKnownPattern(d) && d.Size > 0)
            .OrderByDescending(d => d.Size)
            .Take(MaxKnownPatternItems)
            .ToList();

        var patternItems = patternDirs
            .Select(d => $"{d.Path}|{d.Size}|{BuildContentFingerprint(d)}")
            .ToList();

        // Also attach content fingerprints to top directories that match
        // ambiguous patterns so the AI gets content context for those too.
        var topDirsWithFingerprints = allDirs
            .Where(d => d.Depth >= 1)
            .OrderByDescending(d => d.Size)
            .Take(MaxTopDirs)
            .Select(d =>
            {
                bool needsFingerprint = false;
                foreach (var pattern in AmbiguousPatterns)
                {
                    if (d.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        needsFingerprint = true;
                        break;
                    }
                }
                var baseLine = $"{d.Path}|{d.Size}|{d.Children.Count(c => c.IsDirectory)} dirs, {d.Children.Count(c => !c.IsDirectory)} files";
                return needsFingerprint
                    ? $"{baseLine}|CONTENT_FINGERPRINT: {BuildContentFingerprint(d)}"
                    : baseLine;
            })
            .ToList();

        // Lightweight duplicate detection by metadata only
        var duplicates = allFiles
            .Where(f => f.Size > DuplicateMinSize)
            .GroupBy(f => (f.Name, f.Size))
            .Where(g => g.Count() >= 2)
            .OrderByDescending(g => g.Key.Size * g.Count())
            .Take(MaxDuplicateGroups)
            .Select(g => $"{g.Key.Name}|{g.Key.Size}|{g.Count()} copies: {string.Join("; ", g.Select(f => f.Path))}")
            .ToList();

        // Build a compact pipe-delimited format instead of verbose JSON.
        // This is ~5-10x smaller than the equivalent JSON with full property names.
        var sb = new System.Text.StringBuilder();
        var isFocused = analysisRoot != session.RootEntry;
        sb.AppendLine($"SCAN: {session.TargetPath}");
        if (isFocused)
            sb.AppendLine($"FOCUS: {analysisRoot.Path}");
        sb.AppendLine($"TOTAL_FILES: {allFiles.Count}");
        sb.AppendLine($"TOTAL_FOLDERS: {allDirs.Count}");
        sb.AppendLine($"TOTAL_SIZE: {analysisRoot.Size}");
        sb.AppendLine();

        sb.AppendLine("## LARGEST DIRECTORIES (path|size_bytes|contents [|CONTENT_FINGERPRINT if ambiguous name])");
        foreach (var line in topDirsWithFingerprints)
            sb.AppendLine(line);
        sb.AppendLine();

        sb.AppendLine("## LARGEST FILES (path|size_bytes)");
        foreach (var line in topFiles)
            sb.AppendLine(line);
        sb.AppendLine();

        if (patternItems.Count > 0)
        {
            sb.AppendLine("## CLEANUP CANDIDATES (path|size_bytes|content_fingerprint)");
            sb.AppendLine("NOTE: Each entry includes a content fingerprint. Inspect it before recommending deletion.");
            foreach (var line in patternItems)
                sb.AppendLine(line);
            sb.AppendLine();
        }

        if (duplicates.Count > 0)
        {
            sb.AppendLine("## LIKELY DUPLICATES (name|size_bytes|locations)");
            foreach (var line in duplicates)
                sb.AppendLine(line);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void CollectEntries(FileEntry entry, List<FileEntry> files, List<FileEntry> dirs)
    {
        if (entry.IsDirectory)
        {
            dirs.Add(entry);
            foreach (var child in entry.Children)
                CollectEntries(child, files, dirs);
        }
        else
        {
            files.Add(entry);
        }
    }

    private static bool MatchesKnownPattern(FileEntry dir)
    {
        // Exact directory name match against safe cleanup targets.
        foreach (var name in SafeDirectoryNames)
        {
            if (string.Equals(dir.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Standard OS/app temp paths matched by full path convention.
        var normalizedPath = dir.Path.Replace('/', '\\');
        foreach (var stdPath in StandardTempPaths)
        {
            if (normalizedPath.Contains(stdPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Ambiguous names (e.g. "Temp", "Cache") — include them so they get
        // a content fingerprint, but the AI will decide based on contents.
        foreach (var pattern in AmbiguousPatterns)
        {
            if (dir.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds a content fingerprint for a directory: file type distribution,
    /// date range, and sample file names. This gives the AI enough context to
    /// distinguish a real temp folder from a user working folder that happens
    /// to have "temp" in its name.
    /// </summary>
    private static string BuildContentFingerprint(FileEntry dir)
    {
        var allFiles = new List<FileEntry>();
        CollectFilesOnly(dir, allFiles);

        if (allFiles.Count == 0)
            return "empty";

        // Extension distribution
        var extGroups = allFiles
            .GroupBy(f => f.Extension ?? "(none)", StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Sum(f => f.Size))
            .Take(MaxContentFingerprint)
            .Select(g => $"{g.Key}:{g.Count()}/{FormatSize(g.Sum(f => f.Size))}")
            .ToList();

        // Date range
        var oldest = allFiles.Min(f => f.LastModified);
        var newest = allFiles.Max(f => f.LastModified);
        var dateSpan = newest - oldest;

        // User-content file ratio — what fraction of files look like user documents,
        // source code, media, etc. vs. throwaway temp files
        int userContentCount = allFiles.Count(f =>
            f.Extension is not null && UserContentExtensions.Contains(f.Extension));
        int userContentPct = (int)(100.0 * userContentCount / allFiles.Count);

        // Sample of largest files by name (most informative for the AI)
        var sampleFiles = allFiles
            .OrderByDescending(f => f.Size)
            .Take(MaxSampleFiles)
            .Select(f => $"{f.Name}({FormatSize(f.Size)},{f.LastModified:yyyy-MM-dd})")
            .ToList();

        // Is this in a standard temp location?
        var normalizedPath = dir.Path.Replace('/', '\\');
        bool isStandardTempLocation = false;
        foreach (var stdPath in StandardTempPaths)
        {
            if (normalizedPath.Contains(stdPath, StringComparison.OrdinalIgnoreCase))
            {
                isStandardTempLocation = true;
                break;
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.Append($"files:{allFiles.Count}");
        sb.Append($"|user_content:{userContentPct}%");
        sb.Append($"|dates:{oldest:yyyy-MM-dd}..{newest:yyyy-MM-dd}(span:{(int)dateSpan.TotalDays}d)");
        sb.Append($"|std_temp_location:{(isStandardTempLocation ? "yes" : "no")}");
        sb.Append($"|types:{string.Join(",", extGroups)}");
        sb.Append($"|largest:{string.Join(",", sampleFiles)}");
        return sb.ToString();
    }

    private static void CollectFilesOnly(FileEntry entry, List<FileEntry> files)
    {
        foreach (var child in entry.Children)
        {
            if (child.IsDirectory)
                CollectFilesOnly(child, files);
            else
                files.Add(child);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes}B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1}KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1}TB";
    }

    private static string BuildSystemPrompt(string? responseLanguage)
    {
        var language = string.IsNullOrWhiteSpace(responseLanguage) ? "the same language as the app UI" : responseLanguage.Trim();
        return """
            You are a disk space analysis assistant. Analyze the provided file metadata and return cleanup recommendations as a JSON object.

            ## Response Format

            Return ONLY a raw JSON object with this exact structure. Do not wrap it in markdown fences and do not add explanatory text before or after the JSON:

            {
              "recommendations": [
                {
                  "path": "C:\\path\\to\\item",
                  "size_bytes": 1234567,
                  "category": "TemporaryFiles",
                  "safety_rating": "Safe",
                  "explanation": "Human-readable explanation of why this can be cleaned up."
                }
              ]
            }

            ## Categories

            Use exactly one of these categories for each recommendation:
            - TemporaryFiles — temp directories, tmp files, transient data
            - BuildCache — obj, bin, __pycache__, .gradle build outputs
            - PackageManagerCache — node_modules, .npm/_cacache, .nuget caches
            - OldDownloads — stale files in Downloads folders
            - LogFiles — log files and log directories
            - DuplicateFiles — files identified as likely duplicates (same name and size; not content-verified)
            - BrowserCache — browser cache directories
            - SystemCache — OS-level caches
            - Other — anything that doesn't fit the above

            ## Safety Ratings

            - Safe — can be deleted without risk; applications will regenerate as needed
            - ReviewFirst — likely safe but user should verify before deleting
            - Caution — may have side effects; user should understand implications

            ## Content Fingerprint Analysis (CRITICAL)

            Some directories include a CONTENT_FINGERPRINT field. You MUST analyze it before making a recommendation. The fingerprint contains:
            - **user_content**: percentage of files with user-document/media/code extensions. High % (>20%) = likely a working folder, NOT safe to delete.
            - **dates**: date range and span. Files spanning months or years = accumulated working data, not temp files.
            - **std_temp_location**: "yes" means the folder is in a well-known OS/app temp path (e.g., AppData\Local\Temp, Windows\Temp). "no" means it is NOT in a standard temp location — be very skeptical about recommending deletion.
            - **types**: file extension distribution showing what kinds of files are inside.
            - **largest**: sample of the biggest files with names and dates.

            **Decision rules for ambiguous directories:**
            1. If std_temp_location is "no" AND user_content > 20%, this is almost certainly a user working folder. Do NOT recommend it, or rate it Caution at minimum with a warning that it appears to contain user files.
            2. If std_temp_location is "no" AND the date span is > 90 days AND files include documents, media, code, or archives, treat it as a user working folder.
            3. If std_temp_location is "yes" AND user_content < 10% AND files are mostly .tmp/.log/random hashes, it is safe to recommend.
            4. A folder named "Temp" or "Cache" at a drive root or user-created location is NOT the same as AppData\Local\Temp. Do NOT assume it is temporary just because of its name.
            5. Look at the actual file names in the "largest" sample — human-readable names (documents, photos, project files, backups) are strong signals of important content.

            ## Critical Rules

            - NEVER recommend deleting: OS files (Windows directory), boot files, active application binaries, user documents in Desktop/Documents/Pictures/Music/Videos folders
            - NEVER recommend deleting files currently in use or system-critical files
            - NEVER assume a folder is safe to delete based solely on its name. Always reason about the actual contents (file types, dates, names, location).
            - Only recommend items present in the provided metadata
            - Include a clear, human-readable explanation for every recommendation
            - Return at most 20 recommendations
            - Keep each explanation under 160 characters
            - Focus on items that will recover meaningful disk space
            - For duplicate files (matched by name and size, not content-verified), recommend keeping one copy and note the user should verify before deleting
            - When uncertain, prefer ReviewFirst or Caution over Safe — a false "Safe" rating can cause irreversible data loss
            """ + Environment.NewLine + $"- Localization: write all user-facing explanation text in {language}.";
    }

    private static List<CleanupRecommendation> ParseResponse(string response, FileEntry rootEntry, AnalysisDiagnostics diagnostics)
    {
        var recommendations = new List<CleanupRecommendation>();

        try
        {
            var json = ExtractJsonFromResponse(response);
            if (string.IsNullOrWhiteSpace(json))
            {
                diagnostics.ParseError = "No JSON object or JSON code block was found in the LLM response.";
                return recommendations;
            }

            diagnostics.ExtractedJsonLength = json.Length;
            diagnostics.ExtractedJsonPreview = BuildPreview(json);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("recommendations", out var recsArray) ||
                recsArray.ValueKind != JsonValueKind.Array)
            {
                diagnostics.ParseError = "Extracted JSON does not contain a recommendations array.";
                return recommendations;
            }

            foreach (var rec in recsArray.EnumerateArray())
            {
                try
                {
                    var path = rec.GetProperty("path").GetString();
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    var sizeBytes = rec.GetProperty("size_bytes").GetInt64();
                    var categoryStr = rec.GetProperty("category").GetString() ?? "Other";
                    var safetyStr = rec.GetProperty("safety_rating").GetString() ?? "ReviewFirst";
                    var explanation = rec.GetProperty("explanation").GetString() ?? string.Empty;

                    if (!Enum.TryParse<RecommendationCategory>(categoryStr, ignoreCase: true, out var category))
                    {
                        category = RecommendationCategory.Other;
                    }

                    if (!Enum.TryParse<SafetyRating>(safetyStr, ignoreCase: true, out var safety))
                    {
                        safety = SafetyRating.ReviewFirst;
                    }

                    var entry = FindEntryByPath(rootEntry, path);
                    if (entry is null)
                    {
                        diagnostics.MissingEntryCount++;
                    }

                    recommendations.Add(new CleanupRecommendation
                    {
                        TargetPath = path,
                        Entry = entry,
                        Size = sizeBytes,
                        Category = category,
                        SafetyRating = safety,
                        Explanation = explanation
                    });
                }
                catch (JsonException)
                {
                    diagnostics.MalformedRecommendationCount++;
                }
                catch (KeyNotFoundException)
                {
                    diagnostics.MissingFieldRecommendationCount++;
                }
            }
        }
        catch (JsonException ex)
        {
            diagnostics.ParseError = ex.Message;
        }

        return recommendations;
    }

    private static string BuildPreview(string value)
    {
        const int maxPreviewLength = 1200;
        var normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= maxPreviewLength
            ? normalized
            : normalized[..maxPreviewLength] + "...";
    }

    private static string? WriteRawResponse(string response)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpaceMonger.Next",
                "logs",
                "analysis-responses");
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"analysis-response-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt");
            File.WriteAllText(path, response);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJsonFromResponse(string response)
    {
        // Try to extract JSON from markdown code blocks
        var match = Regex.Match(response, @"```json\s*([\s\S]*?)\s*```");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        // Try plain code blocks
        match = Regex.Match(response, @"```\s*([\s\S]*?)\s*```");
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        var trimmed = response.Trim();

        // Some providers occasionally omit the closing markdown fence while
        // still returning valid JSON after ```json. Treat that as recoverable.
        foreach (var fencePrefix in new[] { "```json", "```" })
        {
            if (trimmed.StartsWith(fencePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var afterFence = trimmed[fencePrefix.Length..].Trim();
                var fencedJson = ExtractBalancedJsonObject(afterFence);
                if (fencedJson is not null)
                {
                    return fencedJson;
                }
            }
        }

        // Try the raw response, or a JSON object embedded after explanatory text.
        return ExtractBalancedJsonObject(trimmed);
    }

    private static string? ExtractBalancedJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var current = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (current == '\\')
                {
                    escaped = true;
                }
                else if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                depth++;
            }
            else if (current == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)].Trim();
                }
            }
        }

        return null;
    }

    private static FileEntry? FindEntryByPath(FileEntry root, string targetPath)
    {
        var normalizedTarget = targetPath.Replace('\\', '/').TrimEnd('/');
        var normalizedRoot = root.Path.Replace('\\', '/').TrimEnd('/');

        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return root;
        }

        foreach (var child in root.Children)
        {
            var found = FindEntryByPath(child, targetPath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsProtectedPath(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Check for protected system directories (Windows, Program Files)
        foreach (var protectedSegment in ProtectedPathSegments)
        {
            if (segments.Any(s => string.Equals(s, protectedSegment, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        // Check for standard user document folders
        // Pattern: .../Users/<username>/<ProtectedFolder>/...
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], "Users", StringComparison.OrdinalIgnoreCase) &&
                i + 2 < segments.Length)
            {
                var folderAfterUsername = segments[i + 2];
                if (ProtectedUserFolders.Any(pf =>
                    string.Equals(pf, folderAfterUsername, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}


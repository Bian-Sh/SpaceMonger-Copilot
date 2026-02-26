using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const long DuplicateMinSize = 1_048_576; // 1 MB

    private static readonly string[] KnownCleanupPatterns =
    [
        "Temp", ".npm", ".nuget", "node_modules", "obj", "bin",
        "__pycache__", ".gradle", "Cache", "CacheStorage",
        "Code Cache", "GPUCache", "INetCache", ".cache", "logs", "Log"
    ];

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
        CancellationToken cancellationToken)
    {
        if (session.RootEntry is null)
        {
            return [];
        }

        var metadataJson = BuildCompactMetadata(session);
        var systemPrompt = BuildSystemPrompt();

        var response = await _llmClient.SendAnalysisAsync(systemPrompt, metadataJson, apiKey, cancellationToken);

        var recommendations = ParseResponse(response, session.RootEntry);

        recommendations = recommendations
            .Where(r => !IsProtectedPath(r.TargetPath))
            .ToList();

        for (int i = 0; i < recommendations.Count; i++)
        {
            recommendations[i].Id = $"REC-{(i + 1):D3}";
        }

        return recommendations;
    }

    private static string BuildCompactMetadata(ScanSession session)
    {
        var root = session.RootEntry!;

        // Collect all entries in a single pass
        var allFiles = new List<FileEntry>();
        var allDirs = new List<FileEntry>();
        CollectEntries(root, allFiles, allDirs);

        // Top directories by size (these are the actionable items)
        var topDirs = allDirs
            .Where(d => d.Depth >= 1) // skip root
            .OrderByDescending(d => d.Size)
            .Take(MaxTopDirs)
            .Select(d => $"{d.Path}|{d.Size}|{d.Children.Count(c => c.IsDirectory)} dirs, {d.Children.Count(c => !c.IsDirectory)} files")
            .ToList();

        // Top files by size
        var topFiles = allFiles
            .OrderByDescending(f => f.Size)
            .Take(MaxTopFiles)
            .Select(f => $"{f.Path}|{f.Size}")
            .ToList();

        // Known cleanup pattern directories (summarized — path and total size only)
        var topDirSet = allDirs
            .Where(d => d.Depth >= 1)
            .OrderByDescending(d => d.Size)
            .Take(MaxTopDirs)
            .ToHashSet();

        var patternItems = allDirs
            .Where(d => !topDirSet.Contains(d) && MatchesKnownPattern(d.Path) && d.Size > 0)
            .OrderByDescending(d => d.Size)
            .Take(MaxKnownPatternItems)
            .Select(d => $"{d.Path}|{d.Size}")
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
        sb.AppendLine($"SCAN: {session.TargetPath}");
        sb.AppendLine($"TOTAL_FILES: {session.TotalFiles}");
        sb.AppendLine($"TOTAL_FOLDERS: {session.TotalFolders}");
        sb.AppendLine($"TOTAL_SIZE: {session.TotalSize}");
        sb.AppendLine();

        sb.AppendLine("## LARGEST DIRECTORIES (path|size_bytes|contents)");
        foreach (var line in topDirs)
            sb.AppendLine(line);
        sb.AppendLine();

        sb.AppendLine("## LARGEST FILES (path|size_bytes)");
        foreach (var line in topFiles)
            sb.AppendLine(line);
        sb.AppendLine();

        if (patternItems.Count > 0)
        {
            sb.AppendLine("## CLEANUP CANDIDATES (path|size_bytes)");
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

    private static bool MatchesKnownPattern(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        foreach (var pattern in KnownCleanupPatterns)
        {
            if (normalizedPath.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are a disk space analysis assistant. Analyze the provided file metadata and return cleanup recommendations as a JSON object.

            ## Response Format

            Return a JSON object wrapped in a ```json code block with this exact structure:

            ```json
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
            ```

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

            ## Critical Rules

            - NEVER recommend deleting: OS files (Windows directory), boot files, active application binaries, user documents in Desktop/Documents/Pictures/Music/Videos folders
            - NEVER recommend deleting files currently in use or system-critical files
            - Only recommend items present in the provided metadata
            - Include a clear, human-readable explanation for every recommendation
            - Focus on items that will recover meaningful disk space
            - For duplicate files (matched by name and size, not content-verified), recommend keeping one copy and note the user should verify before deleting
            """;
    }

    private static List<CleanupRecommendation> ParseResponse(string response, FileEntry rootEntry)
    {
        var recommendations = new List<CleanupRecommendation>();

        try
        {
            var json = ExtractJsonFromResponse(response);
            if (string.IsNullOrWhiteSpace(json))
            {
                return recommendations;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("recommendations", out var recsArray) ||
                recsArray.ValueKind != JsonValueKind.Array)
            {
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
                    // Skip malformed individual recommendations
                }
                catch (KeyNotFoundException)
                {
                    // Skip recommendations with missing required fields
                }
            }
        }
        catch (JsonException)
        {
            // Return empty list if entire response is unparseable
        }

        return recommendations;
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

        // Try the raw response as JSON
        var trimmed = response.Trim();
        if (trimmed.StartsWith('{'))
        {
            return trimmed;
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

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Analysis;

public class RecommendationEngine : IRecommendationEngine
{
    private const int MaxTopItems = 2000;

    private static readonly string[] KnownCleanupPatterns =
    [
        "Temp",
        ".npm/_cacache",
        ".nuget",
        "node_modules",
        "obj",
        "bin",
        "__pycache__",
        ".gradle",
        "Cache",
        "CacheStorage",
        "Code Cache",
        "GPUCache",
        "AppData/Local/Temp",
        "AppData\\Local\\Temp",
        "Local\\Microsoft\\Windows\\INetCache",
        "Local\\Google\\Chrome\\User Data\\Default\\Cache",
        "Local\\Mozilla\\Firefox\\Profiles",
        "Local\\BraveSoftware",
        ".cache",
        "logs",
        "Log",
        ".log"
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
    private readonly IDuplicateDetector _duplicateDetector;

    public RecommendationEngine(ILlmClient llmClient, IDuplicateDetector duplicateDetector)
    {
        _llmClient = llmClient;
        _duplicateDetector = duplicateDetector;
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

        // Step 1: Run duplicate detection
        var duplicates = await _duplicateDetector.FindDuplicatesAsync(session.RootEntry, cancellationToken);

        // Step 2: Collect metadata for LLM
        var allEntries = new List<FileEntry>();
        FlattenTree(session.RootEntry, allEntries);

        var topItems = allEntries
            .OrderByDescending(e => e.Size)
            .Take(MaxTopItems)
            .ToList();

        var knownPatternItems = allEntries
            .Where(e => MatchesKnownPattern(e.Path))
            .Where(e => !topItems.Contains(e))
            .ToList();

        // Step 3: Build the file metadata JSON
        var metadataJson = BuildMetadataJson(session, topItems, knownPatternItems, duplicates);

        // Step 4: Build the analysis system prompt
        var systemPrompt = BuildSystemPrompt();

        // Step 5: Call the LLM
        var response = await _llmClient.SendAnalysisAsync(systemPrompt, metadataJson, apiKey, cancellationToken);

        // Step 6: Parse the response
        var recommendations = ParseResponse(response, session.RootEntry);

        // Step 7: Post-filter protected paths
        recommendations = recommendations
            .Where(r => !IsProtectedPath(r.TargetPath))
            .ToList();

        // Step 8: Assign sequential IDs
        for (int i = 0; i < recommendations.Count; i++)
        {
            recommendations[i].Id = $"REC-{(i + 1):D3}";
        }

        return recommendations;
    }

    private static void FlattenTree(FileEntry entry, List<FileEntry> result)
    {
        result.Add(entry);
        foreach (var child in entry.Children)
        {
            FlattenTree(child, result);
        }
    }

    private static bool MatchesKnownPattern(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        foreach (var pattern in KnownCleanupPatterns)
        {
            var normalizedPattern = pattern.Replace('\\', '/');
            if (normalizedPath.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string BuildMetadataJson(
        ScanSession session,
        List<FileEntry> topItems,
        List<FileEntry> knownPatternItems,
        List<DuplicateGroup> duplicates)
    {
        var metadata = new
        {
            scan_root = session.TargetPath,
            scan_date = session.StartTime.ToString("O"),
            total_files = session.TotalFiles,
            total_size_bytes = session.TotalSize,
            top_items = topItems.Select(e => new
            {
                path = e.Path,
                size_bytes = e.Size,
                type = e.IsDirectory ? "directory" : "file",
                last_modified = e.LastModified.ToString("O"),
                child_count = e.IsDirectory ? e.Children.Count : (int?)null
            }),
            known_patterns = knownPatternItems.Select(e => new
            {
                path = e.Path,
                size_bytes = e.Size,
                type = e.IsDirectory ? "directory" : "file",
                pattern = InferPattern(e.Path)
            }),
            duplicates = duplicates.Select(d => new
            {
                hash = d.Hash,
                size_bytes = d.Size,
                files = d.Files.Select(f => f.Path)
            })
        };

        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }

    private static string InferPattern(string path)
    {
        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();

        if (normalizedPath.Contains("temp"))
            return "temporary_files";
        if (normalizedPath.Contains("node_modules"))
            return "package_manager_cache";
        if (normalizedPath.Contains(".npm"))
            return "package_manager_cache";
        if (normalizedPath.Contains(".nuget"))
            return "package_manager_cache";
        if (normalizedPath.Contains(".gradle"))
            return "build_cache";
        if (normalizedPath.Contains("/obj") || normalizedPath.Contains("/bin"))
            return "build_cache";
        if (normalizedPath.Contains("__pycache__"))
            return "build_cache";
        if (normalizedPath.Contains("cache"))
            return "browser_cache";
        if (normalizedPath.Contains("log"))
            return "log_files";

        return "other";
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
            - DuplicateFiles — files confirmed as duplicates by hash
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
            - For duplicate files, recommend keeping one copy and removing extras
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

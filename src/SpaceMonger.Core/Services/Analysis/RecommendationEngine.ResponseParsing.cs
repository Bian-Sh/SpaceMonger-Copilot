using System.Text.Json;
using System.Text.RegularExpressions;
using SpaceMonger.Core.Diagnostics;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine
{
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

}

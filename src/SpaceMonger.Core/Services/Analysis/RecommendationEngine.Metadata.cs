using System.Text.Json;
using System.Text.RegularExpressions;
using SpaceMonger.Core.Diagnostics;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Analysis;

public partial class RecommendationEngine
{
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

}

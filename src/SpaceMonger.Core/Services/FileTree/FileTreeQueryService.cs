using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.FileTree;

public sealed class FileTreeQueryService : IFileTreeQueryService
{
    public FileEntry? FindByPath(ScanSession session, string path)
    {
        if (session.RootEntry is null || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = NormalizePath(path);
        return EnumerateDepthFirst(session.RootEntry)
            .FirstOrDefault(entry => NormalizePath(entry.Path) == normalizedPath);
    }

    public IReadOnlyList<FileEntry> FindByName(ScanSession session, string name, bool exactMatch = false, int maxResults = 50)
    {
        if (session.RootEntry is null || string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        var limit = Math.Clamp(maxResults, 1, 500);

        return EnumerateDepthFirst(session.RootEntry)
            .Where(entry => exactMatch
                ? string.Equals(entry.Name, name, comparison)
                : entry.Name.Contains(name, comparison))
            .OrderByDescending(entry => entry.Size)
            .Take(limit)
            .ToList();
    }

    public IReadOnlyList<FileEntry> ListChildren(ScanSession session, string path, int maxResults = 100)
    {
        var entry = FindByPath(session, path);
        if (entry is null || !entry.IsDirectory)
        {
            return [];
        }

        var limit = Math.Clamp(maxResults, 1, 500);
        return entry.Children
            .OrderByDescending(child => child.Size)
            .Take(limit)
            .ToList();
    }

    public SubtreeSummary SummarizeSubtree(ScanSession session, string path, int topChildren = 20)
    {
        var entry = FindByPath(session, path)
            ?? throw new InvalidOperationException($"Path not found in scanned tree: {path}");

        var fileCount = 0;
        var directoryCount = 0;
        DateTime? lastModified = null;

        foreach (var child in EnumerateDepthFirst(entry))
        {
            if (child.IsDirectory)
            {
                directoryCount++;
            }
            else
            {
                fileCount++;
            }

            if (lastModified is null || child.LastModified > lastModified)
            {
                lastModified = child.LastModified;
            }
        }

        var limit = Math.Clamp(topChildren, 1, 100);
        var largestChildren = entry.Children
            .OrderByDescending(child => child.Size)
            .Take(limit)
            .ToList();

        return new SubtreeSummary(
            entry.Path,
            entry.Name,
            entry.Size,
            fileCount,
            directoryCount,
            lastModified,
            largestChildren);
    }

    public IReadOnlyList<FileEntry> FindLargeFiles(ScanSession session, string? underPath = null, int maxResults = 50, long? minSizeBytes = null)
    {
        if (session.RootEntry is null)
        {
            return [];
        }

        var root = string.IsNullOrWhiteSpace(underPath)
            ? session.RootEntry
            : FindByPath(session, underPath!);

        if (root is null)
        {
            return [];
        }

        var limit = Math.Clamp(maxResults, 1, 500);

        return EnumerateDepthFirst(root)
            .Where(entry => !entry.IsDirectory && (!minSizeBytes.HasValue || entry.Size >= minSizeBytes.Value))
            .OrderByDescending(entry => entry.Size)
            .Take(limit)
            .ToList();
    }

    private static IEnumerable<FileEntry> EnumerateDepthFirst(FileEntry root)
    {
        var stack = new Stack<FileEntry>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            for (var index = current.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(current.Children[index]);
            }
        }
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');

        while (normalized.Contains("\\\\", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("\\\\", "\\", StringComparison.Ordinal);
        }

        return normalized.TrimEnd('\\').ToUpperInvariant();
    }
}

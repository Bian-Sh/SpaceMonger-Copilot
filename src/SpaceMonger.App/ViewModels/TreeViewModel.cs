using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using SpaceMonger.Core.Models;
using SpaceMonger.App.Diagnostics;

namespace SpaceMonger.App.ViewModels;

public partial class TreeViewModel : ObservableObject
{
    private FileEntry? _scanRoot;
    private ScanSession? _session;
    private readonly Dictionary<FileEntry, TreeEntryStats> _statsCache = new();

    [ObservableProperty]
    private ObservableCollection<TreeViewItemViewModel> _rootItems = new();

    [ObservableProperty]
    private TreeViewItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _sortBy = "Size";

    [ObservableProperty]
    private bool _sortDescending = true;

    [ObservableProperty]
    private long _totalSize;

    [ObservableProperty]
    private int _totalFiles;

    [ObservableProperty]
    private int _totalFolders;

    public void SetRoot(FileEntry root, ScanSession? session = null)
    {
        CrashDiagnostics.Log("TreeView.SetRoot", $"root={root.Path}, children={root.Children.Count}, size={root.Size}, cancelled={session?.IsCancelled}");
        _scanRoot = root;
        _session = session;
        _statsCache.Clear();
        RebuildTree();
    }

    [RelayCommand]
    private void SortBySize()
    {
        SortBy = "Size";
        SortDescending = true;
        RebuildTree();
    }

    [RelayCommand]
    private void SortByName()
    {
        if (SortBy == "Name")
            SortDescending = !SortDescending;
        else
        {
            SortBy = "Name";
            SortDescending = false;
        }
        RebuildTree();
    }

    [RelayCommand]
    private void SortByType()
    {
        if (SortBy == "Type")
            SortDescending = !SortDescending;
        else
        {
            SortBy = "Type";
            SortDescending = false;
        }
        RebuildTree();
    }

    [RelayCommand]
    private void SortByModified()
    {
        if (SortBy == "Modified")
            SortDescending = !SortDescending;
        else
        {
            SortBy = "Modified";
            SortDescending = true;
        }
        RebuildTree();
    }

    private void RebuildTree()
    {
        CrashDiagnostics.Log("TreeView.RebuildTree", $"hasRoot={_scanRoot is not null}");
        RootItems.Clear();

        if (_scanRoot is null)
            return;

        TotalSize = _session?.TotalSize ?? _scanRoot.Size;
        TotalFiles = _session?.TotalFiles ?? 0;
        TotalFolders = _session?.TotalFolders ?? 0;

        var rootItem = new TreeViewItemViewModel(_scanRoot, 0, SortBy, SortDescending, _statsCache);
        RootItems.Add(rootItem);
        rootItem.IsExpanded = true;
    }

    public void SelectEntry(FileEntry entry)
    {
        if (RootItems.Count == 0)
            return;

        var rootVm = RootItems[0];
        var target = FindTreeViewItemByPath(rootVm, entry);
        if (target is not null)
        {
            ExpandToItem(target);
            target.IsSelected = true;
            SelectedItem = target;
        }
    }

    private static TreeViewItemViewModel? FindTreeViewItemByPath(TreeViewItemViewModel root, FileEntry target)
    {
        var path = new Stack<FileEntry>();
        var currentEntry = target;
        while (currentEntry is not null)
        {
            path.Push(currentEntry);
            currentEntry = currentEntry.Parent;
        }

        if (path.Count == 0 || path.Pop() != root.Entry)
            return null;

        var current = root;
        while (path.Count > 0)
        {
            var nextEntry = path.Pop();
            current.EnsureChildrenLoaded();

            var next = current.Children.FirstOrDefault(child => child.Entry == nextEntry);
            if (next is null)
                return null;

            current = next;
        }

        return current;
    }

    private static void ExpandToItem(TreeViewItemViewModel item)
    {
        var parent = item.Parent;
        while (parent is not null)
        {
            parent.IsExpanded = true;
            parent = parent.Parent;
        }
    }
}

public partial class TreeViewItemViewModel : ObservableObject
{
    private const double PercentBarMaxWidth = 124;
    private readonly string _sortBy;
    private readonly bool _sortDescending;
    private readonly Dictionary<FileEntry, TreeEntryStats> _statsCache;

    public FileEntry Entry { get; }
    public TreeViewItemViewModel? Parent { get; }
    public int Depth { get; }

    [ObservableProperty]
    private ObservableCollection<TreeViewItemViewModel> _children = new();

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _hasLoadedChildren;

    public string Name => Entry.Name;
    public long Size => Entry.Size;
    public string? Extension => Entry.Extension;
    public DateTime LastModified => Entry.LastModified;
    public bool IsDirectory => Entry.IsDirectory;
    public bool IsAccessDenied => Entry.IsAccessDenied;
    public bool IsReparsePoint => Entry.IsReparsePoint;
    public bool HasChildren => Entry.IsDirectory && Entry.Children.Count > 0;
    public bool HasNoChildren => !HasChildren;
    public bool HasParent => Parent is not null;
    public bool IsLastChild { get; }
    public double ToggleOffset => Depth * 16.0;
    public double IconOffset => ToggleOffset + 22.0;
    public System.Windows.Thickness ToggleMargin => new(ToggleOffset, 0, 0, 0);
    public System.Windows.Thickness IconMargin => new(IconOffset, 0, 0, 0);
    public IReadOnlyList<TreeGuideSegment> TreeGuideSegments { get; }

    public string SizeText => FormatSize(Entry.Size);
    public string AllocatedText => FormatSize(Entry.Size);
    public string TypeText => Entry.IsDirectory ? "File folder" : (Entry.Extension?.TrimStart('.')?.ToUpperInvariant() ?? "File");
    public string ModifiedText => Entry.LastModified == default ? string.Empty : Entry.LastModified.ToString("yyyy-MM-dd HH:mm:ss");
    public string AttributeText { get; }
    public int ItemCount => Stats.ItemCount;
    public int FileCount => Stats.FileCount;
    public int FolderCount => Stats.FolderCount;
    public double ParentPercent { get; }
    public string ParentPercentText => $"{ParentPercent:F1} %";
    public double ParentPercentBarWidth => Math.Max(0, Math.Min(PercentBarMaxWidth, PercentBarMaxWidth * ParentPercent / 100.0));
    public TreeEntryStats Stats { get; }

    public string IconGlyph => Entry.IsDirectory ? "\uE8B7" : GetFileIconGlyph(Entry.Extension);

    public TreeViewItemViewModel(
        FileEntry entry,
        int depth,
        string sortBy,
        bool sortDescending,
        Dictionary<FileEntry, TreeEntryStats> statsCache,
        TreeViewItemViewModel? parent = null,
        bool isLastChild = true)
    {
        Entry = entry;
        Parent = parent;
        Depth = depth;
        IsLastChild = isLastChild;
        _sortBy = sortBy;
        _sortDescending = sortDescending;
        _statsCache = statsCache;
        TreeGuideSegments = BuildTreeGuideSegments(parent);
        Stats = GetStats(entry);
        AttributeText = BuildAttributeText(entry);
        ParentPercent = parent?.Entry.Size > 0 ? entry.Size * 100.0 / parent.Entry.Size : 100.0;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !HasLoadedChildren)
        {
            EnsureChildrenLoaded();
        }
    }

    public void EnsureChildrenLoaded()
    {
        if (HasLoadedChildren)
            return;

        LoadChildren();
        HasLoadedChildren = true;
    }

    private void LoadChildren()
    {
        CrashDiagnostics.Log("TreeView.LoadChildren", $"entry={Entry.Path}, children={Entry.Children.Count}, depth={Depth}");
        Children.Clear();

        var sorted = GetSortedChildren(Entry.Children, _sortBy, _sortDescending);

        for (var index = 0; index < sorted.Count; index++)
        {
            var child = sorted[index];
            var childVm = new TreeViewItemViewModel(child, Depth + 1, _sortBy, _sortDescending, _statsCache, this, index == sorted.Count - 1);
            Children.Add(childVm);
        }
    }

    private static IReadOnlyList<TreeGuideSegment> BuildTreeGuideSegments(TreeViewItemViewModel? parent)
    {
        if (parent?.Parent is null)
        {
            return Array.Empty<TreeGuideSegment>();
        }

        return parent.TreeGuideSegments.Concat(new[] { new TreeGuideSegment(!parent.IsLastChild) }).ToArray();
    }

    private TreeEntryStats GetStats(FileEntry entry)
    {
        if (_statsCache.TryGetValue(entry, out var stats))
        {
            return stats;
        }

        stats = new TreeEntryStats(1, entry.IsDirectory ? 0 : 1, entry.IsDirectory ? 1 : 0);
        _statsCache[entry] = stats;
        return stats;
    }

    private static List<FileEntry> GetSortedChildren(List<FileEntry> children, string sortBy, bool descending)
    {
        var sorted = sortBy switch
        {
            "Name" => descending
                ? children.OrderByDescending(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "Type" => descending
                ? children.OrderByDescending(c => c.IsDirectory).ThenByDescending(c => c.Extension).ToList()
                : children.OrderBy(c => c.IsDirectory).ThenBy(c => c.Extension).ToList(),
            "Modified" => descending
                ? children.OrderByDescending(c => c.LastModified).ToList()
                : children.OrderBy(c => c.LastModified).ToList(),
            _ => descending
                ? children.OrderByDescending(c => c.Size).ToList()
                : children.OrderBy(c => c.Size).ToList()
        };

        return sorted.OrderByDescending(c => c.IsDirectory).ThenByDescending(c => descending ? c.Size : -c.Size).ToList();
    }

    private static string BuildAttributeText(FileEntry entry)
    {
        var flags = new List<string>();

        if (entry.Attributes is { } attributes)
        {
            AddFlag(flags, attributes, FileAttributes.ReadOnly, "R");
            AddFlag(flags, attributes, FileAttributes.Hidden, "H");
            AddFlag(flags, attributes, FileAttributes.System, "S");
            AddFlag(flags, attributes, FileAttributes.Archive, "A");
            AddFlag(flags, attributes, FileAttributes.Compressed, "C");
            AddFlag(flags, attributes, FileAttributes.Encrypted, "E");
            AddFlag(flags, attributes, FileAttributes.ReparsePoint, "L");
        }
        else if (entry.IsReparsePoint)
        {
            flags.Add("L");
        }
        if (entry.IsAccessDenied)
            flags.Add("DENY");
        if (entry.IsCloudPlaceholder)
            flags.Add("Cloud");

        return string.Join(string.Empty, flags.Distinct());
    }

    private static void AddFlag(List<string> flags, FileAttributes attributes, FileAttributes flag, string text)
    {
        if (attributes.HasFlag(flag))
        {
            flags.Add(text);
        }
    }

    private static string GetFileIconGlyph(string? extension)
    {
        return extension?.ToLowerInvariant() switch
        {
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "\uE7EF",
            ".dll" or ".sys" or ".ocx" => "\uE74C",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "\uEB9F",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "\uE786",
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "\uE8D6",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uE8B8",
            ".pdf" => "\uEA90",
            ".doc" or ".docx" => "\uE8A5",
            ".xls" or ".xlsx" => "\uE8A7",
            ".ppt" or ".pptx" => "\uE8A6",
            ".txt" or ".log" or ".ini" or ".cfg" => "\uE8A5",
            ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h" => "\uE943",
            ".html" or ".css" or ".xml" or ".json" => "\uE943",
            _ => "\uE8A5"
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return bytes == 0 ? "0" : $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }
}

public sealed record TreeEntryStats(int ItemCount, int FileCount, int FolderCount);

public sealed record TreeGuideSegment(bool Continues);

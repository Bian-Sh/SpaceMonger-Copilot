using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Treemap;

namespace SpaceMonger.App.ViewModels;

public partial class TreeViewModel : ObservableObject
{
    private FileEntry? _scanRoot;
    private ScanSession? _session;

    [ObservableProperty]
    private ObservableCollection<TreeViewItemViewModel> _rootItems = new();

    [ObservableProperty]
    private TreeViewItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _sortBy = "Size"; // Size, Name, Type, Modified

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
        _scanRoot = root;
        _session = session;
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
        RootItems.Clear();

        if (_scanRoot is null)
            return;

        TotalSize = _session?.TotalSize ?? _scanRoot.Size;
        TotalFiles = _session?.TotalFiles ?? 0;
        TotalFolders = _session?.TotalFolders ?? 0;

        var rootItem = new TreeViewItemViewModel(_scanRoot, 0, SortBy, SortDescending);
        RootItems.Add(rootItem);

        // Auto-expand root
        rootItem.IsExpanded = true;
    }

    public void SelectEntry(FileEntry entry)
    {
        if (RootItems.Count == 0)
            return;

        var rootVm = RootItems[0];
        var target = FindTreeViewItem(rootVm, entry);
        if (target is not null)
        {
            target.IsSelected = true;
            ExpandToItem(target);
            SelectedItem = target;
        }
    }

    private static TreeViewItemViewModel? FindTreeViewItem(TreeViewItemViewModel current, FileEntry target)
    {
        if (current.Entry == target)
            return current;

        foreach (var child in current.Children)
        {
            var found = FindTreeViewItem(child, target);
            if (found is not null)
                return found;
        }

        return null;
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
    private readonly string _sortBy;
    private readonly bool _sortDescending;

    public FileEntry Entry { get; }
    public TreeViewItemViewModel? Parent { get; }

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

    public string SizeText => FormatSize(Entry.Size);
    public string TypeText => Entry.IsDirectory ? "File folder" : (Entry.Extension?.TrimStart('.')?.ToUpper() ?? "File");
    public string ModifiedText => Entry.LastModified.ToString("yyyy-MM-dd HH:mm");

    // Windows 11 folder icon (Segoe MDL2 Assets)
    public string IconGlyph => Entry.IsDirectory ? "\uE8B7" : GetFileIconGlyph(Entry.Extension);

    public TreeViewItemViewModel(FileEntry entry, int depth, string sortBy, bool sortDescending, TreeViewItemViewModel? parent = null)
    {
        Entry = entry;
        Parent = parent;
        _sortBy = sortBy;
        _sortDescending = sortDescending;

        // Add dummy child for directories to show expand arrow
        if (entry.IsDirectory && entry.Children.Count > 0)
        {
            Children.Add(new TreeViewItemViewModel(entry.Children[0], depth + 1, sortBy, sortDescending, this)
            {
                HasLoadedChildren = true
            });
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !HasLoadedChildren)
        {
            LoadChildren();
            HasLoadedChildren = true;
        }
    }

    private void LoadChildren()
    {
        Children.Clear();

        var sorted = GetSortedChildren(Entry.Children, _sortBy, _sortDescending);

        foreach (var child in sorted)
        {
            var childVm = new TreeViewItemViewModel(child, Entry.Depth + 1, _sortBy, _sortDescending, this);
            Children.Add(childVm);
        }
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

        // Always put directories first
        return sorted.OrderByDescending(c => c.IsDirectory).ThenByDescending(c => descending ? c.Size : -c.Size).ToList();
    }

    private static string GetFileIconGlyph(string? extension)
    {
        return extension?.ToLower() switch
        {
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => "\uE7EF", // Program
            ".dll" or ".sys" or ".ocx" => "\uE74C", // Settings
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "\uEB9F", // Photo
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" => "\uE786", // Video
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" => "\uE8D6", // Music
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\uE8B8", // Zip folder
            ".pdf" => "\uEA90", // PDF
            ".doc" or ".docx" => "\uE8A5", // Word
            ".xls" or ".xlsx" => "\uE8A7", // Excel
            ".ppt" or ".pptx" => "\uE8A6", // PowerPoint
            ".txt" or ".log" or ".ini" or ".cfg" => "\uE8A5", // Text
            ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h" => "\uE943", // Code
            ".html" or ".css" or ".xml" or ".json" => "\uE943", // Code
            _ => "\uE8A5" // Default file
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }
}

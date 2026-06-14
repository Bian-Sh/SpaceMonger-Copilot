using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Localization;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Treemap;

namespace SpaceMonger.App.ViewModels;

public partial class TreemapViewModel : ObservableObject
{
    private readonly ITreemapLayoutEngine _layoutEngine;
    private readonly Stack<FileEntry> _navigationStack = new();
    private FileEntry? _scanRoot;

    public FileEntry? ScanRoot => _scanRoot;
    private ScanSession? _session;

    [ObservableProperty]
    private FileEntry? _currentRoot;

    [ObservableProperty]
    private TreemapNode? _selectedNode;

    [ObservableProperty]
    private TreemapNode? _hoveredNode;

    [ObservableProperty]
    private List<TreemapNode>? _nodes;

    [ObservableProperty]
    private List<string> _breadcrumbPath = new();

    [ObservableProperty]
    private bool _canNavigateUp;

    [ObservableProperty]
    private float _viewWidth;

    [ObservableProperty]
    private float _viewHeight;

    public TreemapViewModel(ITreemapLayoutEngine layoutEngine)
    {
        _layoutEngine = layoutEngine;
    }

    public void SetRoot(FileEntry root, ScanSession? session = null)
    {
        _scanRoot = root;
        _session = session;
        _navigationStack.Clear();
        CurrentRoot = root;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void DrillDown(FileEntry folder)
    {
        if (CurrentRoot is not null)
        {
            _navigationStack.Push(CurrentRoot);
        }

        CurrentRoot = folder;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void NavigateToEntry(FileEntry entry)
    {
        var viewRoot = entry.IsDirectory ? entry : entry.Parent;
        if (viewRoot is null || viewRoot == CurrentRoot)
            return;

        if (!IsInCurrentScan(viewRoot))
            return;

        if (CurrentRoot is not null)
        {
            _navigationStack.Push(CurrentRoot);
        }

        CurrentRoot = viewRoot;
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    public void NavigateUp()
    {
        if (_navigationStack.Count == 0)
            return;

        CurrentRoot = _navigationStack.Pop();
        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void NavigateTo(int breadcrumbIndex)
    {
        if (breadcrumbIndex < 0 || breadcrumbIndex >= BreadcrumbPath.Count - 1)
            return;

        // The breadcrumb list is ordered from the scan root (index 0) to the current root (last index).
        // To navigate to breadcrumbIndex, we pop the navigation stack until we reach
        // the desired level.
        int levelsToGoUp = BreadcrumbPath.Count - 1 - breadcrumbIndex;

        for (int i = 0; i < levelsToGoUp; i++)
        {
            if (_navigationStack.Count == 0)
                break;

            CurrentRoot = _navigationStack.Pop();
        }

        UpdateBreadcrumb();
        RecomputeLayout();
    }

    public void UpdateSize(float width, float height)
    {
        ViewWidth = width;
        ViewHeight = height;
        RecomputeLayout();
    }

    public void ApplyCleanupResults(List<CleanupAction> results, ScanSession session)
    {
        foreach (var action in results)
        {
            if (action.Result is not (CleanupResult.Success or CleanupResult.AlreadyRemoved))
                continue;

            var entry = action.Recommendation.Entry;
            if (entry?.Parent is null)
                continue;

            entry.Parent.RemoveChild(entry);

            if (entry.IsDirectory)
                session.TotalFolders--;
            else
                session.TotalFiles--;

            session.TotalSize -= entry.Size;
        }

        RecomputeLayout();
    }

    private static readonly FileEntry FreeSpaceSentinel = new()
    {
        Name = L.Text("FreeSpaceName"),
        IsDirectory = false,
        Path = L.Text("FreeSpacePath"),
    };

    private void RecomputeLayout()
    {
        if (CurrentRoot is null || ViewWidth <= 0 || ViewHeight <= 0)
        {
            Nodes = null;
            return;
        }

        // When viewing the scan root of a whole-drive scan, temporarily inject
        // a synthetic "Free Space" child so the layout engine allocates a
        // proportional block for it — matching classic SpaceMonger behavior.
        bool injectedFreeSpace = false;
        if (CurrentRoot == _scanRoot
            && _session?.DriveCapacity is not null
            && _session.DriveFreeSpace is not null
            && _session.DriveFreeSpace.Value > 0)
        {
            FreeSpaceSentinel.Size = _session.DriveFreeSpace.Value;

            // Temporarily adjust the root to include free space in the total.
            CurrentRoot.Children.Add(FreeSpaceSentinel);
            var originalSize = CurrentRoot.Size;
            CurrentRoot.Size = _session.DriveCapacity.Value;
            injectedFreeSpace = true;

            var nodes = _layoutEngine.ComputeLayout(CurrentRoot, ViewWidth, ViewHeight, maxDepth: 8);

            // Restore the original tree state.
            CurrentRoot.Children.Remove(FreeSpaceSentinel);
            CurrentRoot.Size = originalSize;

            // Style the free space node: off-white to match the drive root color.
            foreach (var node in nodes)
            {
                if (node.Entry == FreeSpaceSentinel)
                {
                    node.ColorHex = "#F0F0E8";
                    node.Label = FormatSize(_session.DriveFreeSpace.Value);
                }
            }

            Nodes = nodes;
        }

        if (!injectedFreeSpace)
        {
            Nodes = _layoutEngine.ComputeLayout(CurrentRoot, ViewWidth, ViewHeight, maxDepth: 8);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024L) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F1} TB";
    }

    private bool IsInCurrentScan(FileEntry entry)
    {
        if (_scanRoot is null)
            return false;

        var current = entry;
        while (current is not null)
        {
            if (current == _scanRoot)
                return true;

            current = current.Parent;
        }

        return false;
    }

    private void UpdateBreadcrumb()
    {
        var path = new List<string>();

        var entry = CurrentRoot;
        while (entry is not null)
        {
            path.Add(entry.Name);
            if (entry == _scanRoot)
                break;
            entry = entry.Parent;
        }

        path.Reverse();
        BreadcrumbPath = path;

        CanNavigateUp = _navigationStack.Count > 0;
        NavigateUpCommand.NotifyCanExecuteChanged();
    }
}

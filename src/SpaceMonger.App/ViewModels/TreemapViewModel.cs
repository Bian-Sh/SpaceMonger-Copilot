using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Treemap;

namespace SpaceMonger.App.ViewModels;

public partial class TreemapViewModel : ObservableObject
{
    private readonly ITreemapLayoutEngine _layoutEngine;
    private readonly Stack<FileEntry> _navigationStack = new();
    private FileEntry? _scanRoot;

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

    public void SetRoot(FileEntry root)
    {
        _scanRoot = root;
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

    private void RecomputeLayout()
    {
        if (CurrentRoot is null || ViewWidth <= 0 || ViewHeight <= 0)
        {
            Nodes = null;
            return;
        }

        Nodes = _layoutEngine.ComputeLayout(CurrentRoot, ViewWidth, ViewHeight, maxDepth: 3);
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

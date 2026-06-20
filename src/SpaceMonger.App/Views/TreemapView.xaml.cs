using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Controls;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class TreemapView : UserControl
{
    private TreemapViewModel? _viewModel;
    private bool _isScanning;

    public Func<string, string, MessageBoxButton, MessageBoxImage, Task<MessageBoxResult>>? ShowMessageAsync { get; set; }

    public Func<FrameworkElement, double, double, Task<bool?>>? ShowContentAsync { get; set; }

    public Action<object?>? CloseContentModal { get; set; }

    public TreemapView()
    {
        InitializeComponent();
    }

    public void SetViewModel(TreemapViewModel vm)
    {
        // Unsubscribe from previous view model if any
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = vm;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Sync initial state
        Treemap.Nodes = _viewModel.Nodes;
        UpdateEmptyState();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.Nodes):
                Treemap.Nodes = _viewModel?.Nodes;
                UpdateEmptyState();
                break;
        }
    }

    public void SetScanningState(bool isScanning, string? progressText)
    {
        _isScanning = isScanning;
        ScanningOverlay.Visibility = isScanning ? Visibility.Visible : Visibility.Collapsed;
        ScanProgressText.Text = progressText ?? string.Empty;
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        bool hasData = _viewModel?.Nodes is { Count: > 0 };
        bool showEmpty = !_isScanning && !hasData;

        EmptyStatePanel.Visibility = showEmpty ? Visibility.Visible : Visibility.Collapsed;
        if (showEmpty)
        {
            var path = _viewModel?.CurrentRoot?.Path;
            bool hasScan = _viewModel?.ScanRoot is not null;
            bool outsideScan = hasScan && !string.IsNullOrWhiteSpace(path) && !IsUnderPath(path, _viewModel!.ScanRoot!.Path);
            bool insideScan = hasScan && !outsideScan;

            EmptyStateTitle.Text = outsideScan
                ? L.Text("TreemapAnalysisRequiredTitle")
                : insideScan
                    ? L.Text("TreemapNoChildDataTitle")
                    : L.Text("TreemapEmptyTitle");
            EmptyStateHint.Text = outsideScan
                ? L.Text("TreemapAnalysisRequiredHint")
                : insideScan
                    ? L.Text("TreemapNoChildDataHint")
                    : L.Text("TreemapEmptyHint");
        }

        // Hide opaque SkiaSharp canvas when empty (Opacity=0 keeps element in visual tree)
        Treemap.Opacity = hasData ? 1.0 : 0.0;
    }

    private static bool IsUnderPath(string path, string rootPath)
    {
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
                   || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool NavigateToPath(string path)
    {
        return _viewModel?.NavigateToPath(path) == true;
    }

    private void Treemap_NodeClicked(object? sender, TreemapNode node)
    {
        if (node.Entry.IsDirectory)
        {
            _viewModel?.DrillDown(node.Entry);
        }
    }

    private void Treemap_NodeHovered(object? sender, TreemapNode node)
    {
        if (_viewModel is not null)
        {
            _viewModel.HoveredNode = node;
        }
    }

    private void Treemap_NodeRightClicked(object? sender, TreemapNode node)
    {
        var entry = node.Entry;
        var contextMenu = new ContextMenu();

        // "Open in Explorer" menu item
        var openInExplorerItem = new MenuItem { Header = L.Text("OpenInExplorerMenu") };
        openInExplorerItem.Click += async (_, _) =>
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{entry.Path}\"");
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(L.Format("OpenExplorerFailedMessage", ex.Message));
            }
        };
        contextMenu.Items.Add(openInExplorerItem);

        // "Copy Path" menu item
        var copyPathItem = new MenuItem { Header = L.Text("CopyPathMenu") };
        copyPathItem.Click += async (_, _) =>
        {
            try
            {
                Clipboard.SetText(entry.Path);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(L.Format("CopyPathFailedMessage", ex.Message));
            }
        };
        contextMenu.Items.Add(copyPathItem);

        // Separator
        contextMenu.Items.Add(new Separator());

        // "Properties" menu item
        var propertiesItem = new MenuItem { Header = L.Text("PropertiesMenu") };
        propertiesItem.Click += async (_, _) => await ShowPropertiesDialogAsync(entry);
        contextMenu.Items.Add(propertiesItem);

        // Show the context menu at the mouse position
        contextMenu.PlacementTarget = Treemap;
        contextMenu.IsOpen = true;
    }

    private async Task ShowPropertiesDialogAsync(FileEntry entry)
    {
        string type;
        DateTime createdDate = DateTime.MinValue;
        DateTime lastModifiedDate = entry.LastModified;

        try
        {
            if (entry.IsDirectory)
            {
                type = L.Text("FolderType");
                var dirInfo = new DirectoryInfo(entry.Path);
                if (dirInfo.Exists)
                {
                    createdDate = dirInfo.CreationTime;
                    lastModifiedDate = dirInfo.LastWriteTime;
                }
            }
            else
            {
                type = string.IsNullOrEmpty(entry.Extension)
                    ? L.Text("FileType")
                    : L.Format("FileTypeFormat", entry.Extension.TrimStart('.').ToUpperInvariant());
                var fileInfo = new FileInfo(entry.Path);
                if (fileInfo.Exists)
                {
                    createdDate = fileInfo.CreationTime;
                    lastModifiedDate = fileInfo.LastWriteTime;
                }
            }
        }
        catch
        {
            type = entry.IsDirectory ? L.Text("FolderType") : L.Text("FileType");
        }

        var titleBlock = new TextBlock
        {
            Text = L.Text("PropertiesTitle"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)FindResource("VP.TextPrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        };

        var propertiesPanel = new StackPanel { MinWidth = 420 };
        propertiesPanel.Children.Add(titleBlock);

        var grid = new Grid
        {
            Margin = new Thickness(0),
        };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labels = new[]
        {
            (L.Text("PropertiesName"), entry.Name),
            (L.Text("PropertiesPath"), entry.Path),
            (L.Text("PropertiesSize"), FileSizeConverter.FormatSize(entry.Size)),
            (L.Text("PropertiesType"), type),
            (L.Text("PropertiesCreated"), createdDate == DateTime.MinValue ? L.Text("PropertiesUnknown") : createdDate.ToString("yyyy-MM-dd HH:mm:ss")),
            (L.Text("PropertiesModified"), lastModifiedDate == DateTime.MinValue ? L.Text("PropertiesUnknown") : lastModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")),
        };

        for (int i = 0; i < labels.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            var labelBlock = new TextBlock
            {
                Text = labels[i].Item1,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("VP.TextPrimaryBrush"),
            };
            Grid.SetRow(labelBlock, i);
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = labels[i].Item2,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = labels[i].Item2,
                Foreground = (System.Windows.Media.Brush)FindResource("VP.TextSecondaryBrush"),
            };
            Grid.SetRow(valueBlock, i);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
        }

        // Add an OK button at the bottom
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        var okButton = new Button
        {
            Content = L.Text("OkButton"),
            Width = 80,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Style = (Style)FindResource("VP.AccentButton"),
        };
        okButton.Click += (_, _) => CloseContentModal?.Invoke(true);
        Grid.SetRow(okButton, labels.Length);
        Grid.SetColumn(okButton, 1);
        grid.Children.Add(okButton);

        propertiesPanel.Children.Add(grid);

        if (ShowContentAsync is not null)
        {
            await ShowContentAsync(propertiesPanel, 520, 560);
        }
    }

    private Task ShowErrorAsync(string message)
    {
        if (ShowMessageAsync is not null)
        {
            return ShowMessageAsync(message, L.Text("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return Task.CompletedTask;
    }

    private void Treemap_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel?.UpdateSize((float)e.NewSize.Width, (float)e.NewSize.Height);
    }
}





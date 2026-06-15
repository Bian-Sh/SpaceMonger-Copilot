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
        UpButton.IsEnabled = _viewModel.CanNavigateUp;
        RebuildBreadcrumbs();
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

            case nameof(TreemapViewModel.CanNavigateUp):
                UpButton.IsEnabled = _viewModel?.CanNavigateUp ?? false;
                break;

            case nameof(TreemapViewModel.BreadcrumbPath):
                RebuildBreadcrumbs();
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
        NoDataText.Visibility = _isScanning || _viewModel?.Nodes is { Count: > 0 }
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void RebuildBreadcrumbs()
    {
        BreadcrumbPanel.Children.Clear();

        if (_viewModel?.BreadcrumbPath is null)
            return;

        for (int i = 0; i < _viewModel.BreadcrumbPath.Count; i++)
        {
            if (i > 0)
            {
                var separator = new TextBlock
                {
                    Text = " > ",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = System.Windows.Media.Brushes.Gray,
                };
                BreadcrumbPanel.Children.Add(separator);
            }

            int index = i;
            bool isLast = i == _viewModel.BreadcrumbPath.Count - 1;

            var breadcrumbText = new TextBlock
            {
                Text = _viewModel.BreadcrumbPath[i],
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = isLast ? Cursors.Arrow : Cursors.Hand,
                FontWeight = isLast ? FontWeights.SemiBold : FontWeights.Normal,
                TextDecorations = isLast ? null : TextDecorations.Underline,
                Foreground = isLast
                    ? System.Windows.Media.Brushes.Black
                    : System.Windows.Media.Brushes.DodgerBlue,
                Margin = new Thickness(2, 0, 2, 0),
            };

            if (!isLast)
            {
                breadcrumbText.MouseLeftButtonUp += (_, _) =>
                {
                    _viewModel?.NavigateTo(index);
                };
            }

            BreadcrumbPanel.Children.Add(breadcrumbText);
        }
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.NavigateUp();
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
        openInExplorerItem.Click += (_, _) =>
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{entry.Path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    L.Format("OpenExplorerFailedMessage", ex.Message),
                    L.Text("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        contextMenu.Items.Add(openInExplorerItem);

        // "Copy Path" menu item
        var copyPathItem = new MenuItem { Header = L.Text("CopyPathMenu") };
        copyPathItem.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(entry.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    L.Format("CopyPathFailedMessage", ex.Message),
                    L.Text("ErrorTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        contextMenu.Items.Add(copyPathItem);

        // Separator
        contextMenu.Items.Add(new Separator());

        // "Properties" menu item
        var propertiesItem = new MenuItem { Header = L.Text("PropertiesMenu") };
        propertiesItem.Click += (_, _) => ShowPropertiesDialog(entry);
        contextMenu.Items.Add(propertiesItem);

        // Show the context menu at the mouse position
        contextMenu.PlacementTarget = Treemap;
        contextMenu.IsOpen = true;
    }

    private static void ShowPropertiesDialog(FileEntry entry)
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

        var propertiesWindow = new Window
        {
            Title = L.Format("PropertiesTitle", entry.Name),
            Width = 400,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Application.Current.MainWindow,
        };

        var grid = new Grid
        {
            Margin = new Thickness(16),
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
        };
        okButton.Click += (_, _) => propertiesWindow.Close();
        Grid.SetRow(okButton, labels.Length);
        Grid.SetColumn(okButton, 1);
        grid.Children.Add(okButton);

        propertiesWindow.Content = grid;
        propertiesWindow.ShowDialog();
    }

    private void Treemap_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _viewModel?.UpdateSize((float)e.NewSize.Width, (float)e.NewSize.Height);
    }
}

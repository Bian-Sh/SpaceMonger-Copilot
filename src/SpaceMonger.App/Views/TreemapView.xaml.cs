using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SpaceMonger.App.Controls;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Treemap;

namespace SpaceMonger.App.Views;

public partial class TreemapView : UserControl
{
    private TreemapViewModel? _viewModel;

    public TreemapView()
    {
        InitializeComponent();
        PopulateLegend();
    }

    private void PopulateLegend()
    {
        var legendPanel = FindName("LegendPanel") as WrapPanel;
        if (legendPanel is null)
            return;

        foreach (var (category, colorHex) in FileTypeColorMap.GetLegendItems())
        {
            var itemPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 12, 0),
            };

            var colorRect = new Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = new BrushConverter().ConvertFromString(colorHex) as SolidColorBrush,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var label = new TextBlock
            {
                Text = category,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.DimGray,
            };

            itemPanel.Children.Add(colorRect);
            itemPanel.Children.Add(label);
            legendPanel.Children.Add(itemPanel);
        }
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
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.Nodes):
                Treemap.Nodes = _viewModel?.Nodes;
                break;

            case nameof(TreemapViewModel.CanNavigateUp):
                UpButton.IsEnabled = _viewModel?.CanNavigateUp ?? false;
                break;

            case nameof(TreemapViewModel.BreadcrumbPath):
                RebuildBreadcrumbs();
                break;
        }
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
        var openInExplorerItem = new MenuItem { Header = "Open in Explorer" };
        openInExplorerItem.Click += (_, _) =>
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{entry.Path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open Explorer:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        contextMenu.Items.Add(openInExplorerItem);

        // "Copy Path" menu item
        var copyPathItem = new MenuItem { Header = "Copy Path" };
        copyPathItem.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(entry.Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not copy path to clipboard:\n{ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };
        contextMenu.Items.Add(copyPathItem);

        // Separator
        contextMenu.Items.Add(new Separator());

        // "Properties" menu item
        var propertiesItem = new MenuItem { Header = "Properties" };
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
                type = "Folder";
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
                    ? "File"
                    : $"{entry.Extension.TrimStart('.').ToUpperInvariant()} File";
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
            type = entry.IsDirectory ? "Folder" : "File";
        }

        var propertiesWindow = new Window
        {
            Title = $"Properties - {entry.Name}",
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
            ("Name:", entry.Name),
            ("Path:", entry.Path),
            ("Size:", FileSizeConverter.FormatSize(entry.Size)),
            ("Type:", type),
            ("Created:", createdDate == DateTime.MinValue ? "Unknown" : createdDate.ToString("yyyy-MM-dd HH:mm:ss")),
            ("Modified:", lastModifiedDate == DateTime.MinValue ? "Unknown" : lastModifiedDate.ToString("yyyy-MM-dd HH:mm:ss")),
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
            Content = "OK",
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

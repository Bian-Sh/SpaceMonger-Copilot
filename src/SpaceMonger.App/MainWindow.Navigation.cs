using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Helpers;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.App;

public partial class MainWindow
{
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _treemapViewModel?.NavigateUp();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            CommitPathEditText();
            if (DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
                vm.ScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            OnAnalyzeRequested();
            e.Handled = true;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateForward();
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateToParent();
    }

    private void PathEditTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitPathEditText();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            SwitchToBreadcrumbMode();
            e.Handled = true;
        }
    }

    private void ScanButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CommitPathEditText();
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        CommitPathEditText();
    }

    private void CommitPathEditText()
    {
        if (PathEditTextBox.Visibility != Visibility.Visible)
            return;

        var path = PathEditTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            NavigateToPathOrSelect(path);
        }
        SwitchToBreadcrumbMode();
    }

    private void PathEditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ExitPathEditMode();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (PathEditTextBox.Visibility != Visibility.Visible)
            return;

        if (IsOriginalSourceWithin(e.OriginalSource, PathEditTextBox)
            || IsOriginalSourceWithin(e.OriginalSource, BrowseButton))
        {
            return;
        }

        if (IsOriginalSourceWithin(e.OriginalSource, ScanButton))
        {
            CommitPathEditText();
            if (DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
                vm.ScanCommand.Execute(null);
            e.Handled = true;
            return;
        }

        ExitPathEditMode();
        Keyboard.ClearFocus();
    }

    private void AddressBar_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PathEditTextBox.Visibility == Visibility.Visible)
            return;

        // Only switch to edit mode if the click was on the container itself, not on breadcrumb buttons
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is Button)
                return; // Click was on a breadcrumb segment button
            if (source == sender)
                break;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        if (BreadcrumbBar.Visibility == Visibility.Visible)
        {
            SwitchToEditMode();
        }
    }

    private static bool IsOriginalSourceWithin(object originalSource, DependencyObject target)
    {
        var source = originalSource as DependencyObject;
        while (source is not null)
        {
            if (source == target)
                return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }
    private void SwitchToEditMode()
    {
        var path = _treemapViewModel?.CurrentRoot?.Path
                   ?? (DataContext as MainViewModel)?.SelectedPath;
        if (path is not null)
        {
            PathEditTextBox.Text = path;
        }
        BreadcrumbBar.Visibility = Visibility.Collapsed;
        PathEditTextBox.Visibility = Visibility.Visible;
        PathEditTextBox.Focus();
        PathEditTextBox.SelectAll();
    }

    private void SwitchToBreadcrumbMode()
    {
        ExitPathEditMode();
    }

    private void ExitPathEditMode()
    {
        if (PathEditTextBox.Visibility != Visibility.Visible)
            return;

        PathEditTextBox.Visibility = Visibility.Collapsed;
        BreadcrumbBar.Visibility = Visibility.Visible;
        RebuildBreadcrumbBar();
    }

    private bool _rebuildingBreadcrumbs;


    private void OnAppLanguageChanged()
    {
        Dispatcher.InvokeAsync(() => RebuildBreadcrumbBar());
    }

    private void RebuildBreadcrumbBar()
    {
        if (_rebuildingBreadcrumbs)
            return;

        _rebuildingBreadcrumbs = true;
        try
        {
            BreadcrumbBar.Children.Clear();

            var currentPath = _displayPathOverride ?? _treemapViewModel?.CurrentRoot?.Path;
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = (DataContext as MainViewModel)?.SelectedPath;
            }
            if (string.IsNullOrEmpty(currentPath))
                return;

            var segments = ParsePathSegments(currentPath);

            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                {
                    var ownerPath = segments[i - 1].path;
                    var sepBtn = new Button
                    {
                        Content = new TextBlock
                        {
                            Text = "›",
                            FontSize = 18,
                            FontWeight = FontWeights.SemiBold,
                            Opacity = 0.95,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextAlignment = TextAlignment.Center,
                        },
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush"),
                        Padding = new Thickness(5, 0, 5, 0),
                        Cursor = Cursors.Hand,
                        Tag = ownerPath,
                        VerticalContentAlignment = VerticalAlignment.Center,
                    };
                    var sepMenu = new ContextMenu();
                    sepMenu.Opened += BreadcrumbDropdown_Opened;
                    sepBtn.ContextMenu = sepMenu;
                    sepBtn.Click += BreadcrumbChevron_Click;
                    sepBtn.MouseEnter += (s, _) => ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                    sepBtn.MouseLeave += (s, _) => ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                    BreadcrumbBar.Children.Add(sepBtn);
                }

                string segPath = segments[i].path;
                string segName = segments[i].name;

                // ── Name button: click to navigate ──
                var nameButton = new Button
                {
                    Content = new TextBlock
                    {
                        Text = segName,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    Padding = new Thickness(4, 2, 2, 2),
                    Cursor = Cursors.Hand,
                    Tag = segPath,
                };
                nameButton.Click += BreadcrumbSegment_Click;
                nameButton.MouseEnter += (s, _) =>
                {
                    ((Button)s).Background = (SolidColorBrush)FindResource("VP.SurfaceHoverBrush");
                    ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                };
                nameButton.MouseLeave += (s, _) =>
                {
                    ((Button)s).Background = Brushes.Transparent;
                    ((Button)s).Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
                };
                BreadcrumbBar.Children.Add(nameButton);
            }

            // Trailing › chevron (shows children of current folder)
            if (segments.Count > 0)
            {
                var trailBtn = new Button
                {
                    Content = new TextBlock
                    {
                        Text = "›",
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold,
                        Opacity = 0.95,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                    },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush"),
                    Padding = new Thickness(5, 0, 5, 0),
                    Cursor = Cursors.Hand,
                    Tag = segments[^1].path,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                var trailMenu = new ContextMenu();
                trailMenu.Opened += BreadcrumbDropdown_Opened;
                trailBtn.ContextMenu = trailMenu;
                trailBtn.Click += BreadcrumbChevron_Click;
                trailBtn.MouseEnter += (s, _) => ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                trailBtn.MouseLeave += (s, _) => ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                BreadcrumbBar.Children.Add(trailBtn);
            }
        }
        finally
        {
            _rebuildingBreadcrumbs = false;
        }
    }

    private const string ThisPCSentinel = "::thispc::";

    private static string ThisPC => L.Text("ThisPCLabel");

    private List<(string path, string name)> ParsePathSegments(string fullPath)
    {
        var result = new List<(string path, string name)>();
        if (string.IsNullOrEmpty(fullPath))
            return result;

        // Always start with "此电脑" (This PC) — Windows 11 Explorer style
        result.Add((ThisPCSentinel, ThisPC));

        var parts = fullPath.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return result;

        // Drive root (e.g., "C:")
        string rootPart = parts[0];
        result.Add(($"{rootPart}{System.IO.Path.DirectorySeparatorChar}", rootPart));

        var accumulated = $"{rootPart}{System.IO.Path.DirectorySeparatorChar}";
        for (int i = 1; i < parts.Length; i++)
        {
            accumulated += parts[i];
            if (i < parts.Length - 1)
                accumulated += System.IO.Path.DirectorySeparatorChar;
            result.Add((accumulated, parts[i]));
        }

        return result;
    }

    private void BreadcrumbSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path)
        {
            // Skip "此电脑" — it's a virtual root, not a real path
            if (path == ThisPCSentinel)
            {
                e.Handled = true;
                return;
            }

            // Fix 6: skip if already at this path
            if (_treemapViewModel?.CurrentRoot?.Path == path)
            {
                e.Handled = true;
                return;
            }

            NavigateToPathOrSelect(path);
        }
        e.Handled = true;
    }

    private void BreadcrumbChevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu cm)
        {
            cm.PlacementTarget = btn;
            cm.IsOpen = true;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Populates a breadcrumb chevron ContextMenu on open.
    /// Uses ItemsSource + VirtualizingStackPanel for smooth scrolling and O(visible) perf
    /// even with hundreds of subfolders — matches Windows 11 Explorer flyout behavior.
    /// </summary>
    private void BreadcrumbDropdown_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        // Detach previous ItemsSource so we can repopulate
        menu.ItemsSource = null;

        // Cap dropdown height: ~12 items or 70% of screen, whichever is smaller
        double itemHeight = 32;
        double maxItems = 12;
        double screenMax = System.Windows.SystemParameters.WorkArea.Height * 0.7;
        menu.MaxHeight = Math.Min(itemHeight * maxItems, screenMax);

        // Ensure virtualizing panel for perf with long lists
        menu.ItemsPanel = s_breadcrumbItemsPanel;

        // One-time template + style setup (lazy, cached on first call)
        EnsureBreadcrumbMenuTemplate(menu);

        // Discover target directory
        string? dirPath = null;
        if (menu.PlacementTarget is FrameworkElement fe && fe.Tag is string tagPath)
            dirPath = tagPath;

        if (string.IsNullOrEmpty(dirPath))
            return;

        List<BreadcrumbItem> items;

        // ── "此电脑" → list drives ──
        if (dirPath == ThisPCSentinel)
        {
            items = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new BreadcrumbItem(d.Name, d.Name))
                .ToList();
        }
        else
        {
            // ── Try scanned tree first ──
            var dirEntry = FindEntryByPathInTree(_treemapViewModel?.ScanRoot, dirPath);
            if (dirEntry is not null)
            {
                var children = dirEntry.Children
                    .Where(c => c.IsDirectory)
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (children.Count > 0)
                {
                    items = children.Select(c => new BreadcrumbItem(c.Name, c.Path)).ToList();
                    menu.ItemsSource = items;
                    return;
                }
                // Empty scan results → fall through to filesystem
            }

            // ── Filesystem fallback ──
            try
            {
                items = System.IO.Directory.GetDirectories(dirPath)
                    .Select(d => new BreadcrumbItem(System.IO.Path.GetFileName(d), d))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                items = new List<BreadcrumbItem>();
            }
        }

        // Empty state
        if (items.Count == 0)
        {
            items.Add(new BreadcrumbItem(L.Text("NoSubfoldersText"), ""));
        }

        menu.ItemsSource = items;
    }

    /// <summary>Shared VirtualizingStackPanel template — avoids allocating per-dropdown.</summary>
    private static readonly ItemsPanelTemplate s_breadcrumbItemsPanel =
        new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));

    /// <summary>Lazy-init ItemTemplate + ItemContainerStyle for breadcrumb ContextMenus.</summary>
    private void EnsureBreadcrumbMenuTemplate(ContextMenu menu)
    {
        if (menu.ItemTemplate is not null)
            return; // already set

        // ItemTemplate: simple TextBlock bound to BreadcrumbItem.Name
        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        textFactory.SetValue(TextBlock.PaddingProperty, new Thickness(8, 6, 8, 6));
        textFactory.SetValue(TextBlock.FontSizeProperty, 13.0);
        textFactory.SetValue(TextBlock.FontFamilyProperty, (System.Windows.Media.FontFamily)FindResource("VP.FontFamily"));
        menu.ItemTemplate = new DataTemplate(typeof(BreadcrumbItem)) { VisualTree = textFactory };

        // ItemContainerStyle: hover highlight + click handler
        var style = new Style(typeof(MenuItem));

        // Hover background
        var hoverTrigger = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(MenuItem.BackgroundProperty, FindResource("VP.SurfaceHoverBrush")));
        style.Triggers.Add(hoverTrigger);

        // Disabled items (empty state with Path = "")
        var disabledTrigger = new DataTrigger
        {
            Binding = new Binding("Path"),
            Value = ""
        };
        disabledTrigger.Setters.Add(new Setter(MenuItem.IsEnabledProperty, false));
        style.Triggers.Add(disabledTrigger);

        // Click → navigate
        var clickSetter = new EventSetter(MenuItem.ClickEvent, new RoutedEventHandler((s, args) =>
        {
            if (s is MenuItem mi && mi.DataContext is BreadcrumbItem bi && !string.IsNullOrEmpty(bi.Path))
                NavigateToPathOrSelect(bi.Path);
        }));
        style.Setters.Add(clickSetter);

        menu.ItemContainerStyle = style;
    }

    /// <summary>
    /// Navigate to a path: use treemap navigation if scan data exists,
    /// otherwise update SelectedPath for breadcrumb display.
    /// </summary>
    private void NavigateToPathOrSelect(string path, bool updateSelectedPath = true)
    {
        _displayPathOverride = null;

        if (_treemapViewModel is not null)
        {
            if (TreemapView.NavigateToPath(path))
            {
                UpdateSelectedPathFromNavigation(path, updateSelectedPath);
                return;
            }

            _treemapViewModel.NavigateToExternalPath(path);
            _displayPathOverride = path;
        }

        UpdateSelectedPathFromNavigation(path, updateSelectedPath);
        RebuildBreadcrumbBar();
    }

    private void UpdateSelectedPathFromNavigation(string path, bool updateSelectedPath)
    {
        if (!updateSelectedPath || DataContext is not MainViewModel mainVm)
            return;

        if (string.Equals(mainVm.SelectedPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        _suppressSelectedPathNavigation = true;
        try
        {
            mainVm.SelectedPath = path;
        }
        finally
        {
            _suppressSelectedPathNavigation = false;
        }
    }

    private static SpaceMonger.Core.Models.FileEntry? FindEntryByPathInTree(
        SpaceMonger.Core.Models.FileEntry? root, string targetPath)
    {
        if (root is null)
            return null;
        if (string.Equals(root.Path, targetPath, StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (var child in root.Children)
        {
            if (child.IsDirectory)
            {
                var found = FindEntryByPathInTree(child, targetPath);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

}

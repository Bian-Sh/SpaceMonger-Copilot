using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class TreeViewControl : UserControl
{
    public event Action<FileEntry>? AskAiRequested;

    public TreeViewControl()
    {
        InitializeComponent();
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is TreeViewItemViewModel vm)
        {
            if (DataContext is TreeViewModel dataContext)
            {
                dataContext.SelectedItem = vm;
            }

            e.Handled = true;
        }
    }

    private void TreeViewItem_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = FindParent<TreeViewItem>((DependencyObject)e.OriginalSource);
        if (item?.DataContext is not TreeViewItemViewModel vm)
        {
            return;
        }

        item.Focus();
        item.IsSelected = true;
        e.Handled = true;

        if (DataContext is TreeViewModel dataContext)
        {
            dataContext.SelectedItem = vm;
        }

        var menu = BuildContextMenu(vm.Entry);
        // Defer opening until after the mouse event completes to avoid
        // the menu closing immediately (opening on MouseDown can be
        // followed by mouse-up processing that dismisses it).
        menu.PlacementTarget = item;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        Dispatcher.BeginInvoke(() => menu.IsOpen = true);
    }
    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
    private ContextMenu BuildContextMenu(FileEntry entry)
    {
        var contextMenu = new ContextMenu();

        var askAiItem = new MenuItem { Header = "询问 AI" };
        askAiItem.Click += (_, _) => AskAiRequested?.Invoke(entry);
        contextMenu.Items.Add(askAiItem);
        contextMenu.Items.Add(new Separator());

        var openItem = new MenuItem { Header = entry.IsDirectory ? "打开文件夹" : "打开" };
        openItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.Open(entry));
        contextMenu.Items.Add(openItem);

        var openInExplorerItem = new MenuItem { Header = L.Text("OpenInExplorerMenu") };
        openInExplorerItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.ShowInExplorer(entry));
        contextMenu.Items.Add(openInExplorerItem);

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
        contextMenu.Items.Add(new Separator());

        var propertiesItem = new MenuItem { Header = L.Text("PropertiesMenu") };
        propertiesItem.Click += async (_, _) => await RunShellActionAsync(() => FileEntryShellService.ShowProperties(entry));
        contextMenu.Items.Add(propertiesItem);

        return contextMenu;
    }

    private async Task RunShellActionAsync(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(L.Format("OpenExplorerFailedMessage", ex.Message));
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        await Dispatcher.InvokeAsync(() => MessageBox.Show(Window.GetWindow(this), message, "SpaceMonger.Next", MessageBoxButton.OK, MessageBoxImage.Error));
    }
}








using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class TreeViewControl : UserControl
{
    public TreeViewControl()
    {
        InitializeComponent();
    }

    private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item && item.DataContext is TreeViewItemViewModel vm)
        {
            var dataContext = DataContext as TreeViewModel;
            dataContext?.SelectEntry(vm.Entry);
        }
    }
}

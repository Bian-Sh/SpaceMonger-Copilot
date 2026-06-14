using System.Windows;
using System.Windows.Controls;

namespace SpaceMonger.App.Views;

public partial class SettingsPage : UserControl
{
    public event Action? BackRequested;
    public event Action? Saved;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            vm.SaveCommand.Execute(null);
        }

        Saved?.Invoke();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }
}

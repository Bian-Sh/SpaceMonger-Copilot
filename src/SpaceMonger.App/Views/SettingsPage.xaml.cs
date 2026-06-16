using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SpaceMonger.App.Views;

public partial class SettingsPage : UserControl
{
    public event Action? BackRequested;
    public event Action? SettingsChanged;
    private readonly DispatcherTimer _toastTimer;

    public SettingsPage()
    {
        InitializeComponent();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            if (DataContext is ViewModels.SettingsViewModel vm)
            {
                vm.HideSaveToast();
            }
        };
    }

    public void SavePendingChanges()
    {
        if (DataContext is ViewModels.SettingsViewModel vm)
        {
            vm.SaveWithToast();
            SettingsChanged?.Invoke();
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        SavePendingChanges();
        BackRequested?.Invoke();
    }

    private void AutoSaveOnLostFocus(object sender, RoutedEventArgs e)
    {
        SavePendingChanges();
    }

    private void AutoSaveOnChanged(object sender, RoutedEventArgs e)
    {
        SavePendingChanges();
    }
}

using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.ViewModels;

namespace SpaceMonger.App.Views;

public partial class RecommendationsPanel : UserControl
{
    public event Action? CleanupRequested;
    public event Action? CloseRequested;

    public RecommendationsPanel()
    {
        InitializeComponent();
    }

    public void SetViewModel(RecommendationsViewModel vm)
    {
        DataContext = vm;
    }

    private void CleanUpButton_Click(object sender, RoutedEventArgs e)
    {
        CleanupRequested?.Invoke();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke();
    }
}

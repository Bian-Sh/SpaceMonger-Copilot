using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class RecommendationsPanel : UserControl
{
    public event Action? CleanupRequested;
    public event Action? CloseRequested;
    public event Action<CleanupRecommendation>? RecommendationActivated;

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

    private void RecommendationsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((FrameworkElement)e.OriginalSource).DataContext is CleanupRecommendation recommendation)
        {
            RecommendationActivated?.Invoke(recommendation);
        }
    }

    private void RecommendationPath_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CleanupRecommendation recommendation)
        {
            RecommendationActivated?.Invoke(recommendation);
        }
    }
}

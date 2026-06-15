using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.ViewModels;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class RecommendationsPanel : UserControl
{
    public event Action? AnalyzeRequested;
    public event Action? CleanupRequested;
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

    private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecommendationsViewModel { IsAnalyzing: true })
        {
            return;
        }

        AnalyzeRequested?.Invoke();
    }

    private void RecommendationItem_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        e.Handled = true;
    }

    private void RecommendationSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is RecommendationsViewModel vm)
        {
            vm.UpdateTotals();
        }
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

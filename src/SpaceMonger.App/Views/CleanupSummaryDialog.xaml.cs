using System.Windows;
using SpaceMonger.App.Converters;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;

namespace SpaceMonger.App.Views;

public partial class CleanupSummaryDialog : Window
{
    public CleanupSummaryDialog(List<CleanupAction> actions)
    {
        InitializeComponent();
        PopulateFromActions(actions);
    }

    private void PopulateFromActions(List<CleanupAction> actions)
    {
        var successActions = actions.Where(a => a.Result == CleanupResult.Success).ToList();
        var skippedActions = actions.Where(a => a.Result == CleanupResult.Skipped || a.Result == CleanupResult.Failed).ToList();
        var alreadyRemovedActions = actions.Where(a => a.Result == CleanupResult.AlreadyRemoved).ToList();

        long totalFreed = successActions.Sum(a => a.ActualSizeFreed);

        // Success summary
        SuccessSummary.Text = $"{successActions.Count} items removed ({FileSizeConverter.FormatSize(totalFreed)} freed)";

        // Already removed
        if (alreadyRemovedActions.Count > 0)
        {
            AlreadyRemovedText.Text = $"{alreadyRemovedActions.Count} items were already removed";
            AlreadyRemovedText.Visibility = Visibility.Visible;
        }
        else
        {
            AlreadyRemovedText.Visibility = Visibility.Collapsed;
        }

        // Skipped items
        if (skippedActions.Count > 0)
        {
            SkippedGroup.Header = $"Skipped Items ({skippedActions.Count} items skipped)";
            SkippedItemsList.ItemsSource = skippedActions.Select(a => new
            {
                Path = a.Recommendation.TargetPath,
                Reason = a.FailureReason ?? "Unknown"
            }).ToList();
            SkippedGroup.Visibility = Visibility.Visible;
        }
        else
        {
            SkippedGroup.Visibility = Visibility.Collapsed;
        }

        // Total space recovered
        TotalRecovered.Text = $"Total space recovered: {FileSizeConverter.FormatSize(totalFreed)}";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

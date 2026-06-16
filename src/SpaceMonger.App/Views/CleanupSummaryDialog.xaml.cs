using System.Windows;
using SpaceMonger.App.Converters;
using SpaceMonger.App.Localization;
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
        SuccessSummary.Text = L.Format("CleanupSummarySuccess", successActions.Count, FileSizeConverter.FormatSize(totalFreed));

        // Already removed
        if (alreadyRemovedActions.Count > 0)
        {
            AlreadyRemovedText.Text = L.Format("CleanupSummaryAlreadyRemoved", alreadyRemovedActions.Count);
            AlreadyRemovedText.Visibility = Visibility.Visible;
        }
        else
        {
            AlreadyRemovedText.Visibility = Visibility.Collapsed;
        }

        // Skipped items
        if (skippedActions.Count > 0)
        {
            SkippedGroupHeader.Text = L.Format("SkippedItemsWithCount", skippedActions.Count);
            SkippedItemsList.ItemsSource = skippedActions.Select(a => new
            {
                Path = a.Recommendation.TargetPath,
                Reason = a.FailureReason ?? L.Text("PropertiesUnknown")
            }).ToList();
            SkippedGroup.Visibility = Visibility.Visible;
        }
        else
        {
            SkippedGroup.Visibility = Visibility.Collapsed;
        }

        // Total space recovered
        TotalRecovered.Text = L.Format("TotalSpaceRecovered", FileSizeConverter.FormatSize(totalFreed));
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}

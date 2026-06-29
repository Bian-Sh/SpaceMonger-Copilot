using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Converters;
using SpaceMonger.App.Controls;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.App;

public partial class MainWindow
{
    private async void OnCleanupRequested()
    {
        if (_recommendationsViewModel is null)
            return;

        var accepted = _recommendationsViewModel.Recommendations
            .Where(r => r.IsAccepted)
            .ToList();

        if (accepted.Count == 0)
        {
            await ShowAppMessageAsync(
                L.Text("NoItemsSelectedMessage"),
                L.Text("NoItemsSelectedTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var mainVm = DataContext as ViewModels.MainViewModel;
        if (mainVm?.CurrentSession is null)
            return;

        var totalSize = accepted.Sum(r => r.Size);
        var deletionMode = _settingsViewModel?.SelectedDeletionMode ?? DeletionMode.MoveToRecycleBin;
        var confirmResult = await ShowAppModalAsync(
            L.Text("ConfirmCleanupTitle"),
            L.Format("ConfirmCleanupMessage", accepted.Count, FileSizeConverter.FormatSize(totalSize), L.Text(GetDeletionModeTextKey(deletionMode))),
            ModalMessageType.Warning,
            ModalButtonFlags.Positive | ModalButtonFlags.Negative);

        if (confirmResult != ModalResult.Positive)
            return;

        var cleanupService = App.Services!.GetRequiredService<ICleanupService>();
        var progress = new Progress<CleanupProgress>(p =>
        {
            mainVm.ScanProgressText = L.Format("CleaningStatus", p.CompletedCount + 1, p.TotalCount, p.CurrentItemPath);
        });

        List<CleanupAction> results;
        try
        {
            results = await cleanupService.ExecuteCleanupAsync(
                accepted, deletionMode, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await ShowAppMessageAsync(
                L.Format("CleanupFailedMessage", ex.Message),
                L.Text("CleanupErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var summaryDialog = new CleanupSummaryDialog(results);
        summaryDialog.CloseRequested += result => ModalHost.CloseCurrent(result);
        await ShowAppContentAsync(summaryDialog, 520, 560);

        _treemapViewModel?.ApplyCleanupResults(results, mainVm.CurrentSession);

        mainVm.UpdateStatusBar(mainVm.CurrentSession);
        var successCount = results.Count(a =>
            a.Result is CleanupResult.Success or CleanupResult.AlreadyRemoved);
        var freedBytes = results.Where(a => a.Result == CleanupResult.Success)
            .Sum(a => a.ActualSizeFreed);
        mainVm.ScanProgressText = L.Format("CleanupCompleteStatus", successCount, FileSizeConverter.FormatSize(freedBytes));

        var completedPaths = results
            .Where(a => a.Result is CleanupResult.Success or CleanupResult.AlreadyRemoved)
            .Select(a => a.Recommendation.TargetPath)
            .ToHashSet();

        var remaining = _recommendationsViewModel.Recommendations
            .Where(r => !completedPaths.Contains(r.TargetPath))
            .ToList();

        _recommendationsViewModel.Recommendations = new System.Collections.ObjectModel.ObservableCollection<CleanupRecommendation>(remaining);
        _recommendationsViewModel.RefreshAfterCleanup();
    }

    private static string GetDeletionModeTextKey(DeletionMode deletionMode)
    {
        return deletionMode == DeletionMode.PermanentDelete ? "PermanentlyDeleteOption" : "MoveToRecycleBinOption";
    }
}

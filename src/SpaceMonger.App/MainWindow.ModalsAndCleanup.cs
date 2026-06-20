using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Helpers;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.App;

public partial class MainWindow
{
    private Task<MessageBoxResult> ShowAppMessageAsync(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        return ModalHost.ShowMessageAsync(message, title, buttons, image);
    }

    private Task<bool?> ShowAppContentAsync(FrameworkElement content, double maxWidth = 620, double maxHeight = 700)
    {
        return ModalHost.ShowContentAsync(content, maxWidth, maxHeight);
    }

    private void OpenSettingsDialog() => ShowSettingsPage();

    private bool _isChatCollapsed;

    private void ToggleChatPanel()
    {
        _isChatCollapsed = !_isChatCollapsed;

        if (_isChatCollapsed)
        {
            ChatPanelColumn.Width = new GridLength(0);
            ChatPanelColumn.MinWidth = 0;
            ChatPanel.Visibility = Visibility.Collapsed;
            ChatSplitter.Visibility = Visibility.Collapsed;
        }
        else
        {
            ChatPanelColumn.Width = new GridLength(360);
            ChatPanelColumn.MinWidth = 260;
            ChatPanel.Visibility = Visibility.Visible;
            ChatSplitter.Visibility = Visibility.Visible;
        }

        TitleBar.UpdateCollapseIcon(!_isChatCollapsed);
    }

    private void ShowSettingsPage()
    {
        if (_settingsViewModel is null)
            return;

        _settingsViewModel.LoadSettings();
        SettingsPage.Visibility = Visibility.Visible;
    }

    private void HideSettingsPage()
    {
        SettingsPage.Visibility = Visibility.Collapsed;
    }

    private void OnSettingsChanged()
    {
        SpaceMonger.App.Localization.L.SetLanguage(_settingsViewModel?.Language);
        _chatViewModel?.RefreshApiKeyStatus();
    }

    private async void OnCleanupRequested()
    {
        if (_recommendationsViewModel is null)
            return;

        var mainVm = DataContext as MainViewModel;
        if (mainVm?.CurrentSession is null)
            return;

        // 1. Get accepted recommendations
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

        // 2. Show confirmation dialog
        var totalSize = accepted.Sum(r => r.Size);
        var confirmDialog = new CleanupConfirmDialog();
        confirmDialog.SetCleanupInfo(accepted.Count, totalSize);
        confirmDialog.CloseRequested += result => ModalHost.CloseCurrent(result);

        if (await ShowAppContentAsync(confirmDialog, 460, 360) != true)
            return;

        var deletionMode = confirmDialog.SelectedMode;

        // 3. Execute cleanup
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

        // 4. Show summary dialog
        var summaryDialog = new CleanupSummaryDialog(results);
        summaryDialog.CloseRequested += result => ModalHost.CloseCurrent(result);
        await ShowAppContentAsync(summaryDialog, 520, 560);

        // 5. Update treemap with cleanup results
        _treemapViewModel?.ApplyCleanupResults(results, mainVm.CurrentSession);

        // 6. Update the status bar
        mainVm.UpdateStatusBar(mainVm.CurrentSession);
        var successCount = results.Count(a =>
            a.Result is Core.Enums.CleanupResult.Success or Core.Enums.CleanupResult.AlreadyRemoved);
        var freedBytes = results.Where(a => a.Result == Core.Enums.CleanupResult.Success)
            .Sum(a => a.ActualSizeFreed);
        mainVm.ScanProgressText = L.Format("CleanupCompleteStatus", successCount, FileSizeConverter.FormatSize(freedBytes));

        // 7. Remove completed items from recommendations
        var completedPaths = results
            .Where(a => a.Result is Core.Enums.CleanupResult.Success or Core.Enums.CleanupResult.AlreadyRemoved)
            .Select(a => a.Recommendation.TargetPath)
            .ToHashSet();

        var remaining = _recommendationsViewModel.Recommendations
            .Where(r => !completedPaths.Contains(r.TargetPath))
            .ToList();

        _recommendationsViewModel.Recommendations = new System.Collections.ObjectModel.ObservableCollection<CleanupRecommendation>(remaining);
        _recommendationsViewModel.RefreshAfterCleanup();
    }
}

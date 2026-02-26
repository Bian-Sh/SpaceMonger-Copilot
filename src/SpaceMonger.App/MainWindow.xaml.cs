using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SpaceMonger.App.Converters;
using SpaceMonger.App.ViewModels;
using SpaceMonger.App.Views;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Cleanup;

namespace SpaceMonger.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private RecommendationsViewModel? _recommendationsViewModel;
    private TreemapViewModel? _treemapViewModel;
    private SettingsViewModel? _settingsViewModel;
    private ChatViewModel? _chatViewModel;

    public MainWindow()
    {
        InitializeComponent();
        ChatToggleButton.Checked += ChatToggleButton_Checked;
        ChatToggleButton.Unchecked += ChatToggleButton_Unchecked;
    }

    public void SetViewModels(RecommendationsViewModel recsVm, SettingsViewModel settingsVm)
    {
        _recommendationsViewModel = recsVm;
        _settingsViewModel = settingsVm;
        RecommendationsPanel.SetViewModel(recsVm);
        RecommendationsPanel.CleanupRequested += OnCleanupRequested;
        RecommendationsPanel.CloseRequested += () => RecommendationsPanel.Visibility = Visibility.Collapsed;
    }

    public void SetTreemapViewModel(TreemapViewModel treemapVm)
    {
        _treemapViewModel = treemapVm;
    }

    public void SetChatViewModel(ChatViewModel chatVm)
    {
        _chatViewModel = chatVm;
        ChatPanel.SetViewModel(chatVm);

        ChatPanel.OpenSettingsRequested += () => OpenSettingsDialog();

        // Track treemap navigation changes to update chat context
        if (_treemapViewModel is not null)
        {
            _treemapViewModel.PropertyChanged += TreemapViewModel_PropertyChanged;
        }
    }

    private void TreemapViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_chatViewModel is null || _treemapViewModel is null)
            return;

        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.CurrentRoot):
                if (_treemapViewModel.CurrentRoot is not null)
                {
                    _chatViewModel.UpdateViewRoot(_treemapViewModel.CurrentRoot);
                }
                break;

            case nameof(TreemapViewModel.SelectedNode):
                _chatViewModel.LinkedEntry = _treemapViewModel.SelectedNode?.Entry;
                break;
        }
    }

    private void ChatToggleButton_Checked(object sender, RoutedEventArgs e)
    {
        ChatPanelColumn.Width = new GridLength(350);
        ChatPanel.Visibility = Visibility.Visible;
    }

    private void ChatToggleButton_Unchecked(object sender, RoutedEventArgs e)
    {
        ChatPanelColumn.Width = new GridLength(0);
        ChatPanel.Visibility = Visibility.Collapsed;
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recommendationsViewModel is null || _settingsViewModel is null)
            return;

        var mainVm = DataContext as MainViewModel;
        if (mainVm?.CurrentSession is null)
        {
            MessageBox.Show(
                "Please complete a scan first before analyzing.",
                "No Scan Data",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Retrieve the API key from settings (checks actual saved key, not just validation flag)
        var settingsService = App.Services!.GetRequiredService<SpaceMonger.Core.Services.Settings.ISettingsService>();
        var apiKey = settingsService.GetApiKey(settingsService.LoadSettings());

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var result = MessageBox.Show(
                "An Anthropic API key is required for AI analysis. Would you like to open Settings to configure it?",
                "API Key Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                OpenSettingsDialog();
            }

            // Re-check after settings dialog
            apiKey = settingsService.GetApiKey(settingsService.LoadSettings());
            if (string.IsNullOrWhiteSpace(apiKey))
                return;
        }

        // FR-029: Warn if re-running analysis will replace accepted recommendations
        if (_recommendationsViewModel.HasAcceptedRecommendations)
        {
            var confirmResult = MessageBox.Show(
                "Re-running analysis will replace all current recommendations. Accepted items will be lost. Continue?",
                "Confirm Re-Analysis",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;
        }

        // Show the panel immediately so the user sees the loading indicator
        RecommendationsPanel.Visibility = Visibility.Visible;

        // If the user has drilled into a folder, scope the analysis to that subtree.
        // At the top level (CurrentRoot == scan root), analyze the whole drive.
        FileEntry? focusEntry = null;
        if (_treemapViewModel?.CurrentRoot is not null
            && _treemapViewModel.CurrentRoot != mainVm.CurrentSession.RootEntry)
        {
            focusEntry = _treemapViewModel.CurrentRoot;
        }

        _recommendationsViewModel.SetContext(mainVm.CurrentSession, apiKey, focusEntry);

        var scopeLabel = focusEntry is not null
            ? $"Analyzing {focusEntry.Name}..."
            : "Analyzing scan results...";
        mainVm.ScanProgressText = scopeLabel;
        AnalyzeButton.IsEnabled = false;

        await _recommendationsViewModel.AnalyzeCommand.ExecuteAsync(null);

        AnalyzeButton.IsEnabled = true;

        if (_recommendationsViewModel.AnalysisError is not null)
        {
            mainVm.ScanProgressText = $"Analysis failed: {_recommendationsViewModel.AnalysisError}";
        }
        else
        {
            var count = _recommendationsViewModel.Recommendations.Count;
            mainVm.ScanProgressText = $"Analysis complete: {count} recommendation{(count == 1 ? "" : "s")} found.";
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _treemapViewModel?.NavigateUp();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            if (DataContext is MainViewModel vm && vm.ScanCommand.CanExecute(null))
                vm.ScanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            AnalyzeButton_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsDialog();
    }

    private void OpenSettingsDialog()
    {
        if (_settingsViewModel is null)
            return;

        // Reload settings before showing the dialog
        _settingsViewModel.LoadSettings();

        var dialog = new SettingsDialog
        {
            Owner = this,
            DataContext = _settingsViewModel
        };

        dialog.ShowDialog();

        // Refresh chat panel API key status after settings dialog closes
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
            MessageBox.Show(
                "No recommendations are selected. Please check the items you want to clean up.",
                "No Items Selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // 2. Show confirmation dialog
        var totalSize = accepted.Sum(r => r.Size);
        var confirmDialog = new CleanupConfirmDialog { Owner = this };
        confirmDialog.SetCleanupInfo(accepted.Count, totalSize);

        if (confirmDialog.ShowDialog() != true)
            return;

        var deletionMode = confirmDialog.SelectedMode;

        // 3. Execute cleanup
        var cleanupService = App.Services!.GetRequiredService<ICleanupService>();
        var progress = new Progress<CleanupProgress>(p =>
        {
            mainVm.ScanProgressText = $"Cleaning: {p.CompletedCount + 1}/{p.TotalCount} — {p.CurrentItemPath}";
        });

        List<CleanupAction> results;
        try
        {
            results = await cleanupService.ExecuteCleanupAsync(
                accepted, deletionMode, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Cleanup failed: {ex.Message}",
                "Cleanup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // 4. Show summary dialog
        var summaryDialog = new CleanupSummaryDialog(results) { Owner = this };
        summaryDialog.ShowDialog();

        // 5. Update treemap with cleanup results
        _treemapViewModel?.ApplyCleanupResults(results, mainVm.CurrentSession);

        // 6. Update the status bar
        mainVm.UpdateStatusBar(mainVm.CurrentSession);
        var successCount = results.Count(a =>
            a.Result is Core.Enums.CleanupResult.Success or Core.Enums.CleanupResult.AlreadyRemoved);
        var freedBytes = results.Where(a => a.Result == Core.Enums.CleanupResult.Success)
            .Sum(a => a.ActualSizeFreed);
        mainVm.ScanProgressText = $"Cleanup complete: {successCount} items removed ({FileSizeConverter.FormatSize(freedBytes)} freed)";

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

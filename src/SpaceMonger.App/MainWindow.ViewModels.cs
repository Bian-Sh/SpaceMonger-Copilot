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
    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel mainVm)
            return;

        if (e.PropertyName is nameof(MainViewModel.IsScanning) or nameof(MainViewModel.ScanProgressText))
        {
            TreemapView.SetScanningState(mainVm.IsScanning, mainVm.ScanProgressText);
        }
        else if (e.PropertyName is nameof(MainViewModel.SelectedPath))
        {
            if (string.IsNullOrWhiteSpace(mainVm.SelectedPath))
                return;

            if (_suppressSelectedPathNavigation)
            {
                RebuildBreadcrumbBar();
                return;
            }

            NavigateToPathOrSelect(mainVm.SelectedPath, updateSelectedPath: false);
        }
    }

    public void SetViewModels(RecommendationsViewModel recsVm, SettingsViewModel settingsVm)
    {
        _recommendationsViewModel = recsVm;
        _settingsViewModel = settingsVm;
        if (DataContext is MainViewModel mainVm)
            mainVm.RecommendationsVM = recsVm;
        SettingsPage.DataContext = settingsVm;
        SettingsPage.BackRequested += HideSettingsPage;
        settingsVm.SettingsChanged += OnSettingsChanged;
        TitleBar.SettingsRequested += (_, _) => ShowSettingsPage();
        TitleBar.CollapseChatRequested += (_, _) => ToggleChatPanel();
        RecommendationsPanel.SetViewModel(recsVm);
        RecommendationsPanel.AnalyzeRequested += OnAnalyzeRequested;
        RecommendationsPanel.CleanupRequested += OnCleanupRequested;
        RecommendationsPanel.RecommendationActivated += OnRecommendationActivated;
        TreemapView.ShowMessageAsync = ShowAppMessageAsync;
        TreemapView.ShowContentAsync = ShowAppContentAsync;
        TreemapView.CloseContentModal = ModalHost.CloseCurrent;
    }

    public void SetUpdateViewModel(UpdateViewModel updateVm)
    {
        _updateViewModel = updateVm;
        AboutPage.DataContext = updateVm;

        if (DataContext is MainViewModel mainVm)
            mainVm.UpdateVM = updateVm;
    }

    public void SetTreemapViewModel(TreemapViewModel treemapVm)
    {
        _treemapViewModel = treemapVm;
        _treemapViewModel.PropertyChanged += TreemapViewModel_NavigationChanged;
    }

    private void TreemapViewModel_NavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_treemapViewModel is null)
            return;

        switch (e.PropertyName)
        {
            case nameof(TreemapViewModel.CanGoBack):
                BackButton.IsEnabled = _treemapViewModel.CanGoBack;
                break;
            case nameof(TreemapViewModel.CanGoForward):
                ForwardButton.IsEnabled = _treemapViewModel.CanGoForward;
                break;
            case nameof(TreemapViewModel.CanGoUp):
                UpButton.IsEnabled = _treemapViewModel.CanGoUp;
                break;
            case nameof(TreemapViewModel.CurrentRoot):
                if (_treemapViewModel.CurrentRoot is not null)
                {
                    _displayPathOverride = null;
                    UpdateSelectedPathFromNavigation(_treemapViewModel.CurrentRoot.Path, updateSelectedPath: true);
                    RebuildBreadcrumbBar();
                }
                break;
        }
    }

    public void SetChatViewModel(ChatViewModel chatVm)
    {
        _chatViewModel = chatVm;
        ChatPanel.SetViewModel(chatVm);

        ChatPanel.OpenSettingsRequested += () => OpenSettingsDialog();
        TreemapView.AskAiRequested += AskAiAboutEntry;
        TreeViewControl.AskAiRequested += AskAiAboutEntry;

        // Track treemap navigation changes to update chat context
        if (_treemapViewModel is not null)
        {
            _treemapViewModel.PropertyChanged += TreemapViewModel_PropertyChanged;
        }
    }

    public void SetTreeViewModel(TreeViewModel treeVm)
    {
        TreeViewControl.DataContext = treeVm;
    }


    private void AskAiAboutEntry(FileEntry entry)
    {
        if (_chatViewModel is null)
            return;

        _chatViewModel.LinkedEntry = entry;
        _chatViewModel.InputText = entry.IsDirectory
            ? $"深入分析这个目录的空间占用：{entry.Path}"
            : $"分析这个文件的空间占用和清理风险：{entry.Path}";
        ChatPanel.FocusInput();
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

    private void OnRecommendationActivated(CleanupRecommendation recommendation)
    {
        if (_treemapViewModel is null)
            return;

        if (recommendation.Entry is not null)
        {
            _treemapViewModel.NavigateToEntry(recommendation.Entry);
        }
    }

    // ─── Tab switching (replaces TabControl) ────────────────────────

    private void ShowRecommendationsPanel()
    {
        RecommendationsTabBtn.IsChecked = true;
        RecommendationsPanel.Visibility = Visibility.Visible;
        ConsoleFrame.Visibility = Visibility.Collapsed;
        ConsoleFilterButton.Visibility = Visibility.Collapsed;
    }

    private void ShowConsolePanel()
    {
        ConsoleTabBtn.IsChecked = true;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        ConsoleFrame.Visibility = Visibility.Visible;
        ConsoleFilterButton.Visibility = Visibility.Visible;
    }

    private void RecommendationsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (RecommendationsPanel == null || ConsoleTextBox == null || ConsoleFilterButton == null)
            return;
        RecommendationsPanel.Visibility = Visibility.Visible;
        ConsoleFrame.Visibility = Visibility.Collapsed;
        ConsoleFilterButton.Visibility = Visibility.Collapsed;
    }

    private void ConsoleTab_Checked(object sender, RoutedEventArgs e)
    {
        if (RecommendationsPanel == null || ConsoleTextBox == null || ConsoleFilterButton == null)
            return;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        ConsoleFrame.Visibility = Visibility.Visible;
        ConsoleFilterButton.Visibility = Visibility.Visible;
    }

    private void EnsureBottomPanelVisible()
    {
        if (RecommendationsPanelRow.ActualHeight <= 0)
        {
            RecommendationsPanelRow.Height = new GridLength(DefaultRecommendationsHeight);
        }
        RecommendationsSplitter.Visibility = Visibility.Visible;
    }

    // ─── View Mode Tabs (Treemap / TreeView) ────────────────────────

    private void TreemapTab_Checked(object sender, RoutedEventArgs e)
    {
        if (TreemapView == null || TreeViewControl == null || AboutPage == null)
            return;
        TreemapView.Visibility = Visibility.Visible;
        TreeViewControl.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Collapsed;
    }

    private void TreeViewTab_Checked(object sender, RoutedEventArgs e)
    {
        if (TreemapView == null || TreeViewControl == null || AboutPage == null)
            return;
        TreemapView.Visibility = Visibility.Collapsed;
        TreeViewControl.Visibility = Visibility.Visible;
        AboutPage.Visibility = Visibility.Collapsed;

        // Sync TreeView with current Treemap data
        SyncTreeViewWithTreemap();
    }

    private void AboutTab_Checked(object sender, RoutedEventArgs e)
    {
        if (TreemapView == null || TreeViewControl == null || AboutPage == null)
            return;
        TreemapView.Visibility = Visibility.Collapsed;
        TreeViewControl.Visibility = Visibility.Collapsed;
        AboutPage.Visibility = Visibility.Visible;
    }

    private void SyncTreeViewWithTreemap()
    {
        if (_treemapViewModel?.ScanRoot is null)
            return;

        var treeViewModel = (TreeViewModel)TreeViewControl.DataContext;
        var session = _treemapViewModel.ScanRoot is not null
            ? GetScanSession()
            : null;
        treeViewModel.SetRoot(_treemapViewModel.ScanRoot, session);
    }

    private ScanSession? GetScanSession()
    {
        // Try to get the session from MainViewModel
        if (DataContext is MainViewModel mainVm)
        {
            return mainVm.CurrentSession;
        }
        return null;
    }

    // ─── Console ────────────────────────────────────────────────────

}


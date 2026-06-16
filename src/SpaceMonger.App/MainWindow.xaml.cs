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

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const double DefaultRecommendationsHeight = 260;
    private const int WM_GETMINMAXINFO = 0x0024;

    private readonly ObservableCollection<ConsoleLogEntry> _consoleEntries = new();
    private readonly StringBuilder _consoleLog = new();
    private readonly string _consoleLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SpaceMonger.Next",
        "logs",
        $"console-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    private ConsoleLogLevel _visibleConsoleLevels = ConsoleLogLevel.Info | ConsoleLogLevel.Warning | ConsoleLogLevel.Error;
    private RecommendationsViewModel? _recommendationsViewModel;
    private TreemapViewModel? _treemapViewModel;
    private SettingsViewModel? _settingsViewModel;
    private ChatViewModel? _chatViewModel;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(Path.GetDirectoryName(_consoleLogPath)!);
        AppendConsoleLine("Console log file: " + _consoleLogPath, ConsoleLogLevel.Info);
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Enable DWM backdrop and dark mode
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        AcrylicHelper.EnableDarkMode(hwnd);
        AcrylicHelper.EnableMica(hwnd);

        // Hook WndProc for WM_GETMINMAXINFO (maximize bounds)
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);

        if (DataContext is MainViewModel mainVm)
        {
            mainVm.PropertyChanged += MainViewModel_PropertyChanged;
            TreemapView.SetScanningState(mainVm.IsScanning, mainVm.ScanProgressText);
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        TitleBar.UpdateMaximizeIcon(WindowState == WindowState.Maximized);

        // Adjust margin when maximized to avoid covering taskbar
        if (WindowState == WindowState.Maximized)
        {
            RootGrid.Margin = new Thickness(6);
        }
        else
        {
            RootGrid.Margin = new Thickness(0);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            // Constrain maximized size to work area (exclude taskbar)
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var work = monitorInfo.rcWork;
                mmi.ptMaxPosition.x = work.Left;
                mmi.ptMaxPosition.y = work.Top;
                mmi.ptMaxSize.x = work.Right - work.Left;
                mmi.ptMaxSize.y = work.Bottom - work.Top;
                Marshal.StructureToPtr(mmi, lParam, true);
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMaxTrackSize;
        public POINT ptMinTrackSize;
    }

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
            // Update breadcrumb when user picks a folder via Browse (before scan)
            if (_treemapViewModel?.CurrentRoot is null && !string.IsNullOrEmpty(mainVm.SelectedPath))
                RebuildBreadcrumbBar();
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
        SettingsPage.SettingsChanged += OnSettingsChanged;
        TitleBar.SettingsRequested += (_, _) => ShowSettingsPage();
        TitleBar.CollapseChatRequested += (_, _) => ToggleChatPanel();
        RecommendationsPanel.SetViewModel(recsVm);
        RecommendationsPanel.AnalyzeRequested += OnAnalyzeRequested;
        RecommendationsPanel.CleanupRequested += OnCleanupRequested;
        RecommendationsPanel.RecommendationActivated += OnRecommendationActivated;
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
            case nameof(TreemapViewModel.CurrentRoot):
                if (_treemapViewModel.CurrentRoot is not null)
                    RebuildBreadcrumbBar();
                break;
        }
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
        ConsoleTextBox.Visibility = Visibility.Collapsed;
        ConsoleFilterButton.Visibility = Visibility.Collapsed;
    }

    private void ShowConsolePanel()
    {
        ConsoleTabBtn.IsChecked = true;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        ConsoleTextBox.Visibility = Visibility.Visible;
        ConsoleFilterButton.Visibility = Visibility.Visible;
    }

    private void RecommendationsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (RecommendationsPanel == null || ConsoleTextBox == null || ConsoleFilterButton == null)
            return;
        RecommendationsPanel.Visibility = Visibility.Visible;
        ConsoleTextBox.Visibility = Visibility.Collapsed;
        ConsoleFilterButton.Visibility = Visibility.Collapsed;
    }

    private void ConsoleTab_Checked(object sender, RoutedEventArgs e)
    {
        if (RecommendationsPanel == null || ConsoleTextBox == null || ConsoleFilterButton == null)
            return;
        RecommendationsPanel.Visibility = Visibility.Collapsed;
        ConsoleTextBox.Visibility = Visibility.Visible;
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

    // ─── Console ────────────────────────────────────────────────────

    private void AppendConsoleLine(string message, ConsoleLogLevel level = ConsoleLogLevel.Info)
    {
        var entry = new ConsoleLogEntry(DateTime.Now, level, message);
        _consoleEntries.Add(entry);
        File.AppendAllText(_consoleLogPath, entry.ToLogLine() + Environment.NewLine);
        RefreshConsoleText();
    }

    private void RefreshConsoleText()
    {
        _consoleLog.Clear();
        foreach (var entry in _consoleEntries.Where(e => _visibleConsoleLevels.HasFlag(e.Level)))
        {
            _consoleLog.AppendLine(entry.ToLogLine());
        }

        ConsoleTextBox.Text = _consoleLog.ToString();
        ConsoleTextBox.ScrollToEnd();
    }

    private void ConsoleLogLevel_Click(object sender, RoutedEventArgs e)
    {
        _visibleConsoleLevels = ConsoleLogLevel.None;

        if (ConsoleLevelVerboseMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Verbose;
        if (ConsoleLevelInfoMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Info;
        if (ConsoleLevelWarningMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Warning;
        if (ConsoleLevelErrorMenuItem.IsChecked)
            _visibleConsoleLevels |= ConsoleLogLevel.Error;

        RefreshConsoleText();
    }

    private void ConsoleFilterButton_Click(object sender, RoutedEventArgs e)
    {
        ConsoleFilterButton.ContextMenu.PlacementTarget = ConsoleFilterButton;
        ConsoleFilterButton.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void StatusConsoleLink_Click(object sender, RoutedEventArgs e)
    {
        EnsureBottomPanelVisible();
        ShowConsolePanel();
    }

    private void AppendAnalysisDiagnostics(AnalysisDiagnostics? diagnostics)
    {
        if (diagnostics is null)
        {
            AppendConsoleLine("DIAG: no diagnostics were produced; request likely failed before response parsing.");
            return;
        }

        AppendConsoleLine("DIAG: target=" + diagnostics.TargetPath);
        AppendConsoleLine("DIAG: scope=" + diagnostics.ScopePath + " focused=" + diagnostics.IsFocusedScope);
        AppendConsoleLine("DIAG: metadata_chars=" + diagnostics.MetadataLength + " response_chars=" + diagnostics.ResponseLength + " extracted_json_chars=" + diagnostics.ExtractedJsonLength);
        AppendConsoleLine("DIAG: parsed_recs=" + diagnostics.ParsedRecommendationCount + " protected_filtered=" + diagnostics.ProtectedFilteredCount + " missing_entry=" + diagnostics.MissingEntryCount + " malformed=" + diagnostics.MalformedRecommendationCount + " missing_fields=" + diagnostics.MissingFieldRecommendationCount);

        if (!string.IsNullOrWhiteSpace(diagnostics.ParseError))
        {
            AppendConsoleLine("DIAG: parse_error=" + diagnostics.ParseError, ConsoleLogLevel.Warning);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.RawResponsePath))
        {
            AppendConsoleLine("DIAG: raw_response_path=" + diagnostics.RawResponsePath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ResponseEnvelopePath))
        {
            AppendConsoleLine("DIAG: response_envelope_path=" + diagnostics.ResponseEnvelopePath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.StopReason))
        {
            AppendConsoleLine("DIAG: stop_reason=" + diagnostics.StopReason);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ThinkingPath))
        {
            AppendConsoleLine("DIAG: thinking_path=" + diagnostics.ThinkingPath);
        }

        if (!string.IsNullOrWhiteSpace(diagnostics.ExtractedJsonPreview))
        {
            AppendConsoleLine("DIAG: extracted_json_preview=" + diagnostics.ExtractedJsonPreview);
        }
        else if (!string.IsNullOrWhiteSpace(diagnostics.ResponsePreview))
        {
            AppendConsoleLine("DIAG: response_preview=" + diagnostics.ResponsePreview);
        }
    }

    private async void OnAnalyzeRequested()
    {
        if (_recommendationsViewModel is null || _settingsViewModel is null || _recommendationsViewModel.IsAnalyzing)
            return;

        var mainVm = DataContext as MainViewModel;
        if (mainVm?.CurrentSession is null)
        {
            MessageBox.Show(
                L.Text("AnalyzeNoScanMessage"),
                L.Text("AnalyzeNoScanTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        // Retrieve the API key from settings (checks actual saved key, not just validation flag)
        var settingsService = App.Services!.GetRequiredService<SpaceMonger.Core.Services.Settings.ISettingsService>();
        var loadedSettings = settingsService.LoadSettings();
        var apiKey = settingsService.GetApiKey(loadedSettings);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var result = MessageBox.Show(
                L.Text("ApiKeyRequiredMessage"),
                L.Text("ApiKeyRequiredTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                OpenSettingsDialog();
            }

            // Re-check after settings dialog
            loadedSettings = settingsService.LoadSettings();
            apiKey = settingsService.GetApiKey(loadedSettings);
            if (string.IsNullOrWhiteSpace(apiKey))
                return;
        }

        // FR-029: Warn if re-running analysis will replace accepted recommendations
        if (_recommendationsViewModel.HasAcceptedRecommendations)
        {
            var confirmResult = MessageBox.Show(
                L.Text("ConfirmReanalysisMessage"),
                L.Text("ConfirmReanalysisTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
                return;
        }

        // Show the panel immediately so the user sees the loading indicator
        EnsureBottomPanelVisible();
        ShowRecommendationsPanel();
        DebugBreakpoints.Hit("analyze-click");

        // If the user has drilled into a folder, scope the analysis to that subtree.
        // At the top level (CurrentRoot == scan root), analyze the whole drive.
        FileEntry? focusEntry = null;
        if (_treemapViewModel?.CurrentRoot is not null
            && _treemapViewModel.CurrentRoot != mainVm.CurrentSession.RootEntry)
        {
            focusEntry = _treemapViewModel.CurrentRoot;
        }

        _recommendationsViewModel.SetContext(
            mainVm.CurrentSession,
            apiKey,
            loadedSettings.AnthropicBaseUrl,
            loadedSettings.AnalysisModelName,
            loadedSettings.EnableThinking,
            loadedSettings.Language,
            focusEntry);
        DebugBreakpoints.Hit("analyze-context-ready");

        var scopeLabel = focusEntry is not null
            ? L.Format("AnalyzingFolderStatus", focusEntry.Name)
            : L.Text("AnalyzingScanResultsStatus");
        mainVm.ScanProgressText = scopeLabel;
        AppendConsoleLine(scopeLabel);
        AppendConsoleLine(focusEntry is not null
            ? $"Analysis scope: {focusEntry.Path}"
            : $"Analysis scope: {mainVm.CurrentSession.TargetPath}");
        await _recommendationsViewModel.AnalyzeCommand.ExecuteAsync(null);
        DebugBreakpoints.Hit("analyze-command-returned");

        if (_recommendationsViewModel.AnalysisError is not null)
        {
            mainVm.ScanProgressText = L.Format("AnalysisFailedStatus", _recommendationsViewModel.AnalysisError);
            AppendConsoleLine(mainVm.ScanProgressText, ConsoleLogLevel.Error);
            AppendAnalysisDiagnostics(_recommendationsViewModel.LastDiagnostics);
            ShowConsolePanel();
        }
        else
        {
            var count = _recommendationsViewModel.Recommendations.Count;
            mainVm.ScanProgressText = L.Format("AnalysisCompleteStatus", count, count == 1 ? "" : "s");
            AppendConsoleLine(mainVm.ScanProgressText);
            AppendAnalysisDiagnostics(_recommendationsViewModel.LastDiagnostics);
            if (count == 0)
            {
                AppendConsoleLine("DIAG: zero final recommendations. Inspect parsed_recs/protected_filtered/parse_error above to determine whether this was an empty model result, parse failure, or post-filtering.");
                ShowConsolePanel();
            }
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
            OnAnalyzeRequested();
            e.Handled = true;
        }
    }

    private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        OnAnalyzeRequested();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateBack();
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        _treemapViewModel?.NavigateForward();
    }

    private void PathEditTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            var path = PathEditTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                TreemapView.NavigateToPath(path);
            }
            SwitchToBreadcrumbMode();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            SwitchToBreadcrumbMode();
            e.Handled = true;
        }
    }

    private void PathEditTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SwitchToBreadcrumbMode();
    }

    private void AddressBar_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Only switch to edit mode if the click was on the container itself, not on breadcrumb buttons
        var source = e.OriginalSource as DependencyObject;
        while (source is not null)
        {
            if (source is Button)
                return; // Click was on a breadcrumb segment button
            if (source == sender)
                break;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        if (BreadcrumbBar.Visibility == Visibility.Visible)
        {
            SwitchToEditMode();
        }
    }

    private void SwitchToEditMode()
    {
        var path = _treemapViewModel?.CurrentRoot?.Path
                   ?? (DataContext as MainViewModel)?.SelectedPath;
        if (path is not null)
        {
            PathEditTextBox.Text = path;
        }
        BreadcrumbBar.Visibility = Visibility.Collapsed;
        PathEditTextBox.Visibility = Visibility.Visible;
        PathEditTextBox.Focus();
        PathEditTextBox.SelectAll();
    }

    private void SwitchToBreadcrumbMode()
    {
        PathEditTextBox.Visibility = Visibility.Collapsed;
        BreadcrumbBar.Visibility = Visibility.Visible;
        RebuildBreadcrumbBar();
    }

    private bool _rebuildingBreadcrumbs;

    private void RebuildBreadcrumbBar()
    {
        if (_rebuildingBreadcrumbs)
            return;

        _rebuildingBreadcrumbs = true;
        try
        {
            BreadcrumbBar.Children.Clear();

            // Determine current path: prefer treemap view, fall back to SelectedPath
            var currentPath = _treemapViewModel?.CurrentRoot?.Path;
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = (DataContext as MainViewModel)?.SelectedPath;
            }
            if (string.IsNullOrEmpty(currentPath))
                return;

            var segments = ParsePathSegments(currentPath);

            for (int i = 0; i < segments.Count; i++)
            {
                if (i > 0)
                {
                    // Separator "›" between segments (Windows 11 Explorer style)
                    var sepBtn = new Button
                    {
                        Content = new TextBlock { Text = "›", FontSize = 14, Opacity = 0.6 },
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAE, 0xAE, 0xB2)),
                        Padding = new Thickness(2),
                        Cursor = Cursors.Hand,
                        Tag = segments[i].path,
                    };
                    var sepMenu = new ContextMenu();
                    sepMenu.Opened += BreadcrumbDropdown_Opened;
                    sepBtn.ContextMenu = sepMenu;
                    sepBtn.Click += BreadcrumbChevron_Click;
                    sepBtn.MouseEnter += (s, _) => ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                    sepBtn.MouseLeave += (s, _) => ((Button)s).Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAE, 0xAE, 0xB2));
                    BreadcrumbBar.Children.Add(sepBtn);
                }

                string segPath = segments[i].path;
                string segName = segments[i].name;

                // ── Name button: click to navigate ──
                var nameButton = new Button
                {
                    Content = new TextBlock
                    {
                        Text = segName,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
                    Padding = new Thickness(4, 2, 2, 2),
                    Cursor = Cursors.Hand,
                    Tag = segPath,
                };
                nameButton.Click += BreadcrumbSegment_Click;
                nameButton.MouseEnter += (s, _) =>
                {
                    ((Button)s).Background = (SolidColorBrush)FindResource("VP.SurfaceHoverBrush");
                    ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                };
                nameButton.MouseLeave += (s, _) =>
                {
                    ((Button)s).Background = Brushes.Transparent;
                    ((Button)s).Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0));
                };
                BreadcrumbBar.Children.Add(nameButton);
            }

            // Trailing › chevron (shows children of current folder)
            if (segments.Count > 0)
            {
                var trailBtn = new Button
                {
                    Content = new TextBlock { Text = "›", FontSize = 14, Opacity = 0.6 },
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAE, 0xAE, 0xB2)),
                    Padding = new Thickness(2),
                    Cursor = Cursors.Hand,
                    Tag = segments[^1].path,
                };
                var trailMenu = new ContextMenu();
                trailMenu.Opened += BreadcrumbDropdown_Opened;
                trailBtn.ContextMenu = trailMenu;
                trailBtn.Click += BreadcrumbChevron_Click;
                trailBtn.MouseEnter += (s, _) => ((Button)s).Foreground = (SolidColorBrush)FindResource("VP.TextPrimaryBrush");
                trailBtn.MouseLeave += (s, _) => ((Button)s).Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xAE, 0xAE, 0xB2));
                BreadcrumbBar.Children.Add(trailBtn);
            }
        }
        finally
        {
            _rebuildingBreadcrumbs = false;
        }
    }

    private const string ThisPC = "此电脑";

    private List<(string path, string name)> ParsePathSegments(string fullPath)
    {
        var result = new List<(string path, string name)>();
        if (string.IsNullOrEmpty(fullPath))
            return result;

        // Always start with "此电脑" (This PC) — Windows 11 Explorer style
        result.Add((ThisPC, ThisPC));

        var parts = fullPath.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return result;

        // Drive root (e.g., "C:")
        string rootPart = parts[0];
        result.Add(($"{rootPart}{System.IO.Path.DirectorySeparatorChar}", rootPart));

        var accumulated = $"{rootPart}{System.IO.Path.DirectorySeparatorChar}";
        for (int i = 1; i < parts.Length; i++)
        {
            accumulated += parts[i];
            if (i < parts.Length - 1)
                accumulated += System.IO.Path.DirectorySeparatorChar;
            result.Add((accumulated, parts[i]));
        }

        return result;
    }

    private void BreadcrumbSegment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string path && path != ThisPC)
        {
            TreemapView.NavigateToPath(path);
        }
        e.Handled = true;
    }

    private void BreadcrumbChevron_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is ContextMenu cm)
        {
            cm.PlacementTarget = btn;
            cm.IsOpen = true;
        }
        e.Handled = true;
    }

    private void BreadcrumbDropdown_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;

        menu.Items.Clear();

        // Find the directory path from the placement target's Tag
        string? dirPath = null;
        if (menu.PlacementTarget is FrameworkElement fe && fe.Tag is string tagPath)
            dirPath = tagPath;

        if (string.IsNullOrEmpty(dirPath))
            return;

        // Special case: "此电脑" → show all drives
        if (dirPath == ThisPC)
        {
            var drives = (DataContext as MainViewModel)?.DriveList ?? new List<string>();
            if (drives.Count == 0)
            {
                drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.Name)
                    .ToList();
            }

            foreach (var drive in drives)
            {
                var item = new MenuItem { Header = drive, Tag = drive };
                item.Click += (s, args) =>
                {
                    if (s is MenuItem mi && mi.Tag is string drivePath)
                    {
                        if (DataContext is MainViewModel mainVm)
                            mainVm.SelectedPath = drivePath;
                    }
                };
                menu.Items.Add(item);
            }
            return;
        }

        // Get children from the scanned tree
        var dirEntry = FindEntryByPathInTree(_treemapViewModel?.ScanRoot, dirPath);
        if (dirEntry is null)
            return;

        var children = dirEntry.Children
            .Where(c => c.IsDirectory)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (children.Count == 0)
        {
            var emptyItem = new MenuItem { Header = L.Text("NoSubfoldersText"), IsEnabled = false };
            menu.Items.Add(emptyItem);
            return;
        }

        foreach (var child in children)
        {
            var item = new MenuItem { Header = child.Name, Tag = child.Path };
            item.Click += (s, args) =>
            {
                if (s is MenuItem mi && mi.Tag is string childPath)
                    TreemapView.NavigateToPath(childPath);
            };
            menu.Items.Add(item);
        }
    }

    private static SpaceMonger.Core.Models.FileEntry? FindEntryByPathInTree(
        SpaceMonger.Core.Models.FileEntry? root, string targetPath)
    {
        if (root is null)
            return null;
        if (string.Equals(root.Path, targetPath, StringComparison.OrdinalIgnoreCase))
            return root;
        foreach (var child in root.Children)
        {
            if (child.IsDirectory)
            {
                var found = FindEntryByPathInTree(child, targetPath);
                if (found is not null)
                    return found;
            }
        }
        return null;
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
            MessageBox.Show(
                L.Text("NoItemsSelectedMessage"),
                L.Text("NoItemsSelectedTitle"),
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
            MessageBox.Show(
                L.Format("CleanupFailedMessage", ex.Message),
                L.Text("CleanupErrorTitle"),
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

[Flags]
public enum ConsoleLogLevel
{
    None = 0,
    Verbose = 1 << 0,
    Info = 1 << 1,
    Warning = 1 << 2,
    Error = 1 << 3,
}

public sealed record ConsoleLogEntry(DateTime Timestamp, ConsoleLogLevel Level, string Message)
{
    public string ToLogLine() => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
}

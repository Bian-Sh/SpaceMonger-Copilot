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

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
/// <summary>
/// Lightweight data object for breadcrumb dropdown items（面包屑下拉数据项）
/// </summary>
internal record BreadcrumbItem(string Name, string Path);

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
    private AcceptanceAutomationServer? _acceptanceAutomationServer;
    private string? _displayPathOverride;
    private bool _suppressSelectedPathNavigation;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(Path.GetDirectoryName(_consoleLogPath)!);
        AppendConsoleLine("Console log file: " + _consoleLogPath, ConsoleLogLevel.Info);
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closed += (_, _) => _acceptanceAutomationServer?.Dispose();
        SpaceMonger.App.Localization.L.LanguageChanged += OnAppLanguageChanged;
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

            // Set default path to first available drive
            if (string.IsNullOrEmpty(mainVm.SelectedPath))
            {
                var firstDrive = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .FirstOrDefault();
                if (firstDrive is not null)
                    mainVm.SelectedPath = firstDrive;
            }

            // Always rebuild breadcrumb on startup — belt-and-suspenders with PropertyChanged
            RebuildBreadcrumbBar();
        }

        _acceptanceAutomationServer ??= AcceptanceAutomationServer.StartIfEnabled(this);
    }

}

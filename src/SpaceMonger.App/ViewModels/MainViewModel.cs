using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Converters;
using SpaceMonger.App.Diagnostics;
using SpaceMonger.App.Localization;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Scanning;

namespace SpaceMonger.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFileScanner _fileScanner;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private string? _selectedPath;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string? _scanProgressText;

    [ObservableProperty]
    private ScanSession? _currentSession;

    [ObservableProperty]
    private List<string> _driveList = new();

    [ObservableProperty]
    private string _driveCapacity = "--";

    [ObservableProperty]
    private string _usedSpace = "--";

    [ObservableProperty]
    private string _freeSpace = "--";

    [ObservableProperty]
    private string _fileCount = "--";

    [ObservableProperty]
    private string _folderCount = "--";

    /// <summary>
    /// Reference to the recommendations view model, set from MainWindow.SetViewModels.
    /// Used by the AnalyzeButton DataTrigger binding in MainWindow.xaml.
    /// </summary>
    [ObservableProperty]
    private RecommendationsViewModel? _recommendationsVM;

    /// <summary>
    /// Reference to the update view model, set from MainWindow.SetUpdateViewModel.
    /// Used for status bar version display binding.
    /// </summary>
    [ObservableProperty]
    private UpdateViewModel? _updateVM;

    public event Action<ScanSession>? ScanCompleted;

    public MainViewModel(IFileScanner fileScanner)
    {
        _fileScanner = fileScanner;
        _fileScanner.IsReadyChanged += () =>
        {
            // Marshal to UI thread — the event may fire from a background thread.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                ScanCommand.NotifyCanExecuteChanged);
        };

        DriveList = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.Name)
            .ToList();

        if (DriveList.Count > 0)
        {
            SelectedPath = DriveList[0];
        }
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        var scanTarget = SelectedPath;

        if (string.IsNullOrWhiteSpace(scanTarget))
            return;
        scanTarget = scanTarget.Trim();

        CrashDiagnostics.Log("Scan.Start", scanTarget);
        IsScanning = true;
        ScanProgressText = L.Text("ScanningStatus");
        _scanCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgressText = p.FileCount > 0 || p.FolderCount > 0
                    ? L.Format("ScanProgressStatus", p.CurrentPath, p.FileCount, p.FolderCount)
                    : p.CurrentPath;
            });

            var session = await _fileScanner.ScanAsync(scanTarget, progress, _scanCts.Token);
            if (session.IsCancelled || _scanCts.IsCancellationRequested)
            {
                CrashDiagnostics.Log("Scan.CancelledSession", $"target={scanTarget}, hasRoot={session.RootEntry is not null}, files={session.TotalFiles}, folders={session.TotalFolders}");
                ScanProgressText = L.Text("ScanCancelledStatus");
                return;
            }

            CrashDiagnostics.Log("Scan.Completed", $"target={scanTarget}, files={session.TotalFiles}, folders={session.TotalFolders}, hasRoot={session.RootEntry is not null}");
            DebugBreakpoints.Hit("scan-returned");
            CurrentSession = session;
            UpdateStatusBar(session);
            DebugBreakpoints.Hit("scan-session-set");
            ScanCompleted?.Invoke(session);
        }
        catch (OperationCanceledException)
        {
            CrashDiagnostics.Log("Scan.CancelledException", scanTarget);
            ScanProgressText = L.Text("ScanCancelledStatus");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("whitelist", StringComparison.OrdinalIgnoreCase))
        {
            CrashDiagnostics.Log("Scan.WhitelistBlocked", scanTarget);
            ScanProgressText = L.Text("ScanTargetExcludedByWhitelist");
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private bool CanScan() => !IsScanning && SelectedPath is not null && _fileScanner.IsReady;

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedPathChanged(string? value)
    {
        ScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }

    private bool CanCancelScan() => IsScanning;

    [RelayCommand]
    private Task BrowseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            SelectedPath = dialog.FolderName;
        }
        return Task.CompletedTask;
    }

    public void UpdateStatusBar(ScanSession session)
    {
        DriveCapacity = session.DriveCapacity is not null
            ? FileSizeConverter.FormatSize(session.DriveCapacity.Value)
            : "--";

        FreeSpace = session.DriveFreeSpace is not null
            ? FileSizeConverter.FormatSize(session.DriveFreeSpace.Value)
            : "--";

        UsedSpace = session.DriveCapacity is not null && session.DriveFreeSpace is not null
            ? FileSizeConverter.FormatSize(session.DriveCapacity.Value - session.DriveFreeSpace.Value)
            : "--";

        FileCount = session.TotalFiles.ToString("N0");
        FolderCount = session.TotalFolders.ToString("N0");
    }
}

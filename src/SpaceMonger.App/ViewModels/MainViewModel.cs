using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Converters;
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

    public event Action<ScanSession>? ScanCompleted;

    /// <summary>
    /// When set, provides the path of the currently drilled-in folder.
    /// If the user is drilled into a subfolder, Scan will target that folder
    /// instead of the combo box path.
    /// </summary>
    public Func<string?>? GetCurrentViewPath { get; set; }

    public MainViewModel(IFileScanner fileScanner)
    {
        _fileScanner = fileScanner;

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
        // If drilled into a subfolder, scan that folder instead of the combo box path.
        var viewPath = GetCurrentViewPath?.Invoke();
        var scanTarget = viewPath ?? SelectedPath;

        if (scanTarget is null)
            return;

        // Update the combo box to reflect what we're actually scanning.
        SelectedPath = scanTarget;

        IsScanning = true;
        ScanProgressText = "Scanning...";
        _scanCts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanProgressText = $"Scanning: {p.FileCount:N0} files, {p.FolderCount:N0} folders";
            });

            var session = await _fileScanner.ScanAsync(scanTarget, progress, _scanCts.Token);
            CurrentSession = session;
            UpdateStatusBar(session);
            ScanCompleted?.Invoke(session);
        }
        catch (OperationCanceledException)
        {
            ScanProgressText = "Scan cancelled.";
        }
        finally
        {
            IsScanning = false;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private bool CanScan() => !IsScanning && SelectedPath is not null;

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
    private async Task BrowseAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            SelectedPath = dialog.FolderName;

            // Start scanning immediately after folder selection.
            if (ScanCommand.CanExecute(null))
            {
                await ScanCommand.ExecuteAsync(null);
            }
        }
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

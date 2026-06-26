using System.Diagnostics;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Settings;
using SpaceMonger.Core.Services.Whitelist;

namespace SpaceMonger.Core.Services.Scanning;

/// <summary>
/// Decorator around FileScanner that uses the NTFS USN change journal
/// for near-instant rescans of previously-scanned paths.
/// Falls back to full scan on any failure.
/// </summary>
public partial class IncrementalFileScanner : IFileScanner
{
    private readonly FileScanner _inner;
    private readonly ISettingsService? _settingsService;
    private readonly IPathWhitelistMatcher _whitelistMatcher;
    private readonly Dictionary<string, VolumeScanState> _volumeStates = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pendingIndexBuild;

    public bool IsReady => _pendingIndexBuild is null or { IsCompleted: true };
    public event Action? IsReadyChanged;

    public IncrementalFileScanner(FileScanner inner, ISettingsService? settingsService = null, IPathWhitelistMatcher? whitelistMatcher = null)
    {
        _inner = inner;
        _settingsService = settingsService;
        _whitelistMatcher = whitelistMatcher ?? new PathWhitelistMatcher();
    }

    public async Task<ScanSession> ScanAsync(
        string path,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        // If a background FRN index build is still running, wait for it.
        if (_pendingIndexBuild is { IsCompleted: false })
        {
            Trace.WriteLine("[USN] Waiting for background FRN index build to finish...");
            progress.Report(new ScanProgress("Finalizing directory index...", 0, 0));
            await _pendingIndexBuild.ConfigureAwait(false);
        }

        var fullPath = Path.GetFullPath(path);
        var scanWhitelist = _settingsService?.LoadSettings().ScanWhitelist ?? [];
        if (_whitelistMatcher.IsExcluded(fullPath, scanWhitelist))
        {
            throw new InvalidOperationException("Scan target is excluded by whitelist settings.");
        }

        var volumeRoot = Path.GetPathRoot(fullPath);

        Trace.WriteLine($"[USN] ScanAsync called: fullPath={fullPath}, volumeRoot={volumeRoot}, hasState={volumeRoot != null && _volumeStates.ContainsKey(volumeRoot)}");

        // Can we do incremental?
        if (volumeRoot != null
            && _volumeStates.TryGetValue(volumeRoot, out var state)
            && state.Watermark != null
            && string.Equals(state.ScannedPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            Trace.WriteLine($"[USN] Incremental path: cachedPath={state.ScannedPath}, watermark NextUsn={state.Watermark.NextUsn}");
            var result = await Task.Run(
                () => TryIncrementalRescan(state, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (result != null)
                return result;
            // Fallback — incremental failed, do full scan below
        }

        // Try MFT scan (fast path for NTFS volumes)
        if (volumeRoot != null)
        {
            Trace.WriteLine("[USN] Attempting MFT scan");
            var mftResult = await Task.Run(
                () => TryMftScan(fullPath, volumeRoot, progress, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (mftResult != null)
            {
                var (mftSession, frnIndex) = mftResult.Value;

                // FRN index was built during tree construction — just capture watermark
                _pendingIndexBuild = Task.Run(() =>
                {
                    try
                    {
                        var watermark = UsnJournalReader.QueryJournal(volumeRoot);
                        if (watermark != null)
                        {
                            Trace.WriteLine($"[USN] Post-MFT watermark: ID={watermark.JournalId}, NextUsn={watermark.NextUsn}");
                            _volumeStates[volumeRoot] = new VolumeScanState
                            {
                                ScannedPath = fullPath,
                                CachedSession = mftSession,
                                Watermark = watermark,
                                FrnIndex = frnIndex
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"[USN] Post-MFT watermark capture failed: {ex.Message}");
                    }
                }).ContinueWith(_ => IsReadyChanged?.Invoke(), TaskScheduler.Default);
                IsReadyChanged?.Invoke();

                return mftSession;
            }
        }

        // Full scan (first scan or fallback)
        Trace.WriteLine("[USN] Performing full scan via FileScanner");
        var session = await _inner.ScanAsync(path, progress, cancellationToken).ConfigureAwait(false);

        if (!session.IsCancelled && session.RootEntry != null && volumeRoot != null)
        {
            // Build FRN index in the background so the treemap renders immediately.
            // The index is only needed for the *next* rescan. If the user rescans
            // before this completes, we await it at the top of ScanAsync.
            _pendingIndexBuild = Task.Run(() =>
            {
                try
                {
                    CaptureWatermark(session, fullPath, volumeRoot);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[USN] CaptureWatermark failed: {ex.Message}");
                }
            }).ContinueWith(_ => IsReadyChanged?.Invoke(), TaskScheduler.Default);
            IsReadyChanged?.Invoke();
        }

        return session;
    }

    private class VolumeScanState
    {
        public string ScannedPath { get; set; } = string.Empty;
        public ScanSession? CachedSession { get; set; }
        public UsnWatermark? Watermark { get; set; }
        public Dictionary<long, FileEntry> FrnIndex { get; set; } = new();
    }
}


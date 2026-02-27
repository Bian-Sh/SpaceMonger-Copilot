using System.Diagnostics;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Scanning;

/// <summary>
/// Decorator around FileScanner that uses the NTFS USN change journal
/// for near-instant rescans of previously-scanned paths.
/// Falls back to full scan on any failure.
/// </summary>
public class IncrementalFileScanner : IFileScanner
{
    private readonly FileScanner _inner;
    private readonly Dictionary<string, VolumeScanState> _volumeStates = new(StringComparer.OrdinalIgnoreCase);
    private Task? _pendingIndexBuild;

    public bool IsReady => _pendingIndexBuild is null or { IsCompleted: true };
    public event Action? IsReadyChanged;

    public IncrementalFileScanner(FileScanner inner)
    {
        _inner = inner;
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

        // Full scan (first scan or fallback)
        Trace.WriteLine("[USN] Performing full scan");
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

    private void CaptureWatermark(ScanSession session, string fullPath, string volumeRoot)
    {
        var watermark = UsnJournalReader.QueryJournal(volumeRoot);
        if (watermark == null)
        {
            Trace.WriteLine($"[USN] QueryJournal returned null for {volumeRoot} — incremental rescan unavailable");
            return;
        }

        Trace.WriteLine($"[USN] Journal captured: ID={watermark.JournalId}, NextUsn={watermark.NextUsn}");

        var frnIndex = BuildFrnIndex(session.RootEntry!);

        Trace.WriteLine($"[USN] FRN index built: {frnIndex.Count} directories indexed");

        _volumeStates[volumeRoot] = new VolumeScanState
        {
            ScannedPath = fullPath,
            CachedSession = session,
            Watermark = watermark,
            FrnIndex = frnIndex
        };
    }

    /// <summary>
    /// Walks the FileEntry tree and gets FRNs for all directory entries.
    /// </summary>
    private static Dictionary<long, FileEntry> BuildFrnIndex(FileEntry root)
    {
        var index = new Dictionary<long, FileEntry>();
        var stack = new Stack<FileEntry>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.IsDirectory)
            {
                var frn = NtfsUsnNative.GetFileReferenceNumber(entry.Path);
                if (frn != 0)
                {
                    entry.FileReferenceNumber = frn;
                    index[frn] = entry;
                }

                foreach (var child in entry.Children)
                {
                    if (child.IsDirectory)
                        stack.Push(child);
                }
            }
        }

        return index;
    }

    private ScanSession? TryIncrementalRescan(
        VolumeScanState state,
        IProgress<ScanProgress> progress,
        CancellationToken ct)
    {
        try
        {
            progress.Report(new ScanProgress("Reading change journal...", 0, 0));

            var changes = UsnJournalReader.ReadChanges(state.Watermark!, ct);
            if (changes == null)
            {
                Trace.WriteLine("[USN] ReadChanges returned null — falling back to full scan");
                return null;
            }

            if (ct.IsCancellationRequested)
                return null;

            if (changes.Count == 0)
            {
                // No changes — return the cached session with refreshed drive info
                RefreshDriveInfo(state.CachedSession!);
                state.CachedSession!.StartTime = DateTime.Now;
                state.CachedSession!.EndTime = DateTime.Now;

                // Update watermark to current position
                var newWatermark = UsnJournalReader.QueryJournal(state.Watermark!.VolumeRoot);
                if (newWatermark != null)
                    state.Watermark = newWatermark;

                progress.Report(new ScanProgress(
                    "No changes detected",
                    state.CachedSession!.TotalFiles,
                    state.CachedSession!.TotalFolders));

                return state.CachedSession;
            }

            progress.Report(new ScanProgress(
                $"Applying {changes.Count} changes...",
                state.CachedSession!.TotalFiles,
                state.CachedSession!.TotalFolders));

            ApplyChanges(changes, state, ct);

            if (ct.IsCancellationRequested)
                return null;

            // Recount totals
            var (fileCount, folderCount) = CountEntries(state.CachedSession!.RootEntry!);
            state.CachedSession!.TotalFiles = fileCount;
            state.CachedSession!.TotalFolders = folderCount;
            state.CachedSession!.TotalSize = state.CachedSession!.RootEntry!.Size;
            state.CachedSession!.StartTime = DateTime.Now;
            state.CachedSession!.EndTime = DateTime.Now;

            RefreshDriveInfo(state.CachedSession!);

            // Update watermark
            var updatedWatermark = UsnJournalReader.QueryJournal(state.Watermark!.VolumeRoot);
            if (updatedWatermark != null)
                state.Watermark = updatedWatermark;
            else
                return null; // Can't get new watermark — fallback next time

            progress.Report(new ScanProgress(
                "Incremental rescan complete",
                state.CachedSession!.TotalFiles,
                state.CachedSession!.TotalFolders));

            return state.CachedSession;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[USN] Incremental rescan failed: {ex.Message}");
            return null; // Any error — fallback to full scan
        }
    }

    private static void ApplyChanges(List<UsnChange> changes, VolumeScanState state, CancellationToken ct)
    {
        // Process in order: Deletes → Renames → Creates → Modifies
        var deletes = new List<UsnChange>();
        var renames = new List<UsnChange>();
        var creates = new List<UsnChange>();
        var modifies = new List<UsnChange>();

        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case UsnChangeKind.Delete: deletes.Add(change); break;
                case UsnChangeKind.Rename: renames.Add(change); break;
                case UsnChangeKind.Create: creates.Add(change); break;
                case UsnChangeKind.Modify: modifies.Add(change); break;
            }
        }

        foreach (var del in deletes)
        {
            if (ct.IsCancellationRequested) return;
            ApplyDelete(del, state);
        }

        foreach (var ren in renames)
        {
            if (ct.IsCancellationRequested) return;
            ApplyRename(ren, state);
        }

        foreach (var create in creates)
        {
            if (ct.IsCancellationRequested) return;
            ApplyCreate(create, state);
        }

        foreach (var mod in modifies)
        {
            if (ct.IsCancellationRequested) return;
            ApplyModify(mod, state);
        }
    }

    private static void ApplyDelete(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parentDir))
            return; // Parent not in our tree — skip

        var child = parentDir.Children.FirstOrDefault(c =>
            string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));

        if (child == null)
            return;

        parentDir.RemoveChild(child);

        // If it was a directory, remove from FRN index
        if (child.IsDirectory && child.FileReferenceNumber != 0)
        {
            state.FrnIndex.Remove(child.FileReferenceNumber);
            // Also remove any descendant directories from FRN index
            RemoveDescendantFrns(child, state.FrnIndex);
        }
    }

    private static void RemoveDescendantFrns(FileEntry dir, Dictionary<long, FileEntry> frnIndex)
    {
        foreach (var child in dir.Children)
        {
            if (child.IsDirectory)
            {
                if (child.FileReferenceNumber != 0)
                    frnIndex.Remove(child.FileReferenceNumber);
                RemoveDescendantFrns(child, frnIndex);
            }
        }
    }

    private static void ApplyRename(UsnChange change, VolumeScanState state)
    {
        // The USN record tells us the new parent FRN and new name
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var newParent))
            return; // New parent not in our tree — skip

        // Find the entry by its FRN (directories) or by old parent + old name (files)
        FileEntry? entry = null;

        if (change.IsDirectory && state.FrnIndex.TryGetValue(change.FileReferenceNumber, out var dirEntry))
        {
            entry = dirEntry;
        }
        else if (change.OldParentFileReferenceNumber != 0
            && change.OldFileName != null
            && state.FrnIndex.TryGetValue(change.OldParentFileReferenceNumber, out var oldParentDir))
        {
            // Find the file in its old parent by old name
            entry = oldParentDir.Children.FirstOrDefault(c =>
                string.Equals(c.Name, change.OldFileName, StringComparison.OrdinalIgnoreCase));
        }

        if (entry == null)
            return;

        // Remove from old parent
        var oldParent = entry.Parent;
        if (oldParent != null)
        {
            oldParent.Children.Remove(entry);
            oldParent.RecalculateSize();
        }

        // Update entry
        entry.Name = change.FileName;
        entry.Path = Path.Combine(newParent.Path, change.FileName);
        entry.Parent = newParent;
        entry.Depth = newParent.Depth + 1;

        // Add to new parent
        newParent.Children.Add(entry);
        newParent.RecalculateSize();
    }

    private static void ApplyCreate(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parentDir))
            return; // Parent not in our tree — skip

        var fullPath = Path.Combine(parentDir.Path, change.FileName);

        if (change.IsDirectory)
        {
            var newDir = new FileEntry
            {
                Path = fullPath,
                Name = change.FileName,
                IsDirectory = true,
                Depth = parentDir.Depth + 1,
                Parent = parentDir
            };

            // Get FRN for the new directory
            var frn = NtfsUsnNative.GetFileReferenceNumber(fullPath);
            if (frn != 0)
            {
                newDir.FileReferenceNumber = frn;
                state.FrnIndex[frn] = newDir;
            }

            parentDir.Children.Add(newDir);
        }
        else
        {
            long size = 0;
            try
            {
                var fi = new FileInfo(fullPath);
                if (fi.Exists)
                    size = fi.Length;
            }
            catch
            {
                // Best-effort size
            }

            var newFile = new FileEntry
            {
                Path = fullPath,
                Name = change.FileName,
                IsDirectory = false,
                Extension = Path.GetExtension(change.FileName)?.ToLowerInvariant(),
                Size = size,
                Depth = parentDir.Depth + 1,
                Parent = parentDir
            };

            parentDir.Children.Add(newFile);
            parentDir.RecalculateSize();
        }
    }

    private static void ApplyModify(UsnChange change, VolumeScanState state)
    {
        if (!state.FrnIndex.TryGetValue(change.ParentFileReferenceNumber, out var parentDir))
            return; // Parent not in our tree — skip

        var child = parentDir.Children.FirstOrDefault(c =>
            string.Equals(c.Name, change.FileName, StringComparison.OrdinalIgnoreCase));

        if (child == null || child.IsDirectory)
            return; // Only refresh size for files

        try
        {
            var fi = new FileInfo(child.Path);
            if (fi.Exists)
            {
                child.Size = fi.Length;
                child.LastModified = fi.LastWriteTime;
                parentDir.RecalculateSize();
            }
        }
        catch
        {
            // Best-effort — if we can't read the file, leave size as-is
        }
    }

    private static (int files, int folders) CountEntries(FileEntry root)
    {
        int files = 0;
        int folders = 0;
        var stack = new Stack<FileEntry>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.IsDirectory)
            {
                folders++;
                foreach (var child in entry.Children)
                    stack.Push(child);
            }
            else
            {
                files++;
            }
        }

        return (files, folders);
    }

    private static void RefreshDriveInfo(ScanSession session)
    {
        try
        {
            var rootPath = Path.GetPathRoot(session.TargetPath);
            if (rootPath != null
                && string.Equals(Path.GetFullPath(session.TargetPath), rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var driveInfo = new DriveInfo(rootPath);
                if (driveInfo.IsReady)
                {
                    session.DriveCapacity = driveInfo.TotalSize;
                    session.DriveFreeSpace = driveInfo.TotalFreeSpace;
                }
            }
        }
        catch
        {
            // Best-effort
        }
    }

    private class VolumeScanState
    {
        public string ScannedPath { get; set; } = string.Empty;
        public ScanSession? CachedSession { get; set; }
        public UsnWatermark? Watermark { get; set; }
        public Dictionary<long, FileEntry> FrnIndex { get; set; } = new();
    }
}

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

    private (ScanSession session, Dictionary<long, FileEntry> frnIndex)? TryMftScan(
        string fullPath,
        string volumeRoot,
        IProgress<ScanProgress> progress,
        CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            Trace.WriteLine($"[MFT] Scan starting: fullPath={fullPath}, volumeRoot={volumeRoot}");

            // Phase 1: Enumerate MFT
            var mftRecords = MftEnumerator.EnumerateVolume(volumeRoot, progress, ct);
            if (mftRecords == null)
            {
                Trace.WriteLine("[MFT] " +"EnumerateVolume returned null — falling back");
                return null;
            }

            if (ct.IsCancellationRequested)
                return null;

            Trace.WriteLine("[MFT] " +$"Enumeration complete: {mftRecords.Count} records in {sw.ElapsedMilliseconds}ms");

            // Phase 2: Build tree from flat records
            // Count files/folders from MFT for progress reporting during tree build
            int mftFiles = 0, mftFolders = 0;
            foreach (var rec in mftRecords.Values)
            {
                if (rec.IsDirectory) mftFolders++;
                else mftFiles++;
            }
            progress.Report(new ScanProgress("Building directory tree", mftFiles, mftFolders));

            var isWholeVolume = string.Equals(
                Path.GetFullPath(fullPath).TrimEnd('\\'),
                volumeRoot.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            Trace.WriteLine("[MFT] " +$"isWholeVolume={isWholeVolume}");

            // Get the actual FRN for the target path. Don't hardcode segment 5 for root —
            // the full FRN includes a sequence number in the upper 16 bits.
            var targetFrn = NtfsUsnNative.GetFileReferenceNumber(fullPath);
            Trace.WriteLine("[MFT] " +$"targetFrn=0x{targetFrn:X16} (segment {targetFrn & 0x0000_FFFF_FFFF_FFFF})");
            if (targetFrn == 0)
            {
                Trace.WriteLine("[MFT] " +"GetFileReferenceNumber returned 0 — falling back");
                return null;
            }

            // Check if targetFrn exists in MFT records
            Trace.WriteLine("[MFT] " +$"targetFrn in mftRecords: {mftRecords.ContainsKey(targetFrn)}");

            // Determine which FRNs belong to the target subtree
            HashSet<long> subtreeFrns;
            if (isWholeVolume)
            {
                // All records are in scope
                subtreeFrns = new HashSet<long>(mftRecords.Keys);
            }
            else
            {
                // BFS from target FRN to collect descendant FRNs
                subtreeFrns = new HashSet<long> { targetFrn };
                var bfsQueue = new Queue<long>();
                bfsQueue.Enqueue(targetFrn);

                // Build parent→children lookup for BFS
                var childrenByParent = new Dictionary<long, List<long>>();
                foreach (var (frn, rec) in mftRecords)
                {
                    if (!childrenByParent.TryGetValue(rec.ParentFileReferenceNumber, out var list))
                    {
                        list = new List<long>();
                        childrenByParent[rec.ParentFileReferenceNumber] = list;
                    }
                    list.Add(frn);
                }

                while (bfsQueue.Count > 0)
                {
                    var parentFrn = bfsQueue.Dequeue();
                    if (childrenByParent.TryGetValue(parentFrn, out var children))
                    {
                        foreach (var childFrn in children)
                        {
                            if (subtreeFrns.Add(childFrn))
                                bfsQueue.Enqueue(childFrn);
                        }
                    }
                }
            }

            Trace.WriteLine("[MFT] " +$"subtreeFrns count: {subtreeFrns.Count}");

            // Ensure the target FRN is in the subtree set — FSCTL_ENUM_USN_DATA may not
            // return certain system entries (e.g. root directory has no USN record).
            subtreeFrns.Add(targetFrn);

            // Create FileEntry for each record in the subtree
            var frnToEntry = new Dictionary<long, FileEntry>(subtreeFrns.Count);
            foreach (var frn in subtreeFrns)
            {
                if (mftRecords.TryGetValue(frn, out var rec))
                {
                    frnToEntry[frn] = new FileEntry
                    {
                        Name = rec.FileName,
                        IsDirectory = rec.IsDirectory,
                        IsReparsePoint = (rec.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0,
                        FileReferenceNumber = rec.FileReferenceNumber
                    };
                }
                else if (frn == targetFrn)
                {
                    // Synthetic root — not returned by FSCTL_ENUM_USN_DATA (no USN record)
                    var dirInfo = new DirectoryInfo(fullPath);
                    frnToEntry[frn] = new FileEntry
                    {
                        Name = dirInfo.Name,
                        IsDirectory = true,
                        FileReferenceNumber = targetFrn
                    };
                    Trace.WriteLine("[MFT] " +$"Created synthetic root for FRN 0x{targetFrn:X16}");
                }
            }

            Trace.WriteLine("[MFT] " +$"frnToEntry count: {frnToEntry.Count}");
            progress.Report(new ScanProgress("Linking directory tree", mftFiles, mftFolders));

            // Link parent/children
            foreach (var (frn, entry) in frnToEntry)
            {
                if (frn == targetFrn)
                    continue;

                var rec = mftRecords[frn];
                if (frnToEntry.TryGetValue(rec.ParentFileReferenceNumber, out var parent))
                {
                    entry.Parent = parent;
                    parent.Children.Add(entry);
                }
            }

            var rootEntry = frnToEntry[targetFrn];
            Trace.WriteLine("[MFT] " +$"Root entry: Name='{rootEntry.Name}', Children={rootEntry.Children.Count}");

            // Set root path and reconstruct paths via BFS
            rootEntry.Path = fullPath;
            rootEntry.Depth = 0;
            var pathQueue = new Queue<FileEntry>();
            pathQueue.Enqueue(rootEntry);
            while (pathQueue.Count > 0)
            {
                var parent = pathQueue.Dequeue();
                foreach (var child in parent.Children)
                {
                    child.Path = Path.Combine(parent.Path, child.Name);
                    child.Depth = parent.Depth + 1;
                    if (child.IsDirectory)
                        pathQueue.Enqueue(child);
                }
            }

            // Free MFT records — no longer needed
            mftRecords = null;

            Trace.WriteLine("[MFT] " +$"Tree built: {frnToEntry.Count} entries in {sw.ElapsedMilliseconds}ms");

            if (ct.IsCancellationRequested)
                return null;

            // Phase 3: Collect sizes via parallel directory enumeration
            var (fileCount, folderCount) = ParallelSizeCollector.CollectSizes(
                rootEntry, mftFiles, mftFolders, progress, ct);

            if (ct.IsCancellationRequested)
                return null;

            Trace.WriteLine("[MFT] " +$"Size collection complete: {fileCount} files, {folderCount} folders in {sw.ElapsedMilliseconds}ms");

            // Phase 4: Build session
            var session = new ScanSession
            {
                TargetPath = fullPath,
                StartTime = DateTime.Now,
                TotalFiles = fileCount,
                TotalFolders = folderCount,
                TotalSize = rootEntry.Size,
                EndTime = DateTime.Now,
                RootEntry = rootEntry
            };

            FileScanner.PopulateDriveInfo(session, fullPath);

            Trace.WriteLine("[MFT] " +$"SUCCESS — Total scan time: {sw.ElapsedMilliseconds}ms");

            return (session, frnToEntry);
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[MFT] " +$"EXCEPTION: {ex}");
            return null;
        }
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

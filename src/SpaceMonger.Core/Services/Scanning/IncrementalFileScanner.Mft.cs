using System.Diagnostics;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Scanning;

public partial class IncrementalFileScanner
{
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
            if (IsWholeVolumeRoot(fullPath))
            {
                rootEntry.Children.RemoveAll(child => string.Equals(
                    child.Name,
                    "System Volume Information",
                    StringComparison.OrdinalIgnoreCase));
            }
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

    private static bool IsWholeVolumeRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return !string.IsNullOrEmpty(root)
               && string.Equals(
                   fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                   StringComparison.OrdinalIgnoreCase);
    }

}

using System.Diagnostics;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Scanning;

public partial class IncrementalFileScanner
{
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

}

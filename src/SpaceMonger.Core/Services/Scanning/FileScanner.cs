using System.IO.Enumeration;
using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Scanning;

public class FileScanner : IFileScanner
{
    public bool IsReady => true;
    public event Action? IsReadyChanged { add { } remove { } }

    // Windows cloud placeholder file attributes (OneDrive Files On-Demand, etc.)
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes CloudPlaceholderMask = RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline;

    // Throttle progress reports to avoid flooding the UI thread.
    private const int ProgressReportInterval = 200;

    public async Task<ScanSession> ScanAsync(
        string path,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var session = new ScanSession
        {
            TargetPath = path,
            StartTime = DateTime.Now
        };

        PopulateDriveInfo(session, path);

        var rootEntry = CreateRootEntry(path);

        int fileCount = 0;
        int folderCount = 1;

        await Task.Run(() =>
        {
            var queue = new Queue<FileEntry>();
            queue.Enqueue(rootEntry);
            int reportCounter = 0;

            while (queue.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    session.IsCancelled = true;
                    break;
                }

                var current = queue.Dequeue();

                try
                {
                    // Use FileSystemEnumerable<T> to read all entries in one pass.
                    // This maps directly to FindFirstFile/FindNextFile — one syscall per entry,
                    // no extra GetFileAttributesEx or stat calls like FileInfo/DirectoryInfo would cause.
                    var enumerable = new FileSystemEnumerable<(string FullPath, string Name, FileAttributes Attributes, long Length, DateTime LastWrite, bool IsDir)>(
                        current.Path,
                        (ref FileSystemEntry entry) => (
                            entry.ToFullPath(),
                            entry.FileName.ToString(),
                            entry.Attributes,
                            entry.Length,
                            entry.LastWriteTimeUtc.LocalDateTime,
                            entry.IsDirectory
                        ),
                        new EnumerationOptions
                        {
                            IgnoreInaccessible = false,
                            RecurseSubdirectories = false,
                            AttributesToSkip = 0 // Don't skip anything — we handle attributes ourselves.
                        });

                    foreach (var item in enumerable)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            session.IsCancelled = true;
                            break;
                        }

                        var isReparsePoint = (item.Attributes & FileAttributes.ReparsePoint) != 0;

                        if (item.IsDir)
                        {
                            var childDir = new FileEntry
                            {
                                Path = item.FullPath,
                                Name = item.Name,
                                IsDirectory = true,
                                IsReparsePoint = isReparsePoint,
                                LastModified = item.LastWrite,
                                Depth = current.Depth + 1,
                                Parent = current
                            };
                            current.Children.Add(childDir);
                            folderCount++;

                            if (!isReparsePoint)
                            {
                                queue.Enqueue(childDir);
                            }
                        }
                        else
                        {
                            var isCloudPlaceholder = (item.Attributes & CloudPlaceholderMask) != 0;

                            var childFile = new FileEntry
                            {
                                Path = item.FullPath,
                                Name = item.Name,
                                IsDirectory = false,
                                IsReparsePoint = isReparsePoint,
                                IsCloudPlaceholder = isCloudPlaceholder,
                                LastModified = item.LastWrite,
                                Extension = System.IO.Path.GetExtension(item.Name)?.ToLowerInvariant(),
                                // Length comes straight from WIN32_FIND_DATA — no extra syscall.
                                // Cloud placeholders report 0 to avoid triggering downloads.
                                Size = isCloudPlaceholder ? 0 : item.Length,
                                Depth = current.Depth + 1,
                                Parent = current
                            };
                            current.Children.Add(childFile);
                            fileCount++;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    current.IsAccessDenied = true;
                }
                catch (IOException)
                {
                    // Skip directories that cannot be read due to I/O errors.
                }

                // Throttle progress reports — reporting every directory hammers the UI thread.
                if (++reportCounter % ProgressReportInterval == 0)
                {
                    progress.Report(new ScanProgress(current.Path, fileCount, folderCount));
                }
            }

            // Final progress report.
            progress.Report(new ScanProgress("Calculating sizes...", fileCount, folderCount));

            // Calculate directory sizes bottom-up.
            CalculateSizesBottomUp(rootEntry);

        }, cancellationToken).ConfigureAwait(false);

        session.TotalFiles = fileCount;
        session.TotalFolders = folderCount;
        session.TotalSize = rootEntry.Size;
        session.EndTime = DateTime.Now;
        session.RootEntry = rootEntry;

        return session;
    }

    internal static void PopulateDriveInfo(ScanSession session, string path)
    {
        try
        {
            var rootPath = Path.GetPathRoot(path);
            if (rootPath != null && string.Equals(Path.GetFullPath(path), rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var driveInfo = new DriveInfo(rootPath);
                if (driveInfo.IsReady)
                {
                    session.DriveCapacity = driveInfo.TotalSize;
                    session.DriveFreeSpace = driveInfo.TotalFreeSpace;
                }
            }
        }
        catch (Exception)
        {
            // Drive info is best-effort; failures are non-fatal.
        }
    }

    private static FileEntry CreateRootEntry(string path)
    {
        // Only the root needs a DirectoryInfo call — one syscall for the entire scan.
        var dirInfo = new DirectoryInfo(path);
        return new FileEntry
        {
            Path = dirInfo.FullName,
            Name = dirInfo.Name,
            IsDirectory = true,
            IsReparsePoint = (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0,
            LastModified = dirInfo.LastWriteTime,
            Depth = 0
        };
    }

    /// <summary>
    /// Calculates directory sizes bottom-up using a post-order traversal.
    /// Each directory's size is the sum of its children's sizes.
    /// </summary>
    internal static void CalculateSizesBottomUp(FileEntry root)
    {
        var stack = new Stack<FileEntry>();
        var postOrder = new List<FileEntry>();

        stack.Push(root);
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            postOrder.Add(entry);

            foreach (var child in entry.Children)
            {
                if (child.IsDirectory)
                {
                    stack.Push(child);
                }
            }
        }

        for (int i = postOrder.Count - 1; i >= 0; i--)
        {
            var dir = postOrder[i];
            dir.Size = dir.Children.Sum(c => c.Size);
        }
    }
}

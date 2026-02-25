using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Scanning;

public class FileScanner : IFileScanner
{
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

        var rootEntry = CreateDirectoryEntry(path, parent: null, depth: 0);

        int fileCount = 0;
        int folderCount = 1; // Count the root directory itself.

        await Task.Run(() =>
        {
            var queue = new Queue<FileEntry>();
            queue.Enqueue(rootEntry);

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
                    // Skip reparse points to avoid infinite loops from symlinks/junctions.
                    if (current.IsReparsePoint)
                    {
                        continue;
                    }

                    // Enumerate subdirectories.
                    foreach (var dirPath in Directory.EnumerateDirectories(current.Path))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            session.IsCancelled = true;
                            break;
                        }

                        var childDir = CreateDirectoryEntry(dirPath, parent: current, depth: current.Depth + 1);
                        current.Children.Add(childDir);
                        folderCount++;

                        // Only enqueue non-reparse-point directories for further traversal.
                        if (!childDir.IsReparsePoint)
                        {
                            queue.Enqueue(childDir);
                        }
                    }

                    if (session.IsCancelled)
                    {
                        break;
                    }

                    // Enumerate files.
                    foreach (var filePath in Directory.EnumerateFiles(current.Path))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            session.IsCancelled = true;
                            break;
                        }

                        var childFile = CreateFileEntry(filePath, parent: current, depth: current.Depth + 1);
                        current.Children.Add(childFile);
                        fileCount++;
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

                progress.Report(new ScanProgress(current.Path, fileCount, folderCount));
            }

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

    private static void PopulateDriveInfo(ScanSession session, string path)
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

    private static FileEntry CreateDirectoryEntry(string dirPath, FileEntry? parent, int depth)
    {
        var dirInfo = new DirectoryInfo(dirPath);
        var isReparsePoint = (dirInfo.Attributes & FileAttributes.ReparsePoint) != 0;

        return new FileEntry
        {
            Path = dirInfo.FullName,
            Name = dirInfo.Name,
            IsDirectory = true,
            IsReparsePoint = isReparsePoint,
            LastModified = dirInfo.LastWriteTime,
            Extension = null,
            Depth = depth,
            Parent = parent
        };
    }

    private static FileEntry CreateFileEntry(string filePath, FileEntry? parent, int depth)
    {
        var fileInfo = new FileInfo(filePath);
        var isReparsePoint = (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0;

        long size = 0;
        try
        {
            size = fileInfo.Length;
        }
        catch (Exception)
        {
            // File may have been deleted or become inaccessible between enumeration and size read.
        }

        return new FileEntry
        {
            Path = fileInfo.FullName,
            Name = fileInfo.Name,
            IsDirectory = false,
            IsReparsePoint = isReparsePoint,
            LastModified = fileInfo.LastWriteTime,
            Extension = System.IO.Path.GetExtension(fileInfo.Name)?.ToLowerInvariant(),
            Size = size,
            Depth = depth,
            Parent = parent
        };
    }

    /// <summary>
    /// Calculates directory sizes bottom-up using a post-order traversal.
    /// Each directory's size is the sum of its children's sizes.
    /// </summary>
    private static void CalculateSizesBottomUp(FileEntry root)
    {
        // Use an iterative post-order traversal to avoid stack overflow on deep trees.
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

        // Process in reverse order so children are calculated before parents.
        for (int i = postOrder.Count - 1; i >= 0; i--)
        {
            var dir = postOrder[i];
            dir.Size = dir.Children.Sum(c => c.Size);
        }
    }
}

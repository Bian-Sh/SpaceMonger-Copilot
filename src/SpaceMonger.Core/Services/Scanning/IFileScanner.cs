using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Scanning;

public record ScanProgress(string CurrentPath, int FileCount, int FolderCount);

public interface IFileScanner
{
    Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken);
}

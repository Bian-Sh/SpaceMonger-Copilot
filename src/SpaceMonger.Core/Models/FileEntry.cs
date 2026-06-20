using System.IO;

namespace SpaceMonger.Core.Models;

public class FileEntry
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string? Extension { get; set; }
    public DateTime LastModified { get; set; }
    public bool IsDirectory { get; set; }
    public bool IsReparsePoint { get; set; }
    public bool IsAccessDenied { get; set; }
    public bool IsCloudPlaceholder { get; set; }
    public FileAttributes? Attributes { get; set; }
    public byte[]? ContentHash { get; set; }
    public long FileReferenceNumber { get; set; }
    public FileEntry? Parent { get; set; }
    public List<FileEntry> Children { get; set; } = new();
    public int Depth { get; set; }
    public int SubtreeItemCount { get; set; }
    public int SubtreeFileCount { get; set; }
    public int SubtreeFolderCount { get; set; }

    public void RecalculateSize()
    {
        if (IsDirectory)
        {
            Size = Children.Sum(c => c.Size);
            SubtreeFileCount = Children.Sum(c => c.SubtreeFileCount > 0 ? c.SubtreeFileCount : c.IsDirectory ? 0 : 1);
            SubtreeFolderCount = 1 + Children.Sum(c => c.SubtreeFolderCount > 0 ? c.SubtreeFolderCount : c.IsDirectory ? 1 : 0);
            SubtreeItemCount = SubtreeFileCount + SubtreeFolderCount;
        }
        else
        {
            SubtreeFileCount = 1;
            SubtreeFolderCount = 0;
            SubtreeItemCount = 1;
        }

        Parent?.RecalculateSize();
    }

    public void RemoveChild(FileEntry child)
    {
        Children.Remove(child);
        RecalculateSize();
    }
}


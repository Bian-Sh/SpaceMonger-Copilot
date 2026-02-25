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
    public byte[]? ContentHash { get; set; }
    public FileEntry? Parent { get; set; }
    public List<FileEntry> Children { get; set; } = new();
    public int Depth { get; set; }

    public void RecalculateSize()
    {
        Size = Children.Sum(c => c.Size);
        Parent?.RecalculateSize();
    }

    public void RemoveChild(FileEntry child)
    {
        Children.Remove(child);
        RecalculateSize();
    }
}

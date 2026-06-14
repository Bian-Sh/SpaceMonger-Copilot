using SpaceMonger.Core.Enums;

namespace SpaceMonger.Core.Models;

public class AppSettings
{
    public byte[]? EncryptedApiKey { get; set; }
    public bool IsApiKeyValid { get; set; } = false;
    public DeletionMode DeletionMode { get; set; } = DeletionMode.MoveToRecycleBin;
    public string? LastScanPath { get; set; }
    public string? AnthropicBaseUrl { get; set; }
}

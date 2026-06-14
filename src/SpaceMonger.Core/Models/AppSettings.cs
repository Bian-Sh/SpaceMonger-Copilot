using SpaceMonger.Core.Enums;

namespace SpaceMonger.Core.Models;

public class AppSettings
{
    public byte[]? EncryptedApiKey { get; set; }
    public bool IsApiKeyValid { get; set; } = false;
    public DeletionMode DeletionMode { get; set; } = DeletionMode.MoveToRecycleBin;
    public string? LastScanPath { get; set; }
    public string? AnthropicBaseUrl { get; set; }
    public string? AnalysisModelName { get; set; }
    public string? ChatModelName { get; set; }
    public bool EnableThinking { get; set; } = false;
    public string Language { get; set; } = "zh-CN";
}

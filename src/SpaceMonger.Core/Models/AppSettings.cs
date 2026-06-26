using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models.Theme;

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

    /// <summary>Serialized theme profile (or null = use default).</summary>
    public ThemeProfile? ThemeProfile { get; set; }

    public List<PathWhitelistEntry> ScanWhitelist { get; set; } = [];
    public List<PathWhitelistEntry> CleanupRecommendationWhitelist { get; set; } = [];
    public List<PathWhitelistEntry> AiConversationWhitelist { get; set; } = [];
}

public class PathWhitelistEntry
{
    public string Path { get; set; } = string.Empty;
    public string? Description { get; set; }
}

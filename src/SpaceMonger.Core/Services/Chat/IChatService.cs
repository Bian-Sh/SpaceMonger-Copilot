using SpaceMonger.Core.Models;

namespace SpaceMonger.Core.Services.Chat;

public interface IChatService
{
    Task<string> SendMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken);

    Task<string> StreamMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        string apiKey,
        string? baseUrl,
        Action<string> onToken,
        CancellationToken cancellationToken);

    void ClearHistory();
}

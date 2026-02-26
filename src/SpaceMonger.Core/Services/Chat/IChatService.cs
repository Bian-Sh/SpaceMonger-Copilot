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
        CancellationToken cancellationToken);

    Task<string> StreamMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        string apiKey,
        Action<string> onToken,
        CancellationToken cancellationToken);

    void ClearHistory();
}

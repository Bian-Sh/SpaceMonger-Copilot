using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Chat;

public interface IChatService
{
    Task<string> SendMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        IReadOnlyList<AiSkill> activeSkills,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken);

    Task<string> StreamMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        IReadOnlyList<AiSkill> activeSkills,
        string apiKey,
        string? baseUrl,
        Action<string> onToken,
        CancellationToken cancellationToken);

    Task<ChatResponse> StreamMessageWithThinkingAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        IReadOnlyList<AiSkill> activeSkills,
        string apiKey,
        string? baseUrl,
        Action<string>? onThinkingToken,
        Action<string>? onTextToken,
        CancellationToken cancellationToken);

    void ClearHistory();
}

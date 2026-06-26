using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Services.Agent;

public interface IAgentRuntime
{
    Task<AgentResponse> RunAsync(
        AgentContext? context,
        IReadOnlyList<(string Role, string Content)> conversationHistory,
        string userMessage,
        IReadOnlyList<AiSkill> activeSkills,
        string? responseLanguage,
        string apiKey,
        string? baseUrl,
        bool enableThinking,
        Action<string>? onThinkingToken,
        CancellationToken cancellationToken);
}

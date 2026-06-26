using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Chat;

public class ChatService : IChatService
{
    private const int MaxEstimatedTokens = 150_000;
    private const int CharsPerToken = 4;

    private readonly IAgentRuntime _agentRuntime;
    private readonly List<(string role, string content)> _conversationHistory = [];

    public ChatService(IAgentRuntime agentRuntime)
    {
        _agentRuntime = agentRuntime;
    }

    public async Task<string> SendMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        bool hasExistingRecommendations,
        IReadOnlyList<AiSkill> activeSkills,
        string? responseLanguage,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        var response = await _agentRuntime.RunAsync(
            new AgentContext(session, currentViewRoot, linkedEntry, linkedRecommendation, hasExistingRecommendations),
            _conversationHistory,
            userMessage,
            activeSkills,
            responseLanguage,
            apiKey,
            baseUrl,
            cancellationToken).ConfigureAwait(false);

        AddTurnToHistory(userMessage, response.Content);
        return response.Content;
    }

    public async Task<string> StreamMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        bool hasExistingRecommendations,
        IReadOnlyList<AiSkill> activeSkills,
        string? responseLanguage,
        string apiKey,
        string? baseUrl,
        Action<string> onToken,
        CancellationToken cancellationToken)
    {
        var response = await _agentRuntime.RunAsync(
            new AgentContext(session, currentViewRoot, linkedEntry, linkedRecommendation, hasExistingRecommendations),
            _conversationHistory,
            userMessage,
            activeSkills,
            responseLanguage,
            apiKey,
            baseUrl,
            cancellationToken).ConfigureAwait(false);

        onToken(response.Content);
        AddTurnToHistory(userMessage, response.Content);
        return response.Content;
    }

    public async Task<ChatResponse> StreamMessageWithThinkingAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        bool hasExistingRecommendations,
        IReadOnlyList<AiSkill> activeSkills,
        string? responseLanguage,
        string apiKey,
        string? baseUrl,
        Action<string>? onThinkingToken,
        Action<string>? onTextToken,
        CancellationToken cancellationToken)
    {
        var response = await _agentRuntime.RunAsync(
            new AgentContext(session, currentViewRoot, linkedEntry, linkedRecommendation, hasExistingRecommendations),
            _conversationHistory,
            userMessage,
            activeSkills,
            responseLanguage,
            apiKey,
            baseUrl,
            cancellationToken).ConfigureAwait(false);

        if (response.ToolResults.Count > 0)
        {
            var limitNote = response.ReachedToolLimit ? " Tool limit reached; answered from partial observations." : string.Empty;
            onThinkingToken?.Invoke($"Queried {response.ToolResults.Count} read-only file tree tool(s).{limitNote}\n");
        }

        onTextToken?.Invoke(response.Content);
        AddTurnToHistory(userMessage, response.Content);
        return new ChatResponse(response.Content, string.Empty, response.Proposal);
    }

    public async Task<ChatResponse> StreamSkillMessageWithThinkingAsync(
        string userMessage,
        IReadOnlyList<AiSkill> activeSkills,
        string? responseLanguage,
        string apiKey,
        string? baseUrl,
        Action<string>? onThinkingToken,
        Action<string>? onTextToken,
        CancellationToken cancellationToken)
    {
        var response = await _agentRuntime.RunAsync(
            null,
            _conversationHistory,
            userMessage,
            activeSkills,
            responseLanguage,
            apiKey,
            baseUrl,
            cancellationToken).ConfigureAwait(false);

        if (response.ToolResults.Count > 0)
        {
            onThinkingToken?.Invoke($"Ignored {response.ToolResults.Count} file tree tool request(s) because no scan context is available.\n");
        }

        onTextToken?.Invoke(response.Content);
        AddTurnToHistory(userMessage, response.Content);
        return new ChatResponse(response.Content, string.Empty, response.Proposal);
    }

    public void ClearHistory()
    {
        _conversationHistory.Clear();
    }

    private void AddTurnToHistory(string userMessage, string assistantResponse)
    {
        _conversationHistory.Add(("user", userMessage));
        _conversationHistory.Add(("assistant", assistantResponse));
        TruncateHistoryIfNeeded();
    }

    private void TruncateHistoryIfNeeded()
    {
        while (_conversationHistory.Count > 2)
        {
            var totalChars = _conversationHistory.Sum(message => message.content.Length);
            var estimatedTokens = totalChars / CharsPerToken;
            if (estimatedTokens <= MaxEstimatedTokens)
            {
                break;
            }

            _conversationHistory.RemoveAt(0);
            if (_conversationHistory.Count > 0)
            {
                _conversationHistory.RemoveAt(0);
            }
        }
    }
}

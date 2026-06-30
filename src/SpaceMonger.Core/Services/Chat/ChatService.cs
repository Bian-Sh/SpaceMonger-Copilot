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
            false,
            null,
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
            false,
            null,
            cancellationToken).ConfigureAwait(false);

        await StreamTextAsync(response.Content, onToken, cancellationToken).ConfigureAwait(false);
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
        bool enableThinking,
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
            enableThinking,
            onThinkingToken,
            cancellationToken).ConfigureAwait(false);

        if (enableThinking && response.ToolResults.Count > 0)
        {
            await StreamTextAsync(FormatToolObservationNote(response), onThinkingToken, cancellationToken).ConfigureAwait(false);
        }

        await StreamTextAsync(response.Content, onTextToken, cancellationToken).ConfigureAwait(false);
        AddTurnToHistory(userMessage, response.Content);
        return new ChatResponse(response.Content, string.Empty, response.Proposal);
    }

    public async Task<ChatResponse> StreamSkillMessageWithThinkingAsync(
        string userMessage,
        IReadOnlyList<AiSkill> activeSkills,
        string? responseLanguage,
        string apiKey,
        string? baseUrl,
        bool enableThinking,
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
            enableThinking,
            onThinkingToken,
            cancellationToken).ConfigureAwait(false);

        if (enableThinking && response.ToolResults.Count > 0)
        {
            await StreamTextAsync(FormatToolObservationNote(response), onThinkingToken, cancellationToken).ConfigureAwait(false);
        }

        await StreamTextAsync(response.Content, onTextToken, cancellationToken).ConfigureAwait(false);
        AddTurnToHistory(userMessage, response.Content);
        return new ChatResponse(response.Content, string.Empty, response.Proposal);
    }

    public void ClearHistory()
    {
        _conversationHistory.Clear();
    }

    private static async Task StreamTextAsync(string text, Action<string>? onToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (onToken is null)
        {
            return;
        }

        const int maxChunkLength = 48;
        var index = 0;
        while (index < text.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var length = Math.Min(maxChunkLength, text.Length - index);
            var newlineOffset = text.IndexOf('\n', index, length);
            if (newlineOffset >= 0)
            {
                length = newlineOffset - index + 1;
            }
            else if (index + length < text.Length)
            {
                var lastSpace = text.LastIndexOf(' ', index + length - 1, length);
                if (lastSpace > index + 12)
                {
                    length = lastSpace - index + 1;
                }
            }

            onToken(text.Substring(index, length));
            index += length;
            await Task.Delay(18, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string FormatToolObservationNote(AgentResponse response)
    {
        var successCount = response.ToolResults.Count(result => !result.IsError);
        var errorCount = response.ToolResults.Count - successCount;
        var limitNote = response.ReachedToolLimit ? " Tool limit reached; answered from partial observations." : string.Empty;
        return $"Processed {response.ToolResults.Count} agent tool observation(s): {successCount} succeeded, {errorCount} failed.{limitNote}\n";
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

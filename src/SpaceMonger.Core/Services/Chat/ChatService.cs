using System.Text.Json;
using System.Text.Json.Serialization;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Chat;

public class ChatService : IChatService
{
    private const int MaxEstimatedTokens = 150_000;
    private const int CharsPerToken = 4;

    private readonly ILlmClient _llmClient;
    private readonly List<(string role, string content)> _conversationHistory = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ChatService(ILlmClient llmClient)
    {
        _llmClient = llmClient;
    }

    public async Task<string> SendMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        // Step 1: Build context block JSON
        var contextBlock = BuildContextBlock(currentViewRoot, linkedEntry, linkedRecommendation, session);

        // Step 2: Build the full user message with context prefix
        var fullUserMessage = $"{contextBlock}\n\nUser question: {userMessage}";

        // Step 3: Build the system prompt
        var systemPrompt = BuildSystemPrompt();

        // Step 4: Add user message to conversation history
        _conversationHistory.Add(("user", fullUserMessage));

        // Step 5: Call the LLM
        var response = await _llmClient.SendChatAsync(
            systemPrompt,
            _conversationHistory,
            apiKey,
            baseUrl,
            cancellationToken);

        // Step 6: Add assistant response to conversation history
        _conversationHistory.Add(("assistant", response));

        // Step 7: Truncate oldest messages if estimated tokens exceed ~150K
        TruncateHistoryIfNeeded(systemPrompt);

        return response;
    }

    public async Task<string> StreamMessageAsync(
        string userMessage,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry currentViewRoot,
        ScanSession session,
        string apiKey,
        string? baseUrl,
        Action<string> onToken,
        CancellationToken cancellationToken)
    {
        var contextBlock = BuildContextBlock(currentViewRoot, linkedEntry, linkedRecommendation, session);
        var fullUserMessage = $"{contextBlock}\n\nUser question: {userMessage}";
        var systemPrompt = BuildSystemPrompt();

        _conversationHistory.Add(("user", fullUserMessage));

        var fullResponse = await _llmClient.StreamChatAsync(
            systemPrompt, _conversationHistory, apiKey, baseUrl, onToken, cancellationToken);

        _conversationHistory.Add(("assistant", fullResponse));
        TruncateHistoryIfNeeded(systemPrompt);

        return fullResponse;
    }

    public void ClearHistory()
    {
        _conversationHistory.Clear();
    }

    private static string BuildContextBlock(
        FileEntry currentViewRoot,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        ScanSession session)
    {
        // Build selected_item from linkedEntry or linkedRecommendation
        object? selectedItem = null;

        if (linkedEntry is not null)
        {
            selectedItem = new
            {
                path = linkedEntry.Path,
                size_bytes = linkedEntry.Size,
                type = linkedEntry.IsDirectory ? "directory" : "file",
                extension = linkedEntry.Extension,
                last_modified = linkedEntry.LastModified.ToString("O")
            };
        }
        else if (linkedRecommendation is not null)
        {
            selectedItem = new
            {
                path = linkedRecommendation.TargetPath,
                size_bytes = linkedRecommendation.Size,
                type = linkedRecommendation.Entry?.IsDirectory == true ? "directory" : "file",
                extension = linkedRecommendation.Entry?.Extension,
                last_modified = linkedRecommendation.Entry?.LastModified.ToString("O")
            };
        }

        var contextObject = new
        {
            current_view_path = currentViewRoot.Path,
            current_view_items = currentViewRoot.Children
                .OrderByDescending(c => c.Size)
                .Select(c => new
                {
                    path = c.Path,
                    size_bytes = c.Size,
                    type = c.IsDirectory ? "directory" : "file"
                }),
            selected_item = selectedItem,
            scan_summary = new
            {
                total_size_bytes = session.TotalSize,
                total_files = session.TotalFiles,
                drive_capacity_bytes = session.DriveCapacity
            }
        };

        return JsonSerializer.Serialize(contextObject, JsonOptions);
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are a disk space analysis assistant. You help users understand their disk usage and make informed decisions about cleaning up files.

            ## Guidelines

            - Reference actual scan data provided in the context block — never hallucinate paths or sizes
            - Provide accurate information about well-known system files (hiberfil.sys, pagefile.sys, swapfile.sys, etc.)
            - Include specific removal or management instructions when asked, with clear warnings about consequences
            - Format commands as fenced code blocks — the user will manually copy and execute them
            - NEVER claim to execute commands, modify files, or take actions on the system
            - Cite actual file paths and sizes from the provided scan context
            - If a question is not related to disk space analysis, redirect the conversation back to disk space topics
            - When discussing sizes, use human-readable units (KB, MB, GB) alongside exact byte counts when relevant
            """;
    }

    private void TruncateHistoryIfNeeded(string systemPrompt)
    {
        while (_conversationHistory.Count > 2)
        {
            var totalChars = systemPrompt.Length;
            foreach (var (_, content) in _conversationHistory)
            {
                totalChars += content.Length;
            }

            var estimatedTokens = totalChars / CharsPerToken;
            if (estimatedTokens <= MaxEstimatedTokens)
            {
                break;
            }

            // Remove the oldest user/assistant pair (first two entries)
            _conversationHistory.RemoveAt(0);
            if (_conversationHistory.Count > 0)
            {
                _conversationHistory.RemoveAt(0);
            }
        }
    }
}

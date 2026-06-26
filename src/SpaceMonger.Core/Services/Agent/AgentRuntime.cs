using System.Text;
using System.Text.Json;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;

namespace SpaceMonger.Core.Services.Agent;

public sealed class AgentRuntime : IAgentRuntime
{
    private const int MaxToolRounds = 4;
    private const int MaxToolCalls = 8;
    private const int MaxObservationChars = 16_000;

    private readonly ILlmClient _llmClient;
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public AgentRuntime(ILlmClient llmClient, IEnumerable<IAgentTool> tools)
    {
        _llmClient = llmClient;
        _tools = tools.ToDictionary(tool => tool.Name, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<AgentResponse> RunAsync(
        AgentContext context,
        IReadOnlyList<(string Role, string Content)> conversationHistory,
        string userMessage,
        IReadOnlyList<AiSkill> activeSkills,
        string apiKey,
        string? baseUrl,
        CancellationToken cancellationToken)
    {
        var messages = conversationHistory.ToList();
        messages.Add(("user", BuildUserMessage(context, userMessage)));

        var systemPrompt = BuildSystemPrompt(activeSkills);
        var allToolResults = new List<AgentToolResult>();
        var seededResults = await ExecuteHeuristicToolsAsync(context, userMessage, cancellationToken).ConfigureAwait(false);
        if (seededResults.Count > 0)
        {
            allToolResults.AddRange(seededResults);
            messages.Add(("user", BuildObservationMessage(seededResults, reachedToolLimit: false)));
        }
        var toolCallCount = 0;
        var reachedToolLimit = false;

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var assistantText = await _llmClient.SendChatAsync(
                systemPrompt,
                messages,
                apiKey,
                baseUrl,
                cancellationToken).ConfigureAwait(false);

            var toolCalls = TryParseToolCalls(assistantText);
            if (toolCalls.Count == 0)
            {
                return new AgentResponse(assistantText, [], allToolResults, reachedToolLimit);
            }

            if (round == MaxToolRounds || toolCallCount >= MaxToolCalls)
            {
                reachedToolLimit = true;
                break;
            }

            var allowedCalls = toolCalls.Take(MaxToolCalls - toolCallCount).ToList();
            if (allowedCalls.Count < toolCalls.Count)
            {
                reachedToolLimit = true;
            }

            messages.Add(("assistant", assistantText));

            var roundResults = new List<AgentToolResult>();
            foreach (var toolCall in allowedCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ExecuteToolAsync(context, toolCall, cancellationToken).ConfigureAwait(false);
                roundResults.Add(result);
                allToolResults.Add(result);
                toolCallCount++;
            }

            messages.Add(("user", BuildObservationMessage(roundResults, reachedToolLimit)));
        }

        messages.Add(("user", "Tool call limit reached. Provide the best possible final answer from the observations already supplied. Do not request more tools."));
        var finalText = await _llmClient.SendChatAsync(systemPrompt, messages, apiKey, baseUrl, cancellationToken).ConfigureAwait(false);
        return new AgentResponse(finalText, [], allToolResults, true);
    }


    private async Task<IReadOnlyList<AgentToolResult>> ExecuteHeuristicToolsAsync(AgentContext context, string userMessage, CancellationToken cancellationToken)
    {
        var calls = BuildHeuristicToolCalls(context, userMessage);
        if (calls.Count == 0)
        {
            return [];
        }

        var results = new List<AgentToolResult>();
        foreach (var call in calls.Take(3))
        {
            results.Add(await ExecuteToolAsync(context, call, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static IReadOnlyList<AgentToolCall> BuildHeuristicToolCalls(AgentContext context, string userMessage)
    {
        var lower = userMessage.ToLowerInvariant();
        var calls = new List<AgentToolCall>();
        var targetPath = ExtractLikelyPath(userMessage) ?? context.LinkedEntry?.Path;

        if (ContainsAny(lower, "最大文件", "大文件", "largest", "large files", "biggest files"))
        {
            calls.Add(new AgentToolCall("heuristic_large_files", "find_large_files", JsonSerializer.SerializeToElement(new
            {
                under_path = targetPath,
                max_results = 20
            }, AgentJson.Options)));
        }

        if (ContainsAny(lower, "深入", "分析", "看看", "summary", "summarize", "占用") && !string.IsNullOrWhiteSpace(targetPath))
        {
            calls.Add(new AgentToolCall("heuristic_summary", "summarize_subtree", JsonSerializer.SerializeToElement(new
            {
                path = targetPath,
                top_children = 20
            }, AgentJson.Options)));
        }

        var nameQuery = ExtractNameQuery(userMessage);
        if (!string.IsNullOrWhiteSpace(nameQuery))
        {
            calls.Add(new AgentToolCall("heuristic_find_name", "find_by_name", JsonSerializer.SerializeToElement(new
            {
                name = nameQuery,
                max_results = 20
            }, AgentJson.Options)));
        }

        return calls;
    }

    private static string? ExtractLikelyPath(string text)
    {
        var match = System.Text.RegularExpressions.Regex.Match(text, @"[A-Za-z]:[\\/][^\r\n\""']+");
        return match.Success ? match.Value.Trim().TrimEnd('.', ',', '，', '。') : null;
    }

    private static string? ExtractNameQuery(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var marker in new[] { "找一下", "查找", "搜索", "find", "search" })
        {
            var index = lower.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var tail = text[(index + marker.Length)..].Trim();
            var token = tail.Split([' ', '，', ',', '。', '.', '的'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(token) && !token.Contains(':', StringComparison.Ordinal))
            {
                return token.Trim('"', '\'', '“', '”');
            }
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] values)
    {
        return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
    private async Task<AgentToolResult> ExecuteToolAsync(AgentContext context, AgentToolCall toolCall, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(toolCall.Name, out var tool))
        {
            return new AgentToolResult(
                toolCall.Id,
                toolCall.Name,
                true,
                JsonError("unknown_tool", $"Unknown tool: {toolCall.Name}"),
                $"Unknown tool: {toolCall.Name}");
        }

        try
        {
            var content = await tool.ExecuteAsync(context, toolCall.Arguments, cancellationToken).ConfigureAwait(false);
            var isError = content.ValueKind == JsonValueKind.Object
                && content.TryGetProperty("ok", out var ok)
                && ok.ValueKind == JsonValueKind.False;

            return new AgentToolResult(toolCall.Id, tool.Name, isError, content, isError ? "Tool returned an error result." : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AgentToolResult(
                toolCall.Id,
                toolCall.Name,
                true,
                JsonError("execution_failed", ex.Message),
                ex.Message);
        }
    }

    private string BuildSystemPrompt(IReadOnlyList<AiSkill> activeSkills)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are a disk space analysis assistant with read-only access to the already scanned file tree.");
        builder.AppendLine();
        builder.AppendLine("Guidelines:");
        builder.AppendLine("- You MUST use tools for path lookup, name lookup, list children, subtree summary, largest files, or multi-step deep-dive requests.");
        builder.AppendLine("- Never claim to execute commands, delete files, move files, or rescan disk. Tools are read-only and query only the in-memory scan tree.");
        builder.AppendLine("- Base path and size claims on provided context or tool observations.");
        builder.AppendLine("- When you need a tool, respond with a single JSON object only, no markdown, no prose:");
        builder.AppendLine("  {\"tool_calls\":[{\"id\":\"call_1\",\"name\":\"tool_name\",\"arguments\":{}}]}");
        builder.AppendLine("- After observations are provided, answer normally in the user's language.");
        builder.AppendLine();
        if (activeSkills.Count > 0)
        {
            builder.AppendLine("Active disk-management skills for this turn:");
            foreach (var skill in activeSkills)
            {
                builder.AppendLine($"- {skill.Id}: {skill.Prompt}");
            }
            builder.AppendLine();
        }

        builder.AppendLine("Available tools:");

        foreach (var tool in _tools.Values.OrderBy(tool => tool.Name))
        {
            builder.AppendLine($"- {tool.Name} (risk: {tool.RiskLevel}): {tool.Description}");
            builder.AppendLine($"  schema: {tool.Schema.GetRawText()}");
        }

        return builder.ToString();
    }

    private static string BuildUserMessage(AgentContext context, string userMessage)
    {
        var contextBlock = new
        {
            current_view_path = context.CurrentViewRoot.Path,
            current_view_items = context.CurrentViewRoot.Children
                .OrderByDescending(child => child.Size)
                .Take(80)
                .Select(ToEntryContext),
            selected_item = BuildSelectedItem(context.LinkedEntry, context.LinkedRecommendation),
            scan_summary = new
            {
                root_path = context.Session.RootEntry?.Path,
                total_size_bytes = context.Session.TotalSize,
                total_files = context.Session.TotalFiles,
                total_folders = context.Session.TotalFolders,
                drive_capacity_bytes = context.Session.DriveCapacity,
                drive_free_space_bytes = context.Session.DriveFreeSpace
            }
        };

        return $"Scan context JSON:\n{JsonSerializer.Serialize(contextBlock, AgentJson.Options)}\n\nUser question: {userMessage}";
    }

    private static object? BuildSelectedItem(FileEntry? linkedEntry, CleanupRecommendation? linkedRecommendation)
    {
        if (linkedEntry is not null)
        {
            return ToEntryContext(linkedEntry);
        }

        if (linkedRecommendation is null)
        {
            return null;
        }

        return new
        {
            path = linkedRecommendation.TargetPath,
            size_bytes = linkedRecommendation.Size,
            category = linkedRecommendation.Category.ToString(),
            safety_rating = linkedRecommendation.SafetyRating.ToString(),
            type = linkedRecommendation.Entry?.IsDirectory == true ? "directory" : "file"
        };
    }

    private static object ToEntryContext(FileEntry entry)
    {
        return new
        {
            path = entry.Path,
            name = entry.Name,
            size_bytes = entry.Size,
            type = entry.IsDirectory ? "directory" : "file",
            extension = entry.Extension,
            child_count = entry.IsDirectory ? entry.Children.Count : 0
        };
    }

    private static string BuildObservationMessage(IReadOnlyList<AgentToolResult> results, bool reachedToolLimit)
    {
        var observationsJson = JsonSerializer.Serialize(new
        {
            tool_observations = results.Select(result => new
            {
                id = result.ToolCallId,
                tool = result.ToolName,
                is_error = result.IsError,
                content = result.Content
            }),
            reached_tool_limit = reachedToolLimit
        }, AgentJson.Options);

        if (observationsJson.Length > MaxObservationChars)
        {
            observationsJson = observationsJson[..MaxObservationChars] + "\n...TRUNCATED...";
        }

        return $"Tool observations JSON:\n{observationsJson}\n\nContinue. If you have enough evidence, provide the final answer. If another read-only query is necessary, request another tool call JSON object.";
    }

    private static IReadOnlyList<AgentToolCall> TryParseToolCalls(string text)
    {
        var json = ExtractJsonObject(text);
        if (json is null)
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<AgentToolCall>();
            var index = 0;
            foreach (var call in calls.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object || !call.TryGetProperty("name", out var nameElement))
                {
                    continue;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                    ? idElement.GetString() ?? $"call_{index + 1}"
                    : $"call_{index + 1}";
                var arguments = call.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object
                    ? args.Clone()
                    : JsonSerializer.SerializeToElement(new { }, AgentJson.Options);

                results.Add(new AgentToolCall(id, name, arguments));
                index++;
            }

            return results;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd >= 0 && lastFence > firstLineEnd)
            {
                trimmed = trimmed[(firstLineEnd + 1)..lastFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : null;
    }

    private static JsonElement JsonError(string code, string message)
    {
        return JsonSerializer.SerializeToElement(new
        {
            ok = false,
            error = new
            {
                code,
                message
            }
        }, AgentJson.Options);
    }
}




using System.Text.Json;

namespace SpaceMonger.Core.Services.Agent;

public sealed record AgentToolCall(
    string Id,
    string Name,
    JsonElement Arguments);

public sealed record AgentToolResult(
    string ToolCallId,
    string ToolName,
    bool IsError,
    JsonElement Content,
    string? ErrorMessage = null);

public sealed record AgentRequest(
    string SystemPrompt,
    IReadOnlyList<(string Role, string Content)> Messages,
    AgentContext Context,
    IReadOnlyList<AgentToolDefinition> Tools);

public sealed record AgentResponse(
    string Content,
    IReadOnlyList<AgentToolCall> ToolCalls,
    IReadOnlyList<AgentToolResult> ToolResults,
    bool ReachedToolLimit,
    JsonElement? Proposal = null);

public sealed record AgentToolDefinition(
    string Name,
    string Description,
    JsonElement Schema,
    ToolRiskLevel RiskLevel);

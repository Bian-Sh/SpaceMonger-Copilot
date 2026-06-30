using System.Text.Json;
using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Services.Agent;

public abstract class AppCopilotToolBase : IAgentTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JsonElement Schema { get; }
    public virtual ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresScanContext => false;

    public abstract Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken);

    protected static JsonElement Json(object value)
    {
        return JsonSerializer.SerializeToElement(value, AgentJson.Options);
    }

    protected static JsonElement SchemaJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    protected static string? GetString(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

public sealed class GetCopilotContextTool : AppCopilotToolBase
{
    public override string Name => "get_copilot_context";
    public override string Description => "Get current scan, view, selection, and recommendation availability state for the copilot.";
    public override ToolRiskLevel RiskLevel => ToolRiskLevel.ReadOnly;
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{}}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        return Task.FromResult(Json(new
        {
            ok = true,
            has_scan = context.Session is not null,
            scan_root_path = context.Session?.RootEntry?.Path,
            current_view_path = context.CurrentViewRoot?.Path,
            selected_path = context.LinkedEntry?.Path ?? context.LinkedRecommendation?.TargetPath,
            has_existing_recommendations = context.HasExistingRecommendations
        }));
    }
}

public sealed class ProposeCopilotActionTool : AppCopilotToolBase
{
    public override string Name => "propose_copilot_action";
    public override string Description => "Propose a first-level confirmation card for scan, cleanup analysis, or navigation without executing it.";
    public override JsonElement Schema { get; } = SchemaJson("""
        {"type":"object","properties":{"kind":{"type":"string","enum":["StartScan","AnalyzeCleanup","DiscoverUnityLibraries","ClearConversation","NavigateToScannedPath","scan","analyze_cleanup","discover_unity_libraries","clear_conversation","navigate"]},"path":{"type":"string"},"scope_label":{"type":"string"},"will_overwrite_existing_data":{"type":"boolean"},"title":{"type":"string"},"description":{"type":"string"},"impact":{"type":"string"},"confirm_text":{"type":"string"},"cancel_text":{"type":"string"}},"required":["kind"]}
        """);

    public override Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        var kind = GetString(arguments, "kind")?.Trim();
        var path = GetString(arguments, "path")?.Trim();
        var scopeLabel = GetString(arguments, "scope_label")?.Trim();
        var title = GetString(arguments, "title")?.Trim();
        var description = GetString(arguments, "description")?.Trim();
        var impact = GetString(arguments, "impact")?.Trim();
        var confirmText = GetString(arguments, "confirm_text")?.Trim();
        var cancelText = GetString(arguments, "cancel_text")?.Trim();
        var willOverwrite = arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("will_overwrite_existing_data", out var overwriteProp)
            && overwriteProp.ValueKind is JsonValueKind.True or JsonValueKind.False
            && overwriteProp.GetBoolean();

        var actionKind = ResolveActionKind(kind);

        if (actionKind == AiActionKind.None)
        {
            return Task.FromResult(Json(new { ok = false, error = new { code = "invalid_kind", message = "Unsupported copilot action kind." } }));
        }

        return Task.FromResult(Json(new
        {
            ok = true,
            proposal = new
            {
                action = new
                {
                    kind = actionKind.ToString(),
                    path,
                    scope_label = scopeLabel,
                    will_overwrite_existing_data = willOverwrite
                },
                card = new
                {
                    title,
                    description,
                    impact,
                    confirm_text = confirmText,
                    cancel_text = cancelText
                }
            }
        }));
    }

    private static AiActionKind ResolveActionKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return AiActionKind.None;
        }

        if (Enum.TryParse<AiActionKind>(kind, ignoreCase: true, out var actionKind))
        {
            return actionKind;
        }

        return kind switch
        {
            "scan" => AiActionKind.StartScan,
            "analyze_cleanup" => AiActionKind.AnalyzeCleanup,
            "discover_unity_libraries" => AiActionKind.DiscoverUnityLibraries,
            "clear_conversation" => AiActionKind.ClearConversation,
            "navigate" => AiActionKind.NavigateToScannedPath,
            _ => AiActionKind.None
        };
    }
}


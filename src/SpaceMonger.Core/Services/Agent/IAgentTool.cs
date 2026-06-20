using System.Text.Json;

namespace SpaceMonger.Core.Services.Agent;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    ToolRiskLevel RiskLevel { get; }
    JsonElement Schema { get; }

    Task<JsonElement> ExecuteAsync(AgentContext context, JsonElement arguments, CancellationToken cancellationToken);
}

using System.Text.Json;
using FluentAssertions;
using SpaceMonger.Core.Services.Agent;

namespace SpaceMonger.Core.Tests;

public class AgentProposalTests
{
    [Fact]
    public async Task ProposeCopilotActionTool_ReturnsStructuredProposal()
    {
        var tool = new ProposeCopilotActionTool();
        var context = new AgentContext(null, null, null, null, false);
        var arguments = JsonSerializer.SerializeToElement(new
        {
            kind = "scan",
            path = @"D:\Downloads",
            scope_label = @"D:\Downloads",
            title = "扫描这个路径",
            description = "需要先扫描这个路径，才能继续分析。",
            impact = "会创建新的扫描结果。",
            confirm_text = "开始扫描",
            cancel_text = "取消"
        });

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);

        result.TryGetProperty("proposal", out var proposal).Should().BeTrue();
        proposal.GetProperty("action").GetProperty("kind").GetString().Should().Be(nameof(SpaceMonger.Core.Services.Copilot.AiActionKind.StartScan));
        proposal.GetProperty("card").GetProperty("title").GetString().Should().Be("扫描这个路径");
    }
}

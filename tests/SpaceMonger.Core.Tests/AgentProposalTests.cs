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
    [Fact]
    public async Task ReadUnityRegistryContextTool_ReadsFixedUnityRegistryAllowlist()
    {
        var reader = new FakeRegistryReader();
        var tool = new ReadUnityRegistryContextTool(reader);
        var context = new AgentContext(null, null, null, null, false);
        var arguments = JsonSerializer.SerializeToElement(new { });

        var result = await tool.ExecuteAsync(context, arguments, CancellationToken.None);

        result.GetProperty("ok").GetBoolean().Should().BeTrue();
        result.GetProperty("purpose").GetString().Should().Contain("do not infer Hub project membership");
        reader.ReadPaths.Should().Contain(@"Software\Unity Technologies\Installer");
        reader.ReadPaths.Should().Contain(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Unity Hub");
        result.GetProperty("keys").GetArrayLength().Should().Be(6);
    }

    private sealed class FakeRegistryReader : IWindowsRegistryReader
    {
        public List<string> ReadPaths { get; } = [];

        public RegistryKeySnapshot ReadKey(Microsoft.Win32.RegistryHive hive, Microsoft.Win32.RegistryView view, string path)
        {
            ReadPaths.Add(path);
            return new RegistryKeySnapshot(hive.ToString(), view.ToString(), path, true, new Dictionary<string, object?>
            {
                ["DisplayName"] = "Unity Hub"
            });
        }
    }
}

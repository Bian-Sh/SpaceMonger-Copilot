using FluentAssertions;
using NSubstitute;
using System.Text.Json;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Agent;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Tests;

public class ChatServiceStreamingTests
{
    [Fact]
    public async Task StreamMessageWithThinkingAsync_EmitsAnswerInMultipleChunks()
    {
        const string answer = "First line of the answer.\nSecond line of the answer with more details.";
        var runtime = Substitute.For<IAgentRuntime>();
        runtime.RunAsync(
                Arg.Any<AgentContext?>(),
                Arg.Any<IReadOnlyList<(string Role, string Content)>>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<AiSkill>>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AgentResponse(answer, [], [], false));

        var service = new ChatService(runtime);
        var chunks = new List<string>();

        var response = await service.StreamMessageWithThinkingAsync(
            "question",
            null,
            null,
            new FileEntry { Path = @"C:\", Name = "C:", IsDirectory = true },
            new ScanSession { TargetPath = @"C:\" },
            false,
            [],
            "zh-CN",
            "key",
            null,
            false,
            null,
            chunks.Add,
            CancellationToken.None);

        response.Text.Should().Be(answer);
        chunks.Should().HaveCountGreaterThan(1);
        string.Concat(chunks).Should().Be(answer);
    }

    [Fact]
    public async Task StreamMessageWithThinkingAsync_EmitsGenericToolObservationNote()
    {
        var runtime = Substitute.For<IAgentRuntime>();
        runtime.RunAsync(
                Arg.Any<AgentContext?>(),
                Arg.Any<IReadOnlyList<(string Role, string Content)>>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<AiSkill>>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AgentResponse(
                "done",
                [],
                [
                    new AgentToolResult("call_1", "get_copilot_context", false, JsonSerializer.SerializeToElement(new { ok = true })),
                    new AgentToolResult("call_2", "list_top_children", true, JsonSerializer.SerializeToElement(new { error = "missing scan" }))
                ],
                true));

        var service = new ChatService(runtime);
        var thinkingChunks = new List<string>();
        var textChunks = new List<string>();

        await service.StreamMessageWithThinkingAsync(
            "question",
            null,
            null,
            new FileEntry { Path = @"C:\", Name = "C:", IsDirectory = true },
            new ScanSession { TargetPath = @"C:\" },
            false,
            [],
            "zh-CN",
            "key",
            null,
            true,
            thinkingChunks.Add,
            textChunks.Add,
            CancellationToken.None);

        var thinking = string.Concat(thinkingChunks);
        thinking.Should().Contain("Processed 2 agent tool observation(s): 1 succeeded, 1 failed.");
        thinking.Should().Contain("Tool limit reached");
        thinking.Should().NotContain("read-only file tree");
        thinking.Should().NotContain("Ignored");
    }

    [Fact]
    public async Task StreamSkillMessageWithThinkingAsync_EmitsGenericToolObservationNote()
    {
        var runtime = Substitute.For<IAgentRuntime>();
        runtime.RunAsync(
                Arg.Any<AgentContext?>(),
                Arg.Any<IReadOnlyList<(string Role, string Content)>>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<AiSkill>>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<CancellationToken>())
            .Returns(new AgentResponse(
                "done",
                [],
                [new AgentToolResult("call_1", "propose_copilot_action", false, JsonSerializer.SerializeToElement(new { action = "StartScan" }))],
                false));

        var service = new ChatService(runtime);
        var thinkingChunks = new List<string>();

        await service.StreamSkillMessageWithThinkingAsync(
            "scan please",
            [],
            "zh-CN",
            "key",
            null,
            true,
            thinkingChunks.Add,
            _ => { },
            CancellationToken.None);

        var thinking = string.Concat(thinkingChunks);
        thinking.Should().Contain("Processed 1 agent tool observation(s): 1 succeeded, 0 failed.");
        thinking.Should().NotContain("file tree");
        thinking.Should().NotContain("no scan context");
        thinking.Should().NotContain("Ignored");
    }
}

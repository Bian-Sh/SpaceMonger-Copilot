using FluentAssertions;
using NSubstitute;
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
}

using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.App.Services.Copilot;

public interface IAiDiskActionExecutor
{
    bool HasExistingRecommendations { get; }
    Task<AiActionResult> ExecuteAsync(AiActionRequest request, CancellationToken cancellationToken);
}

public sealed class NullAiDiskActionExecutor : IAiDiskActionExecutor
{
    public bool HasExistingRecommendations => false;

    public Task<AiActionResult> ExecuteAsync(AiActionRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(AiActionResult.Fail("AI 动作执行器尚未连接到主窗口。"));
    }
}

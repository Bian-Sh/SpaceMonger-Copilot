using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpaceMonger.Core.Services.Copilot;

public enum AiIntent
{
    GeneralChat,
    Identity,
    ModuleHelp,
    DiskScan,
    FolderCleanupAnalysis,
    FileTreeQuery,
    RecommendationCleanup,
    TreemapNavigation
}

public enum AiActionKind
{
    None,
    StartScan,
    AnalyzeCleanup,
    ClearConversation,
    NavigateToScannedPath,
    SelectRecommendation,
    DeselectRecommendation
}

public enum AiInteractionCardStatus
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}

public sealed record AiSkill(
    string Id,
    AiIntent Intent,
    string Description,
    string Prompt);

public sealed record AiActionRequest(
    AiActionKind Kind,
    string? Path = null,
    string? RecommendationId = null,
    bool WillOverwriteExistingData = false,
    string? ScopeLabel = null);

public sealed record AiActionResult(
    bool Success,
    string Message,
    string? Details = null)
{
    public static AiActionResult Ok(string message, string? details = null) => new(true, message, details);
    public static AiActionResult Fail(string message, string? details = null) => new(false, message, details);
}

public sealed class AiInteractionCard : INotifyPropertyChanged
{
    private AiInteractionCardStatus _status = AiInteractionCardStatus.Pending;
    private string? _statusText;
    private bool _isBusy;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? Impact { get; init; }
    public string ConfirmText { get; init; } = "确认";
    public string CancelText { get; init; } = "取消";
    public string? FollowUpPrompt { get; init; }
    public required AiActionRequest Action { get; init; }

    public AiInteractionCardStatus Status
    {
        get => _status;
        set
        {
            if (_status == value)
                return;

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPending));
            OnPropertyChanged(nameof(IsFinished));
        }
    }

    public string? StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText == value)
                return;

            _statusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatusText));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
                return;

            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsPending));
        }
    }

    public bool IsPending => Status == AiInteractionCardStatus.Pending && !IsBusy;
    public bool IsFinished => Status is AiInteractionCardStatus.Completed or AiInteractionCardStatus.Cancelled or AiInteractionCardStatus.Failed;
    public bool HasStatusText => !string.IsNullOrWhiteSpace(StatusText);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record AiSkillRoutingResult(
    IReadOnlyList<AiIntent> Intents,
    IReadOnlyList<AiSkill> Skills,
    AiActionRequest? SuggestedAction,
    string? LocalAnswer = null,
    bool CanRunWithoutScanContext = false,
    bool PreferModelAnswer = false);

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services.Copilot;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Llm;
using SpaceMonger.Core.Services.Scanning;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ISettingsService _settingsService;
    private readonly IAiSkillRouter _skillRouter;
    private readonly ILogger<ChatViewModel> _logger;
    private IAiDiskActionExecutor _actionExecutor = new NullAiDiskActionExecutor();
    private CancellationTokenSource? _followUpCancellation;
    private CancellationTokenSource? _activeOperationCancellation;

    private ScanSession? _currentSession;
    private FileEntry? _currentViewRoot;

    [ObservableProperty] private ObservableCollection<ChatMessage> _messages = new();
    [ObservableProperty] private bool _hasMessages;
    [ObservableProperty] private string? _inputText;
    [ObservableProperty] private bool _isSlashCommandMenuOpen;
    [ObservableProperty] private bool _isSkillMentionMenuOpen;
    [ObservableProperty] private AiInteractionCard? _pendingInteractionCard;
    [ObservableProperty] private bool _isWorkflowProgressVisible;
    [ObservableProperty] private int _currentWorkflowStepNumber;
    [ObservableProperty] private ObservableCollection<CopilotWorkflowStep> _workflowSteps = new();
    [ObservableProperty] private ObservableCollection<ChatCommandSuggestion> _slashCommandSuggestions = new(BuildSlashCommandSuggestions());
    [ObservableProperty] private ObservableCollection<ChatSkillSuggestion> _skillMentionSuggestions = new();
    [ObservableProperty] private ObservableCollection<ChatSkillSuggestion> _filteredSkillMentionSuggestions = new();
    [ObservableProperty] private ChatCommandSuggestion? _selectedSlashCommandSuggestion;
    [ObservableProperty] private ChatSkillSuggestion? _selectedSkillMentionSuggestion;
    [ObservableProperty] private FileEntry? _linkedEntry;
    [ObservableProperty] private CleanupRecommendation? _linkedRecommendation;
    [ObservableProperty] private bool _isChatAvailable;
    [ObservableProperty] private bool _isApiKeyConfigured;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private bool _isOperationRunning;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _linkedItemPath;

    public ChatViewModel(IChatService chatService, ISettingsService settingsService, IAiSkillRouter skillRouter, ILogger<ChatViewModel>? logger = null)
    {
        _chatService = chatService;
        _settingsService = settingsService;
        _skillRouter = skillRouter;
        _logger = logger ?? NullLogger<ChatViewModel>.Instance;
        _logger.LogInformation("ChatViewModel created");
        SkillMentionSuggestions = new ObservableCollection<ChatSkillSuggestion>(
            _skillRouter.GetSkillCatalog().Select(skill => new ChatSkillSuggestion($"@{skill.Id}", skill.DisplayName, skill.Description)));
        FilteredSkillMentionSuggestions = new ObservableCollection<ChatSkillSuggestion>(SkillMentionSuggestions);
        Messages.CollectionChanged += Messages_CollectionChanged;
        L.LanguageChanged += RefreshSlashCommandSuggestions;
        RefreshApiKeyStatus();
    }

    public event Action? ClearConsoleRequested;

    public void SetActionExecutor(IAiDiskActionExecutor actionExecutor) => _actionExecutor = actionExecutor;

    partial void OnMessagesChanged(ObservableCollection<ChatMessage>? oldValue, ObservableCollection<ChatMessage> newValue)
    {
        if (oldValue is not null) oldValue.CollectionChanged -= Messages_CollectionChanged;
        newValue.CollectionChanged += Messages_CollectionChanged;
        HasMessages = newValue.Count > 0;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => HasMessages = Messages.Count > 0;

    public void SetContext(ScanSession session, FileEntry viewRoot)
    {
        _currentSession = session;
        _currentViewRoot = viewRoot;
        IsChatAvailable = true;
        RefreshApiKeyStatus();
    }

    public void UpdateViewRoot(FileEntry viewRoot) => _currentViewRoot = viewRoot;

    public void RefreshApiKeyStatus()
    {
        var settings = _settingsService.LoadSettings();
        IsApiKeyConfigured = !string.IsNullOrEmpty(_settingsService.GetApiKey(settings));
    }

    public bool HasPendingInteractionCard => PendingInteractionCard is not null;
    public bool HasWorkflowSteps => WorkflowSteps.Count > 0;
    public bool ShouldShowWorkflowStepIndicator => WorkflowSteps.Count > 1;
    public string WorkflowProgressText => HasWorkflowSteps
        ? Localized($"Step {CurrentWorkflowStepNumber}/{WorkflowSteps.Count}", $"\u7b2c {CurrentWorkflowStepNumber}/{WorkflowSteps.Count} \u6b65")
        : string.Empty;
    public string CurrentWorkflowIconState => CurrentWorkflowStepNumber > 0 && CurrentWorkflowStepNumber <= WorkflowSteps.Count
        ? WorkflowSteps[CurrentWorkflowStepNumber - 1].StatusIconState
        : "idle";
    public string SendButtonText => IsOperationRunning ? Localized("Stop", "\u505c\u6b62") : L.Text("SendButton");
    public bool IsCompletionMenuOpen => IsSlashCommandMenuOpen || IsSkillMentionMenuOpen;

    partial void OnLinkedEntryChanged(FileEntry? value) => LinkedItemPath = value?.Path;
    partial void OnLinkedRecommendationChanged(CleanupRecommendation? value) => LinkedItemPath = value?.TargetPath;
    partial void OnInputTextChanged(string? value)
    {
        IsSlashCommandMenuOpen = IsSlashCommandPrompt(value);
        IsSkillMentionMenuOpen = IsSkillMentionPrompt(value);
        FilteredSkillMentionSuggestions = new ObservableCollection<ChatSkillSuggestion>(BuildFilteredSkillMentionSuggestions(value));
        SelectedSlashCommandSuggestion = IsSlashCommandMenuOpen ? SlashCommandSuggestions.FirstOrDefault() : null;
        SelectedSkillMentionSuggestion = IsSkillMentionMenuOpen ? FilteredSkillMentionSuggestions.FirstOrDefault() : null;
        OnPropertyChanged(nameof(IsCompletionMenuOpen));
    }

    partial void OnIsSlashCommandMenuOpenChanged(bool value) => OnPropertyChanged(nameof(IsCompletionMenuOpen));
    partial void OnIsSkillMentionMenuOpenChanged(bool value) => OnPropertyChanged(nameof(IsCompletionMenuOpen));
    partial void OnPendingInteractionCardChanged(AiInteractionCard? value) => OnPropertyChanged(nameof(HasPendingInteractionCard));
    partial void OnIsOperationRunningChanged(bool value) => OnPropertyChanged(nameof(SendButtonText));
    partial void OnCurrentWorkflowStepNumberChanged(int value)
    {
        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SubmitOrStopAsync()
    {
        if (IsOperationRunning)
        {
            CancelActiveOperation();
            return;
        }

        await SendAsync();
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsOperationRunning)
        {
            CancelActiveOperation();
            return;
        }

        RefreshApiKeyStatus();
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userInput = InputText.Trim();
        _logger.LogInformation("Chat send requested; length={Length}", userInput.Length);
        PendingInteractionCard = null;
        var settings = _settingsService.LoadSettings();
        var responseLanguage = ResolveResponseLanguage(settings.Language);

        if (TryExecuteSlashCommand(userInput))
        {
            InputText = null;
            IsSlashCommandMenuOpen = false;
            return;
        }

        Messages.Add(new ChatMessage
        {
            Sender = ChatSender.User,
            Text = userInput,
            Timestamp = DateTime.Now,
            LinkedEntry = LinkedEntry,
            LinkedRecommendation = LinkedRecommendation
        });
        InputText = null;

        var routed = _skillRouter.Route(userInput, LinkedEntry, _currentViewRoot, _actionExecutor.HasExistingRecommendations, responseLanguage);
        if (!IsApiKeyConfigured)
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = Localized("Configure a model service API Key before using Copilot.", "需要先配置模型服务 API Key，才能使用 Copilot。"),
                Timestamp = DateTime.Now,
                IsError = true
            });
            return;
        }

        IsSending = true;
        ErrorMessage = null;
        var cancellationToken = BeginActiveOperation();

        var assistantMessage = new ChatMessage
        {
            Sender = ChatSender.Assistant,
            Text = string.Empty,
            Thinking = string.Empty,
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        var operationStartedAt = DateTime.Now;
        var operationStatus = "completed";
        using var operationStatusCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var operationStatusTask = UpdateOperationStatusAsync(assistantMessage, operationStartedAt, operationStatusCancellation.Token);
        Messages.Add(assistantMessage);

        try
        {
            var apiKey = _settingsService.GetApiKey(settings)!;
            var baseUrl = settings.AnthropicBaseUrl;
            var modelUserInput = BuildModelUserInput(userInput, routed, LinkedEntry, LinkedRecommendation, _currentViewRoot, _currentSession, _actionExecutor.HasExistingRecommendations);
            var enableThinking = ShouldEnableThinking(settings, routed);

            ChatResponse response;

            if (_currentSession is not null && _currentViewRoot is not null)
            {
                response = await _chatService.StreamMessageWithThinkingAsync(
                    modelUserInput,
                    LinkedEntry,
                    LinkedRecommendation,
                    _currentViewRoot,
                    _currentSession,
                    _actionExecutor.HasExistingRecommendations,
                    routed.Skills,
                    responseLanguage,
                    apiKey,
                    baseUrl,
                    enableThinking,
                    thinkingToken => assistantMessage.Thinking += thinkingToken,
                    textToken => assistantMessage.Text += textToken,
                    cancellationToken);
            }
            else
            {
                response = await _chatService.StreamSkillMessageWithThinkingAsync(
                    modelUserInput,
                    routed.Skills,
                    responseLanguage,
                    apiKey,
                    baseUrl,
                    enableThinking,
                    thinkingToken => assistantMessage.Thinking += thinkingToken,
                    textToken => assistantMessage.Text += textToken,
                    cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(assistantMessage.Text) && !string.IsNullOrWhiteSpace(response.Text))
            {
                assistantMessage.Text = response.Text;
            }

            if (await TryRunDirectActionProposalAsync(assistantMessage, response.Proposal, routed, userInput, cancellationToken))
            {
                return;
            }

            ApplyProposalIfAny(assistantMessage, response.Proposal);

            if (response.Proposal.HasValue && assistantMessage.InteractionCard is null)
            {
                _logger.LogWarning("Chat response contained a proposal that could not be converted to an interaction card: {Proposal}", response.Proposal.Value.GetRawText());
            }

            _logger.LogInformation("Chat response completed; textLength={TextLength}, hasProposal={HasProposal}", assistantMessage.Text.Length, response.Proposal.HasValue);
            MoveMessageInteractionCardToInputOverlay(assistantMessage);
            assistantMessage.IsStreaming = false;
            LinkedEntry = null;
            LinkedRecommendation = null;
            LinkedItemPath = null;
        }
        catch (OperationCanceledException)
        {
            operationStatus = "stopped";
            _logger.LogWarning("Chat operation cancelled");
            assistantMessage.IsStreaming = false;
            assistantMessage.IsError = true;
            assistantMessage.Text = string.IsNullOrEmpty(assistantMessage.Text)
                ? Localized("Stopped.", "\u5df2\u505c\u6b62\u3002")
                : assistantMessage.Text + Localized("\nStopped.", "\n\u5df2\u505c\u6b62\u3002");
            MarkRunningWorkflowStep(false);
            HideWorkflowProgress();
        }
        catch (Exception ex)
        {
            operationStatus = "failed";
            _logger.LogError(ex, "Chat operation failed");
            assistantMessage.IsStreaming = false;
            assistantMessage.IsError = true;
            assistantMessage.Text = string.IsNullOrEmpty(assistantMessage.Text)
                ? ex.Message
                : assistantMessage.Text + L.Format("ChatErrorAppend", ex.Message);
            if (routed.SelectedSkillIds.Count > 0 && WorkflowSteps.Count > 0)
            {
                CompleteWorkflowStep(Math.Max(0, CurrentWorkflowStepNumber - 1), false);
            }
            ErrorMessage = ex.Message;
        }
        finally
        {
            operationStatusCancellation.Cancel();
            try { await operationStatusTask; } catch (OperationCanceledException) { }
            if (operationStatus == "completed" && assistantMessage.IsError)
            {
                operationStatus = "failed";
            }
            var operationCompletedAt = DateTime.Now;
            assistantMessage.OperationStatusText = FormatOperationStatus(operationStatus, operationCompletedAt - operationStartedAt);
            assistantMessage.MarkCompletedAt(operationCompletedAt);
            IsSending = false;
            FinishActiveOperation();
            if (!assistantMessage.IsStreaming)
            {
                HideWorkflowProgress();
            }
        }
    }


    private async Task<bool> TryRunDirectActionProposalAsync(ChatMessage assistantMessage, JsonElement? proposal, AiSkillRoutingResult routed, string userInput, CancellationToken cancellationToken)
    {
        if (!TryGetActionRequest(proposal, out var action) || !ShouldExecuteProposalDirectly(action, routed))
        {
            return false;
        }

        action = ResolveActionPath(action);

        if (action.Kind == AiActionKind.AnalyzeCleanup && !IsCleanupAnalysisIntentClear(userInput, action))
        {
            assistantMessage.InteractionCard = null;
            assistantMessage.IsStreaming = false;
            assistantMessage.Text = BuildAmbiguousAnalysisClarification();
            HideWorkflowProgress();
            return true;
        }

        if (action.Kind == AiActionKind.StartScan)
        {
            var path = action.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!Directory.Exists(path))
            {
                assistantMessage.InteractionCard = null;
                assistantMessage.IsStreaming = false;
                assistantMessage.IsError = true;
                assistantMessage.OperationResultText = Localized($"Path not found or cannot be scanned: {path}", $"路径不存在或无法扫描：{path}");
                return true;
            }
        }

        assistantMessage.InteractionCard = null;
        SetWorkflowPlan(BuildWorkflowSteps(action, null));
        StartWorkflowStep(0);
        var progress = new Progress<AiActionProgress>(ApplyWorkflowProgress);
        var result = await _actionExecutor.ExecuteAsync(action, cancellationToken, progress);
        CompleteWorkflowStep(0, result.Success);
        assistantMessage.IsStreaming = false;
        assistantMessage.IsError = !result.Success;
        if (action.Kind == AiActionKind.AnalyzeCleanup && result.Success)
        {
            assistantMessage.Text = string.Empty;
        }
        assistantMessage.OperationResultText = FormatDirectActionResult(action, result);
        if (!result.Success)
        {
            ErrorMessage = assistantMessage.OperationResultText;
        }

        return true;
    }

    private static bool ShouldExecuteProposalDirectly(AiActionRequest action, AiSkillRoutingResult routed)
        => action.Kind is AiActionKind.StartScan or AiActionKind.AnalyzeCleanup
           && !routed.SelectedSkillIds.Any(id => id.Contains("unity", StringComparison.OrdinalIgnoreCase));

    private static bool IsCleanupAnalysisIntentClear(string userInput, AiActionRequest action)
    {
        if (action.Kind != AiActionKind.AnalyzeCleanup)
        {
            return true;
        }

        var text = string.Join(' ', userInput, action.ScopeLabel, action.UserNotes).ToLowerInvariant();
        return text.Contains("清理", StringComparison.Ordinal)
               || text.Contains("释放", StringComparison.Ordinal)
               || text.Contains("可删", StringComparison.Ordinal)
               || text.Contains("删除建议", StringComparison.Ordinal)
               || text.Contains("推荐", StringComparison.Ordinal)
               || text.Contains("垃圾", StringComparison.Ordinal)
               || text.Contains("缓存", StringComparison.Ordinal)
               || text.Contains("临时", StringComparison.Ordinal)
               || text.Contains("cleanup", StringComparison.Ordinal)
               || text.Contains("clean up", StringComparison.Ordinal)
               || text.Contains("recommendation", StringComparison.Ordinal)
               || text.Contains("free space", StringComparison.Ordinal)
               || text.Contains("cache", StringComparison.Ordinal)
               || text.Contains("temp", StringComparison.Ordinal);
    }

    private static string BuildAmbiguousAnalysisClarification()
        => Localized(
            "What would you like me to analyze? I can scan a specific path, generate cleanup recommendations for the current scan, or explain the selected folder/file.",
            "你想让我分析什么？我可以扫描指定路径、为当前扫描生成推荐清理项，或解释选中的文件夹/文件。请明确一个方向后我再执行。");

    private static string FormatDirectActionResult(AiActionRequest action, AiActionResult result)
    {
        if (!result.Success)
        {
            return result.Details is null ? result.Message : $"{result.Message}{Environment.NewLine}{result.Details}";
        }

        if (action.Kind == AiActionKind.StartScan && !string.IsNullOrWhiteSpace(action.Path))
        {
            return Localized($"Scan complete: {action.Path}", $"扫描完成：{action.Path}");
        }

        return result.Details is null ? result.Message : $"{result.Message}{Environment.NewLine}{result.Details}";
    }

    [RelayCommand]
    private void SelectSlashCommand(ChatCommandSuggestion? suggestion)
    {
        if (suggestion is null) return;
        InputText = suggestion.Command;
        IsSlashCommandMenuOpen = false;
    }

    [RelayCommand]
    private void SelectSkillMention(ChatSkillSuggestion? suggestion)
    {
        if (suggestion is null) return;
        InputText = suggestion.Mention + " ";
        IsSkillMentionMenuOpen = false;
    }

    public void MoveCompletionSelection(int delta)
    {
        if (IsSlashCommandMenuOpen)
        {
            SelectedSlashCommandSuggestion = MoveSelection(SlashCommandSuggestions, SelectedSlashCommandSuggestion, delta);
            return;
        }

        if (IsSkillMentionMenuOpen)
        {
            SelectedSkillMentionSuggestion = MoveSelection(FilteredSkillMentionSuggestions, SelectedSkillMentionSuggestion, delta);
        }
    }

    public bool ConfirmActiveCompletion()
    {
        if (IsSlashCommandMenuOpen)
        {
            SelectSlashCommand(SelectedSlashCommandSuggestion ?? SlashCommandSuggestions.FirstOrDefault());
            return true;
        }

        if (IsSkillMentionMenuOpen)
        {
            SelectSkillMention(SelectedSkillMentionSuggestion ?? FilteredSkillMentionSuggestions.FirstOrDefault());
            return true;
        }

        return false;
    }

    private static T? MoveSelection<T>(IReadOnlyList<T> items, T? selected, int delta) where T : class
    {
        if (items.Count == 0) return null;
        var index = 0;
        if (selected is not null)
        {
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                if (EqualityComparer<T>.Default.Equals(items[itemIndex], selected))
                {
                    index = itemIndex;
                    break;
                }
            }
        }
        index = (index + delta + items.Count) % items.Count;
        return items[index];
    }

    private static bool IsSlashCommandPrompt(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed == "/" || (trimmed.StartsWith("/", StringComparison.Ordinal) && !trimmed.Contains(" ", StringComparison.Ordinal));
    }

    private static bool IsSkillMentionPrompt(string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        return trimmed == "@" || (trimmed.StartsWith("@", StringComparison.Ordinal) && !trimmed.Contains(" ", StringComparison.Ordinal));
    }

    private IEnumerable<ChatSkillSuggestion> BuildFilteredSkillMentionSuggestions(string? text)
    {
        var query = (text?.Trim() ?? string.Empty).TrimStart('@');
        if (string.IsNullOrWhiteSpace(query))
        {
            return SkillMentionSuggestions;
        }

        return SkillMentionSuggestions.Where(skill =>
            skill.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || skill.Mention.Contains(query, StringComparison.OrdinalIgnoreCase)
            || skill.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryExecuteSlashCommand(string text)
    {
        var command = text.Trim().ToLowerInvariant();
        if (command == "/new")
        {
            StartNewConversationSegment();
            return true;
        }

        if (command == "/clear")
        {
            ClearConversation();
            return true;
        }

        if (command == "/clear console")
        {
            ClearConsoleRequested?.Invoke();
            return true;
        }

        return false;
    }

    private static IEnumerable<ChatCommandSuggestion> BuildSlashCommandSuggestions()
    {
        yield return new ChatCommandSuggestion("/new", L.Text("SlashCommandNewDescription"));
        yield return new ChatCommandSuggestion("/clear", L.Text("SlashCommandClearDescription"));
        yield return new ChatCommandSuggestion("/clear console", L.Text("SlashCommandClearConsoleDescription"));
    }

    private void RefreshSlashCommandSuggestions()
    {
        SlashCommandSuggestions = new ObservableCollection<ChatCommandSuggestion>(BuildSlashCommandSuggestions());
        SelectedSlashCommandSuggestion = IsSlashCommandMenuOpen ? SlashCommandSuggestions.FirstOrDefault() : null;
    }

    private void MoveMessageInteractionCardToInputOverlay(ChatMessage message)
    {
        if (message.InteractionCard is null) return;
        PendingInteractionCard = message.InteractionCard;
        message.InteractionCard = null;
    }

    [RelayCommand]
    private async Task ConfirmInteractionAsync(AiInteractionCard? card)
    {
        if (card is null || !card.IsPending) return;
        card.IsBusy = true;
        card.Status = AiInteractionCardStatus.Running;
        card.StatusText = L.Text("CopilotCardRunning");
        if (ReferenceEquals(PendingInteractionCard, card))
        {
            PendingInteractionCard = null;
        }

        var cancellationToken = BeginActiveOperation();
        try
        {
            if (card.Action.Kind == AiActionKind.ClearConversation)
            {
                ClearConversation();
                return;
            }

            if (card.Action.Kind == AiActionKind.DiscoverUnityLibraries)
            {
                SetWorkflowPlan(BuildUnityDiscoveryWorkflowSteps(card.Action));
            }
            else
            {
                SetWorkflowPlan(BuildWorkflowSteps(card.Action, null));
            }

            StartWorkflowStep(0);
            var progress = new Progress<AiActionProgress>(ApplyWorkflowProgress);
            var action = WithCardUserNotes(card.Action, card.UserNotes);
            var result = await _actionExecutor.ExecuteAsync(action, cancellationToken, progress);
            if (card.Action.Kind != AiActionKind.DiscoverUnityLibraries)
            {
                CompleteWorkflowStep(0, result.Success);
            }
            card.Status = result.Success ? AiInteractionCardStatus.Completed : AiInteractionCardStatus.Failed;
            card.StatusText = result.Details is null ? result.Message : $"{result.Message}\n{result.Details}";
            if (result.Success && card.Action.Kind == AiActionKind.StartScan && !string.IsNullOrWhiteSpace(card.FollowUpPrompt))
            {
                _ = ContinueAfterConfirmedScanAsync(card.FollowUpPrompt);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chat operation cancelled");
            card.Status = AiInteractionCardStatus.Cancelled;
            card.StatusText = Localized("Stopped.", "\u5df2\u505c\u6b62\u3002");
            MarkRunningWorkflowStep(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat operation failed");
            card.Status = AiInteractionCardStatus.Failed;
            card.StatusText = ex.Message;
        }
        finally
        {
            card.IsBusy = false;
            FinishActiveOperation();
            HideWorkflowProgress();
            if (card.IsFinished)
            {
                PendingInteractionCard = null;
            }
        }
    }

    [RelayCommand]
    private void CancelInteraction(AiInteractionCard? card)
    {
        if (card is null || !card.IsPending) return;
        card.Status = AiInteractionCardStatus.Cancelled;
        card.StatusText = L.Text("CopilotCardCancelled");
        PendingInteractionCard = null;
    }

    private static AiActionRequest WithCardUserNotes(AiActionRequest action, string? userNotes)
        => string.IsNullOrWhiteSpace(userNotes) ? action : action with { UserNotes = userNotes.Trim() };

    private static async Task UpdateOperationStatusAsync(ChatMessage message, DateTime startedAt, CancellationToken cancellationToken)
    {
        while (true)
        {
            message.OperationStatusText = FormatOperationStatus("running", DateTime.Now - startedAt);
            await Task.Delay(1000, cancellationToken);
        }
    }

    private static string FormatElapsedForRunning(TimeSpan elapsed)
        => elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
            : $"{Math.Max(0, (int)elapsed.TotalSeconds)}s";

    private static string FormatOperationStatus(string status, TimeSpan elapsed)
    {
        var elapsedText = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        return status switch
        {
            "running" => L.Format("ChatOperationRunningStatus", FormatElapsedForRunning(elapsed)),
            "stopped" => L.Format("ChatOperationStoppedStatus", elapsedText),
            "failed" => L.Format("ChatOperationFailedStatus", elapsedText),
            _ => L.Format("ChatOperationCompleteStatus", elapsedText)
        };
    }
    private void SetWorkflowPlan(IReadOnlyList<string> stepTitles)
    {
        WorkflowSteps = new ObservableCollection<CopilotWorkflowStep>(stepTitles.Select(title => new CopilotWorkflowStep(title)));
        CurrentWorkflowStepNumber = WorkflowSteps.Count > 0 ? 1 : 0;
        if (WorkflowSteps.Count > 0)
        {
            WorkflowSteps[0].Status = CopilotWorkflowStepStatus.Running;
        }
        IsWorkflowProgressVisible = WorkflowSteps.Count > 0;
        OnPropertyChanged(nameof(HasWorkflowSteps));
        OnPropertyChanged(nameof(ShouldShowWorkflowStepIndicator));
        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
    }

    private void SetWorkflowPlan(IReadOnlyList<WorkflowStepPlan> steps)
    {
        WorkflowSteps = new ObservableCollection<CopilotWorkflowStep>(steps.Select(step => new CopilotWorkflowStep(step.Title, step.StepId)));
        CurrentWorkflowStepNumber = WorkflowSteps.Count > 0 ? 1 : 0;
        if (WorkflowSteps.Count > 0)
        {
            WorkflowSteps[0].Status = CopilotWorkflowStepStatus.Running;
        }
        IsWorkflowProgressVisible = WorkflowSteps.Count > 0;
        OnPropertyChanged(nameof(HasWorkflowSteps));
        OnPropertyChanged(nameof(ShouldShowWorkflowStepIndicator));
        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
    }

    private void StartWorkflowStep(int index)
    {
        if (index < 0 || index >= WorkflowSteps.Count) return;
        CurrentWorkflowStepNumber = index + 1;
        WorkflowSteps[index].Status = CopilotWorkflowStepStatus.Running;
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
    }

    private void CompleteWorkflowStep(int index, bool success)
    {
        if (index < 0 || index >= WorkflowSteps.Count) return;
        WorkflowSteps[index].Status = success ? CopilotWorkflowStepStatus.Finished : CopilotWorkflowStepStatus.Idle;
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
    }

    private void ApplyWorkflowProgress(AiActionProgress progress)
    {
        var index = WorkflowSteps
            .Select((step, stepIndex) => new { step, stepIndex })
            .FirstOrDefault(item => string.Equals(item.step.StepId, progress.StepId, StringComparison.OrdinalIgnoreCase))
            ?.stepIndex ?? -1;

        if (index < 0)
        {
            WorkflowSteps.Add(new CopilotWorkflowStep(progress.Title, progress.StepId));
            index = WorkflowSteps.Count - 1;
            OnPropertyChanged(nameof(HasWorkflowSteps));
            OnPropertyChanged(nameof(WorkflowProgressText));
            OnPropertyChanged(nameof(ShouldShowWorkflowStepIndicator));
        }

        WorkflowSteps[index].Status = progress.Status switch
        {
            AiActionProgressStatus.Running => CopilotWorkflowStepStatus.Running,
            AiActionProgressStatus.Completed => CopilotWorkflowStepStatus.Finished,
            AiActionProgressStatus.Failed => CopilotWorkflowStepStatus.Failed,
            _ => WorkflowSteps[index].Status
        };

        if (progress.Status == AiActionProgressStatus.Running)
        {
            CurrentWorkflowStepNumber = index + 1;
        }

        IsWorkflowProgressVisible = WorkflowSteps.Count > 0;
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
    }

    private CancellationToken BeginActiveOperation()
    {
        _activeOperationCancellation?.Dispose();
        _activeOperationCancellation = new CancellationTokenSource();
        IsOperationRunning = true;
        return _activeOperationCancellation.Token;
    }

    private void CancelActiveOperation()
    {
        _activeOperationCancellation?.Cancel();
    }

    private void FinishActiveOperation()
    {
        _activeOperationCancellation?.Dispose();
        _activeOperationCancellation = null;
        IsOperationRunning = false;
    }

    private void HideWorkflowProgress()
    {
        IsWorkflowProgressVisible = false;
    }

    private void MarkRunningWorkflowStep(bool success)
    {
        var index = WorkflowSteps
            .Select((step, stepIndex) => new { step, stepIndex })
            .FirstOrDefault(item => item.step.Status == CopilotWorkflowStepStatus.Running)
            ?.stepIndex ?? Math.Max(0, CurrentWorkflowStepNumber - 1);
        CompleteWorkflowStep(index, success);
    }

    private static IReadOnlyList<string> BuildWorkflowSteps(AiActionRequest action, AiSkillRoutingResult? routed)
    {
        action = ResolveActionPath(action);
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "褰撳墠鑼冨洿");
        return action.Kind switch
        {
            AiActionKind.StartScan =>
            [
                Localized($"Run confirmed scan for {scope}", $"执行已确认的扫描：{scope}")
            ],
            AiActionKind.AnalyzeCleanup =>
            [
                Localized($"Run confirmed cleanup analysis for {scope}", $"执行已确认的清理分析：{scope}")
            ],
            AiActionKind.NavigateToScannedPath =>
            [
                Localized($"Navigate to {scope}", $"瀵艰埅鍒?{scope}")
            ],
            _ =>
            [
                Localized("Run confirmed action", "鎵ц宸茬‘璁ょ殑鎿嶄綔")
            ]
        };
    }

    private static IReadOnlyList<WorkflowStepPlan> BuildUnityDiscoveryWorkflowSteps(AiActionRequest action)
    {
        var steps = new List<WorkflowStepPlan>
        {
            new("enumerate_drives", string.IsNullOrWhiteSpace(action.Path)
                ? Localized("AI checks ready drives", "AI 确认可扫描磁盘")
                : Localized("AI checks the scan root", "AI 确认扫描根目录"))
        };

        if (!string.IsNullOrWhiteSpace(action.Path))
        {
            var scope = action.ScopeLabel ?? action.Path;
            steps.Add(new WorkflowStepPlan("scan_scope:" + action.Path, Localized($"AI scans {scope} for cleanup candidates", $"AI 鎵弿 {scope} 涓殑娓呯悊鍊欓€夐」")));
        }
        else
        {
            steps.AddRange(DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Select(drive => new WorkflowStepPlan(
                    "scan_drive:" + drive.Name.TrimEnd('\\'),
                    Localized($"AI scans {drive.Name} for cleanup candidates", $"AI 鎵弿 {drive.Name} 涓殑娓呯悊鍊欓€夐」"))));
        }

        steps.Add(new WorkflowStepPlan("write_unity_recommendations", Localized("AI writes cleanup recommendations", "AI 鍐欏叆娓呯悊寤鸿")));
        return steps;
    }


    private static string BuildModelUserInput(
        string userInput,
        AiSkillRoutingResult routed,
        FileEntry? linkedEntry,
        CleanupRecommendation? linkedRecommendation,
        FileEntry? currentViewRoot,
        ScanSession? currentSession,
        bool hasExistingRecommendations)
    {
        var context = new
        {
            scan_available = currentSession is not null,
            scan_root_path = currentSession?.RootEntry?.Path ?? currentSession?.TargetPath,
            current_view_path = currentViewRoot?.Path,
            selected_path = linkedEntry?.Path ?? linkedRecommendation?.TargetPath,
            has_existing_recommendations = hasExistingRecommendations,
            selected_skills = routed.SelectedSkillIds.Select(id => "@" + id).ToArray(),
            available_drives = DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .Select(drive => drive.Name)
                .ToArray()
        };
        var contextJson = JsonSerializer.Serialize(context);
        return $"{userInput}\n\nHost disk context JSON:\n{contextJson}";
    }

    
    private static bool ShouldEnableThinking(AppSettings settings, AiSkillRoutingResult routed)
        => settings.EnableThinking && routed.SelectedSkillIds.Count == 0;

    private async Task ContinueAfterConfirmedScanAsync(string? followUpPrompt, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(followUpPrompt))
        {
            return;
        }

        _followUpCancellation?.Cancel();
        _followUpCancellation?.Dispose();
        _followUpCancellation = new CancellationTokenSource();
        var cancellationToken = _followUpCancellation.Token;

        try
        {
            _logger.LogInformation("Chat follow-up queued after confirmed action; reason={Reason}", reason ?? "confirmed action");
            while (IsOperationRunning)
            {
                await Task.Delay(100, cancellationToken);
            }

            InputText = followUpPrompt;
            await SendAsync();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chat follow-up cancelled; reason={Reason}", reason ?? "confirmed action");
        }
    }

    private static AiInteractionCard BuildInteractionCard(AiActionRequest action, string followUpPrompt)
    {
        action = ResolveActionPath(action);
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "褰撳墠鑼冨洿");
        return action.Kind switch
        {
            AiActionKind.DiscoverUnityLibraries => new AiInteractionCard
            {
                Title = Localized("Discover cleanup candidates", "鍙戠幇娓呯悊鍊欓€夐」"),
                Description = Localized("Scan ready drives one by one, detect candidates described by the selected skill, and write reviewable cleanup recommendations.", "依次扫描可用磁盘，按已选 skill 描述发现候选项，并写入可复核的清理建议。"),
                Impact = action.WillOverwriteExistingData
                    ? Localized("This replaces the current recommendations list. Actual deletion still requires another confirmation.", "这会替换当前推荐列表；真正删除仍需要再次确认。")
                    : Localized("The current TreeView/Treemap scan result stays unchanged. Actual deletion still requires another confirmation.", "当前 TreeView/Treemap 扫描结果保持不变；真正删除仍需要再次确认。"),
                ConfirmText = Localized("Start Discovery", "开始发现"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                FollowUpPrompt = followUpPrompt,
                Action = action
            },
            AiActionKind.StartScan => new AiInteractionCard
            {
                Title = Localized("Scan this path", "扫描这个路径"),
                Description = Localized($"Scan {scope} before analyzing its space usage.", $"需要先扫描 {scope}，才能继续分析里面的空间占用。"),
                Impact = Localized("This replaces the current scan result and refreshes Treemap, TreeView, and AI-readable space context.", "会替换当前扫描结果，并刷新 Treemap、TreeView 和 AI 可理解的空间上下文。"),
                ConfirmText = Localized("Start Scan", "开始扫描"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                FollowUpPrompt = followUpPrompt,
                Action = action
            },
            AiActionKind.AnalyzeCleanup => new AiInteractionCard
            {
                Title = Localized("Analyze cleanup recommendations", "分析清理建议"),
                Description = Localized($"Generate reviewable cleanup candidates for {scope}.", $"将基于 {scope} 生成可复核的清理候选项。"),
                Impact = action.WillOverwriteExistingData
                    ? Localized("This overwrites existing recommendations; actual cleanup still requires another confirmation.", "会覆盖现有推荐结果；真正清理仍需要你再次确认。")
                    : Localized("Actual cleanup still requires another confirmation.", "真正清理仍需要你再次确认。"),
                ConfirmText = Localized("Start Analysis", "开始分析"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                FollowUpPrompt = followUpPrompt,
                Action = action
            },
            _ => new AiInteractionCard
            {
                Title = L.Text("CopilotCardDefaultTitle"),
                Description = Localized($"Prepare to handle {scope}.", $"准备处理 {scope}。"),
                ConfirmText = L.Text("CopilotCardDefaultConfirm"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                FollowUpPrompt = followUpPrompt,
                Action = action
            }
        };
    }

    private static async Task AppendStreamingTextAsync(ChatMessage message, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        message.Text += text;
        await Task.CompletedTask;
    }

    private static string FormatActionResult(AiActionResult result)
    {
        var text = result.Details is null ? result.Message : $"{result.Message}\n{result.Details}";
        return Environment.NewLine + text + Environment.NewLine;
    }

    private static string Localized(string english, string chinese)
        => L.CurrentLanguageName.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? english : chinese;

    private static string ResolveResponseLanguage(string? configuredLanguage)
    {
        if (string.IsNullOrWhiteSpace(configuredLanguage))
        {
            return "zh-CN";
        }

        return string.Equals(configuredLanguage, L.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? L.CurrentLanguageName
            : configuredLanguage.Trim();
    }

    public static void ApplyProposalIfAny(ChatMessage message, JsonElement? proposal)
    {
        if (!TryGetActionRequest(proposal, out var request)) return;
        if (!TryGetProposalRoot(proposal, out var root)) return;
        root.TryGetProperty("card", out var card);
        var hasCard = card.ValueKind == JsonValueKind.Object;

        message.InteractionCard = new AiInteractionCard
        {
            Title = hasCard ? GetString(card, "title") ?? L.Text("CopilotCardDefaultTitle") : L.Text("CopilotCardDefaultTitle"),
            Description = hasCard ? GetString(card, "description") ?? request.ScopeLabel ?? request.Path ?? L.Text("CopilotCardDefaultTitle") : request.ScopeLabel ?? request.Path ?? L.Text("CopilotCardDefaultTitle"),
            Impact = hasCard ? GetString(card, "impact") : null,
            ConfirmText = hasCard ? GetString(card, "confirm_text") ?? L.Text("CopilotCardDefaultConfirm") : L.Text("CopilotCardDefaultConfirm"),
            CancelText = hasCard ? GetString(card, "cancel_text") ?? L.Text("CopilotCardDefaultCancel") : L.Text("CopilotCardDefaultCancel"),
            UserNotes = request.UserNotes,
            Action = request
        };
    }

    private static bool TryGetActionRequest(JsonElement? proposal, out AiActionRequest request)
    {
        request = new AiActionRequest(AiActionKind.None);
        if (!TryGetProposalRoot(proposal, out var root)) return false;
        if (!root.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object) return false;
        if (!action.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String) return false;

        var actionKind = ResolveActionKind(kindElement.GetString());
        if (actionKind == AiActionKind.None) return false;

        request = ResolveActionPath(new AiActionRequest(
            actionKind,
            Path: GetString(action, "path"),
            WillOverwriteExistingData: GetBool(action, "will_overwrite_existing_data"),
            ScopeLabel: GetString(action, "scope_label"),
            UserNotes: GetString(action, "user_notes")));
        return true;
    }

    private static bool TryGetProposalRoot(JsonElement? proposal, out JsonElement root)
    {
        root = default;
        if (proposal is null || proposal.Value.ValueKind != JsonValueKind.Object) return false;
        root = proposal.Value;
        if (root.TryGetProperty("proposal", out var nestedProposal) && nestedProposal.ValueKind == JsonValueKind.Object)
        {
            root = nestedProposal;
        }

        return true;
    }

    private static AiActionRequest ResolveActionPath(AiActionRequest action)
    {
        if (action.Kind is not (AiActionKind.StartScan or AiActionKind.NavigateToScannedPath)
            || string.IsNullOrWhiteSpace(action.Path))
        {
            return action;
        }

        return action with { Path = ScanPathResolver.Resolve(action.Path) };
    }
    private static AiActionKind ResolveActionKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return AiActionKind.None;
        if (Enum.TryParse<AiActionKind>(kind, ignoreCase: true, out var actionKind)) return actionKind;
        return kind.Trim() switch
        {
            "scan" => AiActionKind.StartScan,
            "start_scan" => AiActionKind.StartScan,
            "analyze_cleanup" => AiActionKind.AnalyzeCleanup,
            "discover_unity_libraries" => AiActionKind.DiscoverUnityLibraries,
            "clear_conversation" => AiActionKind.ClearConversation,
            "navigate" => AiActionKind.NavigateToScannedPath,
            "navigate_to_scanned_path" => AiActionKind.NavigateToScannedPath,
            _ => AiActionKind.None
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private void ClearConversation()
    {
        _logger.LogInformation("Chat conversation cleared; messages={MessageCount}", Messages.Count);
        _chatService.ClearHistory();
        Messages.Clear();
        PendingInteractionCard = null;
        IsWorkflowProgressVisible = false;
        WorkflowSteps.Clear();
        OnPropertyChanged(nameof(HasWorkflowSteps));
        OnPropertyChanged(nameof(ShouldShowWorkflowStepIndicator));
        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
        LinkedEntry = null;
        LinkedRecommendation = null;
        LinkedItemPath = null;
        ErrorMessage = null;
    }

    private void StartNewConversationSegment()
    {
        _logger.LogInformation("Chat conversation segment started; messages={MessageCount}", Messages.Count);
        _chatService.ClearHistory();
        PendingInteractionCard = null;
        IsWorkflowProgressVisible = false;
        WorkflowSteps.Clear();
        OnPropertyChanged(nameof(HasWorkflowSteps));
        OnPropertyChanged(nameof(ShouldShowWorkflowStepIndicator));
        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
        LinkedEntry = null;
        LinkedRecommendation = null;
        LinkedItemPath = null;
        ErrorMessage = null;
        Messages.Add(new ChatMessage
        {
            Sender = ChatSender.System,
            Text = L.Text("ChatNewSessionDividerDescription"),
            Timestamp = DateTime.Now
        });
    }

    [RelayCommand]
    private void ToggleThinking(ChatMessage message) => message.IsThinkingExpanded = !message.IsThinkingExpanded;
}

internal sealed record WorkflowStepPlan(string StepId, string Title);

public sealed partial class CopilotWorkflowStep(string title, string? stepId = null) : ObservableObject
{
    [ObservableProperty] private CopilotWorkflowStepStatus _status = CopilotWorkflowStepStatus.Idle;

    public string? StepId { get; } = stepId;
    public string Title { get; } = title;
    public string StatusIconState => Status switch
    {
        CopilotWorkflowStepStatus.Running => "running",
        CopilotWorkflowStepStatus.Finished => "finish",
        CopilotWorkflowStepStatus.Failed => "failed",
        _ => "idle"
    };
    public string StatusIconGlyph => Status switch
    {
        CopilotWorkflowStepStatus.Running => "●",
        CopilotWorkflowStepStatus.Finished => "✓",
        CopilotWorkflowStepStatus.Failed => "✕",
        _ => "○"
    };

    partial void OnStatusChanged(CopilotWorkflowStepStatus value)
    {
        OnPropertyChanged(nameof(StatusIconState));
        OnPropertyChanged(nameof(StatusIconGlyph));
    }
}

public enum CopilotWorkflowStepStatus
{
    Idle,
    Running,
    Finished,
    Failed
}

public sealed record ChatCommandSuggestion(string Command, string Description);

public sealed record ChatSkillSuggestion(string Mention, string DisplayName, string Description);










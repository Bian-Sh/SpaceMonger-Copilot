using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
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
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ISettingsService _settingsService;
    private readonly IAiSkillRouter _skillRouter;
    private readonly ILogger<ChatViewModel> _logger;
    private IAiDiskActionExecutor _actionExecutor = new NullAiDiskActionExecutor();
    private Func<string, bool> _scanTargetAvailabilityProbe = IsScanTargetAvailable;
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
    [ObservableProperty] private ObservableCollection<ChatCommandSuggestion> _slashCommandSuggestions = new(
    [
        new ChatCommandSuggestion("/new", "鏂板缓浼氳瘽锛屾竻绌哄綋鍓嶈亰澶╀笂涓嬫枃"),
        new ChatCommandSuggestion("/clear", "娓呴櫎褰撳墠瀵硅瘽锛屼笉褰卞搷鎵弿鏁版嵁")
    ]);
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
        RefreshApiKeyStatus();
    }

    public void SetActionExecutor(IAiDiskActionExecutor actionExecutor) => _actionExecutor = actionExecutor;

    public void SetScanTargetAvailabilityProbe(Func<string, bool> probe) => _scanTargetAvailabilityProbe = probe;

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

        if (TryHandleChatWindowIntent(userInput))
        {
            return;
        }

        var routed = _skillRouter.Route(userInput, LinkedEntry, _currentViewRoot, _actionExecutor.HasExistingRecommendations, responseLanguage);
        if (await TryHandleLocalRoutedResponseAsync(routed, userInput, settings, responseLanguage))
        {
            InputText = null;
            return;
        }

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
        Messages.Add(assistantMessage);

        try
        {
            var apiKey = _settingsService.GetApiKey(settings)!;
            var baseUrl = settings.AnthropicBaseUrl;
            var modelUserInput = BuildModelUserInput(userInput, routed);
            var enableThinking = ShouldEnableThinking(settings, routed);
            var showSelectedSkillWorkflow = routed.SelectedSkillIds.Count > 0 && routed.SuggestedAction is null;
            if (showSelectedSkillWorkflow)
            {
                SetWorkflowPlan(BuildSelectedSkillWorkflowSteps(routed));
                StartWorkflowStep(0);
            }

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

            ApplyProposalIfAny(assistantMessage, response.Proposal);
            _logger.LogInformation("Chat response completed; textLength={TextLength}, hasProposal={HasProposal}", assistantMessage.Text.Length, response.Proposal.HasValue);
            MoveMessageInteractionCardToInputOverlay(assistantMessage);
            assistantMessage.IsStreaming = false;
            LinkedEntry = null;
            LinkedRecommendation = null;
            LinkedItemPath = null;
        }
        catch (OperationCanceledException)
        {
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
            IsSending = false;
            FinishActiveOperation();
            if (!assistantMessage.IsStreaming)
            {
                HideWorkflowProgress();
            }
        }
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
        if (command is not "/new" and not "/clear")
        {
            return false;
        }

        ClearConversation();
        return true;
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
            var result = await _actionExecutor.ExecuteAsync(card.Action, cancellationToken, progress);
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
        if (!string.IsNullOrWhiteSpace(card.FollowUpPrompt))
        {
            _ = ContinueAfterConfirmedScanAsync(card.FollowUpPrompt, "cancelled action");
        }
    }

    private async Task<bool> TryHandleLocalRoutedResponseAsync(AiSkillRoutingResult routed, string userInput, AppSettings settings, string responseLanguage)
    {
        if (routed.SuggestedAction is not null)
        {
            if (ShouldRouteScanRequestToModel(routed.SuggestedAction))
            {
                return false;
            }

            if (ShouldAutoExecuteAction(routed.SuggestedAction))
            {
                await ExecuteAutomaticActionAsync(routed, routed.SuggestedAction, userInput, settings, responseLanguage);
                return true;
            }

            PendingInteractionCard = BuildInteractionCard(routed.SuggestedAction, userInput);
            return true;
        }

        if (routed.SelectedSkillIds.Count == 0
            && !string.IsNullOrWhiteSpace(routed.LocalAnswer)
            && routed.Intents.All(intent => intent is AiIntent.Identity or AiIntent.ModuleHelp))
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = routed.LocalAnswer,
                Timestamp = DateTime.Now
            });
            return true;
        }

        return false;
    }

    private bool ShouldAutoExecuteAction(AiActionRequest action)
        => action.Kind switch
        {
            AiActionKind.StartScan => _currentSession is null,
            AiActionKind.AnalyzeCleanup => _currentSession is not null && !action.WillOverwriteExistingData,
            AiActionKind.DiscoverUnityLibraries => !action.WillOverwriteExistingData,
            _ => false
        };

    private bool ShouldRouteScanRequestToModel(AiActionRequest action)
    {
        if (action.Kind != AiActionKind.StartScan || _currentSession is not null || !IsApiKeyConfigured)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(action.Path))
        {
            return true;
        }

        return !_scanTargetAvailabilityProbe(NormalizeScanTarget(action.Path));
    }

    private async Task ExecuteAutomaticActionAsync(
        AiSkillRoutingResult routed,
        AiActionRequest action,
        string userInput,
        AppSettings settings,
        string responseLanguage)
    {
        if (!TryValidateAutomaticAction(action, out var validationMessage))
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = validationMessage,
                Timestamp = DateTime.Now,
                IsError = true
            });
            SetWorkflowPlan(Array.Empty<string>());
            return;
        }

        IsSending = true;
        ErrorMessage = null;
        var cancellationToken = BeginActiveOperation();
        var message = new ChatMessage
        {
            Sender = ChatSender.Assistant,
            Text = string.Empty,
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        Messages.Add(message);

        try
        {
            if (action.Kind == AiActionKind.DiscoverUnityLibraries)
            {
                SetWorkflowPlan(BuildUnityDiscoveryWorkflowSteps(action));
            }
            else
            {
                SetWorkflowPlan(BuildWorkflowSteps(action, routed));
            }

            await AppendStreamingTextAsync(message, BuildAutomaticActionIntro(action, routed), cancellationToken);
            StartWorkflowStep(0);
            var progress = new Progress<AiActionProgress>(ApplyWorkflowProgress);
            var result = await _actionExecutor.ExecuteAsync(action, cancellationToken, progress);
            if (action.Kind != AiActionKind.DiscoverUnityLibraries)
            {
                CompleteWorkflowStep(0, result.Success);
            }
            await AppendStreamingTextAsync(message, FormatActionResult(result), cancellationToken);

            if (!result.Success)
            {
                message.IsError = true;
                return;
            }

            if (action.Kind == AiActionKind.StartScan && routed.Intents.Contains(AiIntent.FolderCleanupAnalysis))
            {
                var analyzeAction = new AiActionRequest(
                    AiActionKind.AnalyzeCleanup,
                    Path: action.Path,
                    WillOverwriteExistingData: _actionExecutor.HasExistingRecommendations,
                    ScopeLabel: action.ScopeLabel ?? action.Path);

                if (analyzeAction.WillOverwriteExistingData)
                {
                    await AppendStreamingTextAsync(
                        message,
                        Localized("Existing cleanup recommendations are present, so I need your confirmation before replacing them.", "\u5df2\u7ecf\u6709\u6e05\u7406\u5206\u6790\u7ed3\u679c\uff0c\u8981\u8986\u76d6\u5b83\u4eec\u9700\u8981\u4f60\u5148\u786e\u8ba4\u3002"),
                        cancellationToken);
                    PendingInteractionCard = BuildInteractionCard(analyzeAction, userInput);
                    return;
                }

                await AppendStreamingTextAsync(message, Localized("\nNow I will analyze cleanup recommendations.\n", "\n\u63a5\u7740\u6211\u4f1a\u5206\u6790\u53ef\u6e05\u7406\u9879\u3002\n"), cancellationToken);
                StartWorkflowStep(1);
                var analysisResult = await _actionExecutor.ExecuteAsync(analyzeAction, cancellationToken);
                CompleteWorkflowStep(1, analysisResult.Success);
                await AppendStreamingTextAsync(message, FormatActionResult(analysisResult), cancellationToken);
                if (!analysisResult.Success)
                {
                    message.IsError = true;
                    return;
                }

                StartWorkflowStep(Math.Min(2, WorkflowSteps.Count - 1));
                await ContinueAfterConfirmedScanAsync(userInput, "scan and cleanup recommendation analysis");
                CompleteWorkflowStep(Math.Min(2, WorkflowSteps.Count - 1), true);
                return;
            }

            if (action.Kind == AiActionKind.StartScan)
            {
                StartWorkflowStep(Math.Min(1, WorkflowSteps.Count - 1));
                await ContinueAfterConfirmedScanAsync(userInput);
                CompleteWorkflowStep(Math.Min(1, WorkflowSteps.Count - 1), true);
                return;
            }

            if (action.Kind == AiActionKind.AnalyzeCleanup)
            {
                StartWorkflowStep(Math.Min(1, WorkflowSteps.Count - 1));
                await ContinueAfterConfirmedScanAsync(userInput, "cleanup recommendation analysis");
                CompleteWorkflowStep(Math.Min(1, WorkflowSteps.Count - 1), true);
            }

            if (action.Kind == AiActionKind.DiscoverUnityLibraries)
            {
                StartWorkflowStep(Math.Max(0, WorkflowSteps.Count - 1));
                CompleteWorkflowStep(Math.Max(0, WorkflowSteps.Count - 1), true);
                await AppendStreamingTextAsync(
                    message,
                    Localized(
                        "Review the recommendations panel, sort by last modified time, and confirm selected items before cleanup. Unity Hub membership is shown on each Unity Library row.\n",
                        "璇峰湪鎺ㄨ崘娓呯悊闈㈡澘涓寜鏈€鍚庝慨鏀规椂闂村拰 Unity Hub 鐘舵€佸鏍革紝鍐嶅嬀閫夎娓呯悊鐨?Library锛涚湡姝ｅ垹闄ゅ墠浠嶄細鍐嶆纭銆俓n"),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chat operation cancelled");
            message.IsError = true;
            message.Text = string.IsNullOrEmpty(message.Text)
                ? Localized("Stopped. The AI workflow state has been released.", "\u5df2\u505c\u6b62\uff0cAI \u5de5\u4f5c\u6d41\u72b6\u6001\u5df2\u91ca\u653e\u3002")
                : message.Text + Localized("\nStopped. The AI workflow state has been released.\n", "\n\u5df2\u505c\u6b62\uff0cAI \u5de5\u4f5c\u6d41\u72b6\u6001\u5df2\u91ca\u653e\u3002\n");
            MarkRunningWorkflowStep(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat operation failed");
            message.IsError = true;
            message.Text = string.IsNullOrEmpty(message.Text)
                ? ex.Message
                : message.Text + L.Format("ChatErrorAppend", ex.Message);
            ErrorMessage = ex.Message;
        }
        finally
        {
            message.IsStreaming = false;
            IsSending = false;
            FinishActiveOperation();
            HideWorkflowProgress();
        }
    }

    private bool TryValidateAutomaticAction(AiActionRequest action, out string message)
    {
        message = string.Empty;
        if (action.Kind != AiActionKind.StartScan || string.IsNullOrWhiteSpace(action.Path))
        {
            return true;
        }

        var scanTarget = NormalizeScanTarget(action.Path);
        if (_scanTargetAvailabilityProbe(scanTarget))
        {
            return true;
        }

        message = Localized(
            $"Cannot scan {scanTarget}: the drive or folder does not exist, is not mounted, or is currently inaccessible.",
            $"无法扫描 {scanTarget}：该磁盘或文件夹不存在、未挂载，或当前不可访问。");
        return false;
    }

    private static string NormalizeScanTarget(string path)
    {
        var trimmed = path.Trim();
        return trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':'
            ? trimmed + @"\"
            : trimmed;
    }

    private static bool IsScanTargetAvailable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.Equals(root, path, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(root))
            {
                return DriveInfo.GetDrives().Any(drive =>
                    string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase) && drive.IsReady);
            }

            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
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
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "褰撳墠鑼冨洿");
        if (action.Kind == AiActionKind.StartScan && routed?.Intents.Contains(AiIntent.FolderCleanupAnalysis) == true)
        {
            return [Localized($"Scan {scope}", $"鎵弿 {scope}"), Localized("Analyze cleanup candidates", "鍒嗘瀽鍙竻鐞嗛」"), Localized("Write recommendation summary", "鍐欏叆鎺ㄨ崘娓呯悊鍒楄〃")];
        }

        return [];
    }

    private static IReadOnlyList<WorkflowStepPlan> BuildUnityDiscoveryWorkflowSteps(AiActionRequest action)
    {
        var steps = new List<WorkflowStepPlan>
        {
            new("enumerate_drives", string.IsNullOrWhiteSpace(action.Path)
                ? Localized("AI checks ready drives", "AI 确认可扫描磁盘")
                : Localized("AI checks the Unity scan root", "AI 确认 Unity 扫描根目录"))
        };

        if (!string.IsNullOrWhiteSpace(action.Path))
        {
            var scope = action.ScopeLabel ?? action.Path;
            steps.Add(new WorkflowStepPlan("scan_scope:" + action.Path, Localized($"AI scans {scope} for Unity projects", $"AI 鎵弿 {scope} 涓殑 Unity 宸ョ▼")));
        }
        else
        {
            steps.AddRange(DriveInfo.GetDrives()
                .Where(drive => drive.IsReady)
                .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                .Select(drive => new WorkflowStepPlan(
                    "scan_drive:" + drive.Name.TrimEnd('\\'),
                    Localized($"AI scans {drive.Name} for Unity projects", $"AI 鎵弿 {drive.Name} 涓殑 Unity 宸ョ▼"))));
        }

        steps.Add(new WorkflowStepPlan("write_unity_recommendations", Localized("AI writes Unity cleanup recommendations", "AI 鍐欏叆 Unity 娓呯悊鎺ㄨ崘")));
        return steps;
    }

    private static IReadOnlyList<string> BuildSelectedSkillWorkflowSteps(AiSkillRoutingResult routed)
    {
        var label = string.Join(", ", routed.SelectedSkillIds.Select(id => $"@{id}"));
        return [
            Localized($"Run selected skill {label}", $"\u8fd0\u884c\u5df2\u9009\u62e9\u7684 skill {label}"),
            Localized("Write guided response", "\u8f93\u51fa\u5f15\u5bfc\u5f0f\u56de\u590d")
        ];
    }

    private static string BuildAutomaticActionIntro(AiActionRequest action, AiSkillRoutingResult routed)
    {
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "\u5f53\u524d\u8303\u56f4");
        return action.Kind switch
        {
            AiActionKind.DiscoverUnityLibraries => Localized(
                "Sure — I prepared a confirmation card to discover Unity Library cleanup candidates across ready drives. It only writes reviewable recommendations; deletion is a separate confirmation.",
                "可以，我先准备一张确认卡，用来在可用磁盘中发现 Unity Library 清理候选项。它只会写入可复核推荐；删除还要单独确认。"),
            AiActionKind.StartScan => Localized(
                $"Sure — I prepared a confirmation card to scan {scope}. After you confirm, the scan starts; after it finishes, I can analyze the space usage from the result.",
                $"可以，我先给你一张“扫描 {scope}”的确认卡片。你确认后开始扫描；扫描完成后我再基于结果分析占用情况。"),
            AiActionKind.AnalyzeCleanup => Localized(
                $"Sure — I prepared a confirmation card to analyze cleanup recommendations for {scope}. Analysis starts after you confirm.",
                $"可以，我先给你一张“分析 {scope} 清理建议”的确认卡片。你确认后开始分析。"),
            _ => Localized("I prepared a confirmation card and will run it only after you confirm.", "我先给你一个确认卡片，你确认后再执行。")
        };
    }

    private static string BuildModelUserInput(string userInput, AiSkillRoutingResult routed)
    {
        if (routed.SelectedSkillIds.Count == 0)
        {
            return userInput;
        }

        var selectedSkills = string.Join(", ", routed.SelectedSkillIds.Select(id => "@" + id));
        return $"{userInput}\n\nSelected skills: {selectedSkills}";
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
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "当前范围");
        return action.Kind switch
        {
            AiActionKind.DiscoverUnityLibraries => new AiInteractionCard
            {
                Title = Localized("Discover Unity Library cleanup candidates", "发现 Unity Library 清理候选项"),
                Description = Localized("Scan ready drives one by one, detect Unity project Library folders, and write reviewable cleanup recommendations.", "依次扫描可用磁盘，检测 Unity 项目 Library 文件夹，并写入可复核的清理建议。"),
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
        => string.IsNullOrWhiteSpace(configuredLanguage) || string.Equals(configuredLanguage, L.AutoLanguage, StringComparison.OrdinalIgnoreCase)
            ? L.CurrentLanguageName
            : configuredLanguage.Trim();

    public static void ApplyProposalIfAny(ChatMessage message, JsonElement? proposal)
    {
        if (proposal is null || proposal.Value.ValueKind != JsonValueKind.Object) return;
        var root = proposal.Value;
        if (!root.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("card", out var card) || card.ValueKind != JsonValueKind.Object) return;
        if (!action.TryGetProperty("kind", out var kindElement) || kindElement.ValueKind != JsonValueKind.String) return;

        var actionKind = kindElement.GetString() switch
        {
            nameof(AiActionKind.StartScan) => AiActionKind.StartScan,
            nameof(AiActionKind.AnalyzeCleanup) => AiActionKind.AnalyzeCleanup,
            nameof(AiActionKind.ClearConversation) => AiActionKind.ClearConversation,
            nameof(AiActionKind.NavigateToScannedPath) => AiActionKind.NavigateToScannedPath,
            _ => AiActionKind.None
        };
        if (actionKind == AiActionKind.None) return;

        var request = new AiActionRequest(
            actionKind,
            Path: GetString(action, "path"),
            WillOverwriteExistingData: GetBool(action, "will_overwrite_existing_data"),
            ScopeLabel: GetString(action, "scope_label"));

        message.InteractionCard = new AiInteractionCard
        {
            Title = GetString(card, "title") ?? L.Text("CopilotCardDefaultTitle"),
            Description = GetString(card, "description") ?? request.ScopeLabel ?? request.Path ?? "纾佺洏绌洪棿绠＄悊鍔ㄤ綔",
            Impact = GetString(card, "impact"),
            ConfirmText = GetString(card, "confirm_text") ?? L.Text("CopilotCardDefaultConfirm"),
            CancelText = GetString(card, "cancel_text") ?? L.Text("CopilotCardDefaultCancel"),
            Action = request
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private bool TryHandleChatWindowIntent(string text)
    {
        var clearIntent = ClassifyClearConversationIntent(text);
        if (clearIntent == ClearConversationIntent.None)
        {
            return false;
        }

        var explicitRequest = clearIntent == ClearConversationIntent.Explicit;
        PendingInteractionCard = BuildClearConversationCard(explicitRequest);
        return true;
    }

    private static ClearConversationIntent ClassifyClearConversationIntent(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ClearConversationIntent.None;
        }

        string[] explicitPhrases =
        [
            "clear", "clear chat", "clear conversation", "reset chat", "new chat", "start a new chat",
            "清空", "清空对话", "清除对话", "清空聊天", "删除聊天记录", "清空本窗口", "清空当前对话",
            "清空当前对话上下文", "新建会话", "开启新会话", "开新会话", "新话题", "重新开始对话"
        ];

        if (explicitPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal)))
        {
            return ClearConversationIntent.Explicit;
        }

        string[] guidedPhrases =
        [
            "上下文乱", "上下文太乱", "聊乱了", "对话乱", "你记混", "记混了", "忘掉前面", "不要记之前",
            "重新来", "重来吧", "答非所问", "越聊越乱", "forget previous", "forget earlier", "start fresh",
            "context is messy", "you are confused", "this chat is messy"
        ];

        return guidedPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal))
            ? ClearConversationIntent.Guided
            : ClearConversationIntent.None;
    }

    private static AiInteractionCard BuildClearConversationCard(bool explicitRequest)
        => new()
        {
            Title = explicitRequest
                ? Localized("Confirm clearing chat?", "确认清空对话？")
                : Localized("Clear this chat window?", "清空本窗口？"),
            Description = Localized(
                "This clears messages and Copilot conversation context in this window. Scan data and disk files stay unchanged.",
                "这会清空本窗口消息和 Copilot 对话上下文；扫描数据和磁盘文件不会改变。"),
            Impact = Localized("You can continue with a fresh chat after confirming.", "确认后可以从一个全新的对话继续。"),
            ConfirmText = Localized("Clear Chat", "娓呯┖瀵硅瘽"),
            CancelText = L.Text("CopilotCardDefaultCancel"),
            Action = new AiActionRequest(AiActionKind.ClearConversation)
        };

    private enum ClearConversationIntent
    {
        None,
        Explicit,
        Guided
    }

    private void ClearConversation()
    {
        _logger.LogInformation("Chat conversation cleared; messages={MessageCount}", Messages.Count);
        _chatService.ClearHistory();
        Messages.Clear();
        PendingInteractionCard = null;
        IsWorkflowProgressVisible = false;
        WorkflowSteps.Clear();
        OnPropertyChanged(nameof(HasWorkflowSteps));
        OnPropertyChanged(nameof(WorkflowProgressText));
        OnPropertyChanged(nameof(CurrentWorkflowIconState));
        LinkedEntry = null;
        LinkedRecommendation = null;
        LinkedItemPath = null;
        ErrorMessage = null;
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



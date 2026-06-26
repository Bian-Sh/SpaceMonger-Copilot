using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private IAiDiskActionExecutor _actionExecutor = new NullAiDiskActionExecutor();
    private CancellationTokenSource? _followUpCancellation;

    private ScanSession? _currentSession;
    private FileEntry? _currentViewRoot;

    [ObservableProperty] private ObservableCollection<ChatMessage> _messages = new();
    [ObservableProperty] private bool _hasMessages;
    [ObservableProperty] private string? _inputText;
    [ObservableProperty] private FileEntry? _linkedEntry;
    [ObservableProperty] private CleanupRecommendation? _linkedRecommendation;
    [ObservableProperty] private bool _isChatAvailable;
    [ObservableProperty] private bool _isApiKeyConfigured;
    [ObservableProperty] private bool _isSending;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _linkedItemPath;

    public ChatViewModel(IChatService chatService, ISettingsService settingsService, IAiSkillRouter skillRouter)
    {
        _chatService = chatService;
        _settingsService = settingsService;
        _skillRouter = skillRouter;
        Messages.CollectionChanged += Messages_CollectionChanged;
        RefreshApiKeyStatus();
    }

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

    partial void OnLinkedEntryChanged(FileEntry? value) => LinkedItemPath = value?.Path;
    partial void OnLinkedRecommendationChanged(CleanupRecommendation? value) => LinkedItemPath = value?.TargetPath;

    [RelayCommand]
    private async Task SendAsync()
    {
        RefreshApiKeyStatus();
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userInput = InputText.Trim();
        var settings = _settingsService.LoadSettings();
        var responseLanguage = ResolveResponseLanguage(settings.Language);

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
        if (TryHandleLocalRoutedResponse(routed, userInput))
        {
            InputText = null;
            return;
        }

        if (!IsApiKeyConfigured)
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = "需要先配置模型服务 API Key，才能使用 Copilot。",
                Timestamp = DateTime.Now,
                IsError = true
            });
            return;
        }

        IsSending = true;
        ErrorMessage = null;

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
            ChatResponse response;

            if (_currentSession is not null && _currentViewRoot is not null)
            {
                response = await _chatService.StreamMessageWithThinkingAsync(
                    userInput,
                    LinkedEntry,
                    LinkedRecommendation,
                    _currentViewRoot,
                    _currentSession,
                    _actionExecutor.HasExistingRecommendations,
                    routed.Skills,
                    responseLanguage,
                    apiKey,
                    baseUrl,
                    settings.EnableThinking,
                    thinkingToken => assistantMessage.Thinking += thinkingToken,
                    textToken => assistantMessage.Text += textToken,
                    CancellationToken.None);
            }
            else
            {
                response = await _chatService.StreamSkillMessageWithThinkingAsync(
                    userInput,
                    routed.Skills,
                    responseLanguage,
                    apiKey,
                    baseUrl,
                    settings.EnableThinking,
                    thinkingToken => assistantMessage.Thinking += thinkingToken,
                    textToken => assistantMessage.Text += textToken,
                    CancellationToken.None);
            }

            ApplyProposalIfAny(assistantMessage, response.Proposal);
            assistantMessage.IsStreaming = false;
            LinkedEntry = null;
            LinkedRecommendation = null;
            LinkedItemPath = null;
        }
        catch (Exception ex)
        {
            assistantMessage.IsStreaming = false;
            assistantMessage.IsError = true;
            assistantMessage.Text = string.IsNullOrEmpty(assistantMessage.Text)
                ? ex.Message
                : assistantMessage.Text + L.Format("ChatErrorAppend", ex.Message);
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmInteractionAsync(AiInteractionCard? card)
    {
        if (card is null || !card.IsPending) return;
        card.IsBusy = true;
        card.Status = AiInteractionCardStatus.Running;
        card.StatusText = L.Text("CopilotCardRunning");
        try
        {
            if (card.Action.Kind == AiActionKind.ClearConversation)
            {
                ClearConversation();
                return;
            }

            var result = await _actionExecutor.ExecuteAsync(card.Action, CancellationToken.None);
            card.Status = result.Success ? AiInteractionCardStatus.Completed : AiInteractionCardStatus.Failed;
            card.StatusText = result.Details is null ? result.Message : $"{result.Message}\n{result.Details}";
            if (result.Success && card.Action.Kind == AiActionKind.StartScan && !string.IsNullOrWhiteSpace(card.FollowUpPrompt))
            {
                _ = ContinueAfterConfirmedScanAsync(card.FollowUpPrompt);
            }
        }
        catch (Exception ex)
        {
            card.Status = AiInteractionCardStatus.Failed;
            card.StatusText = ex.Message;
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelInteraction(AiInteractionCard? card)
    {
        if (card is null || !card.IsPending) return;
        card.Status = AiInteractionCardStatus.Cancelled;
        card.StatusText = L.Text("CopilotCardCancelled");
    }

    private bool TryHandleLocalRoutedResponse(AiSkillRoutingResult routed, string userInput)
    {
        if (routed.SuggestedAction is not null)
        {
            var message = new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = BuildSuggestedActionText(routed.SuggestedAction),
                Timestamp = DateTime.Now
            };
            message.InteractionCard = BuildInteractionCard(routed.SuggestedAction, userInput);
            Messages.Add(message);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(routed.LocalAnswer)
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

    private async Task ContinueAfterConfirmedScanAsync(string originalPrompt)
    {
        if (_currentSession is null || _currentViewRoot is null)
        {
            return;
        }

        RefreshApiKeyStatus();
        if (!IsApiKeyConfigured)
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = Localized("Scan completed. Configure a model service API Key before I can continue analyzing the scanned data.", "扫描已完成。需要先配置模型服务 API Key，我才能继续分析扫描数据。"),
                Timestamp = DateTime.Now,
                IsError = true
            });
            return;
        }

        _followUpCancellation?.Cancel();
        _followUpCancellation?.Dispose();
        _followUpCancellation = new CancellationTokenSource();
        var cancellationToken = _followUpCancellation.Token;

        var settings = _settingsService.LoadSettings();
        var responseLanguage = ResolveResponseLanguage(settings.Language);
        var apiKey = _settingsService.GetApiKey(settings)!;
        var baseUrl = settings.AnthropicBaseUrl;
        var followUpPrompt = BuildPostScanFollowUpPrompt(originalPrompt);

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
            var response = await _chatService.StreamMessageWithThinkingAsync(
                followUpPrompt,
                null,
                null,
                _currentViewRoot,
                _currentSession,
                _actionExecutor.HasExistingRecommendations,
                [],
                responseLanguage,
                apiKey,
                baseUrl,
                settings.EnableThinking,
                thinkingToken => assistantMessage.Thinking += thinkingToken,
                textToken => assistantMessage.Text += textToken,
                cancellationToken);

            ApplyProposalIfAny(assistantMessage, response.Proposal);
            assistantMessage.IsStreaming = false;
        }
        catch (OperationCanceledException)
        {
            assistantMessage.IsStreaming = false;
        }
        catch (Exception ex)
        {
            assistantMessage.IsStreaming = false;
            assistantMessage.IsError = true;
            assistantMessage.Text = string.IsNullOrEmpty(assistantMessage.Text)
                ? ex.Message
                : assistantMessage.Text + L.Format("ChatErrorAppend", ex.Message);
        }
    }

    private static string BuildPostScanFollowUpPrompt(string originalPrompt)
        => $"The user originally asked: {originalPrompt}\n\nThe scan requested by the user has completed successfully. Continue and complete the original request using the current scanned file tree. Use the required answer language from the system prompt, not the language of the quoted original request. Do not ask the user to scan again. Do not propose another scan. Do not stop at saying the scan is complete. First provide a useful initial analysis from the scanned data, including the largest relevant folders/files you can identify. If the original request mentions games or bought games, infer likely game libraries and installed game folders from names such as SteamLibrary, steamapps, common, Epic, Xbox, Ubisoft, Battle.net, GOG, or recognizable game titles, then summarize the best findings with sizes. Only ask a follow-up after giving that initial analysis.";

    private static AiInteractionCard BuildInteractionCard(AiActionRequest action, string? followUpPrompt = null)
    {
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "当前范围");
        return action.Kind switch
        {
            AiActionKind.StartScan => new AiInteractionCard
            {
                Title = Localized("Scan this path", "扫描这个路径"),
                Description = Localized($"Scan {scope} before analyzing its space usage.", $"需要先扫描 {scope}，才能继续分析里面的空间占用。"),
                Impact = Localized("This replaces the current scan result and refreshes Treemap, TreeView, and AI-readable space context.", "会替换当前扫描结果，并刷新 Treemap、TreeView 和 AI 可理解的空间上下文。"),
                ConfirmText = Localized("Start Scan", "开始扫描"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                Action = action,
                FollowUpPrompt = followUpPrompt
            },
            AiActionKind.AnalyzeCleanup => new AiInteractionCard
            {
                Title = Localized("Analyze cleanup recommendations", "分析清理建议"),
                Description = Localized($"Generate reviewable cleanup candidates for {scope}.", $"将基于 {scope} 生成可复核的清理候选。"),
                Impact = action.WillOverwriteExistingData
                    ? Localized("This overwrites existing recommendations; actual cleanup still requires another confirmation.", "会覆盖现有推荐结果；真正清理仍需你再次确认。")
                    : Localized("Actual cleanup still requires another confirmation.", "真正清理仍需你再次确认。"),
                ConfirmText = Localized("Start Analysis", "开始分析"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                Action = action,
                FollowUpPrompt = followUpPrompt
            },
            _ => new AiInteractionCard
            {
                Title = L.Text("CopilotCardDefaultTitle"),
                Description = Localized($"Prepare to handle {scope}.", $"准备处理 {scope}。"),
                ConfirmText = L.Text("CopilotCardDefaultConfirm"),
                CancelText = L.Text("CopilotCardDefaultCancel"),
                Action = action,
                FollowUpPrompt = followUpPrompt
            }
        };
    }

    private static string BuildSuggestedActionText(AiActionRequest action)
    {
        var scope = action.ScopeLabel ?? action.Path ?? Localized("current scope", "当前范围");
        return action.Kind switch
        {
            AiActionKind.StartScan => Localized(
                $"Sure — I prepared a confirmation card to scan {scope}. After you confirm, the scan starts; after it finishes, I can analyze the space usage from the result.",
                $"可以，我先给你一个“扫描 {scope}”的确认卡片。你确认后开始扫描；扫描完成后我再基于结果分析占用情况。"),
            AiActionKind.AnalyzeCleanup => Localized(
                $"Sure — I prepared a confirmation card to analyze cleanup recommendations for {scope}. Analysis starts after you confirm.",
                $"可以，我先给你一个“分析 {scope} 清理建议”的确认卡片。你确认后开始分析。"),
            _ => Localized("I prepared a confirmation card and will run it only after you confirm.", "我先给你一个确认卡片，你确认后再执行。")
        };
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
            Description = GetString(card, "description") ?? request.ScopeLabel ?? request.Path ?? "磁盘空间管理动作",
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
        Messages.Add(new ChatMessage
        {
            Sender = ChatSender.Assistant,
            Text = explicitRequest
                ? Localized("Confirm clearing this chat?", "确认清空对话？")
                : Localized("You can clear this chat window and start fresh. Confirm clearing it?", "你可以清空本窗口并重新开始，确认清空吗？"),
            Timestamp = DateTime.Now,
            InteractionCard = BuildClearConversationCard(explicitRequest)
        });
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
            ConfirmText = Localized("Clear Chat", "清空对话"),
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
        _chatService.ClearHistory();
        Messages.Clear();
        LinkedEntry = null;
        LinkedRecommendation = null;
        LinkedItemPath = null;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ToggleThinking(ChatMessage message) => message.IsThinkingExpanded = !message.IsThinkingExpanded;
}



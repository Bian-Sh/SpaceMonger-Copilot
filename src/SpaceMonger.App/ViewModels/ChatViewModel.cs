using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Localization;
using SpaceMonger.App.Services.Copilot;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Copilot;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ISettingsService _settingsService;
    private readonly IAiSkillRouter _skillRouter;
    private IAiDiskActionExecutor _actionExecutor = new NullAiDiskActionExecutor();

    private ScanSession? _currentSession;
    private FileEntry? _currentViewRoot;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _messages = new();

    [ObservableProperty]
    private bool _hasMessages;

    [ObservableProperty]
    private string? _inputText;

    [ObservableProperty]
    private FileEntry? _linkedEntry;

    [ObservableProperty]
    private CleanupRecommendation? _linkedRecommendation;

    [ObservableProperty]
    private bool _isChatAvailable;

    [ObservableProperty]
    private bool _isApiKeyConfigured;

    [ObservableProperty]
    private bool _isSending;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _linkedItemPath;

    public ChatViewModel(IChatService chatService, ISettingsService settingsService, IAiSkillRouter skillRouter)
    {
        _chatService = chatService;
        _settingsService = settingsService;
        _skillRouter = skillRouter;
        Messages.CollectionChanged += Messages_CollectionChanged;
        RefreshApiKeyStatus();
    }

    public void SetActionExecutor(IAiDiskActionExecutor actionExecutor)
    {
        _actionExecutor = actionExecutor;
    }

    partial void OnMessagesChanged(ObservableCollection<ChatMessage>? oldValue, ObservableCollection<ChatMessage> newValue)
    {
        if (oldValue is not null)
            oldValue.CollectionChanged -= Messages_CollectionChanged;

        newValue.CollectionChanged += Messages_CollectionChanged;
        HasMessages = newValue.Count > 0;
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasMessages = Messages.Count > 0;
    }

    public void SetContext(ScanSession session, FileEntry viewRoot)
    {
        _currentSession = session;
        _currentViewRoot = viewRoot;
        IsChatAvailable = true;

        var settings = _settingsService.LoadSettings();
        IsApiKeyConfigured = !string.IsNullOrEmpty(_settingsService.GetApiKey(settings));
    }

    public void UpdateViewRoot(FileEntry viewRoot)
    {
        _currentViewRoot = viewRoot;
    }

    public void RefreshApiKeyStatus()
    {
        var settings = _settingsService.LoadSettings();
        IsApiKeyConfigured = !string.IsNullOrEmpty(_settingsService.GetApiKey(settings));
    }

    partial void OnLinkedEntryChanged(FileEntry? value)
    {
        LinkedItemPath = value?.Path;
    }

    partial void OnLinkedRecommendationChanged(CleanupRecommendation? value)
    {
        LinkedItemPath = value?.TargetPath;
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        RefreshApiKeyStatus();

        if (string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }

        if (IsClearConversationRequest(InputText))
        {
            ClearConversation();
            InputText = null;
            return;
        }

        var userInput = InputText.Trim();
        var routing = _skillRouter.Route(userInput, LinkedEntry, _currentViewRoot, _actionExecutor.HasExistingRecommendations);

        var userMessage = new ChatMessage
        {
            Sender = ChatSender.User,
            Text = userInput,
            Timestamp = DateTime.Now,
            LinkedEntry = LinkedEntry,
            LinkedRecommendation = LinkedRecommendation
        };
        Messages.Add(userMessage);
        InputText = null;

        if (routing.SuggestedAction is not null)
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = BuildActionIntro(routing.SuggestedAction),
                Timestamp = DateTime.Now,
                InteractionCard = BuildInteractionCard(routing.SuggestedAction)
            });
            LinkedEntry = null;
            LinkedRecommendation = null;
            LinkedItemPath = null;
            return;
        }

        if (!string.IsNullOrWhiteSpace(routing.LocalAnswer))
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = routing.LocalAnswer,
                Timestamp = DateTime.Now
            });
            return;
        }

        if (!IsApiKeyConfigured)
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = "需要先配置模型服务 API Key，才能继续进行 AI 对话或深度分析。",
                Timestamp = DateTime.Now,
                IsError = true
            });
            return;
        }

        if (_currentSession is null || _currentViewRoot is null)
        {
            Messages.Add(new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = "请先完成扫描，聊天需要当前磁盘分析上下文。",
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
            Text = "",
            Thinking = "",
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);

        try
        {
            var settings = _settingsService.LoadSettings();
            var apiKey = _settingsService.GetApiKey(settings)!;
            var baseUrl = settings.AnthropicBaseUrl;

            await _chatService.StreamMessageWithThinkingAsync(
                userInput,
                LinkedEntry,
                LinkedRecommendation,
                _currentViewRoot!,
                _currentSession!,
                routing.Skills,
                apiKey,
                baseUrl,
                thinkingToken => assistantMessage.Thinking += thinkingToken,
                textToken => assistantMessage.Text += textToken,
                CancellationToken.None);

            assistantMessage.IsStreaming = false;
            LinkedEntry = null;
            LinkedRecommendation = null;
            LinkedItemPath = null;
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(assistantMessage.Text))
            {
                assistantMessage.IsStreaming = false;
                assistantMessage.Text += L.Format("ChatErrorAppend", ex.Message);
                assistantMessage.IsError = true;
            }
            else
            {
                assistantMessage.Text = ex.Message;
                assistantMessage.IsStreaming = false;
                assistantMessage.IsError = true;
            }

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
        if (card is null || !card.IsPending)
            return;

        card.IsBusy = true;
        card.Status = AiInteractionCardStatus.Running;
        card.StatusText = "正在执行...";

        try
        {
            var result = await _actionExecutor.ExecuteAsync(card.Action, CancellationToken.None);
            card.Status = result.Success ? AiInteractionCardStatus.Completed : AiInteractionCardStatus.Failed;
            card.StatusText = result.Details is null ? result.Message : $"{result.Message}\n{result.Details}";
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
        if (card is null || !card.IsPending)
            return;

        card.Status = AiInteractionCardStatus.Cancelled;
        card.StatusText = "已取消，未改变当前数据。";
    }

    private static string BuildActionIntro(AiActionRequest action)
    {
        return action.Kind switch
        {
            AiActionKind.StartScan => $"我可以扫描 `{action.ScopeLabel}`，完成后会刷新 TreeView、Treemap 和聊天上下文。",
            AiActionKind.AnalyzeCleanup => "我可以基于当前扫描数据运行推荐清理分析。",
            AiActionKind.NavigateToScannedPath => $"我可以尝试在当前扫描树中定位 `{action.ScopeLabel}`。",
            _ => "我可以执行这个磁盘空间管理动作。"
        };
    }

    private static AiInteractionCard BuildInteractionCard(AiActionRequest action)
    {
        return action.Kind switch
        {
            AiActionKind.StartScan => new AiInteractionCard
            {
                Title = "扫描指定路径",
                Description = action.ScopeLabel ?? action.Path ?? "指定路径",
                Impact = "会替换当前扫描结果，并刷新 TreeView、Treemap、推荐分析上下文。",
                ConfirmText = "开始扫描",
                CancelText = "先不扫描",
                Action = action
            },
            AiActionKind.AnalyzeCleanup => new AiInteractionCard
            {
                Title = "运行推荐清理分析",
                Description = action.ScopeLabel ?? "当前扫描范围",
                Impact = action.WillOverwriteExistingData
                    ? "已有推荐清理结果会被新的分析结果覆盖。"
                    : "会调用模型分析当前扫描数据，生成可审查的清理建议。",
                ConfirmText = "开始分析",
                CancelText = "先不分析",
                Action = action
            },
            AiActionKind.NavigateToScannedPath => new AiInteractionCard
            {
                Title = "定位扫描树路径",
                Description = action.ScopeLabel ?? action.Path ?? "指定路径",
                Impact = "只会在当前已扫描数据中导航，不会访问未扫描磁盘。",
                ConfirmText = "定位路径",
                CancelText = "取消",
                Action = action
            },
            _ => new AiInteractionCard
            {
                Title = "确认操作",
                Description = action.ScopeLabel ?? "磁盘空间管理动作",
                Impact = "确认后执行。",
                Action = action
            }
        };
    }

    private bool IsClearConversationRequest(string text)
    {
        var normalized = text.Trim().ToLowerInvariant();
        return normalized is "clear" or "clear chat" or "clear conversation" or "reset chat"
            || normalized.Contains("清空对话", StringComparison.Ordinal)
            || normalized.Contains("清除对话", StringComparison.Ordinal)
            || normalized.Contains("重新开始对话", StringComparison.Ordinal)
            || normalized.Contains("开启新话题", StringComparison.Ordinal)
            || normalized.Contains("新话题", StringComparison.Ordinal);
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
    private void ToggleThinking(ChatMessage message)
    {
        message.IsThinkingExpanded = !message.IsThinkingExpanded;
    }
}

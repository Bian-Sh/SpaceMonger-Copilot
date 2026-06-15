using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceMonger.App.Localization;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Models;
using SpaceMonger.Core.Services.Chat;
using SpaceMonger.Core.Services.Settings;

namespace SpaceMonger.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IChatService _chatService;
    private readonly ISettingsService _settingsService;

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

    public ChatViewModel(IChatService chatService, ISettingsService settingsService)
    {
        _chatService = chatService;
        _settingsService = settingsService;
        Messages.CollectionChanged += Messages_CollectionChanged;
        RefreshApiKeyStatus();
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

        if (string.IsNullOrWhiteSpace(InputText) || !IsApiKeyConfigured)
        {
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

        var userMessage = new ChatMessage
        {
            Sender = ChatSender.User,
            Text = InputText,
            Timestamp = DateTime.Now,
            LinkedEntry = LinkedEntry,
            LinkedRecommendation = LinkedRecommendation
        };
        Messages.Add(userMessage);

        // Add a placeholder assistant message that will be updated with streamed tokens.
        var assistantMessage = new ChatMessage
        {
            Sender = ChatSender.Assistant,
            Text = "",
            Timestamp = DateTime.Now,
            IsStreaming = true
        };
        Messages.Add(assistantMessage);

        try
        {
            var settings = _settingsService.LoadSettings();
            var apiKey = _settingsService.GetApiKey(settings)!;
            var baseUrl = settings.AnthropicBaseUrl;

            var userInput = InputText;
            await _chatService.StreamMessageAsync(
                userInput,
                LinkedEntry,
                LinkedRecommendation,
                _currentViewRoot!,
                _currentSession!,
                apiKey,
                baseUrl,
                token => assistantMessage.Text += token,
                CancellationToken.None);

            assistantMessage.IsStreaming = false;

            InputText = null;
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
}


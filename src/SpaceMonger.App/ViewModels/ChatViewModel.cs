using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        if (string.IsNullOrWhiteSpace(InputText) || !IsChatAvailable || !IsApiKeyConfigured)
        {
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

        try
        {
            var settings = _settingsService.LoadSettings();
            var apiKey = _settingsService.GetApiKey(settings)!;

            var response = await _chatService.SendMessageAsync(
                InputText,
                LinkedEntry,
                LinkedRecommendation,
                _currentViewRoot!,
                _currentSession!,
                apiKey,
                CancellationToken.None);

            var assistantMessage = new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = response,
                Timestamp = DateTime.Now
            };
            Messages.Add(assistantMessage);

            InputText = null;
            LinkedEntry = null;
            LinkedRecommendation = null;
            LinkedItemPath = null;
        }
        catch (Exception ex)
        {
            var errorChatMessage = new ChatMessage
            {
                Sender = ChatSender.Assistant,
                Text = ex.Message,
                Timestamp = DateTime.Now,
                IsError = true
            };
            Messages.Add(errorChatMessage);

            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }
}

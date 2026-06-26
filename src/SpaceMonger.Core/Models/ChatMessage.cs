using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpaceMonger.Core.Enums;
using SpaceMonger.Core.Services.Copilot;

namespace SpaceMonger.Core.Models;

public class ChatMessage : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private string _thinking = string.Empty;
    private bool _isError;
    private bool _isStreaming;
    private bool _isThinkingExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public ChatSender Sender { get; set; }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasText)); }
    }

    /// <summary>
    /// The thinking/reasoning content from the AI model.
    /// </summary>
    public string Thinking
    {
        get => _thinking;
        set { _thinking = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasThinking)); }
    }

    public DateTime Timestamp { get; set; }

    public FileEntry? LinkedEntry { get; set; }

    public CleanupRecommendation? LinkedRecommendation { get; set; }

    public AiInteractionCard? InteractionCard { get; set; }

    public bool IsError
    {
        get => _isError;
        set { _isError = value; OnPropertyChanged(); }
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set { _isStreaming = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether the thinking section is expanded (visible).
    /// </summary>
    public bool IsThinkingExpanded
    {
        get => _isThinkingExpanded;
        set { _isThinkingExpanded = value; OnPropertyChanged(); }
    }

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}



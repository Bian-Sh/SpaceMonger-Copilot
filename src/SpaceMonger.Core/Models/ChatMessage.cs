using System.ComponentModel;
using System.Runtime.CompilerServices;
using SpaceMonger.Core.Enums;

namespace SpaceMonger.Core.Models;

public class ChatMessage : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isError;
    private bool _isStreaming;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; set; } = Guid.NewGuid();

    public ChatSender Sender { get; set; }

    public string Text
    {
        get => _text;
        set { _text = value; OnPropertyChanged(); }
    }

    public DateTime Timestamp { get; set; }

    public FileEntry? LinkedEntry { get; set; }

    public CleanupRecommendation? LinkedRecommendation { get; set; }

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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

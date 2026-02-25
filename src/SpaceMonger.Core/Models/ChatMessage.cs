using SpaceMonger.Core.Enums;

namespace SpaceMonger.Core.Models;

public class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ChatSender Sender { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public FileEntry? LinkedEntry { get; set; }

    public CleanupRecommendation? LinkedRecommendation { get; set; }

    public bool IsError { get; set; }
}

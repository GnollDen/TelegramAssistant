namespace TgAssistant.Core.Models;

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ChatId { get; set; }
    public int SessionIndex { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public DateTime LastMessageAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public bool IsAnalyzed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

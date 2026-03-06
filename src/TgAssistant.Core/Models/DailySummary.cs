namespace TgAssistant.Core.Models;

public class DailySummary
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ChatId { get; set; }
    public Guid? EntityId { get; set; }
    public DateOnly Date { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int MediaCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

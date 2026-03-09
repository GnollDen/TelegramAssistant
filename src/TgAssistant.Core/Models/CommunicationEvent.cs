namespace TgAssistant.Core.Models;

public class CommunicationEvent
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public Guid? EntityId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? Sentiment { get; set; }
    public string? Summary { get; set; }
    public float Confidence { get; set; } = 0.5f;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

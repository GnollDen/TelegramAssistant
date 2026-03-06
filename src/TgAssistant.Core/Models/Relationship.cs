namespace TgAssistant.Core.Models;

public class Relationship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromEntityId { get; set; }
    public Guid ToEntityId { get; set; }
    public string Type { get; set; } = string.Empty;
    public ConfidenceStatus Status { get; set; } = ConfidenceStatus.Tentative;
    public float Confidence { get; set; } = 0.5f;
    public string? ContextText { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

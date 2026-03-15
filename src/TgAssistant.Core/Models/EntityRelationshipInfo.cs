namespace TgAssistant.Core.Models;

public class EntityRelationshipInfo
{
    public Guid RelationshipId { get; set; }
    public Guid FromEntityId { get; set; }
    public string FromEntityName { get; set; } = string.Empty;
    public Guid ToEntityId { get; set; }
    public string ToEntityName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public ConfidenceStatus Status { get; set; } = ConfidenceStatus.Tentative;
    public float Confidence { get; set; } = 0.5f;
    public string? ContextText { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

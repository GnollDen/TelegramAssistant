namespace TgAssistant.Core.Models;

/// <summary>
/// An edge in the knowledge graph: relationship between two entities.
/// </summary>
public class Relationship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromEntityId { get; set; }
    public Guid ToEntityId { get; set; }
    
    /// <summary>
    /// Relationship type: friend, sibling, colleague, partner, parent, child, etc.
    /// </summary>
    public string Type { get; set; } = string.Empty;
    
    public ConfidenceStatus Status { get; set; } = ConfidenceStatus.Tentative;
    public float Confidence { get; set; } = 0.5f;
    
    /// <summary>
    /// Original message text that led to this relationship being created.
    /// </summary>
    public string? ContextText { get; set; }
    public long? SourceMessageId { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

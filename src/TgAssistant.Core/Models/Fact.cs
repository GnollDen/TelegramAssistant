namespace TgAssistant.Core.Models;

/// <summary>
/// A fact about an entity: preference, trait, life event, etc.
/// </summary>
public class Fact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    
    /// <summary>
    /// Category: personal, work, preferences, relationships, health, habits, communication_style
    /// </summary>
    public string Category { get; set; } = string.Empty;
    
    /// <summary>
    /// Fact key: "favorite_food", "birthday", "pet_name", "job_title"
    /// </summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>
    /// Fact value: "sushi", "March 15", "Barsik", "Software Engineer"
    /// </summary>
    public string Value { get; set; } = string.Empty;
    
    public ConfidenceStatus Status { get; set; } = ConfidenceStatus.Tentative;
    public float Confidence { get; set; } = 0.5f;
    
    public long? SourceMessageId { get; set; }
    
    /// <summary>
    /// When this fact became true.
    /// </summary>
    public DateTime? ValidFrom { get; set; }
    
    /// <summary>
    /// When this fact stopped being true. Null = still current.
    /// </summary>
    public DateTime? ValidUntil { get; set; }
    
    public bool IsCurrent { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Status for facts and relationships extracted by LLM.
/// </summary>
public enum ConfidenceStatus
{
    Confirmed,   // manually confirmed by owner
    Inferred,    // high confidence from Claude
    Tentative,   // low confidence, needs review
    Rejected     // explicitly rejected by owner, never re-suggest
}

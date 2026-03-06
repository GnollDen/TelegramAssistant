namespace TgAssistant.Core.Models;

/// <summary>
/// A node in the knowledge graph: person, organization, place, pet, event.
/// </summary>
public class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EntityType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    
    /// <summary>
    /// Link to Telegram contact if this entity is a known TG user.
    /// </summary>
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    
    /// <summary>
    /// Freeform metadata (photo_url, notes, etc.)
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum EntityType
{
    Person,
    Organization,
    Place,
    Pet,
    Event
}

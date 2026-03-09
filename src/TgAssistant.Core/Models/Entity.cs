namespace TgAssistant.Core.Models;

public class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public EntityType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string? ActorKey { get; set; }
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum EntityType
{
    Person, Organization, Place, Pet, Event
}

namespace TgAssistant.Core.Models;

public class Fact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntityId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ConfidenceStatus Status { get; set; } = ConfidenceStatus.Tentative;
    public float Confidence { get; set; } = 0.5f;
    public long? SourceMessageId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsCurrent { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum ConfidenceStatus
{
    Confirmed, Inferred, Tentative, Rejected
}

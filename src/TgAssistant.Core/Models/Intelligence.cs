namespace TgAssistant.Core.Models;

public class IntelligenceObservation
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public Guid? EntityId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string ObservationType { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? Value { get; set; }
    public string? Evidence { get; set; }
    public float Confidence { get; set; } = 0.5f;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class IntelligenceClaim
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public Guid? EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string ClaimType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public ConfidenceStatus Status { get; set; } = ConfidenceStatus.Tentative;
    public float Confidence { get; set; } = 0.5f;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

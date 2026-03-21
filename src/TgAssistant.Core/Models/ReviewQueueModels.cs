namespace TgAssistant.Core.Models;

public class InboxItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ItemType { get; set; } = string.Empty;
    public string SourceObjectType { get; set; } = string.Empty;
    public string SourceObjectId { get; set; } = string.Empty;
    public string Priority { get; set; } = "important";
    public bool IsBlocking { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string Status { get; set; } = "open";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? LastActor { get; set; }
    public string? LastReason { get; set; }
}

public class ConflictRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ConflictType { get; set; } = string.Empty;
    public string ObjectAType { get; set; } = string.Empty;
    public string ObjectAId { get; set; } = string.Empty;
    public string ObjectBType { get; set; } = string.Empty;
    public string ObjectBId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public string Status { get; set; } = "open";
    public Guid? PeriodId { get; set; }
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? LastActor { get; set; }
    public string? LastReason { get; set; }
}

public class DependencyLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UpstreamType { get; set; } = string.Empty;
    public string UpstreamId { get; set; } = string.Empty;
    public string DownstreamType { get; set; } = string.Empty;
    public string DownstreamId { get; set; } = string.Empty;
    public string LinkType { get; set; } = "affects";
    public string? LinkReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

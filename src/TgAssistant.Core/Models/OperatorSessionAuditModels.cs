namespace TgAssistant.Core.Models;

public class OperatorSessionAuditRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string SessionEventType { get; set; } = string.Empty;
    public string DecisionOutcome { get; set; } = OperatorAuditDecisionOutcomes.Accepted;
    public string? FailureReason { get; set; }
    public string? ScopeKey { get; set; }
    public Guid? TrackedPersonId { get; set; }
    public string? ScopeItemKey { get; set; }
    public string? ItemType { get; set; }
    public OperatorIdentityContext OperatorIdentity { get; set; } = new();
    public OperatorSessionContext Session { get; set; } = new();
    public Dictionary<string, object?> Details { get; set; } = [];
    public DateTime EventTimeUtc { get; set; } = DateTime.UtcNow;
}

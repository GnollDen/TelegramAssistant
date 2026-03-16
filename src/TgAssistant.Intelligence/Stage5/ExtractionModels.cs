namespace TgAssistant.Intelligence.Stage5;

public class AnalysisInputMessage
{
    public long MessageId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class ExtractionBatchResult
{
    public List<ExtractionItem> Items { get; set; } = new();
}

public class ExtractionItem
{
    public long MessageId { get; set; }
    public List<ExtractionEntity> Entities { get; set; } = new();
    public List<ExtractionObservation> Observations { get; set; } = new();
    public List<ExtractionClaim> Claims { get; set; } = new();
    public List<ExtractionFact> Facts { get; set; } = new();
    public List<ExtractionRelationship> Relationships { get; set; } = new();
    public List<ExtractionEvent> Events { get; set; } = new();
    public List<ExtractionProfileSignal> ProfileSignals { get; set; } = new();
    public bool RequiresExpensive { get; set; }
    public string? Reason { get; set; }
}

public class ExtractionEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Person";
    public float Confidence { get; set; } = 0.8f;
    public float TrustFactor { get; set; }
    public bool NeedsClarification { get; set; }
}

public class ExtractionObservation
{
    public string SubjectName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? Value { get; set; }
    public string? Evidence { get; set; }
    public float Confidence { get; set; } = 0.8f;
}

public class ExtractionClaim
{
    public string EntityName { get; set; } = string.Empty;
    public string ClaimType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public float Confidence { get; set; } = 0.8f;
}

public class ExtractionFact
{
    public string EntityName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0.8f;
    public float TrustFactor { get; set; }
    public bool NeedsClarification { get; set; }
}

public class ExtractionRelationship
{
    public string FromEntityName { get; set; } = string.Empty;
    public string ToEntityName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public float Confidence { get; set; } = 0.8f;
}

public class ExtractionEvent
{
    public string Type { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string? ObjectName { get; set; }
    public string? Sentiment { get; set; }
    public string? Summary { get; set; }
    public float Confidence { get; set; } = 0.8f;
}

public class ExtractionProfileSignal
{
    public string SubjectName { get; set; } = string.Empty;
    public string Trait { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string? Evidence { get; set; }
    public float Confidence { get; set; } = 0.8f;
}

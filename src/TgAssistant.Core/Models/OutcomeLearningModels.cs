namespace TgAssistant.Core.Models;

public class OutcomeRecordRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public Guid StrategyRecordId { get; set; }
    public Guid DraftRecordId { get; set; }
    public long? ActualMessageId { get; set; }
    public string? ActualActionText { get; set; }
    public long? FollowUpMessageId { get; set; }
    public string? FollowUpText { get; set; }
    public string? UserOutcomeLabel { get; set; }
    public string? Notes { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "outcome_service";
    public string SourceId { get; set; } = "outcome_mvp";
    public bool Persist { get; set; } = true;
}

public class DraftActionMatchResult
{
    public long? MatchedMessageId { get; set; }
    public float MatchScore { get; set; }
    public bool IsPartialMatch { get; set; }
    public string MatchMethod { get; set; } = "none";
}

public class ObservedOutcomeAssessment
{
    public string Label { get; set; } = "unclear";
    public float Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class LearningSignal
{
    public string SignalKey { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class OutcomeRecordResult
{
    public DraftOutcome Outcome { get; set; } = new();
    public DraftActionMatchResult Match { get; set; } = new();
    public ObservedOutcomeAssessment ObservedOutcome { get; set; } = new();
    public List<LearningSignal> LearningSignals { get; set; } = [];
}

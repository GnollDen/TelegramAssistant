namespace TgAssistant.Core.Legacy.Models;

public class CurrentStateRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "state_engine";
    public string SourceId { get; set; } = "current_state_mvp";
    public DateTime? AsOfUtc { get; set; }
    public bool Persist { get; set; } = true;
}

public class CurrentStateContext
{
    public DateTime AsOfUtc { get; set; }
    public Period? CurrentPeriod { get; set; }
    public IReadOnlyList<Period> Periods { get; set; } = [];
    public IReadOnlyList<Message> RecentMessages { get; set; } = [];
    public IReadOnlyList<ChatSession> RecentSessions { get; set; } = [];
    public IReadOnlyList<ClarificationQuestion> ClarificationQuestions { get; set; } = [];
    public IReadOnlyList<ClarificationAnswer> ClarificationAnswers { get; set; } = [];
    public IReadOnlyList<OfflineEvent> OfflineEvents { get; set; } = [];
    public IReadOnlyList<ConflictRecord> Conflicts { get; set; } = [];
    public IReadOnlyList<StateSnapshot> HistoricalSnapshots { get; set; } = [];
}

public class StateScoreResult
{
    public float Initiative { get; set; }
    public float Responsiveness { get; set; }
    public float Openness { get; set; }
    public float Warmth { get; set; }
    public float Reciprocity { get; set; }
    public float Ambiguity { get; set; }
    public float AvoidanceRisk { get; set; }
    public float EscalationReadiness { get; set; }
    public float ExternalPressure { get; set; }
    public float HistoricalModulationWeight { get; set; }
    public bool HistoryConflictDetected { get; set; }
    public List<string> SignalRefs { get; set; } = [];
    public List<string> RiskRefs { get; set; } = [];
}

public class StateConfidenceResult
{
    public float Confidence { get; set; }
    public bool HighAmbiguity { get; set; }
    public bool HistoryConflictDetected { get; set; }
    public float ScoreCoherence { get; set; }
    public float EvidenceQuality { get; set; }
    public float ConflictLevel { get; set; }
}

public class CurrentStateResult
{
    public StateSnapshot Snapshot { get; set; } = new();
    public StateScoreResult Scores { get; set; } = new();
    public StateConfidenceResult Confidence { get; set; } = new();
}

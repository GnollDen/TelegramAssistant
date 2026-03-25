namespace TgAssistant.Core.Models;

public class StrategyEngineRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long? SelfSenderId { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "strategy_engine";
    public string SourceId { get; set; } = "strategy_mvp";
    public DateTime? AsOfUtc { get; set; }
    public bool? ForceHighUncertainty { get; set; }
    public bool Persist { get; set; } = true;
}

public class StrategyEvaluationContext
{
    public DateTime AsOfUtc { get; set; }
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long SelfSenderId { get; set; }
    public long OtherSenderId { get; set; }
    public Period? CurrentPeriod { get; set; }
    public StateSnapshot? CurrentState { get; set; }
    public IReadOnlyList<Period> Periods { get; set; } = [];
    public IReadOnlyList<ClarificationQuestion> ClarificationQuestions { get; set; } = [];
    public IReadOnlyList<ClarificationAnswer> ClarificationAnswers { get; set; } = [];
    public IReadOnlyList<ConflictRecord> Conflicts { get; set; } = [];
    public IReadOnlyList<Message> RecentMessages { get; set; } = [];
    public IReadOnlyList<ChatSession> RecentSessions { get; set; } = [];
    public IReadOnlyList<ProfileSnapshot> ProfileSnapshots { get; set; } = [];
    public IReadOnlyList<ProfileTrait> ProfileTraits { get; set; } = [];
    public string SelfStyleHint { get; set; } = "balanced_pragmatic";
    public bool HighUncertainty { get; set; }
}

public class StrategyCandidateOption
{
    public string ActionType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public List<string> RiskLabels { get; set; } = [];
    public List<string> EthicalFlags { get; set; } = [];
    public string WhenToUse { get; set; } = string.Empty;
    public string SuccessSigns { get; set; } = string.Empty;
    public string FailureSigns { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public float StateFit { get; set; }
    public float ProfileFit { get; set; }
    public float PairPatternFit { get; set; }
    public float RiskScore { get; set; }
    public float EthicalPenalty { get; set; }
    public float FinalScore { get; set; }
}

public class StrategyRiskAssessment
{
    public List<string> Labels { get; set; } = [];
    public float RiskScore { get; set; }
}

public class StrategyConfidenceAssessment
{
    public float Confidence { get; set; }
    public float OptionSeparation { get; set; }
    public float Ambiguity { get; set; }
    public float ConflictLevel { get; set; }
    public bool HighUncertainty { get; set; }
    public bool HorizonAllowed { get; set; }
}

public class StrategyEngineResult
{
    public StrategyRecord Record { get; set; } = new();
    public List<StrategyOption> Options { get; set; } = [];
    public string MicroStep { get; set; } = string.Empty;
    public List<string> Horizon { get; set; } = [];
    public string WhyNotNotes { get; set; } = string.Empty;
    public StrategyConfidenceAssessment Confidence { get; set; } = new();
}

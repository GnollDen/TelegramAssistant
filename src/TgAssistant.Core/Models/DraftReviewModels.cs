namespace TgAssistant.Core.Legacy.Models;

public class DraftReviewRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public string? CandidateText { get; set; }
    public Guid? DraftRecordId { get; set; }
    public Guid? StrategyRecordId { get; set; }
    public long? SelfSenderId { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "draft_review";
    public string SourceId { get; set; } = "draft_review_mvp";
    public DateTime? AsOfUtc { get; set; }
    public bool Persist { get; set; } = true;
    public bool AllowStrategyAutogeneration { get; set; } = true;
}

public class DraftReviewContext
{
    public Guid ReviewId { get; set; } = Guid.NewGuid();
    public DateTime AsOfUtc { get; set; }
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long SelfSenderId { get; set; }
    public long OtherSenderId { get; set; }
    public string CandidateText { get; set; } = string.Empty;
    public Guid? SourceDraftRecordId { get; set; }
    public StrategyRecord StrategyRecord { get; set; } = new();
    public StrategyOption PrimaryOption { get; set; } = new();
    public StateSnapshot? CurrentState { get; set; }
    public Period? CurrentPeriod { get; set; }
    public IReadOnlyList<ProfileTrait> ProfileTraits { get; set; } = [];
    public IReadOnlyList<Message> RecentMessages { get; set; } = [];
    public IReadOnlyList<ChatSession> RecentSessions { get; set; } = [];
}

public class DraftStrategyFitResult
{
    public bool HasMaterialConflict { get; set; }
    public string ConflictNote { get; set; } = string.Empty;
}

public class DraftRiskAssessment
{
    public string Summary { get; set; } = string.Empty;
    public List<string> MainRisks { get; set; } = [];
    public List<string> RiskLabels { get; set; } = [];
}

public class DraftReviewResult
{
    public Guid ReviewId { get; set; }
    public string Assessment { get; set; } = string.Empty;
    public List<string> MainRisks { get; set; } = [];
    public List<string> RiskLabels { get; set; } = [];
    public string SaferRewrite { get; set; } = string.Empty;
    public string NaturalRewrite { get; set; } = string.Empty;
    public bool StrategyConflictDetected { get; set; }
    public string StrategyConflictNote { get; set; } = string.Empty;
    public Guid? SourceDraftRecordId { get; set; }
}

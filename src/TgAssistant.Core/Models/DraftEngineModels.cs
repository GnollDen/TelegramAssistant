namespace TgAssistant.Core.Legacy.Models;

public class DraftEngineRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public Guid? StrategyRecordId { get; set; }
    public long? SelfSenderId { get; set; }
    public string? UserNotes { get; set; }
    public string? DesiredTone { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "draft_engine";
    public string SourceId { get; set; } = "draft_mvp";
    public DateTime? AsOfUtc { get; set; }
    public bool Persist { get; set; } = true;
    public bool AllowStrategyAutogeneration { get; set; } = true;
}

public class DraftGenerationContext
{
    public DateTime AsOfUtc { get; set; }
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long SelfSenderId { get; set; }
    public long OtherSenderId { get; set; }
    public StrategyRecord StrategyRecord { get; set; } = new();
    public StrategyOption PrimaryOption { get; set; } = new();
    public IReadOnlyList<StrategyOption> StrategyOptions { get; set; } = [];
    public StateSnapshot? CurrentState { get; set; }
    public Period? CurrentPeriod { get; set; }
    public IReadOnlyList<ProfileTrait> ProfileTraits { get; set; } = [];
    public IReadOnlyList<Message> RecentMessages { get; set; } = [];
    public IReadOnlyList<ChatSession> RecentSessions { get; set; } = [];
    public string? UserNotes { get; set; }
    public string? DesiredTone { get; set; }
}

public class DraftContentSet
{
    public string MainDraft { get; set; } = string.Empty;
    public string AltDraft1 { get; set; } = string.Empty;
    public string AltDraft2 { get; set; } = string.Empty;
    public float BaseConfidence { get; set; }
}

public class DraftStyledContent
{
    public string MainDraft { get; set; } = string.Empty;
    public string AltDraft1 { get; set; } = string.Empty;
    public string AltDraft2 { get; set; } = string.Empty;
    public string StyleNotes { get; set; } = string.Empty;
    public float Confidence { get; set; }
}

public class DraftConflictAssessment
{
    public bool HasConflict { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? RiskyIntentAlternative { get; set; }
}

public class DraftEngineResult
{
    public DraftRecord Record { get; set; } = new();
    public bool HasIntentConflict { get; set; }
    public string ConflictReason { get; set; } = string.Empty;
    public Guid? ConflictRecordId { get; set; }
}

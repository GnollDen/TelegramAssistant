namespace TgAssistant.Core.Legacy.Models;

public class ProfileEngineRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long? SelfSenderId { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "profile_engine";
    public string SourceId { get; set; } = "profile_mvp";
    public DateTime? AsOfUtc { get; set; }
    public int MaxPeriodSlices { get; set; } = 3;
    public bool Persist { get; set; } = true;
}

public class ProfileEvidenceContext
{
    public DateTime AsOfUtc { get; set; }
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long SelfSenderId { get; set; }
    public long OtherSenderId { get; set; }
    public IReadOnlyList<Message> Messages { get; set; } = [];
    public IReadOnlyList<ChatSession> Sessions { get; set; } = [];
    public IReadOnlyList<Period> Periods { get; set; } = [];
    public IReadOnlyList<ClarificationQuestion> ClarificationQuestions { get; set; } = [];
    public IReadOnlyList<ClarificationAnswer> ClarificationAnswers { get; set; } = [];
    public IReadOnlyList<OfflineEvent> OfflineEvents { get; set; } = [];
    public IReadOnlyList<StateSnapshot> StateSnapshots { get; set; } = [];
    public IReadOnlyList<IntelligenceClaim> ProfileSignalClaims { get; set; } = [];
}

public class ProfileTraitDraft
{
    public string TraitKey { get; set; } = string.Empty;
    public string ValueLabel { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public bool IsSensitive { get; set; }
    public bool IsTemporary { get; set; }
    public bool IsPeriodSpecific { get; set; }
    public List<EvidenceRef> EvidenceRefs { get; set; } = [];
}

public class ProfileAssessment
{
    public float Confidence { get; set; }
    public float Stability { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class ProfilePatternRecord
{
    public string PatternType { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public Guid? PeriodId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public List<EvidenceRef> EvidenceRefs { get; set; } = [];
}

public class ProfileEngineResult
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public long SelfSenderId { get; set; }
    public long OtherSenderId { get; set; }
    public List<ProfileSnapshot> Snapshots { get; set; } = [];
    public List<ProfileTrait> Traits { get; set; } = [];
    public List<ProfilePatternRecord> Patterns { get; set; } = [];
}

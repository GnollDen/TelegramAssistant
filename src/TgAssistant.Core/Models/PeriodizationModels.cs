namespace TgAssistant.Core.Models;

public class PeriodizationRunRequest
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public string Actor { get; set; } = "system";
    public string SourceType { get; set; } = "periodization";
    public string SourceId { get; set; } = "timeline-mvp";
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public bool Persist { get; set; } = true;
}

public class PeriodizationRunResult
{
    public long CaseId { get; set; }
    public long ChatId { get; set; }
    public List<Period> Periods { get; set; } = new();
    public List<PeriodTransition> Transitions { get; set; } = new();
    public List<PeriodProposalRecord> Proposals { get; set; } = new();
}

public class PeriodProposalRecord
{
    public string ProposalType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<Guid> PeriodIds { get; set; } = new();
    public short ReviewPriority { get; set; }
    public Guid? InboxItemId { get; set; }
}

public class PeriodBoundaryCandidate
{
    public DateTime BoundaryAt { get; set; }
    public float PauseScore { get; set; }
    public float EventScore { get; set; }
    public float DynamicShiftScore { get; set; }
    public bool HasKeyEvent { get; set; }
    public bool HasDynamicShift { get; set; }
    public bool HasLongPause { get; set; }
    public string ReasonSummary { get; set; } = string.Empty;
}

public class PeriodEvidencePack
{
    public List<string> KeySignals { get; set; } = new();
    public List<EvidenceRef> EvidenceRefs { get; set; } = new();
    public int OpenQuestionsCount { get; set; }
    public List<string> OpenQuestionRefs { get; set; } = new();
    public string WhatHelped { get; set; } = string.Empty;
    public string WhatHurt { get; set; } = string.Empty;
    public float InterpretationConfidence { get; set; }
}

public class EvidenceRef
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string? Note { get; set; }
}

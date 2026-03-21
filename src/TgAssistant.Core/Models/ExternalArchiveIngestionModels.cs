using System.Security.Cryptography;
using System.Text;

namespace TgAssistant.Core.Models;

public static class ExternalArchiveSourceClasses
{
    public const string SupportingContextArchive = "supporting_context_archive";
    public const string MutualGroupArchive = "mutual_group_archive";
    public const string IndirectMentionArchive = "indirect_mention_archive";
    public const string CompetingRelationshipArchive = "competing_relationship_archive";

    public static readonly HashSet<string> Allowed =
    [
        SupportingContextArchive,
        MutualGroupArchive,
        IndirectMentionArchive,
        CompetingRelationshipArchive
    ];
}

public static class ExternalArchiveRecordTypes
{
    public const string Message = "message";
    public const string Event = "event";
    public const string RelationshipSignal = "relationship_signal";
    public const string ClarificationInput = "clarification_input";
    public const string Note = "note";

    public static readonly HashSet<string> Allowed =
    [
        Message,
        Event,
        RelationshipSignal,
        ClarificationInput,
        Note
    ];
}

public static class ExternalArchiveTruthLayers
{
    public const string ObservedFromChat = "observed_from_chat";
    public const string ObservedFromAudio = "observed_from_audio";
    public const string UserReported = "user_reported";
    public const string UserConfirmed = "user_confirmed";
    public const string ModelInferred = "model_inferred";
    public const string ModelHypothesis = "model_hypothesis";
}

public class ExternalArchiveImportRequest
{
    public long CaseId { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ImportBatchId { get; set; }
    public string Actor { get; set; } = "system";
    public List<ExternalArchiveRecord> Records { get; set; } = [];
}

public class ExternalArchiveRecord
{
    public string RecordId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? SubjectActorKey { get; set; }
    public string? TargetActorKey { get; set; }
    public long? ChatId { get; set; }
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
    public float Confidence { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
}

public class ExternalArchiveContractValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; } = [];
}

public class ExternalArchiveProvenance
{
    public string TruthLayer { get; set; } = string.Empty;
    public string SourceClass { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ImportBatchId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
}

public class ExternalArchiveWeighting
{
    public float BaseWeight { get; set; }
    public float ConfidenceMultiplier { get; set; }
    public float CorroborationMultiplier { get; set; }
    public float FinalWeight { get; set; }
    public bool NeedsClarification { get; set; }
    public string WeightingReason { get; set; } = string.Empty;
}

public static class ExternalArchiveLinkTypes
{
    public const string GraphLink = "graph_link";
    public const string PeriodLink = "period_link";
    public const string EventLink = "event_link";
    public const string ClarificationLink = "clarification_link";
}

public class ExternalArchiveLinkage
{
    public string LinkType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public float LinkConfidence { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ExternalArchivePreparedRecord
{
    public ExternalArchiveRecord Record { get; set; } = new();
    public ExternalArchiveProvenance Provenance { get; set; } = new();
    public ExternalArchiveWeighting Weighting { get; set; } = new();
    public List<ExternalArchiveLinkage> Linkages { get; set; } = [];
}

public class ExternalArchivePreparationResult
{
    public ExternalArchiveImportRequest Request { get; set; } = new();
    public ExternalArchiveContractValidationResult Validation { get; set; } = new();
    public List<ExternalArchivePreparedRecord> PreparedRecords { get; set; } = [];
}

public static class ExternalArchiveIngestionStatuses
{
    public const string Prepared = "prepared";
    public const string Persisted = "persisted";
    public const string Replayed = "replayed";
    public const string PartialRejected = "partial_rejected";
}

public class ExternalArchiveImportBatch
{
    public Guid RunId { get; set; }
    public long CaseId { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ImportBatchId { get; set; } = string.Empty;
    public string RequestPayloadHash { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; }
    public string Actor { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public int AcceptedCount { get; set; }
    public int ReplayedCount { get; set; }
    public int RejectedCount { get; set; }
    public string Status { get; set; } = ExternalArchiveIngestionStatuses.Prepared;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ExternalArchivePersistedRecord
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public long CaseId { get; set; }
    public string SourceClass { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string ImportBatchId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string? Text { get; set; }
    public string? SubjectActorKey { get; set; }
    public string? TargetActorKey { get; set; }
    public long? ChatId { get; set; }
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public float Confidence { get; set; }
    public string RawPayloadJson { get; set; } = "{}";
    public string EvidenceRefsJson { get; set; } = "[]";
    public string TruthLayer { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public float BaseWeight { get; set; }
    public float ConfidenceMultiplier { get; set; }
    public float CorroborationMultiplier { get; set; }
    public float FinalWeight { get; set; }
    public bool NeedsClarification { get; set; }
    public string WeightingReason { get; set; } = string.Empty;
    public string Status { get; set; } = ExternalArchiveIngestionStatuses.Prepared;
    public DateTime CreatedAt { get; set; }
}

public class ExternalArchiveLinkageArtifact
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid RecordRowId { get; set; }
    public long CaseId { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public float LinkConfidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ReviewStatus { get; set; } = ExternalArchiveIngestionStatuses.Prepared;
    public bool AutoApplyAllowed { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ExternalArchiveIngestionResult
{
    public ExternalArchiveImportBatch Batch { get; set; } = new();
    public List<string> Rejections { get; set; } = [];
    public int PersistedRecordCount { get; set; }
    public int PersistedLinkageCount { get; set; }
    public bool IsReplay { get; set; }
}

public static class ExternalArchiveHashing
{
    public static string ComputeSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

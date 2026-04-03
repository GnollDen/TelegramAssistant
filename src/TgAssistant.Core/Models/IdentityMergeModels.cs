namespace TgAssistant.Core.Models;

public static class IdentityMergeConfidenceTiers
{
    public const string Weak = "weak";
    public const string Medium = "medium";
    public const string Strong = "strong";
}

public static class IdentityMergeStatuses
{
    public const string PendingReview = "pending_review";
    public const string Applied = "applied";
    public const string Reversed = "reversed";
}

public static class IdentityMergeReviewStatuses
{
    public const string NotRequired = "not_required";
    public const string Pending = "pending";
    public const string Approved = "approved";
}

public static class IdentityMergeCorrectionKinds
{
    public const string MergeApplied = "merge_applied";
    public const string MergeReversed = "merge_reversed";
}

public class IdentityMergeApplyRequest
{
    public Guid TargetPersonId { get; set; }
    public Guid SourcePersonId { get; set; }
    public string ConfidenceTier { get; set; } = IdentityMergeConfidenceTiers.Medium;
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = "system";
    public bool ReviewApproved { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public Guid? ModelPassRunId { get; set; }
}

public class IdentityMergeReverseRequest
{
    public Guid MergeId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = "system";
}

public class IdentityMergeRecord
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TargetPersonId { get; set; }
    public Guid SourcePersonId { get; set; }
    public string ConfidenceTier { get; set; } = IdentityMergeConfidenceTiers.Medium;
    public string Status { get; set; } = IdentityMergeStatuses.PendingReview;
    public string ReviewStatus { get; set; } = IdentityMergeReviewStatuses.Pending;
    public string Reason { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = "system";
    public string? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReversalReason { get; set; }
    public Guid? ModelPassRunId { get; set; }
    public string BeforeStateJson { get; set; } = "{}";
    public string AfterStateJson { get; set; } = "{}";
    public string RecomputePlanJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? AppliedAtUtc { get; set; }
    public DateTime? RecomputeEnqueuedAtUtc { get; set; }
    public DateTime? ReversedAtUtc { get; set; }
}

public class IdentityMergeRecomputePlan
{
    public string ScopeKey { get; set; } = string.Empty;
    public string CorrectionKind { get; set; } = IdentityMergeCorrectionKinds.MergeApplied;
    public bool GlobalRerunRequired { get; set; }
    public List<Guid> AffectedPersonIds { get; set; } = [];
    public List<string> InvalidatedObjectFamilies { get; set; } = [];
    public List<IdentityMergeRecomputeTarget> Targets { get; set; } = [];
}

public class IdentityMergeRecomputeTarget
{
    public Guid? PersonId { get; set; }
    public string TargetFamily { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public int Priority { get; set; } = 100;
}

public class IdentityMergeSnapshot
{
    public string ScopeKey { get; set; } = string.Empty;
    public List<IdentityMergePersonState> Persons { get; set; } = [];
    public List<IdentityMergePersonOperatorLinkState> PersonOperatorLinks { get; set; } = [];
    public List<IdentityMergeBindingState> IdentityBindings { get; set; } = [];
    public List<IdentityMergeCandidateState> CandidateStates { get; set; } = [];
    public List<IdentityMergeRelationshipAnchorState> RelationshipAnchors { get; set; } = [];
}

public class IdentityMergePersonState
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string PersonType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string CanonicalName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? PrimaryActorKey { get; set; }
    public long? PrimaryTelegramUserId { get; set; }
    public string? PrimaryTelegramUsername { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class IdentityMergePersonOperatorLinkState
{
    public long Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid OperatorPersonId { get; set; }
    public Guid PersonId { get; set; }
    public string LinkType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? SourceBindingType { get; set; }
    public string? SourceBindingValue { get; set; }
    public string? SourceBindingNormalized { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class IdentityMergeBindingState
{
    public long Id { get; set; }
    public Guid PersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string BindingType { get; set; } = string.Empty;
    public string BindingValue { get; set; } = string.Empty;
    public string BindingNormalized { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public float Confidence { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class IdentityMergeCandidateState
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string CandidateType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string SourceBindingType { get; set; } = string.Empty;
    public string SourceBindingValue { get; set; } = string.Empty;
    public string SourceBindingNormalized { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? MatchedPersonId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class IdentityMergeRelationshipAnchorState
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid FromPersonId { get; set; }
    public Guid ToPersonId { get; set; }
    public string AnchorType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? SourceBindingType { get; set; }
    public string? SourceBindingValue { get; set; }
    public string? SourceBindingNormalized { get; set; }
    public long? SourceMessageId { get; set; }
    public Guid? CandidateIdentityStateId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

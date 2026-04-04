namespace TgAssistant.Core.Models;

public static class ResolutionQueueSortFields
{
    public const string Priority = "priority";
    public const string UpdatedAt = "updated_at";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Priority,
        UpdatedAt
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? Priority
            : sortBy.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? sortBy)
        => Supported.Contains(Normalize(sortBy));
}

public static class ResolutionEvidenceSortFields
{
    public const string ObservedAt = "observed_at";
    public const string TrustFactor = "trust_factor";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        ObservedAt,
        TrustFactor
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? sortBy)
    {
        return string.IsNullOrWhiteSpace(sortBy)
            ? ObservedAt
            : sortBy.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? sortBy)
        => Supported.Contains(Normalize(sortBy));
}

public static class ResolutionSortDirections
{
    public const string Asc = "asc";
    public const string Desc = "desc";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Asc,
        Desc
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? direction)
    {
        return string.IsNullOrWhiteSpace(direction)
            ? Desc
            : direction.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? direction)
        => Supported.Contains(Normalize(direction));
}

public class ResolutionFacetCount
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public abstract class OperatorContractRequestBase
{
    public OperatorIdentityContext OperatorIdentity { get; set; } = new();
    public OperatorSessionContext Session { get; set; } = new();
}

public class OperatorTrackedPersonQueryRequest : OperatorContractRequestBase
{
    public Guid? PreferredTrackedPersonId { get; set; }
    public int Limit { get; set; } = 25;
}

public class OperatorTrackedPersonSelectionRequest : OperatorContractRequestBase
{
    public Guid TrackedPersonId { get; set; }
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
}

public class OperatorTrackedPersonScopeSummary
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public int UnresolvedCount { get; set; }
    public bool HasUnresolved { get; set; }
    public DateTime RecentUpdateAtUtc { get; set; }
    public DateTime? LastUnresolvedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class OperatorTrackedPersonQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public bool AutoSelected { get; set; }
    public string SelectionSource { get; set; } = "none";
    public Guid? ActiveTrackedPersonId { get; set; }
    public OperatorTrackedPersonScopeSummary? ActiveTrackedPerson { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public List<OperatorTrackedPersonScopeSummary> TrackedPersons { get; set; } = [];
}

public class OperatorTrackedPersonSelectionResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public bool ScopeChanged { get; set; }
    public Guid? AuditEventId { get; set; }
    public OperatorTrackedPersonScopeSummary? ActiveTrackedPerson { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
}

public class OperatorPersonWorkspaceListQueryRequest : OperatorContractRequestBase
{
    public string? Search { get; set; }
    public int Limit { get; set; } = 50;
}

public class OperatorPersonWorkspaceListQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public Guid? ActiveTrackedPersonId { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public int TotalCount { get; set; }
    public int FilteredCount { get; set; }
    public List<OperatorTrackedPersonScopeSummary> Persons { get; set; } = [];
}

public class OperatorPersonWorkspaceSummaryQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceDossierQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceProfileQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspacePairDynamicsQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceTimelineQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceEvidenceQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceRevisionsQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceResolutionQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
}

public class OperatorPersonWorkspaceSummaryQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceView Workspace { get; set; } = new();
}

public class OperatorPersonWorkspaceDossierQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceDossierSectionView Dossier { get; set; } = new();
}

public class OperatorPersonWorkspaceProfileQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceProfileSectionView Profile { get; set; } = new();
}

public class OperatorPersonWorkspacePairDynamicsQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspacePairDynamicsSectionView PairDynamics { get; set; } = new();
}

public class OperatorPersonWorkspaceTimelineQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceTimelineSectionView Timeline { get; set; } = new();
}

public class OperatorPersonWorkspaceEvidenceQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceEvidenceSectionView Evidence { get; set; } = new();
}

public class OperatorPersonWorkspaceRevisionsQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceRevisionsSectionView Revisions { get; set; } = new();
}

public class OperatorPersonWorkspaceResolutionQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorPersonWorkspaceResolutionSectionView Resolution { get; set; } = new();
}

public class OperatorPersonWorkspaceView
{
    public OperatorTrackedPersonScopeSummary? TrackedPerson { get; set; }
    public List<OperatorWorkspaceSectionState> Sections { get; set; } = [];
    public OperatorPersonWorkspaceSummarySectionView Summary { get; set; } = new();
}

public class OperatorWorkspaceSectionState
{
    public string SectionKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool Available { get; set; }
}

public class OperatorPersonWorkspaceSummarySectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurableObjectCount { get; set; }
    public int UnresolvedCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public List<ResolutionFacetCount> TruthLayerCounts { get; set; } = [];
    public List<ResolutionFacetCount> PromotionStateCounts { get; set; } = [];
    public List<OperatorWorkspaceDurableFamilyCard> Families { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspaceDossierSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurableDossierCount { get; set; }
    public int DurableFieldCount { get; set; }
    public int ProposalOnlyFieldCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public int TotalEvidenceLinkCount { get; set; }
    public List<OperatorWorkspaceDossierFactView> Facts { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspaceProfileSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurableProfileCount { get; set; }
    public int InferenceCount { get; set; }
    public int HypothesisCount { get; set; }
    public int AmbiguityCount { get; set; }
    public int ContradictionCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public int TotalEvidenceLinkCount { get; set; }
    public List<OperatorWorkspaceProfileSignalView> Signals { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspacePairDynamicsSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurablePairCount { get; set; }
    public int DimensionCount { get; set; }
    public int InferenceCount { get; set; }
    public int HypothesisCount { get; set; }
    public int ConflictCount { get; set; }
    public int AmbiguityCount { get; set; }
    public int ContradictionCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public string DirectionOfChange { get; set; } = string.Empty;
    public int TotalEvidenceLinkCount { get; set; }
    public List<OperatorWorkspacePairDynamicsSignalView> Signals { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspaceTimelineSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurableEpisodeCount { get; set; }
    public int DurableStoryArcCount { get; set; }
    public int KeyShiftCount { get; set; }
    public int OpenArcCount { get; set; }
    public int ContradictionCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public int TotalEvidenceLinkCount { get; set; }
    public List<OperatorWorkspaceTimelineShiftView> Shifts { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspaceEvidenceSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurableObjectCount { get; set; }
    public int EvidenceItemCount { get; set; }
    public int SourceObjectCount { get; set; }
    public int TotalEvidenceLinkCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public List<OperatorWorkspaceEvidenceLinkView> Links { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspaceRevisionsSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int DurableObjectCount { get; set; }
    public int RevisionCount { get; set; }
    public int TriggeredRevisionCount { get; set; }
    public int ContradictionRevisionCount { get; set; }
    public float OverallTrust { get; set; }
    public float OverallUncertainty { get; set; }
    public List<OperatorWorkspaceRevisionView> Revisions { get; set; } = [];
    public List<OperatorWorkspaceProvenanceItem> Provenance { get; set; } = [];
}

public class OperatorPersonWorkspaceResolutionSectionView
{
    public DateTime GeneratedAtUtc { get; set; }
    public int UnresolvedCount { get; set; }
    public int ResolvedCount { get; set; }
    public int ResolvedActionCount { get; set; }
    public DateTime? LastResolvedAtUtc { get; set; }
    public List<ResolutionFacetCount> StatusCounts { get; set; } = [];
    public List<ResolutionFacetCount> PriorityCounts { get; set; } = [];
    public List<OperatorWorkspaceResolutionItemView> Items { get; set; } = [];
}

public class OperatorWorkspaceResolutionItemView
{
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string WhyItMatters { get; set; } = string.Empty;
    public string AffectedFamily { get; set; } = string.Empty;
    public string AffectedObjectRef { get; set; } = string.Empty;
    public float TrustFactor { get; set; }
    public string Status { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string? RecommendedNextAction { get; set; }
    public List<string> AvailableActions { get; set; } = [];
}

public class OperatorWorkspaceEvidenceLinkView
{
    public Guid DurableObjectMetadataId { get; set; }
    public Guid EvidenceItemId { get; set; }
    public Guid SourceObjectId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string DurableObjectFamily { get; set; } = string.Empty;
    public string DurableObjectKey { get; set; } = string.Empty;
    public string DurableTruthLayer { get; set; } = string.Empty;
    public string DurablePromotionState { get; set; } = string.Empty;
    public float DurableConfidence { get; set; }
    public string EvidenceKind { get; set; } = string.Empty;
    public string EvidenceTruthLayer { get; set; } = string.Empty;
    public float EvidenceConfidence { get; set; }
    public string LinkRole { get; set; } = string.Empty;
    public string? EvidenceSummary { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public string SourceDisplayLabel { get; set; } = string.Empty;
    public DateTime? ObservedAtUtc { get; set; }
    public DateTime? SourceOccurredAtUtc { get; set; }
    public DateTime LinkedAtUtc { get; set; }
}

public class OperatorWorkspaceRevisionView
{
    public string Family { get; set; } = string.Empty;
    public Guid DurableObjectId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public Guid? ModelPassRunId { get; set; }
    public int RevisionNumber { get; set; }
    public string RevisionHash { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public float Freshness { get; set; }
    public float Stability { get; set; }
    public int ContradictionCount { get; set; }
    public int EvidenceRefCount { get; set; }
    public string TruthLayer { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public string TriggerKind { get; set; } = string.Empty;
    public string TriggerRef { get; set; } = string.Empty;
    public string PassFamily { get; set; } = string.Empty;
    public string RunKind { get; set; } = string.Empty;
    public string ResultStatus { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class OperatorWorkspaceDossierFactView
{
    public Guid DurableDossierId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public int RevisionNumber { get; set; }
    public string DossierType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ApprovalState { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public int EvidenceRefCount { get; set; }
    public string TruthLayer { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class OperatorWorkspaceProfileSignalView
{
    public Guid DurableProfileId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public int RevisionNumber { get; set; }
    public string ProfileScope { get; set; } = string.Empty;
    public string SignalType { get; set; } = string.Empty;
    public string SignalKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public int EvidenceRefCount { get; set; }
    public string TruthLayer { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class OperatorWorkspacePairDynamicsSignalView
{
    public Guid DurablePairDynamicsId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public int RevisionNumber { get; set; }
    public string PairDynamicsType { get; set; } = string.Empty;
    public string SignalType { get; set; } = string.Empty;
    public string SignalKey { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? SignalValue { get; set; }
    public string DirectionOfChange { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public int EvidenceRefCount { get; set; }
    public string TruthLayer { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class OperatorWorkspaceTimelineShiftView
{
    public string Family { get; set; } = string.Empty;
    public Guid DurableObjectId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public int RevisionNumber { get; set; }
    public string ShiftType { get; set; } = string.Empty;
    public string ClosureState { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime? ShiftStartedAtUtc { get; set; }
    public DateTime? ShiftEndedAtUtc { get; set; }
    public float Confidence { get; set; }
    public int EvidenceRefCount { get; set; }
    public string TruthLayer { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

public class OperatorWorkspaceDurableFamilyCard
{
    public string Family { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int ObjectCount { get; set; }
    public float Trust { get; set; }
    public float Uncertainty { get; set; }
    public float Confidence { get; set; }
    public float Coverage { get; set; }
    public float Freshness { get; set; }
    public float Stability { get; set; }
    public int ContradictionCount { get; set; }
    public int EvidenceLinkCount { get; set; }
    public DateTime? LatestUpdatedAtUtc { get; set; }
    public string TruthLayer { get; set; } = string.Empty;
    public string PromotionState { get; set; } = string.Empty;
    public string? LatestSummary { get; set; }
}

public class OperatorWorkspaceProvenanceItem
{
    public string Family { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public int EvidenceLinkCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public string? Summary { get; set; }
}

public class OperatorResolutionQueueQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public List<string> ItemTypes { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<string> Priorities { get; set; } = [];
    public List<string> RecommendedActions { get; set; } = [];
    public string SortBy { get; set; } = ResolutionQueueSortFields.Priority;
    public string SortDirection { get; set; } = ResolutionSortDirections.Desc;
    public int Limit { get; set; } = 50;
}

public class OperatorResolutionQueueQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public ResolutionQueueResult Queue { get; set; } = new();
}

public class OperatorResolutionDetailQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public int EvidenceLimit { get; set; } = 5;
    public string EvidenceSortBy { get; set; } = ResolutionEvidenceSortFields.ObservedAt;
    public string EvidenceSortDirection { get; set; } = ResolutionSortDirections.Desc;
}

public class OperatorResolutionDetailQueryResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public ResolutionDetailResult Detail { get; set; } = new();
}

public class OperatorResolutionActionResultEnvelope
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public ResolutionActionResult Action { get; set; } = new();
}

public class OperatorOfflineEventQueryApiRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public List<string> Statuses { get; set; } = [];
    public string SortBy { get; set; } = OperatorOfflineEventSortFields.UpdatedAt;
    public string SortDirection { get; set; } = ResolutionSortDirections.Desc;
    public int Limit { get; set; } = 50;
}

public class OperatorOfflineEventQueryApiResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventQueryResult OfflineEvents { get; set; } = new();
}

public class OperatorOfflineEventDetailQueryRequest : OperatorContractRequestBase
{
    public Guid? TrackedPersonId { get; set; }
    public Guid OfflineEventId { get; set; }
}

public class OperatorOfflineEventDetailQueryResultEnvelope
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventDetailView OfflineEvent { get; set; } = new();
}

public class OperatorOfflineEventRefinementResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventDetailView OfflineEvent { get; set; } = new();
}

public class OperatorOfflineEventTimelineLinkageUpdateResult
{
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public Guid? AuditEventId { get; set; }
    public OperatorSessionContext Session { get; set; } = new();
    public OperatorOfflineEventDetailView OfflineEvent { get; set; } = new();
}

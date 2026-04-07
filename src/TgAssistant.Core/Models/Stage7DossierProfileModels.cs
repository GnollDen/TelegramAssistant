namespace TgAssistant.Core.Models;

public static class Stage7DurableObjectFamilies
{
    public const string Dossier = "dossier";
    public const string Profile = "profile";
    public const string PairDynamics = "pair_dynamics";
    public const string Event = "event";
    public const string TimelineEpisode = "timeline_episode";
    public const string StoryArc = "story_arc";
}

public static class Stage7DossierTypes
{
    public const string PersonDossier = "person_dossier";
}

public static class Stage7ProfileScopes
{
    public const string Global = "global";
}

public static class Stage7DossierProfileTemporalFactTypes
{
    public const string ProfileStatus = TemporalSingleValuedFactFamilies.ProfileStatus;
    public const string ProfileLocation = TemporalSingleValuedFactFamilies.ProfileLocation;
    public const string RelationshipState = TemporalSingleValuedFactFamilies.RelationshipState;
}

public class Stage7DossierProfileFormationRequest
{
    public Stage6BootstrapGraphResult BootstrapResult { get; set; } = new();
    public string RunKind { get; set; } = "manual";
    public string RequestedModel { get; set; } = "stage7-dossier-profile-deterministic";
    public string TriggerKind { get; set; } = "manual";
    public string? TriggerRef { get; set; }
}

public class Stage7DurableDossier
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid PersonId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string DossierType { get; set; } = Stage7DossierTypes.PersonDossier;
    public string Status { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public string CurrentRevisionHash { get; set; } = string.Empty;
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7DurableProfile
{
    public Guid Id { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid PersonId { get; set; }
    public Guid DurableObjectMetadataId { get; set; }
    public Guid? LastModelPassRunId { get; set; }
    public string ProfileScope { get; set; } = Stage7ProfileScopes.Global;
    public string Status { get; set; } = string.Empty;
    public int CurrentRevisionNumber { get; set; }
    public string CurrentRevisionHash { get; set; } = string.Empty;
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7DurableDossierRevision
{
    public Guid Id { get; set; }
    public Guid DurableDossierId { get; set; }
    public int RevisionNumber { get; set; }
    public string RevisionHash { get; set; } = string.Empty;
    public Guid? ModelPassRunId { get; set; }
    public float Confidence { get; set; }
    public float Coverage { get; set; }
    public float Freshness { get; set; }
    public float Stability { get; set; }
    public string ContradictionMarkersJson { get; set; } = "[]";
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class Stage7DurableProfileRevision
{
    public Guid Id { get; set; }
    public Guid DurableProfileId { get; set; }
    public int RevisionNumber { get; set; }
    public string RevisionHash { get; set; } = string.Empty;
    public Guid? ModelPassRunId { get; set; }
    public float Confidence { get; set; }
    public float Coverage { get; set; }
    public float Freshness { get; set; }
    public float Stability { get; set; }
    public string ContradictionMarkersJson { get; set; } = "[]";
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class Stage7DossierProfileFormationResult
{
    public ModelPassAuditRecord AuditRecord { get; set; } = new();
    public bool Formed { get; set; }
    public Stage6BootstrapPersonRef? TrackedPerson { get; set; }
    public Stage7DurableDossier? Dossier { get; set; }
    public Stage7DurableProfile? Profile { get; set; }
    public Stage7DurableDossierRevision? CurrentDossierRevision { get; set; }
    public Stage7DurableProfileRevision? CurrentProfileRevision { get; set; }
    public List<Guid> EvidenceItemIds { get; set; } = [];
}

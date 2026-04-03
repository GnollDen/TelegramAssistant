namespace TgAssistant.Core.Models;

public static class Stage7DurableObjectFamilies
{
    public const string Dossier = "dossier";
    public const string Profile = "profile";
    public const string PairDynamics = "pair_dynamics";
}

public static class Stage7DossierTypes
{
    public const string PersonDossier = "person_dossier";
}

public static class Stage7ProfileScopes
{
    public const string Global = "global";
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
    public string SummaryJson { get; set; } = "{}";
    public string PayloadJson { get; set; } = "{}";
}

public class Stage7DossierProfileFormationResult
{
    public ModelPassAuditRecord AuditRecord { get; set; } = new();
    public bool Formed { get; set; }
    public Stage6BootstrapPersonRef? TrackedPerson { get; set; }
    public Stage7DurableDossier? Dossier { get; set; }
    public Stage7DurableProfile? Profile { get; set; }
    public List<Guid> EvidenceItemIds { get; set; } = [];
}

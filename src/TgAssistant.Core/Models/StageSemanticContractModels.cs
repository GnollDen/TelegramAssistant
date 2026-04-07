namespace TgAssistant.Core.Models;

public static class StageSemanticStages
{
    public const string Stage6 = "stage6";
    public const string Stage7 = "stage7";
    public const string Stage8 = "stage8";

    public static IReadOnlyList<string> All { get; } =
    [
        Stage6,
        Stage7,
        Stage8
    ];
}

public static class StageSemanticOwnedOutputFamilies
{
    public const string Stage6BootstrapGraph = "stage6_bootstrap_graph";
    public const string Stage6DiscoveryPool = "stage6_discovery_pool";
    public const string Stage7DurableProfile = "stage7_durable_profile";
    public const string Stage7PairDynamics = "stage7_pair_dynamics";
    public const string Stage7DurableTimeline = "stage7_durable_timeline";
    public const string Stage7CasePool = "stage7_case_pool";
    public const string Stage8RecomputeRequest = "stage8_recompute_request";
    public const string Stage8ReintegrationUpdate = "stage8_reintegration_update";

    public static IReadOnlyList<string> All { get; } =
    [
        Stage6BootstrapGraph,
        Stage6DiscoveryPool,
        Stage7DurableProfile,
        Stage7PairDynamics,
        Stage7DurableTimeline,
        Stage7CasePool,
        Stage8RecomputeRequest,
        Stage8ReintegrationUpdate
    ];
}

public static class StageSemanticAcceptedInputFamilies
{
    public const string Stage6SeedScope = "stage6_seed_scope";
    public const string Stage6BootstrapGraph = "stage6_bootstrap_graph";
    public const string Stage6DiscoveryPool = "stage6_discovery_pool";
    public const string Stage7CasePool = "stage7_case_pool";
    public const string Stage7DurableProfile = "stage7_durable_profile";
    public const string Stage7PairDynamics = "stage7_pair_dynamics";
    public const string Stage7DurableTimeline = "stage7_durable_timeline";
    public const string Stage8RecomputeRequest = "stage8_recompute_request";

    public static IReadOnlyList<string> All { get; } =
    [
        Stage6SeedScope,
        Stage6BootstrapGraph,
        Stage6DiscoveryPool,
        Stage7CasePool,
        Stage7DurableProfile,
        Stage7PairDynamics,
        Stage7DurableTimeline,
        Stage8RecomputeRequest
    ];
}

public static class StageSemanticHandoffReasons
{
    public const string BootstrapComplete = "bootstrap_complete";
    public const string DurableReady = "durable_ready";
    public const string NeedsRecompute = "needs_recompute";
    public const string RecomputeApplied = "recompute_applied";
    public const string StageContractViolation = "stage_contract_violation";

    public static IReadOnlyList<string> All { get; } =
    [
        BootstrapComplete,
        DurableReady,
        NeedsRecompute,
        RecomputeApplied,
        StageContractViolation
    ];
}

public sealed record StageSemanticHandoffValidationResult(bool IsValid, string? Reason = null)
{
    public static StageSemanticHandoffValidationResult Valid() => new(true);

    public static StageSemanticHandoffValidationResult Invalid(string reason) => new(false, reason);
}

public static class StageSemanticContract
{
    private static readonly HashSet<string> Stage6OwnedOutputFamilies =
    [
        StageSemanticOwnedOutputFamilies.Stage6BootstrapGraph,
        StageSemanticOwnedOutputFamilies.Stage6DiscoveryPool
    ];

    private static readonly HashSet<string> Stage7OwnedOutputFamilies =
    [
        StageSemanticOwnedOutputFamilies.Stage7DurableProfile,
        StageSemanticOwnedOutputFamilies.Stage7PairDynamics,
        StageSemanticOwnedOutputFamilies.Stage7DurableTimeline,
        StageSemanticOwnedOutputFamilies.Stage7CasePool
    ];

    private static readonly HashSet<string> Stage8OwnedOutputFamilies =
    [
        StageSemanticOwnedOutputFamilies.Stage8RecomputeRequest,
        StageSemanticOwnedOutputFamilies.Stage8ReintegrationUpdate
    ];

    private static readonly HashSet<string> Stage7AcceptedInputFamilies =
    [
        StageSemanticAcceptedInputFamilies.Stage6BootstrapGraph,
        StageSemanticAcceptedInputFamilies.Stage6DiscoveryPool
    ];

    private static readonly HashSet<string> Stage8AcceptedInputFamilies =
    [
        StageSemanticAcceptedInputFamilies.Stage7CasePool,
        StageSemanticAcceptedInputFamilies.Stage7DurableProfile,
        StageSemanticAcceptedInputFamilies.Stage7PairDynamics,
        StageSemanticAcceptedInputFamilies.Stage7DurableTimeline,
        StageSemanticAcceptedInputFamilies.Stage8RecomputeRequest
    ];

    private static readonly HashSet<string> Stage6ToStage7AllowedReasons =
    [
        StageSemanticHandoffReasons.BootstrapComplete,
        StageSemanticHandoffReasons.StageContractViolation
    ];

    private static readonly HashSet<string> Stage7ToStage8AllowedReasons =
    [
        StageSemanticHandoffReasons.DurableReady,
        StageSemanticHandoffReasons.NeedsRecompute,
        StageSemanticHandoffReasons.RecomputeApplied,
        StageSemanticHandoffReasons.StageContractViolation
    ];

    public static bool OwnsOutputFamily(string stage, string outputFamily)
    {
        if (!IsKnownStage(stage) || string.IsNullOrWhiteSpace(outputFamily))
        {
            return false;
        }

        return stage switch
        {
            StageSemanticStages.Stage6 => Stage6OwnedOutputFamilies.Contains(outputFamily),
            StageSemanticStages.Stage7 => Stage7OwnedOutputFamilies.Contains(outputFamily),
            StageSemanticStages.Stage8 => Stage8OwnedOutputFamilies.Contains(outputFamily),
            _ => false
        };
    }

    public static bool AcceptsInputFamily(string stage, string inputFamily)
    {
        if (!IsKnownStage(stage) || string.IsNullOrWhiteSpace(inputFamily))
        {
            return false;
        }

        return stage switch
        {
            StageSemanticStages.Stage6 => string.Equals(inputFamily, StageSemanticAcceptedInputFamilies.Stage6SeedScope, StringComparison.Ordinal),
            StageSemanticStages.Stage7 => Stage7AcceptedInputFamilies.Contains(inputFamily),
            StageSemanticStages.Stage8 => Stage8AcceptedInputFamilies.Contains(inputFamily),
            _ => false
        };
    }

    public static StageSemanticHandoffValidationResult ValidateStage6ToStage7Handoff(
        string stage6OwnedOutputFamily,
        string stage7AcceptedInputFamily,
        string handoffReason)
    {
        if (!OwnsOutputFamily(StageSemanticStages.Stage6, stage6OwnedOutputFamily))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        if (!AcceptsInputFamily(StageSemanticStages.Stage7, stage7AcceptedInputFamily))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        if (!string.Equals(stage6OwnedOutputFamily, stage7AcceptedInputFamily, StringComparison.Ordinal))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        if (!Stage6ToStage7AllowedReasons.Contains(handoffReason))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        return StageSemanticHandoffValidationResult.Valid();
    }

    public static StageSemanticHandoffValidationResult ValidateStage7ToStage8Handoff(
        string stage7OwnedOutputFamily,
        string stage8AcceptedInputFamily,
        string handoffReason)
    {
        if (!OwnsOutputFamily(StageSemanticStages.Stage7, stage7OwnedOutputFamily))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        if (!AcceptsInputFamily(StageSemanticStages.Stage8, stage8AcceptedInputFamily))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        if (!IsValidStage7ToStage8FamilyPair(stage7OwnedOutputFamily, stage8AcceptedInputFamily))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        if (!Stage7ToStage8AllowedReasons.Contains(handoffReason))
        {
            return StageSemanticHandoffValidationResult.Invalid(StageSemanticHandoffReasons.StageContractViolation);
        }

        return StageSemanticHandoffValidationResult.Valid();
    }

    public static IReadOnlyList<string> MapSemanticFamilyToStage7DurableObjectFamilies(string semanticOwnedOutputFamily)
    {
        return semanticOwnedOutputFamily switch
        {
            StageSemanticOwnedOutputFamilies.Stage7DurableProfile =>
            [
                Stage7DurableObjectFamilies.Dossier,
                Stage7DurableObjectFamilies.Profile
            ],
            StageSemanticOwnedOutputFamilies.Stage7PairDynamics =>
            [
                Stage7DurableObjectFamilies.PairDynamics
            ],
            StageSemanticOwnedOutputFamilies.Stage7DurableTimeline =>
            [
                Stage7DurableObjectFamilies.Event,
                Stage7DurableObjectFamilies.TimelineEpisode,
                Stage7DurableObjectFamilies.StoryArc
            ],
            _ => []
        };
    }

    public static bool TryMapSemanticFamilyToStage8RecomputeTargetFamily(string semanticOwnedOutputFamily, out string? targetFamily)
    {
        targetFamily = semanticOwnedOutputFamily switch
        {
            StageSemanticOwnedOutputFamilies.Stage7DurableProfile => Stage8RecomputeTargetFamilies.DossierProfile,
            StageSemanticOwnedOutputFamilies.Stage7PairDynamics => Stage8RecomputeTargetFamilies.PairDynamics,
            StageSemanticOwnedOutputFamilies.Stage7DurableTimeline => Stage8RecomputeTargetFamilies.TimelineObjects,
            _ => null
        };

        return targetFamily is not null;
    }

    private static bool IsKnownStage(string stage)
    {
        return stage == StageSemanticStages.Stage6
            || stage == StageSemanticStages.Stage7
            || stage == StageSemanticStages.Stage8;
    }

    private static bool IsValidStage7ToStage8FamilyPair(string stage7OwnedOutputFamily, string stage8AcceptedInputFamily)
    {
        return (stage7OwnedOutputFamily, stage8AcceptedInputFamily) switch
        {
            (StageSemanticOwnedOutputFamilies.Stage7DurableProfile, StageSemanticAcceptedInputFamilies.Stage7DurableProfile) => true,
            (StageSemanticOwnedOutputFamilies.Stage7PairDynamics, StageSemanticAcceptedInputFamilies.Stage7PairDynamics) => true,
            (StageSemanticOwnedOutputFamilies.Stage7DurableTimeline, StageSemanticAcceptedInputFamilies.Stage7DurableTimeline) => true,
            (StageSemanticOwnedOutputFamilies.Stage7CasePool, StageSemanticAcceptedInputFamilies.Stage7CasePool) => true,
            _ => false
        };
    }
}

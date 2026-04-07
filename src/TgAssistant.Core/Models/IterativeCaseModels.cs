namespace TgAssistant.Core.Models;

public static class IterativeCaseStatuses
{
    public const string Open = "open";
    public const string ResolvingAi = "resolving_ai";
    public const string ResolvedByAi = "resolved_by_ai";
    public const string NeedsMoreContext = "needs_more_context";
    public const string NeedsOperator = "needs_operator";
    public const string DeferredToNextPass = "deferred_to_next_pass";
    public const string Superseded = "superseded";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Open,
        ResolvingAi,
        ResolvedByAi,
        NeedsMoreContext,
        NeedsOperator,
        DeferredToNextPass,
        Superseded
    };

    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions = new(StringComparer.Ordinal)
    {
        [Open] = new HashSet<string>(StringComparer.Ordinal)
        {
            ResolvingAi,
            NeedsMoreContext,
            NeedsOperator,
            DeferredToNextPass,
            Superseded
        },
        [ResolvingAi] = new HashSet<string>(StringComparer.Ordinal)
        {
            ResolvedByAi,
            NeedsMoreContext,
            NeedsOperator,
            DeferredToNextPass,
            Superseded
        },
        [NeedsMoreContext] = new HashSet<string>(StringComparer.Ordinal)
        {
            ResolvingAi,
            DeferredToNextPass,
            Superseded
        },
        [NeedsOperator] = new HashSet<string>(StringComparer.Ordinal)
        {
            ResolvingAi,
            DeferredToNextPass,
            Superseded
        },
        [DeferredToNextPass] = new HashSet<string>(StringComparer.Ordinal)
        {
            ResolvingAi,
            Superseded
        },
        [ResolvedByAi] = new HashSet<string>(StringComparer.Ordinal)
        {
            Superseded
        },
        [Superseded] = new HashSet<string>(StringComparer.Ordinal)
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    public static bool IsSupported(string? value)
        => Supported.Contains(Normalize(value));

    public static bool CanTransition(string? previousStatus, string? nextStatus)
    {
        var previous = Normalize(previousStatus);
        var next = Normalize(nextStatus);
        return AllowedTransitions.TryGetValue(previous, out var allowed) && allowed.Contains(next);
    }

    public static IReadOnlyCollection<string> GetAllowedNextStatuses(string? previousStatus)
    {
        var normalized = Normalize(previousStatus);
        return AllowedTransitions.TryGetValue(normalized, out var allowed)
            ? allowed
            : [];
    }
}

public static class ReintegrationOriginSourceKinds
{
    public const string Stage7DurableProfile = "stage7_durable_profile";
    public const string Stage7PairDynamics = "stage7_pair_dynamics";
    public const string Stage7DurableTimeline = "stage7_durable_timeline";
    public const string Stage8RecomputeRequest = "stage8_recompute_request";
    public const string ResolutionAction = "resolution_action";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Stage7DurableProfile,
        Stage7PairDynamics,
        Stage7DurableTimeline,
        Stage8RecomputeRequest,
        ResolutionAction
    };

    public static IReadOnlyCollection<string> All => Supported;

    public static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();

    public static bool IsSupported(string? value)
        => Supported.Contains(Normalize(value));
}

public static class IterativeCaseIdentityContract
{
    public static bool IsCarryForwardCaseIdDistinct(
        Guid carryForwardCaseId,
        string? scopeItemKey,
        Guid? conflictSessionId)
    {
        if (carryForwardCaseId == Guid.Empty)
        {
            return false;
        }

        if (conflictSessionId.HasValue && conflictSessionId.Value == carryForwardCaseId)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(scopeItemKey))
        {
            return true;
        }

        var normalizedScopeItemKey = scopeItemKey.Trim();
        return !string.Equals(normalizedScopeItemKey, carryForwardCaseId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && !string.Equals(normalizedScopeItemKey, carryForwardCaseId.ToString("N"), StringComparison.OrdinalIgnoreCase);
    }
}

public class IterativeCarryForwardCaseRecord
{
    public Guid CarryForwardCaseId { get; set; }
    public Guid? ReintegrationEntryId { get; set; }
    public string OriginSourceKind { get; set; } = ReintegrationOriginSourceKinds.Stage7DurableProfile;
    public string Status { get; set; } = IterativeCaseStatuses.Open;
    public Guid? PredecessorCarryForwardCaseId { get; set; }
    public Guid? SuccessorCarryForwardCaseId { get; set; }
    public Guid? ConflictSessionId { get; set; }
    public Guid? ResolutionActionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

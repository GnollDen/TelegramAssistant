namespace TgAssistant.Core.Models;

public static class ModelPassEnvelopeSchema
{
    public const int CurrentVersion = 1;
}

public static class ModelPassResultStatuses
{
    public const string ResultReady = "result_ready";
    public const string NeedMoreData = "need_more_data";
    public const string NeedOperatorClarification = "need_operator_clarification";
    public const string BlockedInvalidInput = "blocked_invalid_input";

    public static IReadOnlyList<string> All { get; } =
    [
        ResultReady,
        NeedMoreData,
        NeedOperatorClarification,
        BlockedInvalidInput
    ];
}

public class ModelPassEnvelope
{
    public Guid RunId { get; set; } = Guid.NewGuid();
    public int SchemaVersion { get; set; } = ModelPassEnvelopeSchema.CurrentVersion;
    public string Stage { get; set; } = string.Empty;
    public string PassFamily { get; set; } = string.Empty;
    public string RunKind { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public ModelPassScope Scope { get; set; } = new();
    public ModelPassTarget Target { get; set; } = new();
    public Guid? PersonId { get; set; }
    public Guid? SourceObjectId { get; set; }
    public Guid? EvidenceItemId { get; set; }
    public string? RequestedModel { get; set; }
    public string? TriggerKind { get; set; }
    public string? TriggerRef { get; set; }
    public List<ModelPassSourceRef> SourceRefs { get; set; } = [];
    public ModelPassTruthSummary TruthSummary { get; set; } = new();
    public List<ModelPassConflict> Conflicts { get; set; } = [];
    public List<ModelPassUnknown> Unknowns { get; set; } = [];
    public ModelPassBudgetEnvelope Budget { get; set; } = new();
    public string ResultStatus { get; set; } = ModelPassResultStatuses.BlockedInvalidInput;
    public ModelPassOutputSummary OutputSummary { get; set; } = new();
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ModelPassScope
{
    public string ScopeType { get; set; } = string.Empty;
    public string ScopeRef { get; set; } = string.Empty;
    public List<string> AdditionalRefs { get; set; } = [];
}

public class ModelPassTarget
{
    public string TargetType { get; set; } = string.Empty;
    public string TargetRef { get; set; } = string.Empty;
}

public class ModelPassSourceRef
{
    public string SourceType { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public Guid? SourceObjectId { get; set; }
    public Guid? EvidenceItemId { get; set; }
}

public class ModelPassTruthSummary
{
    public string TruthLayer { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public List<string> CanonicalRefs { get; set; } = [];
}

public class ModelPassConflict
{
    public string ConflictType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RelatedObjectRef { get; set; }
}

public class ModelPassUnknown
{
    public string UnknownType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RequiredAction { get; set; }
}

public class ModelPassBudgetEnvelope
{
    public string BudgetProfileKey { get; set; } = string.Empty;
    public int MaxIterations { get; set; }
    public int IterationsConsumed { get; set; }
    public int MaxInputTokens { get; set; }
    public int InputTokensConsumed { get; set; }
    public int MaxOutputTokens { get; set; }
    public int OutputTokensConsumed { get; set; }
    public int MaxTotalTokens { get; set; }
    public int TotalTokensConsumed { get; set; }
    public decimal MaxCostUsd { get; set; }
    public decimal CostUsdConsumed { get; set; }
}

public static class ModelPassBudgetCatalog
{
    public static ModelPassBudgetEnvelope Create(string stage, string passFamily)
    {
        var normalizedStage = string.IsNullOrWhiteSpace(stage) ? "unknown_stage" : stage.Trim();
        var normalizedPassFamily = string.IsNullOrWhiteSpace(passFamily) ? "unknown_pass_family" : passFamily.Trim();

        return (normalizedStage, normalizedPassFamily) switch
        {
            ("stage6_bootstrap", "graph_init") => Build("stage6_bootstrap.graph_init.v1", 3, 6000, 1200, 7200, 0.30m),
            ("stage7_durable_formation", "dossier_profile") => Build("stage7_durable_formation.dossier_profile.v1", 3, 9000, 1800, 10800, 0.45m),
            ("stage7_durable_formation", "pair_dynamics") => Build("stage7_durable_formation.pair_dynamics.v1", 3, 8500, 1700, 10200, 0.42m),
            ("stage7_durable_formation", "timeline_objects") => Build("stage7_durable_formation.timeline_objects.v1", 4, 10000, 2200, 12200, 0.55m),
            ("stage8_recompute", "stage6_bootstrap") => Build("stage8_recompute.stage6_bootstrap.v1", 3, 6000, 1200, 7200, 0.30m),
            ("stage8_recompute", "dossier_profile") => Build("stage8_recompute.dossier_profile.v1", 3, 9000, 1800, 10800, 0.45m),
            ("stage8_recompute", "pair_dynamics") => Build("stage8_recompute.pair_dynamics.v1", 3, 8500, 1700, 10200, 0.42m),
            ("stage8_recompute", "timeline_objects") => Build("stage8_recompute.timeline_objects.v1", 4, 10000, 2200, 12200, 0.55m),
            _ => Build($"{normalizedStage}.{normalizedPassFamily}.v1", 2, 4000, 1000, 5000, 0.25m)
        };
    }

    public static ModelPassBudgetEnvelope ConsumeOneIteration(ModelPassBudgetEnvelope budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        return new ModelPassBudgetEnvelope
        {
            BudgetProfileKey = budget.BudgetProfileKey,
            MaxIterations = budget.MaxIterations,
            IterationsConsumed = Math.Min(1, budget.MaxIterations),
            MaxInputTokens = budget.MaxInputTokens,
            InputTokensConsumed = budget.InputTokensConsumed,
            MaxOutputTokens = budget.MaxOutputTokens,
            OutputTokensConsumed = budget.OutputTokensConsumed,
            MaxTotalTokens = budget.MaxTotalTokens,
            TotalTokensConsumed = budget.TotalTokensConsumed,
            MaxCostUsd = budget.MaxCostUsd,
            CostUsdConsumed = budget.CostUsdConsumed
        };
    }

    private static ModelPassBudgetEnvelope Build(
        string profileKey,
        int maxIterations,
        int maxInputTokens,
        int maxOutputTokens,
        int maxTotalTokens,
        decimal maxCostUsd)
    {
        return new ModelPassBudgetEnvelope
        {
            BudgetProfileKey = profileKey,
            MaxIterations = maxIterations,
            IterationsConsumed = 0,
            MaxInputTokens = maxInputTokens,
            InputTokensConsumed = 0,
            MaxOutputTokens = maxOutputTokens,
            OutputTokensConsumed = 0,
            MaxTotalTokens = maxTotalTokens,
            TotalTokensConsumed = 0,
            MaxCostUsd = maxCostUsd,
            CostUsdConsumed = 0
        };
    }
}

public class ModelPassOutputSummary
{
    public string Summary { get; set; } = string.Empty;
    public string? BlockedReason { get; set; }
}

namespace TgAssistant.Core.Models;

public static class BudgetModalities
{
    public const string TextAnalysis = "text_analysis";
    public const string Embeddings = "embeddings";
    public const string Vision = "vision";
    public const string Audio = "audio";
}

public static class BudgetPathStates
{
    public const string Active = "active";
    public const string SoftLimited = "soft_limited";
    public const string HardPaused = "hard_paused";
    public const string QuotaBlocked = "quota_blocked";
}

public class BudgetPathCheckRequest
{
    public string PathKey { get; set; } = string.Empty;
    public string Modality { get; set; } = BudgetModalities.TextAnalysis;
    public bool IsImportScope { get; set; }
    public bool IsOptionalPath { get; set; } = true;
}

public class BudgetPathDecision
{
    public string PathKey { get; set; } = string.Empty;
    public string Modality { get; set; } = BudgetModalities.TextAnalysis;
    public string State { get; set; } = BudgetPathStates.Active;
    public string Reason { get; set; } = "budget_ok";
    public bool ShouldPausePath { get; set; }
    public bool ShouldDegradeOptionalPath { get; set; }
    public decimal DailySpentUsd { get; set; }
    public decimal DailyBudgetUsd { get; set; }
    public decimal StageSpentUsd { get; set; }
    public decimal StageBudgetUsd { get; set; }
    public decimal ImportSpentUsd { get; set; }
    public decimal ImportBudgetUsd { get; set; }
    public DateTime EvaluatedAt { get; set; } = DateTime.UtcNow;
}

public class BudgetOperationalState
{
    public string PathKey { get; set; } = string.Empty;
    public string Modality { get; set; } = BudgetModalities.TextAnalysis;
    public string State { get; set; } = BudgetPathStates.Active;
    public string Reason { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}

public class EvalRunRequest
{
    public string RunName { get; set; } = string.Empty;
    public List<string> Scenarios { get; set; } = new();
    public string Actor { get; set; } = "eval_harness";
}

public class EvalRunResult
{
    public Guid RunId { get; set; }
    public string RunName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = "{}";
    public List<EvalScenarioResult> Scenarios { get; set; } = new();
}

public class EvalScenarioResult
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string ScenarioName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string MetricsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

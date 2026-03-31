using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Control;

public class BudgetVerificationService
{
    private readonly BudgetGuardrailSettings _settings;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<BudgetVerificationService> _logger;

    public BudgetVerificationService(
        IOptions<BudgetGuardrailSettings> settings,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<BudgetVerificationService> logger)
    {
        _settings = settings.Value;
        _usageRepository = usageRepository;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Budget smoke failed: budget guardrails are disabled.");
        }

        var costs = await _usageRepository.GetCostUsdByPhaseSinceAsync(DateTime.UtcNow.AddDays(-1), ct);
        var dailySpent = costs.Values.Sum();
        var dailyBudget = Math.Max(0m, _settings.DailyBudgetUsd);
        var softRatio = ClampRatio(_settings.SoftLimitRatio, 0.50m, 0.99m);
        var hardRatio = Math.Max(softRatio, ClampRatio(_settings.HardLimitRatio, 0.80m, 1.20m));

        var embeddingBudget = Math.Max(0.02m, _settings.StageEmbeddingsBudgetUsd);
        var currentEmbeddingSpent = costs.TryGetValue("embedding", out var embeddingSpent) ? embeddingSpent : 0m;
        var softTarget = embeddingBudget * 0.90m;
        if (currentEmbeddingSpent < softTarget)
        {
            await _usageRepository.LogAsync(new AnalysisUsageEvent
            {
                Phase = "embedding",
                Model = "budget_smoke",
                PromptTokens = 1,
                CompletionTokens = 1,
                TotalTokens = 2,
                CostUsd = Math.Max(0.01m, softTarget - currentEmbeddingSpent),
                CreatedAt = DateTime.UtcNow
            }, ct);

            dailySpent += Math.Max(0.01m, softTarget - currentEmbeddingSpent);
            currentEmbeddingSpent += Math.Max(0.01m, softTarget - currentEmbeddingSpent);
        }

        var softPathKey = $"smoke_soft_optional_embeddings_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var soft = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = softPathKey,
            Modality = BudgetModalities.Embeddings,
            IsOptionalPath = true,
            IsImportScope = false
        }, ct);

        var hardSaturated = (dailyBudget > 0 && dailySpent >= dailyBudget * hardRatio)
                            || currentEmbeddingSpent >= embeddingBudget * hardRatio;
        if (!soft.ShouldDegradeOptionalPath && !soft.ShouldPausePath)
        {
            throw new InvalidOperationException("Budget smoke failed: optional embedding path was neither degraded nor paused.");
        }

        if (hardSaturated && !string.Equals(soft.State, BudgetPathStates.HardPaused, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Budget smoke failed: expected hard pause for saturated optional embedding path.");
        }

        if (!hardSaturated && !string.Equals(soft.State, BudgetPathStates.SoftLimited, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Budget smoke failed: soft-limit behavior did not degrade optional path.");
        }

        var hardPathKey = $"smoke_hard_text_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var hard = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = hardPathKey,
            Modality = BudgetModalities.TextAnalysis,
            IsOptionalPath = false,
            IsImportScope = false
        }, ct);

        if (hard.ShouldPausePath && !string.Equals(hard.State, BudgetPathStates.HardPaused, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Budget smoke failed: non-optional text path was paused without hard-paused state.");
        }

        var quotaPathKey = $"smoke_quota_path_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var quota = await _budgetGuardrailService.RegisterQuotaBlockedAsync(
            pathKey: quotaPathKey,
            modality: BudgetModalities.TextAnalysis,
            reason: "quota_like_provider_failure",
            isImportScope: false,
            isOptionalPath: true,
            ct: ct);

        if (!quota.ShouldPausePath || !string.Equals(quota.State, BudgetPathStates.QuotaBlocked, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Budget smoke failed: quota-like failure did not produce explicit quota-blocked state.");
        }

        var quotaLike = BudgetErrorClassifier.IsQuotaLike("insufficient credits / billing required");
        if (!quotaLike)
        {
            throw new InvalidOperationException("Budget smoke failed: quota-like classifier does not recognize billing failures.");
        }

        var states = await _budgetGuardrailService.GetOperationalStatesAsync(ct);
        if (!states.Any(x => x.PathKey == quotaPathKey && x.State == BudgetPathStates.QuotaBlocked)
            || !states.Any(x => x.PathKey == hardPathKey)
            || !states.Any(x => x.PathKey == softPathKey))
        {
            throw new InvalidOperationException("Budget smoke failed: budget-limited states are not visible operationally.");
        }

        _logger.LogInformation(
            "Budget smoke passed. soft_state={SoftState}, hard_state={HardState}, quota_state={QuotaState}, visible_states={VisibleCount}",
            soft.State,
            hard.State,
            quota.State,
            states.Count);
    }

    private static decimal ClampRatio(decimal value, decimal fallbackMin, decimal fallbackMax)
    {
        if (value <= 0m)
        {
            return fallbackMin;
        }

        if (value > fallbackMax)
        {
            return fallbackMax;
        }

        return value;
    }
}

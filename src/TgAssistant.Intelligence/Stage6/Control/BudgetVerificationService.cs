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
        }

        var soft = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = "smoke_soft_optional_embeddings",
            Modality = BudgetModalities.Embeddings,
            IsOptionalPath = true,
            IsImportScope = false
        }, ct);

        if (!soft.ShouldDegradeOptionalPath)
        {
            throw new InvalidOperationException("Budget smoke failed: soft-limit behavior did not degrade optional path.");
        }

        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = "cheap",
            Model = "budget_smoke",
            PromptTokens = 1,
            CompletionTokens = 1,
            TotalTokens = 2,
            CostUsd = Math.Max(0.01m, _settings.StageTextAnalysisBudgetUsd * 0.50m),
            CreatedAt = DateTime.UtcNow
        }, ct);

        var hard = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = "smoke_hard_text",
            Modality = BudgetModalities.TextAnalysis,
            IsOptionalPath = false,
            IsImportScope = false
        }, ct);

        if (!hard.ShouldPausePath || !string.Equals(hard.State, BudgetPathStates.HardPaused, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Budget smoke failed: hard-limit behavior did not pause the path.");
        }

        var quota = await _budgetGuardrailService.RegisterQuotaBlockedAsync(
            pathKey: "smoke_quota_path",
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
        if (!states.Any(x => x.PathKey == "smoke_quota_path" && x.State == BudgetPathStates.QuotaBlocked)
            || !states.Any(x => x.PathKey == "smoke_hard_text" && x.State == BudgetPathStates.HardPaused))
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
}

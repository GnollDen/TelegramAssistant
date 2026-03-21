using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Intelligence.Stage5;

public class Stage5VerificationService
{
    private readonly AnalysisSettings _analysisSettings;
    private readonly EmbeddingSettings _embeddingSettings;
    private readonly SummaryHistoricalRetrievalService _summaryHistoricalRetrievalService;
    private readonly ILogger<Stage5VerificationService> _logger;

    public Stage5VerificationService(
        IOptions<AnalysisSettings> analysisSettings,
        IOptions<EmbeddingSettings> embeddingSettings,
        SummaryHistoricalRetrievalService summaryHistoricalRetrievalService,
        ILogger<Stage5VerificationService> logger)
    {
        _analysisSettings = analysisSettings.Value;
        _embeddingSettings = embeddingSettings.Value;
        _summaryHistoricalRetrievalService = summaryHistoricalRetrievalService;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken ct = default)
    {
        var expensiveEnabled = _analysisSettings.ExpensivePassEnabled;
        var expensiveBatch = Math.Max(0, _analysisSettings.MaxExpensivePerBatch);
        if (expensiveEnabled && expensiveBatch == 0)
        {
            throw new InvalidOperationException("Stage5 smoke failed: expensive pass is enabled but MaxExpensivePerBatch is 0.");
        }

        var summaryRouting = _summaryHistoricalRetrievalService.GetOperationalState();
        var embeddingModel = _embeddingSettings.Model?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(summaryRouting.EmbeddingModel))
        {
            throw new InvalidOperationException("Stage5 smoke failed: summary historical embedding model resolved to empty value.");
        }

        if (!string.IsNullOrWhiteSpace(embeddingModel)
            && !string.Equals(summaryRouting.EmbeddingModel, embeddingModel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Stage5 smoke failed: summary historical embedding model '{summaryRouting.EmbeddingModel}' is not aligned with Embedding:Model '{embeddingModel}'.");
        }

        if (_analysisSettings.SummaryWorkerEnabled && !_analysisSettings.SummaryEnabled)
        {
            throw new InvalidOperationException("Stage5 smoke failed: SummaryWorkerEnabled=true while SummaryEnabled=false.");
        }

        if (summaryRouting.HistoricalHintsEnabled && !_analysisSettings.SummaryEnabled)
        {
            throw new InvalidOperationException("Stage5 smoke failed: SummaryHistoricalHintsEnabled=true while SummaryEnabled=false.");
        }

        var summaryInlinePath = _analysisSettings.Enabled
            ? (_analysisSettings.SummaryEnabled ? "inline_llm_plus_fallback" : "inline_fallback_only")
            : "disabled_with_stage5";
        var summaryWorkerPath = _analysisSettings.Enabled && _analysisSettings.SummaryEnabled && _analysisSettings.SummaryWorkerEnabled
            ? "enabled"
            : "disabled";

        _logger.LogInformation(
            "Stage5 smoke passed. cheap_model={CheapModel}, expensive_pass_enabled={ExpensiveEnabled}, expensive_batch_limit={ExpensiveBatchLimit}, expensive_daily_budget_usd={ExpensiveDailyBudget:0.000000}, edit_diff_enabled={EditDiffEnabled}, summary_inline_path={SummaryInlinePath}, summary_worker_path={SummaryWorkerPath}, summary_model={SummaryModel}, historical_hints_enabled={HistoricalHintsEnabled}, summary_embedding_model={SummaryEmbeddingModel}, summary_embedding_from_config={SummaryEmbeddingFromConfig}",
            _analysisSettings.CheapModel,
            expensiveEnabled,
            expensiveBatch,
            _analysisSettings.ExpensiveDailyBudgetUsd,
            _analysisSettings.EditDiffEnabled,
            summaryInlinePath,
            summaryWorkerPath,
            string.IsNullOrWhiteSpace(_analysisSettings.SummaryModel) ? _analysisSettings.ExpensiveModel : _analysisSettings.SummaryModel,
            summaryRouting.HistoricalHintsEnabled,
            summaryRouting.EmbeddingModel,
            summaryRouting.EmbeddingModelFromConfig);

        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

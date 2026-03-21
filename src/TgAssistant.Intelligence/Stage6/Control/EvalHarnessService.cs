using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Intelligence.Stage6.Control;

public class EvalHarnessService : IEvalHarnessService
{
    private readonly EvalHarnessSettings _settings;
    private readonly IEvalRepository _evalRepository;
    private readonly Stage5VerificationService _stage5VerificationService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<EvalHarnessService> _logger;

    public EvalHarnessService(
        IOptions<EvalHarnessSettings> settings,
        IEvalRepository evalRepository,
        Stage5VerificationService stage5VerificationService,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<EvalHarnessService> logger)
    {
        _settings = settings.Value;
        _evalRepository = evalRepository;
        _stage5VerificationService = stage5VerificationService;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
    }

    public async Task<EvalRunResult> RunAsync(EvalRunRequest request, CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Eval harness is disabled by configuration.");
        }

        var runName = string.IsNullOrWhiteSpace(request.RunName)
            ? _settings.DefaultRunName
            : request.RunName.Trim();
        var scenarioNames = request.Scenarios.Count > 0
            ? request.Scenarios
            : ["budget_visibility", "stage5_config"];

        var previousRun = await _evalRepository.GetLatestRunByNameAsync(runName, ct);
        var startedAt = DateTime.UtcNow;
        var run = new EvalRunResult
        {
            RunId = Guid.NewGuid(),
            RunName = runName,
            Passed = false,
            StartedAt = startedAt,
            FinishedAt = startedAt,
            Summary = "running",
            MetricsJson = "{}"
        };

        await _evalRepository.CreateRunAsync(run, ct);

        var scenarioResults = new List<EvalScenarioResult>();
        foreach (var scenarioName in scenarioNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var result = await RunScenarioAsync(run.RunId, scenarioName, ct);
            await _evalRepository.AddScenarioResultAsync(result, ct);
            scenarioResults.Add(result);
        }

        var passedCount = scenarioResults.Count(x => x.Passed);
        var failedCount = scenarioResults.Count - passedCount;
        var passed = failedCount == 0;
        var finishedAt = DateTime.UtcNow;
        var metricsJson = JsonSerializer.Serialize(new
        {
            scenario_count = scenarioResults.Count,
            passed_count = passedCount,
            failed_count = failedCount,
            previous_run_id = previousRun?.RunId,
            previous_passed = previousRun?.Passed,
            pass_delta = previousRun == null ? "n/a" : previousRun.Passed == passed ? "stable" : "changed"
        });

        var summary = previousRun == null
            ? $"eval run completed: pass={passed}, scenarios={scenarioResults.Count}, baseline=none"
            : $"eval run completed: pass={passed}, scenarios={scenarioResults.Count}, previous_pass={previousRun.Passed}";

        await _evalRepository.CompleteRunAsync(run.RunId, passed, summary, metricsJson, finishedAt, ct);
        run.Passed = passed;
        run.Summary = summary;
        run.FinishedAt = finishedAt;
        run.MetricsJson = metricsJson;
        run.Scenarios = scenarioResults;

        _logger.LogInformation(
            "Eval harness run completed. run_id={RunId}, run_name={RunName}, passed={Passed}, scenarios={ScenarioCount}, previous_run_id={PreviousRunId}",
            run.RunId,
            run.RunName,
            run.Passed,
            run.Scenarios.Count,
            previousRun?.RunId);

        return run;
    }

    private async Task<EvalScenarioResult> RunScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var normalized = scenarioName.Trim().ToLowerInvariant();
        try
        {
            return normalized switch
            {
                "budget_visibility" => await RunBudgetVisibilityScenarioAsync(runId, scenarioName, ct),
                "stage5_config" => await RunStage5ConfigScenarioAsync(runId, scenarioName, ct),
                _ => new EvalScenarioResult
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ScenarioName = scenarioName,
                    Passed = false,
                    Summary = "unknown scenario",
                    MetricsJson = JsonSerializer.Serialize(new { error = "unknown_scenario" }),
                    CreatedAt = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            return new EvalScenarioResult
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ScenarioName = scenarioName,
                Passed = false,
                Summary = ex.Message,
                MetricsJson = JsonSerializer.Serialize(new { exception = ex.GetType().Name }),
                CreatedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<EvalScenarioResult> RunBudgetVisibilityScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var decision = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = "eval_harness_text_path",
            Modality = BudgetModalities.TextAnalysis,
            IsImportScope = false,
            IsOptionalPath = true
        }, ct);

        var states = await _budgetGuardrailService.GetOperationalStatesAsync(ct);
        var hasState = states.Any(x => string.Equals(x.PathKey, "eval_harness_text_path", StringComparison.Ordinal));

        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioName = scenarioName,
            Passed = hasState,
            Summary = hasState ? "budget visibility works" : "budget state not persisted",
            MetricsJson = JsonSerializer.Serialize(new
            {
                decision_state = decision.State,
                decision_reason = decision.Reason,
                states_count = states.Count
            }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<EvalScenarioResult> RunStage5ConfigScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        await _stage5VerificationService.RunAsync(ct);
        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioName = scenarioName,
            Passed = true,
            Summary = "stage5 config smoke passed",
            MetricsJson = JsonSerializer.Serialize(new { check = "stage5_smoke" }),
            CreatedAt = DateTime.UtcNow
        };
    }
}

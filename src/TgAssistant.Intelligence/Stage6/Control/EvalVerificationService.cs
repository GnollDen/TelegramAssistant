// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Control;

public class EvalVerificationService
{
    private readonly IEvalHarnessService _evalHarnessService;
    private readonly IEvalRepository _evalRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly ILogger<EvalVerificationService> _logger;

    public EvalVerificationService(
        IEvalHarnessService evalHarnessService,
        IEvalRepository evalRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        ILogger<EvalVerificationService> logger)
    {
        _evalHarnessService = evalHarnessService;
        _evalRepository = evalRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var runName = "eval_smoke";
        var run = await _evalHarnessService.RunAsync(new EvalRunRequest
        {
            RunName = runName,
            Scenarios = ["budget_visibility", "stage5_config"],
            Actor = "eval_smoke"
        }, ct);

        if (run.RunId == Guid.Empty)
        {
            throw new InvalidOperationException("Eval smoke failed: run id was not generated.");
        }

        if (run.Scenarios.Count < 2)
        {
            throw new InvalidOperationException("Eval smoke failed: expected at least two recorded scenarios.");
        }

        var persistedRun = await _evalRepository.GetRunByIdAsync(run.RunId, ct)
            ?? throw new InvalidOperationException("Eval smoke failed: persisted run not found.");
        var persistedScenarios = await _evalRepository.GetScenarioResultsAsync(run.RunId, ct);
        if (persistedScenarios.Count != run.Scenarios.Count)
        {
            throw new InvalidOperationException("Eval smoke failed: scenario result persistence mismatch.");
        }

        if (persistedScenarios.Any(x => x.LatencyMs < 0))
        {
            throw new InvalidOperationException("Eval smoke failed: scenario latency must be non-negative.");
        }

        if (persistedScenarios.Any(x => string.IsNullOrWhiteSpace(x.ModelSummaryJson)))
        {
            throw new InvalidOperationException("Eval smoke failed: scenario model summary is empty.");
        }

        if (string.IsNullOrWhiteSpace(persistedRun.MetricsJson))
        {
            throw new InvalidOperationException("Eval smoke failed: run metrics are empty.");
        }

        using var metricsDoc = JsonDocument.Parse(persistedRun.MetricsJson);
        if (!metricsDoc.RootElement.TryGetProperty("scenario_count", out _))
        {
            throw new InvalidOperationException("Eval smoke failed: run metrics do not contain scenario_count.");
        }

        _logger.LogInformation(
            "Eval smoke passed. run_id={RunId}, run_name={RunName}, passed={Passed}, scenarios={ScenarioCount}",
            run.RunId,
            run.RunName,
            run.Passed,
            run.Scenarios.Count);

        await RunExperimentSmokeAsync(ct);
        await RunStage6PackSmokeAsync(ct);
    }

    private async Task RunExperimentSmokeAsync(CancellationToken ct)
    {
        var experimentDefinition = new EvalExperimentDefinition
        {
            ExperimentKey = "ab_layer_smoke",
            DisplayName = "A/B Layer Smoke",
            Description = "Smoke definition for practical experiment layer validation.",
            DefaultScenarioPackKey = "smoke_core",
            Variants =
            [
                new EvalExperimentVariantDefinition
                {
                    VariantKey = "baseline",
                    DisplayName = "Smoke Baseline",
                    IsBaseline = true,
                    RunNamePrefix = "exp:ab_layer_smoke:baseline"
                },
                new EvalExperimentVariantDefinition
                {
                    VariantKey = "candidate",
                    DisplayName = "Smoke Candidate",
                    IsBaseline = false,
                    RunNamePrefix = "exp:ab_layer_smoke:candidate"
                }
            ],
            ScenarioPacks =
            [
                new EvalScenarioPackDefinition
                {
                    PackKey = "smoke_core",
                    Description = "Core smoke scenarios",
                    Scenarios =
                    [
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "budget_visibility",
                            Required = true,
                            Description = "Budget visibility path"
                        },
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage5_config",
                            Required = true,
                            Description = "Stage5 verification path"
                        }
                    ]
                }
            ]
        };

        await _evalHarnessService.RegisterExperimentDefinitionAsync(experimentDefinition, ct);
        var comparison = await _evalHarnessService.RunExperimentComparisonAsync(new EvalExperimentRunComparisonRequest
        {
            ExperimentKey = experimentDefinition.ExperimentKey,
            ScenarioPackKey = "smoke_core",
            Actor = "eval_experiment_smoke",
            PersistComparisonRun = true,
            RecordReviewEvent = true
        }, ct);

        if (comparison.ExperimentRunId == Guid.Empty)
        {
            throw new InvalidOperationException("Eval experiment smoke failed: experiment run id was not generated.");
        }

        if (comparison.Variants.Count < 2)
        {
            throw new InvalidOperationException("Eval experiment smoke failed: expected at least two variant results.");
        }

        if (!comparison.ComparisonEvalRunId.HasValue || comparison.ComparisonEvalRunId.Value == Guid.Empty)
        {
            throw new InvalidOperationException("Eval experiment smoke failed: comparison eval run id was not generated.");
        }

        var persistedComparisonRun = await _evalRepository.GetRunByIdAsync(comparison.ComparisonEvalRunId.Value, ct)
            ?? throw new InvalidOperationException("Eval experiment smoke failed: comparison run was not persisted.");
        var persistedVariantScenarios = await _evalRepository.GetScenarioResultsAsync(comparison.ComparisonEvalRunId.Value, ct);
        if (persistedVariantScenarios.Count != comparison.Variants.Count)
        {
            throw new InvalidOperationException("Eval experiment smoke failed: persisted variant scenario count mismatch.");
        }

        var reviewEvents = await _domainReviewEventRepository.GetByObjectAsync(
            "eval_experiment_run",
            comparison.ExperimentRunId.ToString("D"),
            limit: 5,
            ct);
        if (!reviewEvents.Any(x => string.Equals(x.Action, "comparison_completed", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Eval experiment smoke failed: experiment review event was not recorded.");
        }

        if (string.IsNullOrWhiteSpace(persistedComparisonRun.MetricsJson))
        {
            throw new InvalidOperationException("Eval experiment smoke failed: comparison run metrics are empty.");
        }

        using var metricsDoc = JsonDocument.Parse(comparison.MetricsJson);
        if (!metricsDoc.RootElement.TryGetProperty("variant_count", out _))
        {
            throw new InvalidOperationException("Eval experiment smoke failed: comparison metrics do not contain variant_count.");
        }

        _logger.LogInformation(
            "Eval experiment smoke passed. experiment_run_id={ExperimentRunId}, comparison_run_id={ComparisonRunId}, variants={VariantCount}, passed={Passed}",
            comparison.ExperimentRunId,
            comparison.ComparisonEvalRunId,
            comparison.Variants.Count,
            comparison.Passed);
    }

    private async Task RunStage6PackSmokeAsync(CancellationToken ct)
    {
        var comparison = await _evalHarnessService.RunExperimentComparisonAsync(new EvalExperimentRunComparisonRequest
        {
            ExperimentKey = "stage6_guarded_default",
            ScenarioPackKey = "stage6_quality",
            Actor = "eval_stage6_pack_smoke",
            PersistComparisonRun = true,
            RecordReviewEvent = true
        }, ct);

        if (comparison.Variants.Count < 2)
        {
            throw new InvalidOperationException("Eval Stage6 pack smoke failed: expected two variants.");
        }

        var variantRuns = new List<EvalRunResult>();
        foreach (var variant in comparison.Variants)
        {
            var run = await _evalRepository.GetRunByIdAsync(variant.EvalRunId, ct)
                ?? throw new InvalidOperationException($"Eval Stage6 pack smoke failed: run not found {variant.EvalRunId}.");
            run.Scenarios = await _evalRepository.GetScenarioResultsAsync(run.RunId, ct);
            variantRuns.Add(run);
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "stage6_dossier_current_state_quality",
            "stage6_draft_review_quality",
            "stage6_clarification_usefulness",
            "stage6_case_usefulness_noise",
            "stage6_behavioral_usefulness"
        };
        var observed = variantRuns
            .SelectMany(x => x.Scenarios)
            .Select(x => x.ScenarioName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!required.All(observed.Contains))
        {
            throw new InvalidOperationException("Eval Stage6 pack smoke failed: required stage6 quality scenarios are missing.");
        }

        if (variantRuns.SelectMany(x => x.Scenarios).All(x => x.CostUsd == 0m))
        {
            _logger.LogWarning("Eval Stage6 pack smoke: scenario costs are zero in this environment; visibility path still verified.");
        }

        _logger.LogInformation(
            "Eval Stage6 pack smoke passed. experiment_run_id={ExperimentRunId}, variants={Variants}, scenarios_checked={ScenarioCount}",
            comparison.ExperimentRunId,
            comparison.Variants.Count,
            required.Count);
    }
}

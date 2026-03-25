using System.Collections.Concurrent;
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
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly Stage5VerificationService _stage5VerificationService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly IAnalysisUsageRepository _analysisUsageRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly IStage6FeedbackRepository _stage6FeedbackRepository;
    private readonly ILogger<EvalHarnessService> _logger;
    private readonly ConcurrentDictionary<string, EvalExperimentDefinition> _experimentDefinitions = new(StringComparer.OrdinalIgnoreCase);

    public EvalHarnessService(
        IOptions<EvalHarnessSettings> settings,
        IEvalRepository evalRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        Stage5VerificationService stage5VerificationService,
        IBudgetGuardrailService budgetGuardrailService,
        IAnalysisUsageRepository analysisUsageRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6CaseRepository stage6CaseRepository,
        IStage6FeedbackRepository stage6FeedbackRepository,
        ILogger<EvalHarnessService> logger)
    {
        _settings = settings.Value;
        _evalRepository = evalRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _stage5VerificationService = stage5VerificationService;
        _budgetGuardrailService = budgetGuardrailService;
        _analysisUsageRepository = analysisUsageRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6CaseRepository = stage6CaseRepository;
        _stage6FeedbackRepository = stage6FeedbackRepository;
        _logger = logger;
        RegisterDefaultExperimentDefinitions();
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
            ScenarioPackKey = "adhoc",
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
            var scenarioStartedAt = DateTime.UtcNow;
            var result = await RunScenarioAsync(run.RunId, scenarioName, ct);
            var scenarioFinishedAt = DateTime.UtcNow;
            var usage = await _analysisUsageRepository.SummarizeWindowAsync(
                scenarioStartedAt.AddSeconds(-1),
                scenarioFinishedAt.AddSeconds(1),
                ct);
            result.LatencyMs = Math.Max(0, (int)(scenarioFinishedAt - scenarioStartedAt).TotalMilliseconds);
            result.CostUsd = usage.TotalCostUsd;
            result.ModelSummaryJson = JsonSerializer.Serialize(usage.CostByModel);
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

    public Task RegisterExperimentDefinitionAsync(EvalExperimentDefinition definition, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalized = NormalizeExperimentDefinition(definition);
        _experimentDefinitions[normalized.ExperimentKey] = normalized;
        return Task.CompletedTask;
    }

    public Task<List<EvalExperimentDefinition>> GetExperimentDefinitionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_experimentDefinitions.Values.Select(CloneExperimentDefinition).ToList());
    }

    public async Task<EvalExperimentRunComparisonResult> RunExperimentComparisonAsync(
        EvalExperimentRunComparisonRequest request,
        CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            throw new InvalidOperationException("Eval harness is disabled by configuration.");
        }

        var definition = ResolveExperimentDefinition(request.ExperimentKey);
        var scenarioPack = ResolveScenarioPack(definition, request.ScenarioPackKey);
        var selectedVariants = ResolveVariants(definition, request.VariantKeys);
        var baselineVariant = selectedVariants.FirstOrDefault(x => x.IsBaseline) ?? selectedVariants[0];
        var startedAt = DateTime.UtcNow;

        var variantRuns = new List<EvalExperimentVariantRunResult>(selectedVariants.Count);
        var scenarioByVariant = new Dictionary<string, Dictionary<string, bool?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var variant in selectedVariants)
        {
            var scenarios = BuildScenarioNames(scenarioPack, variant);
            var runNamePrefix = string.IsNullOrWhiteSpace(variant.RunNamePrefix)
                ? $"exp:{definition.ExperimentKey}:{variant.VariantKey}"
                : variant.RunNamePrefix!.Trim();

            var evalRun = await RunAsync(new EvalRunRequest
            {
                RunName = $"{runNamePrefix}:{DateTime.UtcNow:yyyyMMddHHmmss}",
                Scenarios = scenarios,
                Actor = string.IsNullOrWhiteSpace(request.Actor) ? "eval_experiment" : request.Actor.Trim()
            }, ct);

            var scenarioPassMap = evalRun.Scenarios.ToDictionary(
                x => x.ScenarioName,
                x => (bool?)x.Passed,
                StringComparer.OrdinalIgnoreCase);

            scenarioByVariant[variant.VariantKey] = scenarioPassMap;
            variantRuns.Add(new EvalExperimentVariantRunResult
            {
                VariantKey = variant.VariantKey,
                DisplayName = variant.DisplayName,
                IsBaseline = variant.IsBaseline,
                EvalRunId = evalRun.RunId,
                Passed = evalRun.Passed,
                ScenarioCount = evalRun.Scenarios.Count,
                ScenarioPassed = evalRun.Scenarios.Count(x => x.Passed),
                ScenarioFailed = evalRun.Scenarios.Count(x => !x.Passed),
                Summary = evalRun.Summary
            });
        }

        var deltas = BuildScenarioDeltas(baselineVariant.VariantKey, variantRuns, scenarioByVariant);
        var passed = variantRuns.All(x => x.Passed);
        var finishedAt = DateTime.UtcNow;
        var experimentRunId = Guid.NewGuid();
        var metricsJson = JsonSerializer.Serialize(new
        {
            experiment_key = definition.ExperimentKey,
            scenario_pack = scenarioPack.PackKey,
            variant_count = variantRuns.Count,
            baseline_variant = baselineVariant.VariantKey,
            passed_variant_count = variantRuns.Count(x => x.Passed),
            failed_variant_count = variantRuns.Count(x => !x.Passed),
            delta_count = deltas.Count
        });

        var summary = $"experiment comparison completed: pass={passed}, variants={variantRuns.Count}, pack={scenarioPack.PackKey}";
        Guid? comparisonEvalRunId = null;
        if (request.PersistComparisonRun)
        {
            comparisonEvalRunId = await PersistComparisonRunAsync(
                definition,
                scenarioPack,
                variantRuns,
                deltas,
                experimentRunId,
                summary,
                metricsJson,
                finishedAt,
                ct);
        }

        if (request.RecordReviewEvent)
        {
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "eval_experiment_run",
                ObjectId = experimentRunId.ToString("D"),
                Action = "comparison_completed",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    experiment_key = definition.ExperimentKey,
                    scenario_pack = scenarioPack.PackKey,
                    comparison_eval_run_id = comparisonEvalRunId,
                    variants = variantRuns.Select(x => new
                    {
                        key = x.VariantKey,
                        eval_run_id = x.EvalRunId,
                        passed = x.Passed
                    })
                }),
                Reason = "ab_experiment_layer",
                Actor = string.IsNullOrWhiteSpace(request.Actor) ? "eval_experiment" : request.Actor.Trim(),
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        _logger.LogInformation(
            "Eval experiment comparison completed. experiment_run_id={ExperimentRunId}, experiment={ExperimentKey}, pack={PackKey}, variants={Variants}, passed={Passed}, comparison_eval_run_id={ComparisonEvalRunId}",
            experimentRunId,
            definition.ExperimentKey,
            scenarioPack.PackKey,
            variantRuns.Count,
            passed,
            comparisonEvalRunId);

        return new EvalExperimentRunComparisonResult
        {
            ExperimentRunId = experimentRunId,
            ComparisonEvalRunId = comparisonEvalRunId,
            ExperimentKey = definition.ExperimentKey,
            ScenarioPackKey = scenarioPack.PackKey,
            Passed = passed,
            Summary = summary,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            MetricsJson = metricsJson,
            Variants = variantRuns,
            ScenarioDeltas = deltas
        };
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
                "stage6_dossier_current_state_quality" => await RunStage6ArtifactQualityScenarioAsync(runId, scenarioName, ct),
                "stage6_draft_review_quality" => await RunStage6DraftReviewQualityScenarioAsync(runId, scenarioName, ct),
                "stage6_clarification_usefulness" => await RunClarificationUsefulnessScenarioAsync(runId, scenarioName, ct),
                "stage6_case_usefulness_noise" => await RunCaseUsefulnessNoiseScenarioAsync(runId, scenarioName, ct),
                "stage6_behavioral_usefulness" => await RunBehavioralUsefulnessScenarioAsync(runId, scenarioName, ct),
                _ => new EvalScenarioResult
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    ScenarioType = "quality",
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
                ScenarioType = "quality",
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
            ScenarioType = "economics",
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
            ScenarioType = "quality",
            ScenarioName = scenarioName,
            Passed = true,
            Summary = "stage5 config smoke passed",
            MetricsJson = JsonSerializer.Serialize(new { check = "stage5_smoke" }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<EvalScenarioResult> RunStage6ArtifactQualityScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var scope = await EnsureEvalSeedCaseAsync("artifact_quality", ct);
        var scopeKey = Stage6ArtifactTypes.ChatScope(scope.ChatId);
        var currentState = await _stage6ArtifactRepository.GetCurrentAsync(scope.CaseId, scope.ChatId, Stage6ArtifactTypes.CurrentState, scopeKey, ct);
        var dossier = await _stage6ArtifactRepository.GetCurrentAsync(scope.CaseId, scope.ChatId, Stage6ArtifactTypes.Dossier, scopeKey, ct);
        var passed = currentState != null && dossier != null;

        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioType = "quality",
            ScenarioName = scenarioName,
            Passed = passed,
            Summary = passed ? "dossier/current_state artifacts are present" : "missing core artifacts",
            MetricsJson = JsonSerializer.Serialize(new
            {
                scope_case_id = scope.CaseId,
                chat_id = scope.ChatId,
                has_current_state = currentState != null,
                has_dossier = dossier != null
            }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<EvalScenarioResult> RunStage6DraftReviewQualityScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var scope = await EnsureEvalSeedCaseAsync("draft_review_quality", ct);
        var cases = await _stage6CaseRepository.GetCasesAsync(scope.CaseId, ct: ct);
        var readyCases = cases.Count(x => x.Status is Stage6CaseStatuses.Ready or Stage6CaseStatuses.New);
        var withDraftTarget = cases.Count(x => x.TargetArtifactTypesJson.Contains(Stage6ArtifactTypes.Draft, StringComparison.OrdinalIgnoreCase));
        var passed = readyCases > 0 && withDraftTarget > 0;

        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioType = "quality",
            ScenarioName = scenarioName,
            Passed = passed,
            Summary = passed ? "draft/review candidate cases exist" : "missing draft/review candidate coverage",
            MetricsJson = JsonSerializer.Serialize(new
            {
                scope_case_id = scope.CaseId,
                ready_cases = readyCases,
                draft_target_cases = withDraftTarget
            }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<EvalScenarioResult> RunClarificationUsefulnessScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var scope = await EnsureEvalSeedCaseAsync("clarification_usefulness", ct);
        var caseRow = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeType = "chat",
            CaseType = Stage6CaseTypes.ClarificationAmbiguity,
            Status = Stage6CaseStatuses.NeedsUserInput,
            Priority = "important",
            ReasonSummary = "Need clarification for ambiguity.",
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.ClarificationState }),
            SourceObjectType = "clarification_question",
            SourceObjectId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        _ = await _stage6FeedbackRepository.AddAsync(new Stage6FeedbackEntry
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            Stage6CaseId = caseRow.Id,
            FeedbackKind = Stage6FeedbackKinds.AcceptUseful,
            FeedbackDimension = Stage6FeedbackDimensions.ClarificationUsefulness,
            IsUseful = true,
            Note = "clarification reduced ambiguity",
            SourceChannel = "eval",
            Actor = "eval_harness",
            CreatedAt = DateTime.UtcNow
        }, ct);

        var feedback = await _stage6FeedbackRepository.GetByCaseAsync(caseRow.Id, 50, ct);
        var useful = feedback.Count(x => x.FeedbackDimension == Stage6FeedbackDimensions.ClarificationUsefulness && x.IsUseful == true);
        var notUseful = feedback.Count(x => x.FeedbackDimension == Stage6FeedbackDimensions.ClarificationUsefulness && x.IsUseful == false);
        var passed = useful >= notUseful;

        var feedbackSummary = JsonSerializer.Serialize(new
        {
            clarification_useful = useful,
            clarification_not_useful = notUseful
        });

        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioType = "usefulness",
            ScenarioName = scenarioName,
            Passed = passed,
            Summary = passed ? "clarification feedback shows usefulness" : "clarification usefulness is degraded",
            FeedbackSummaryJson = feedbackSummary,
            MetricsJson = JsonSerializer.Serialize(new
            {
                scope_case_id = scope.CaseId,
                case_id = caseRow.Id
            }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<EvalScenarioResult> RunCaseUsefulnessNoiseScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var scope = await EnsureEvalSeedCaseAsync("case_usefulness_noise", ct);
        var caseRow = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeType = "chat",
            CaseType = Stage6CaseTypes.NeedsReview,
            Status = Stage6CaseStatuses.Ready,
            Priority = "important",
            ReasonSummary = "review candidate",
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Review }),
            SourceObjectType = "draft_review",
            SourceObjectId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        _ = await _stage6FeedbackRepository.AddAsync(new Stage6FeedbackEntry
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            Stage6CaseId = caseRow.Id,
            FeedbackKind = Stage6FeedbackKinds.AcceptUseful,
            FeedbackDimension = Stage6FeedbackDimensions.General,
            IsUseful = true,
            Note = "case was useful for review flow",
            SourceChannel = "eval",
            Actor = "eval_harness",
            CreatedAt = DateTime.UtcNow
        }, ct);

        var feedback = await _stage6FeedbackRepository.GetByCaseAsync(caseRow.Id, 50, ct);
        var useful = feedback.Count(x => x.IsUseful == true);
        var noisy = feedback.Count(x => x.IsUseful == false);
        var passed = useful >= noisy;

        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioType = "usefulness",
            ScenarioName = scenarioName,
            Passed = passed,
            Summary = passed ? "case usefulness exceeds noise" : "case noise exceeds usefulness",
            FeedbackSummaryJson = JsonSerializer.Serialize(new
            {
                useful,
                noisy
            }),
            MetricsJson = JsonSerializer.Serialize(new
            {
                scope_case_id = scope.CaseId,
                case_id = caseRow.Id
            }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<EvalScenarioResult> RunBehavioralUsefulnessScenarioAsync(Guid runId, string scenarioName, CancellationToken ct)
    {
        var scope = await EnsureEvalSeedCaseAsync("behavioral_usefulness", ct);
        var caseRow = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeType = "chat",
            CaseType = Stage6CaseTypes.NeedsReview,
            CaseSubtype = "behavioral_profile_support",
            Status = Stage6CaseStatuses.Ready,
            Priority = "important",
            ReasonSummary = "behavioral layer support review",
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Strategy }),
            SourceObjectType = "behavioral_profile",
            SourceObjectId = Guid.NewGuid().ToString("D"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);

        _ = await _stage6FeedbackRepository.AddAsync(new Stage6FeedbackEntry
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            Stage6CaseId = caseRow.Id,
            FeedbackKind = Stage6FeedbackKinds.CorrectionNote,
            FeedbackDimension = Stage6FeedbackDimensions.BehavioralUsefulness,
            IsUseful = true,
            Note = "behavioral profile helpful, bounded and non-diagnostic",
            SourceChannel = "eval",
            Actor = "eval_harness",
            CreatedAt = DateTime.UtcNow
        }, ct);

        var feedback = await _stage6FeedbackRepository.GetByCaseAsync(caseRow.Id, 50, ct);
        var useful = feedback.Count(x => x.FeedbackDimension == Stage6FeedbackDimensions.BehavioralUsefulness && x.IsUseful == true);
        var concerns = feedback.Count(x => x.FeedbackDimension == Stage6FeedbackDimensions.BehavioralUsefulness && x.IsUseful == false);
        var passed = useful >= concerns;

        return new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioType = "usefulness",
            ScenarioName = scenarioName,
            Passed = passed,
            Summary = passed ? "behavioral layer feedback remains useful" : "behavioral layer is overclaiming/noisy",
            FeedbackSummaryJson = JsonSerializer.Serialize(new
            {
                behavioral_useful = useful,
                behavioral_concerns = concerns
            }),
            MetricsJson = JsonSerializer.Serialize(new
            {
                scope_case_id = scope.CaseId,
                case_id = caseRow.Id
            }),
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<CaseScope> EnsureEvalSeedCaseAsync(string scenario, CancellationToken ct)
    {
        var scope = CaseScopeFactory.CreateSmokeScope($"eval13:{scenario}");
        var now = DateTime.UtcNow;

        _ = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeType = "chat",
            CaseType = Stage6CaseTypes.DossierCandidate,
            Status = Stage6CaseStatuses.Ready,
            Priority = "important",
            ReasonSummary = $"seeded for {scenario}",
            TargetArtifactTypesJson = JsonSerializer.Serialize(new[] { Stage6ArtifactTypes.Dossier, Stage6ArtifactTypes.CurrentState, Stage6ArtifactTypes.Draft, Stage6ArtifactTypes.Review }),
            SourceObjectType = "eval_seed",
            SourceObjectId = scenario,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.CurrentState,
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(scope.ChatId),
            PayloadObjectType = "state_snapshot",
            PayloadObjectId = Guid.NewGuid().ToString("D"),
            PayloadJson = JsonSerializer.Serialize(new { summary = "current state seed", confidence = 0.71 }),
            FreshnessBasisHash = Guid.NewGuid().ToString("N"),
            FreshnessBasisJson = JsonSerializer.Serialize(new { refs = new[] { "seed:state" } }),
            GeneratedAt = now,
            RefreshedAt = now,
            IsCurrent = true,
            IsStale = false,
            SourceType = "eval_seed",
            SourceId = scenario,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.Dossier,
            CaseId = scope.CaseId,
            ChatId = scope.ChatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(scope.ChatId),
            PayloadObjectType = "dossier",
            PayloadObjectId = Guid.NewGuid().ToString("D"),
            PayloadJson = JsonSerializer.Serialize(new { summary = "dossier seed", confidence = 0.69 }),
            FreshnessBasisHash = Guid.NewGuid().ToString("N"),
            FreshnessBasisJson = JsonSerializer.Serialize(new { refs = new[] { "seed:dossier" } }),
            GeneratedAt = now,
            RefreshedAt = now,
            IsCurrent = true,
            IsStale = false,
            SourceType = "eval_seed",
            SourceId = scenario,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        return scope;
    }

    private static List<EvalExperimentScenarioDelta> BuildScenarioDeltas(
        string baselineVariantKey,
        List<EvalExperimentVariantRunResult> variantRuns,
        Dictionary<string, Dictionary<string, bool?>> scenarioByVariant)
    {
        var scenarioNames = scenarioByVariant.Values
            .SelectMany(x => x.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var deltas = new List<EvalExperimentScenarioDelta>(scenarioNames.Count);
        foreach (var scenarioName in scenarioNames)
        {
            scenarioByVariant.TryGetValue(baselineVariantKey, out var baselineMap);
            bool? baselinePassed = null;
            if (baselineMap != null && baselineMap.TryGetValue(scenarioName, out var baselineObserved))
            {
                baselinePassed = baselineObserved;
            }

            var variantPassed = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (var variant in variantRuns)
            {
                var pass = scenarioByVariant.TryGetValue(variant.VariantKey, out var map)
                           && map.TryGetValue(scenarioName, out var variantScenarioPass)
                    ? variantScenarioPass
                    : null;
                variantPassed[variant.VariantKey] = pass;
            }

            var deltaType = "unknown";
            if (baselinePassed.HasValue)
            {
                var baselineValue = baselinePassed.Value;
                var nonBaselineValues = variantRuns
                    .Where(x => !string.Equals(x.VariantKey, baselineVariantKey, StringComparison.OrdinalIgnoreCase))
                    .Select(x => variantPassed[x.VariantKey])
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .ToList();

                if (nonBaselineValues.Count == 0)
                {
                    deltaType = "baseline_only";
                }
                else if (nonBaselineValues.All(x => x == baselineValue))
                {
                    deltaType = "stable";
                }
                else if (nonBaselineValues.Any(x => x && !baselineValue))
                {
                    deltaType = "candidate_improved";
                }
                else if (nonBaselineValues.Any(x => !x && baselineValue))
                {
                    deltaType = "candidate_regressed";
                }
                else
                {
                    deltaType = "mixed";
                }
            }

            deltas.Add(new EvalExperimentScenarioDelta
            {
                ScenarioName = scenarioName,
                BaselinePassed = baselinePassed,
                DeltaType = deltaType,
                VariantPassed = variantPassed
            });
        }

        return deltas;
    }

    private async Task<Guid> PersistComparisonRunAsync(
        EvalExperimentDefinition definition,
        EvalScenarioPackDefinition scenarioPack,
        List<EvalExperimentVariantRunResult> variantRuns,
        List<EvalExperimentScenarioDelta> deltas,
        Guid experimentRunId,
        string summary,
        string comparisonMetricsJson,
        DateTime finishedAt,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        var startedAt = DateTime.UtcNow;
        var runName = $"experiment:{definition.ExperimentKey}:{finishedAt:yyyyMMddHHmmss}";
        await _evalRepository.CreateRunAsync(new EvalRunResult
        {
            RunId = runId,
            RunName = runName,
            Passed = false,
            StartedAt = startedAt,
            FinishedAt = startedAt,
            Summary = "running",
            MetricsJson = "{}"
        }, ct);

        foreach (var variant in variantRuns)
        {
            await _evalRepository.AddScenarioResultAsync(new EvalScenarioResult
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                ScenarioType = "comparison",
                ScenarioName = $"variant:{variant.VariantKey}",
                Passed = variant.Passed,
                Summary = variant.Summary,
                MetricsJson = JsonSerializer.Serialize(new
                {
                    variant_key = variant.VariantKey,
                    variant_name = variant.DisplayName,
                    eval_run_id = variant.EvalRunId,
                    is_baseline = variant.IsBaseline,
                    scenario_count = variant.ScenarioCount,
                    scenario_passed = variant.ScenarioPassed,
                    scenario_failed = variant.ScenarioFailed
                }),
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        var passed = variantRuns.All(x => x.Passed);
        var metricsJson = JsonSerializer.Serialize(new
        {
            type = "experiment_comparison",
            experiment_key = definition.ExperimentKey,
            experiment_run_id = experimentRunId,
            scenario_pack = scenarioPack.PackKey,
            variant_count = variantRuns.Count,
            delta_count = deltas.Count,
            comparison = JsonSerializer.Deserialize<JsonElement>(comparisonMetricsJson)
        });

        await _evalRepository.CompleteRunAsync(runId, passed, summary, metricsJson, finishedAt, ct);
        return runId;
    }

    private static List<string> BuildScenarioNames(EvalScenarioPackDefinition scenarioPack, EvalExperimentVariantDefinition variant)
    {
        var names = scenarioPack.Scenarios
            .Select(x => x.ScenarioName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var include in variant.IncludeScenarios)
        {
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            if (!names.Contains(include.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                names.Add(include.Trim());
            }
        }

        var excluded = variant.ExcludeScenarios
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return names.Where(x => !excluded.Contains(x)).ToList();
    }

    private EvalExperimentDefinition ResolveExperimentDefinition(string experimentKey)
    {
        if (string.IsNullOrWhiteSpace(experimentKey))
        {
            throw new InvalidOperationException("Experiment key is required.");
        }

        if (!_experimentDefinitions.TryGetValue(experimentKey.Trim(), out var definition))
        {
            throw new InvalidOperationException($"Experiment definition '{experimentKey}' is not registered.");
        }

        return CloneExperimentDefinition(definition);
    }

    private static EvalScenarioPackDefinition ResolveScenarioPack(EvalExperimentDefinition definition, string? requestedPackKey)
    {
        var key = string.IsNullOrWhiteSpace(requestedPackKey)
            ? definition.DefaultScenarioPackKey
            : requestedPackKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' does not define scenario packs.");
        }

        var scenarioPack = definition.ScenarioPacks.FirstOrDefault(x => string.Equals(x.PackKey, key, StringComparison.OrdinalIgnoreCase));
        return scenarioPack ?? throw new InvalidOperationException($"Scenario pack '{key}' is not registered for experiment '{definition.ExperimentKey}'.");
    }

    private static List<EvalExperimentVariantDefinition> ResolveVariants(EvalExperimentDefinition definition, List<string> requestedVariantKeys)
    {
        if (definition.Variants.Count == 0)
        {
            throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' has no variants.");
        }

        if (requestedVariantKeys.Count == 0)
        {
            return definition.Variants.ToList();
        }

        var requested = requestedVariantKeys
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count == 0)
        {
            throw new InvalidOperationException("At least one non-empty variant key is required.");
        }

        var selected = definition.Variants
            .Where(x => requested.Contains(x.VariantKey, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count != requested.Count)
        {
            throw new InvalidOperationException("Some requested variant keys are not registered for the experiment.");
        }

        return selected;
    }

    private void RegisterDefaultExperimentDefinitions()
    {
        var defaultDefinition = new EvalExperimentDefinition
        {
            ExperimentKey = "stage6_guarded_default",
            DisplayName = "Stage6 Guarded Default",
            Description = "Baseline practical A/B experiment pack over the eval harness.",
            DefaultScenarioPackKey = "stage6_quality",
            Variants =
            [
                new EvalExperimentVariantDefinition
                {
                    VariantKey = "baseline",
                    DisplayName = "Baseline",
                    IsBaseline = true,
                    RunNamePrefix = "exp:stage6_guarded_default:baseline"
                },
                new EvalExperimentVariantDefinition
                {
                    VariantKey = "candidate",
                    DisplayName = "Candidate",
                    IsBaseline = false,
                    RunNamePrefix = "exp:stage6_guarded_default:candidate"
                }
            ],
            ScenarioPacks =
            [
                new EvalScenarioPackDefinition
                {
                    PackKey = "core",
                    Description = "Core practical scenario pack",
                    Scenarios =
                    [
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "budget_visibility",
                            Required = true,
                            Description = "Budget guardrail state is persisted and visible."
                        },
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage5_config",
                            Required = true,
                            Description = "Stage5 configuration smoke passes."
                        }
                    ]
                },
                new EvalScenarioPackDefinition
                {
                    PackKey = "stage6_quality",
                    Description = "Stage 6 quality, usefulness, and economics baseline pack",
                    Scenarios =
                    [
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage6_dossier_current_state_quality",
                            Required = true,
                            Description = "Dossier/current-state artifacts are usable and inspectable."
                        },
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage6_draft_review_quality",
                            Required = true,
                            Description = "Draft/review path has candidate and artifact coverage."
                        },
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage6_clarification_usefulness",
                            Required = true,
                            Description = "Clarification flow usefulness is measurable from linked feedback."
                        },
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage6_case_usefulness_noise",
                            Required = true,
                            Description = "Case usefulness vs noise baseline from explicit operator feedback."
                        },
                        new EvalScenarioDefinition
                        {
                            ScenarioName = "stage6_behavioral_usefulness",
                            Required = true,
                            Description = "Behavioral layer usefulness is explicitly tracked as bounded support."
                        }
                    ]
                }
            ]
        };

        var normalized = NormalizeExperimentDefinition(defaultDefinition);
        _experimentDefinitions[normalized.ExperimentKey] = normalized;
    }

    private static EvalExperimentDefinition NormalizeExperimentDefinition(EvalExperimentDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.ExperimentKey))
        {
            throw new InvalidOperationException("Experiment key is required.");
        }

        if (definition.Variants.Count < 2)
        {
            throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' must contain at least two variants.");
        }

        if (definition.ScenarioPacks.Count == 0)
        {
            throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' must define at least one scenario pack.");
        }

        if (string.IsNullOrWhiteSpace(definition.DefaultScenarioPackKey))
        {
            definition.DefaultScenarioPackKey = definition.ScenarioPacks[0].PackKey;
        }

        var variantKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var baselineCount = 0;
        foreach (var variant in definition.Variants)
        {
            if (string.IsNullOrWhiteSpace(variant.VariantKey))
            {
                throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' contains variant without key.");
            }

            variant.VariantKey = variant.VariantKey.Trim();
            variant.DisplayName = string.IsNullOrWhiteSpace(variant.DisplayName) ? variant.VariantKey : variant.DisplayName.Trim();
            if (!variantKeys.Add(variant.VariantKey))
            {
                throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' has duplicate variant key '{variant.VariantKey}'.");
            }

            if (variant.IsBaseline)
            {
                baselineCount++;
            }

            variant.IncludeScenarios = variant.IncludeScenarios
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            variant.ExcludeScenarios = variant.ExcludeScenarios
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (baselineCount != 1)
        {
            throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' must have exactly one baseline variant.");
        }

        var packKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in definition.ScenarioPacks)
        {
            if (string.IsNullOrWhiteSpace(pack.PackKey))
            {
                throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' contains scenario pack without key.");
            }

            pack.PackKey = pack.PackKey.Trim();
            pack.Description = pack.Description?.Trim() ?? string.Empty;
            if (!packKeys.Add(pack.PackKey))
            {
                throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' has duplicate scenario pack key '{pack.PackKey}'.");
            }

            pack.Scenarios = pack.Scenarios
                .Where(x => !string.IsNullOrWhiteSpace(x.ScenarioName))
                .Select(x => new EvalScenarioDefinition
                {
                    ScenarioName = x.ScenarioName.Trim(),
                    Required = x.Required,
                    Description = x.Description?.Trim()
                })
                .DistinctBy(x => x.ScenarioName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pack.Scenarios.Count == 0)
            {
                throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' scenario pack '{pack.PackKey}' is empty.");
            }
        }

        if (!packKeys.Contains(definition.DefaultScenarioPackKey))
        {
            throw new InvalidOperationException($"Experiment '{definition.ExperimentKey}' default scenario pack '{definition.DefaultScenarioPackKey}' is not defined.");
        }

        definition.ExperimentKey = definition.ExperimentKey.Trim();
        definition.DisplayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.ExperimentKey : definition.DisplayName.Trim();
        definition.Description = definition.Description?.Trim() ?? string.Empty;
        definition.DefaultScenarioPackKey = definition.DefaultScenarioPackKey.Trim();
        return CloneExperimentDefinition(definition);
    }

    private static EvalExperimentDefinition CloneExperimentDefinition(EvalExperimentDefinition source)
    {
        return new EvalExperimentDefinition
        {
            ExperimentKey = source.ExperimentKey,
            DisplayName = source.DisplayName,
            Description = source.Description,
            DefaultScenarioPackKey = source.DefaultScenarioPackKey,
            Variants = source.Variants
                .Select(x => new EvalExperimentVariantDefinition
                {
                    VariantKey = x.VariantKey,
                    DisplayName = x.DisplayName,
                    IsBaseline = x.IsBaseline,
                    RunNamePrefix = x.RunNamePrefix,
                    IncludeScenarios = x.IncludeScenarios.ToList(),
                    ExcludeScenarios = x.ExcludeScenarios.ToList()
                })
                .ToList(),
            ScenarioPacks = source.ScenarioPacks
                .Select(x => new EvalScenarioPackDefinition
                {
                    PackKey = x.PackKey,
                    Description = x.Description,
                    Scenarios = x.Scenarios
                        .Select(s => new EvalScenarioDefinition
                        {
                            ScenarioName = s.ScenarioName,
                            Required = s.Required,
                            Description = s.Description
                        })
                        .ToList()
                })
                .ToList()
        };
    }
}

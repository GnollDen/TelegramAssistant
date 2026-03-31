using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using System.Text.Json;

namespace TgAssistant.Web.Read;

public class WebOpsVerificationService
{
    private static readonly string[] W2ArtifactTypes =
    [
        Stage6ArtifactTypes.Dossier,
        Stage6ArtifactTypes.CurrentState,
        Stage6ArtifactTypes.Strategy,
        Stage6ArtifactTypes.Draft,
        Stage6ArtifactTypes.Review
    ];

    private readonly IWebRouteRenderer _webRouteRenderer;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IBudgetOpsRepository _budgetOpsRepository;
    private readonly IEvalRepository _evalRepository;
    private readonly IStage6CaseRepository _stage6CaseRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6FeedbackRepository _stage6FeedbackRepository;
    private readonly IStage6CaseOutcomeRepository _stage6CaseOutcomeRepository;

    public WebOpsVerificationService(
        IWebRouteRenderer webRouteRenderer,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IClarificationRepository clarificationRepository,
        IInboxConflictRepository inboxConflictRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IOfflineEventRepository offlineEventRepository,
        IBudgetOpsRepository budgetOpsRepository,
        IEvalRepository evalRepository,
        IStage6CaseRepository stage6CaseRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6FeedbackRepository stage6FeedbackRepository,
        IStage6CaseOutcomeRepository stage6CaseOutcomeRepository)
    {
        _webRouteRenderer = webRouteRenderer;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _clarificationRepository = clarificationRepository;
        _inboxConflictRepository = inboxConflictRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _offlineEventRepository = offlineEventRepository;
        _budgetOpsRepository = budgetOpsRepository;
        _evalRepository = evalRepository;
        _stage6CaseRepository = stage6CaseRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6FeedbackRepository = stage6FeedbackRepository;
        _stage6CaseOutcomeRepository = stage6CaseOutcomeRepository;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var baseId = 9_700_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L);
        var caseId = baseId;
        var chatId = baseId;

        var question = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = "Need clarity on readiness for direct invitation this week?",
            QuestionType = "strategy",
            Priority = "blocking",
            Status = "open",
            WhyItMatters = "High impact for immediate next move",
            ExpectedGain = 0.8f,
            SourceType = "smoke",
            SourceId = "ops_web"
        }, ct);

        await SeedScenarioMinerArtifactsAsync(caseId, chatId, ct);

        _ = await _clarificationRepository.UpdateQuestionWorkflowAsync(
            question.Id,
            "in_progress",
            "blocking",
            "ops_web_smoke",
            "seed history event",
            ct);

        _ = await _inboxConflictRepository.CreateInboxItemAsync(new InboxItem
        {
            CaseId = caseId,
            ChatId = chatId,
            ItemType = "clarification",
            SourceObjectType = "clarification_question",
            SourceObjectId = question.Id.ToString(),
            Priority = "blocking",
            IsBlocking = true,
            Title = "Clarify invitation readiness",
            Summary = "Blocking clarification before escalation.",
            Status = "open",
            LastActor = "ops_web_smoke",
            LastReason = "seeded"
        }, ct);

        await _budgetOpsRepository.UpsertBudgetOperationalStateAsync(new BudgetOperationalState
        {
            PathKey = "stage5.expensive_pass",
            Modality = BudgetModalities.TextAnalysis,
            State = BudgetPathStates.SoftLimited,
            Reason = "daily_budget_soft_limit",
            DetailsJson = "{\"daily_spent_usd\":8.5,\"daily_budget_usd\":10.0}",
            UpdatedAt = DateTime.UtcNow
        }, ct);

        await _budgetOpsRepository.UpsertBudgetOperationalStateAsync(new BudgetOperationalState
        {
            PathKey = "stage5.expensive_pass.backfill",
            Modality = BudgetModalities.TextAnalysis,
            State = BudgetPathStates.QuotaBlocked,
            Reason = "external_provider_quota_exhausted",
            DetailsJson = "{\"quota_remaining\":0,\"window_reset_minutes\":41}",
            UpdatedAt = DateTime.UtcNow
        }, ct);

        await _budgetOpsRepository.UpsertBudgetOperationalStateAsync(new BudgetOperationalState
        {
            PathKey = "stage5.deep_check",
            Modality = BudgetModalities.TextAnalysis,
            State = BudgetPathStates.HardPaused,
            Reason = "manual_operator_pause",
            DetailsJson = "{\"operator\":\"ops_web_smoke\"}",
            UpdatedAt = DateTime.UtcNow
        }, ct);

        var runId = Guid.NewGuid();
        await _evalRepository.CreateRunAsync(new EvalRunResult
        {
            RunId = runId,
            RunName = "ops_web_smoke",
            Passed = true,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
            FinishedAt = DateTime.UtcNow,
            Summary = "ops visibility smoke run",
            MetricsJson = "{\"pass_rate\":1.0,\"scenarios\":1}"
        }, ct);

        var previousRunId = Guid.NewGuid();
        await _evalRepository.CreateRunAsync(new EvalRunResult
        {
            RunId = previousRunId,
            RunName = "ops_web_smoke",
            Passed = false,
            StartedAt = DateTime.UtcNow.AddMinutes(-16),
            FinishedAt = DateTime.UtcNow.AddMinutes(-15),
            Summary = "ops visibility prior run",
            MetricsJson = "{\"pass_rate\":0.0,\"scenarios\":1}"
        }, ct);

        await _evalRepository.AddScenarioResultAsync(new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ScenarioType = "economics",
            ScenarioName = "budget_visibility",
            Passed = true,
            Summary = "budget state visible",
            LatencyMs = 240,
            CostUsd = 0.014m,
            ModelSummaryJson = "{\"openai/gpt-4o-mini\":0.014}",
            FeedbackSummaryJson = "{\"clarification_useful\":1}",
            MetricsJson = "{\"states_visible\":1}",
            CreatedAt = DateTime.UtcNow
        }, ct);

        await _evalRepository.AddScenarioResultAsync(new EvalScenarioResult
        {
            Id = Guid.NewGuid(),
            RunId = previousRunId,
            ScenarioType = "economics",
            ScenarioName = "budget_visibility",
            Passed = false,
            Summary = "previous run failed for comparison visibility",
            LatencyMs = 330,
            CostUsd = 0.021m,
            ModelSummaryJson = "{\"openai/gpt-4o-mini\":0.021}",
            FeedbackSummaryJson = "{\"clarification_useful\":0}",
            MetricsJson = "{\"states_visible\":0}",
            CreatedAt = DateTime.UtcNow.AddMinutes(-15)
        }, ct);

        var request = new WebReadRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "ops_web_smoke"
        };

        var stage6Case = await _stage6CaseRepository.GetBySourceAsync(
            caseId,
            Stage6CaseTypes.NeedsInput,
            "clarification_question",
            question.Id.ToString(),
            ct)
            ?? await _stage6CaseRepository.GetBySourceAsync(
                caseId,
                Stage6CaseTypes.ClarificationMissingData,
                "clarification_question",
                question.Id.ToString(),
                ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: clarification question did not materialize as stage6 case.");

        await SeedStage6ArtifactsAsync(caseId, chatId, stage6Case, ct);

        var inboxPage = await _webRouteRenderer.RenderAsync("/inbox", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /inbox route did not resolve.");
        if (string.IsNullOrWhiteSpace(inboxPage.Html)
            || !inboxPage.Html.Contains("Операционная очередь Stage 6", StringComparison.OrdinalIgnoreCase)
            || !inboxPage.Html.Contains(question.QuestionText, StringComparison.OrdinalIgnoreCase)
            || !inboxPage.Html.Contains("Статус:", StringComparison.OrdinalIgnoreCase)
            || !inboxPage.Html.Contains("Приоритет:", StringComparison.OrdinalIgnoreCase)
            || !inboxPage.Html.Contains("Уверенность:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /inbox route does not show seeded stage6 queue items.");
        }

        var caseDetailPage = await _webRouteRenderer.RenderAsync($"/case-detail?caseId={stage6Case.Id}", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-detail route did not resolve.");
        if (string.IsNullOrWhiteSpace(caseDetailPage.Html)
            || !caseDetailPage.Html.Contains("Evidence First Context", StringComparison.OrdinalIgnoreCase)
            || !caseDetailPage.Html.Contains("Clarification", StringComparison.OrdinalIgnoreCase)
            || !caseDetailPage.Html.Contains("Deep Review Snapshot", StringComparison.OrdinalIgnoreCase)
            || !caseDetailPage.Html.Contains("Long-form Context / Correction", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: case detail route does not show evidence-first clarification workflow.");
        }

        var evidencePage = await _webRouteRenderer.RenderAsync($"/case-evidence?caseId={stage6Case.Id}", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-evidence route did not resolve.");
        if (string.IsNullOrWhiteSpace(evidencePage.Html)
            || !evidencePage.Html.Contains("Case Evidence Drill-Down", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /case-evidence route does not show evidence drill-down.");
        }

        var contextPage = await _webRouteRenderer.RenderAsync(
            $"/case-action?caseId={stage6Case.Id}&action=annotate&contextEntryMode=correction&contextSourceKind=user_context_correction&correctionTargetRef={Uri.EscapeDataString("message:1")}&note={Uri.EscapeDataString("Long-form correction from web smoke. Clarifies date mismatch.")}&reason={Uri.EscapeDataString("w3 smoke correction")}",
            request,
            ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: long-form correction route did not resolve.");
        if (!contextPage.Html.Contains("success=True", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: long-form correction submission did not report success.");
        }

        foreach (var artifactType in W2ArtifactTypes)
        {
            var artifactPage = await _webRouteRenderer.RenderAsync($"/artifact-detail?artifactType={Uri.EscapeDataString(artifactType)}", request, ct)
                ?? throw new InvalidOperationException($"Ops web smoke failed: /artifact-detail for '{artifactType}' did not resolve.");
            if (string.IsNullOrWhiteSpace(artifactPage.Html)
                || !artifactPage.Html.Contains("Artifact Detail", StringComparison.OrdinalIgnoreCase)
                || !artifactPage.Html.Contains(artifactType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Ops web smoke failed: artifact detail for '{artifactType}' is not operator-usable.");
            }
        }

        var rejectConfirmPage = await _webRouteRenderer.RenderAsync($"/case-action?caseId={stage6Case.Id}&action=reject", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-action reject route did not resolve.");
        if (string.IsNullOrWhiteSpace(rejectConfirmPage.Html)
            || !rejectConfirmPage.Html.Contains("Confirmation Required", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: reject action did not require confirmation.");
        }

        var rejectPage = await _webRouteRenderer.RenderAsync($"/case-action?caseId={stage6Case.Id}&action=reject&confirm=1", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-action reject confirm route did not resolve.");
        if (string.IsNullOrWhiteSpace(rejectPage.Html)
            || !rejectPage.Html.Contains("success=True", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: reject action did not report success.");
        }

        var stage6CaseAfterReject = await _stage6CaseRepository.GetByIdAsync(stage6Case.Id, ct);
        if (stage6CaseAfterReject == null || !stage6CaseAfterReject.Status.Equals(Stage6CaseStatuses.Rejected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: reject action did not persist rejected case status.");
        }

        var resolveConfirmPage = await _webRouteRenderer.RenderAsync($"/case-action?caseId={stage6Case.Id}&action=resolve", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-action resolve route did not resolve.");
        if (string.IsNullOrWhiteSpace(resolveConfirmPage.Html)
            || !resolveConfirmPage.Html.Contains("Confirmation Required", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: resolve action did not require confirmation.");
        }

        var resolvePage = await _webRouteRenderer.RenderAsync($"/case-action?caseId={stage6Case.Id}&action=resolve&confirm=1", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-action resolve confirm route did not resolve.");
        if (string.IsNullOrWhiteSpace(resolvePage.Html)
            || !resolvePage.Html.Contains("success=True", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: resolve action did not report success.");
        }

        var stage6CaseAfterResolve = await _stage6CaseRepository.GetByIdAsync(stage6Case.Id, ct);
        if (stage6CaseAfterResolve == null || !stage6CaseAfterResolve.Status.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: resolve action did not persist resolved case status.");
        }

        var refreshPage = await _webRouteRenderer.RenderAsync($"/case-action?caseId={stage6Case.Id}&action=refresh", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-action refresh route did not resolve.");
        if (string.IsNullOrWhiteSpace(refreshPage.Html)
            || !refreshPage.Html.Contains("Case Action", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: refresh action page did not render.");
        }

        var stage6CaseAfterRefresh = await _stage6CaseRepository.GetByIdAsync(stage6Case.Id, ct);
        if (stage6CaseAfterRefresh == null || !stage6CaseAfterRefresh.Status.Equals(Stage6CaseStatuses.Ready, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: refresh action did not return case to ready status.");
        }

        var invalidCaseDetailPage = await _webRouteRenderer.RenderAsync($"/case-detail?caseId={Guid.NewGuid()}", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: invalid /case-detail route did not resolve.");
        if (!invalidCaseDetailPage.Html.Contains("Case Not Found", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: invalid case detail state is not explicit.");
        }

        var missingArtifactPage = await _webRouteRenderer.RenderAsync("/artifact-detail?artifactType=unknown_artifact", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: missing artifact route did not resolve.");
        if (!missingArtifactPage.Html.Contains("Artifact Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: missing artifact state is not explicit.");
        }

        var answerPage = await _webRouteRenderer.RenderAsync(
            $"/clarification-answer?caseId={stage6Case.Id}&answer={Uri.EscapeDataString("web smoke answer")}&confirm=1",
            request,
            ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /clarification-answer route did not resolve.");
        if (string.IsNullOrWhiteSpace(answerPage.Html)
            || !answerPage.Html.Contains("Clarification Answer", StringComparison.OrdinalIgnoreCase)
            || !answerPage.Html.Contains("web smoke answer", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: clarification answer intake route did not persist/render answer.");
        }

        var stage6CaseAfterAnswer = await _stage6CaseRepository.GetByIdAsync(stage6Case.Id, ct);
        if (stage6CaseAfterAnswer == null || !stage6CaseAfterAnswer.Status.Equals(Stage6CaseStatuses.Resolved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: clarification answer did not persist resolved case status.");
        }

        var feedbackRows = await _stage6FeedbackRepository.GetByCaseAsync(stage6Case.Id, 20, ct);
        if (feedbackRows.Count == 0)
        {
            throw new InvalidOperationException("Ops web smoke failed: case feedback was not recorded after clarification answer.");
        }

        var outcomeRows = await _stage6CaseOutcomeRepository.GetByCaseAsync(stage6Case.Id, 20, ct);
        if (outcomeRows.Count == 0)
        {
            throw new InvalidOperationException("Ops web smoke failed: case outcomes were not recorded after clarification answer.");
        }

        var caseAfterAnswer = await _webRouteRenderer.RenderAsync($"/case-detail?caseId={stage6Case.Id}", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /case-detail after answer did not resolve.");
        if (string.IsNullOrWhiteSpace(caseAfterAnswer.Html)
            || !caseAfterAnswer.Html.Contains("Feedback", StringComparison.OrdinalIgnoreCase)
            || !caseAfterAnswer.Html.Contains("Outcomes", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: case detail does not expose feedback/outcome history.");
        }

        var historyPage = await _webRouteRenderer.RenderAsync("/history?objectType=clarification_question", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /history route did not resolve.");
        if (string.IsNullOrWhiteSpace(historyPage.Html)
            || !historyPage.Html.Contains("History / Activity", StringComparison.OrdinalIgnoreCase)
            || !historyPage.Html.Contains("clarification_question", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /history route does not show seeded history events.");
        }

        var trailPage = await _webRouteRenderer.RenderAsync(
            $"/history-object?objectType=clarification_question&objectId={question.Id}",
            request,
            ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /history-object route did not resolve.");
        if (string.IsNullOrWhiteSpace(trailPage.Html)
            || !trailPage.Html.Contains("Object / History Trail", StringComparison.OrdinalIgnoreCase)
            || !trailPage.Html.Contains(question.QuestionText, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: cross-link object/history path did not render clarification trail.");
        }

        var budgetPage = await _webRouteRenderer.RenderAsync("/ops-budget", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /ops-budget route did not resolve.");
        if (string.IsNullOrWhiteSpace(budgetPage.Html)
            || !budgetPage.Html.Contains("Ops Budget", StringComparison.OrdinalIgnoreCase)
            || !budgetPage.Html.Contains("stage5.expensive_pass", StringComparison.OrdinalIgnoreCase)
            || !budgetPage.Html.Contains("quota-blocked", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /ops-budget route does not show budget states.");
        }

        var quotaBudgetPage = await _webRouteRenderer.RenderAsync("/ops-budget?state=quota_blocked", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: filtered /ops-budget route did not resolve.");
        if (string.IsNullOrWhiteSpace(quotaBudgetPage.Html)
            || !quotaBudgetPage.Html.Contains("external_provider_quota_exhausted", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: quota-blocked filter is not visible.");
        }

        var evalPage = await _webRouteRenderer.RenderAsync($"/ops-eval?runId={runId}", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /ops-eval route did not resolve.");
        if (string.IsNullOrWhiteSpace(evalPage.Html)
            || !evalPage.Html.Contains("Ops Eval", StringComparison.OrdinalIgnoreCase)
            || !evalPage.Html.Contains("budget_visibility", StringComparison.OrdinalIgnoreCase)
            || !evalPage.Html.Contains("latency_ms", StringComparison.OrdinalIgnoreCase)
            || !evalPage.Html.Contains("cost_usd", StringComparison.OrdinalIgnoreCase)
            || !evalPage.Html.Contains("Run Comparisons", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /ops-eval route does not show eval run/scenarios.");
        }

        var failedEvalPage = await _webRouteRenderer.RenderAsync("/ops-eval?status=failed", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: filtered /ops-eval route did not resolve.");
        if (string.IsNullOrWhiteSpace(failedEvalPage.Html)
            || !failedEvalPage.Html.Contains("ops visibility prior run", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: failed eval filter is not visible.");
        }

        var repeatRoutes = new[]
        {
            "/inbox",
            $"/case-detail?caseId={stage6Case.Id}",
            $"/case-evidence?caseId={stage6Case.Id}",
            $"/artifact-detail?artifactType={Stage6ArtifactTypes.Dossier}",
            "/inbox?status=all"
        };
        foreach (var route in repeatRoutes)
        {
            var page = await _webRouteRenderer.RenderAsync(route, request, ct)
                ?? throw new InvalidOperationException($"Ops web smoke failed: repeat navigation route '{route}' did not resolve.");
            if (string.IsNullOrWhiteSpace(page.Html) || !page.Html.Contains("<h1>", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Ops web smoke failed: repeat navigation route '{route}' rendered empty content.");
            }
        }

        var candidatesPage = await _webRouteRenderer.RenderAsync("/ops-ab-candidates?target=24", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /ops-ab-candidates route did not resolve.");
        if (string.IsNullOrWhiteSpace(candidatesPage.Html)
            || !candidatesPage.Html.Contains("Ops A/B Scenario Candidates", StringComparison.OrdinalIgnoreCase)
            || !candidatesPage.Html.Contains("candidate_id=", StringComparison.OrdinalIgnoreCase)
            || !candidatesPage.Html.Contains("source_artifacts:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /ops-ab-candidates route did not render candidate pool shape.");
        }

        var candidatesJsonPage = await _webRouteRenderer.RenderAsync("/ops-ab-candidates?target=24&format=json", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: json /ops-ab-candidates route did not resolve.");
        if (string.IsNullOrWhiteSpace(candidatesJsonPage.Html)
            || !candidatesJsonPage.Html.Contains("candidateId", StringComparison.OrdinalIgnoreCase)
            || !candidatesJsonPage.Html.Contains("sourceArtifacts", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /ops-ab-candidates json output is not readable/stable.");
        }
    }

    private async Task SeedStage6ArtifactsAsync(long caseId, long chatId, Stage6CaseRecord stage6Case, CancellationToken ct)
    {
        var scopeKey = Stage6ArtifactTypes.ChatScope(chatId);
        var targetArtifactTypes = ParseJsonStringArray(stage6Case.TargetArtifactTypesJson);
        var artifactTypes = W2ArtifactTypes
            .Concat(targetArtifactTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var artifactType in artifactTypes)
        {
            _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
            {
                ArtifactType = artifactType,
                CaseId = caseId,
                ChatId = chatId,
                ScopeKey = scopeKey,
                PayloadObjectType = "ops_web_smoke",
                PayloadObjectId = stage6Case.Id.ToString(),
                PayloadJson = $"{{\"artifactType\":\"{artifactType}\",\"stage6CaseId\":\"{stage6Case.Id:D}\"}}",
                FreshnessBasisHash = $"ops_web_smoke:{artifactType}",
                FreshnessBasisJson = "{\"source\":\"ops_web_smoke\"}",
                GeneratedAt = DateTime.UtcNow,
                RefreshedAt = DateTime.UtcNow,
                StaleAt = DateTime.UtcNow.AddHours(2),
                IsStale = false,
                SourceType = "smoke",
                SourceId = "ops_web",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }, ct);
        }
    }

    private static List<string> ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var rows = JsonSerializer.Deserialize<List<string>>(json);
            return rows?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task SeedScenarioMinerArtifactsAsync(long caseId, long chatId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var baseTg = 9_710_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var messages = new List<Message>();
        for (var i = 0; i < 180; i++)
        {
            var ts = now.AddDays(-36).AddHours(i * 4);
            var text = i % 11 == 0
                ? "Need to keep this low pressure and avoid over-reading intent."
                : (i % 7 == 0 ? "Let's coordinate timing and logistics for next week." : "Warm exchange with some ambiguity around next move.");
            messages.Add(new Message
            {
                TelegramMessageId = baseTg + i,
                ChatId = chatId,
                SenderId = i % 2 == 0 ? 1001 : 1002,
                SenderName = i % 2 == 0 ? "Self" : "Other",
                Timestamp = ts,
                Text = text,
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = now
            });
        }

        _ = await _messageRepository.SaveBatchAsync(messages, ct);

        var sessionBase = (int)(Math.Abs(now.Ticks) % 1_000_000);
        for (var i = 0; i < 8; i++)
        {
            await _chatSessionRepository.UpsertAsync(new ChatSession
            {
                ChatId = chatId,
                SessionIndex = sessionBase + i,
                StartDate = now.AddDays(-36 + (i * 4)),
                EndDate = now.AddDays(-34 + (i * 4)),
                LastMessageAt = now.AddDays(-34 + (i * 4)),
                Summary = i % 3 == 0 ? "ambiguous warming" : (i % 2 == 0 ? "strategy-sensitive exchange" : "logistics-heavy"),
                IsFinalized = true,
                IsAnalyzed = true
            }, ct);
        }

        var createdPeriods = new List<Period>();
        for (var i = 0; i < 5; i++)
        {
            var period = await _periodRepository.CreatePeriodAsync(new Period
            {
                CaseId = caseId,
                ChatId = chatId,
                Label = i switch
                {
                    0 => "warming",
                    1 => "ambiguous",
                    2 => "fragile",
                    3 => "cooling",
                    _ => "logistics"
                },
                StartAt = now.AddDays(-35 + (i * 6)),
                EndAt = now.AddDays(-30 + (i * 6)),
                IsOpen = i == 4,
                Summary = i % 2 == 0
                    ? "Signal-rich period with uncertainty and need for careful calibration."
                    : "Period with logistics-heavy messages and sparse emotional clarity.",
                StatusSnapshot = i % 2 == 0 ? "ambiguous" : "stable",
                DynamicSnapshot = i % 2 == 0 ? "fragile" : "cooling",
                ReviewPriority = (short)(3 + i),
                SourceType = "smoke",
                SourceId = "ops_ab_candidates"
            }, ct);
            createdPeriods.Add(period);

            _ = await _stateProfileRepository.CreateStateSnapshotAsync(new StateSnapshot
            {
                CaseId = caseId,
                ChatId = chatId,
                PeriodId = period.Id,
                AsOf = period.EndAt ?? period.UpdatedAt,
                DynamicLabel = i % 2 == 0 ? "fragile" : "ambiguous",
                RelationshipStatus = i % 2 == 0 ? "uncertain_warming" : "mixed_signal",
                AmbiguityScore = 0.62f,
                ExternalPressureScore = 0.55f + (i * 0.05f),
                Confidence = 0.58f,
                InitiativeScore = 0.45f,
                ResponsivenessScore = 0.49f,
                OpennessScore = 0.42f,
                WarmthScore = 0.48f,
                ReciprocityScore = 0.47f,
                AvoidanceRiskScore = 0.41f,
                EscalationReadinessScore = 0.36f,
                SourceMessageId = null,
                SourceSessionId = null
            }, ct);
        }

        for (var i = 0; i < createdPeriods.Count - 1; i++)
        {
            _ = await _periodRepository.CreateTransitionAsync(new PeriodTransition
            {
                FromPeriodId = createdPeriods[i].Id,
                ToPeriodId = createdPeriods[i + 1].Id,
                TransitionType = i % 2 == 0 ? "warming_to_ambiguous" : "ambiguity_shift",
                Summary = "Transition seeded for A/B candidate miner smoke.",
                IsResolved = i % 2 == 1,
                Confidence = 0.66f,
                SourceType = "smoke",
                SourceId = "ops_ab_candidates"
            }, ct);
        }

        foreach (var period in createdPeriods.Take(4))
        {
            var strategy = await _strategyDraftRepository.CreateStrategyRecordAsync(new StrategyRecord
            {
                CaseId = caseId,
                ChatId = chatId,
                PeriodId = period.Id,
                StrategyConfidence = 0.61f,
                RecommendedGoal = "maintain low-pressure contact",
                WhyNotOthers = "Avoid escalation due to ambiguity and pressure risk.",
                MicroStep = "Send short warm check-in and wait for reciprocity.",
                SourceMessageId = null,
                SourceSessionId = null
            }, ct);

            var draft = await _strategyDraftRepository.CreateDraftRecordAsync(new DraftRecord
            {
                StrategyRecordId = strategy.Id,
                MainDraft = "Hey, no rush from my side, just checking in warmly.",
                AltDraft1 = "If timing works, we can keep this easy and short.",
                AltDraft2 = "No pressure, I appreciate staying in touch.",
                StyleNotes = "brief warm low-pressure",
                Confidence = 0.62f
            }, ct);

            _ = await _strategyDraftRepository.CreateDraftOutcomeAsync(new DraftOutcome
            {
                DraftId = draft.Id,
                StrategyRecordId = strategy.Id,
                OutcomeLabel = "mixed",
                UserOutcomeLabel = "unclear",
                SystemOutcomeLabel = "neutral",
                OutcomeConfidence = 0.55f,
                MatchScore = 0.73f,
                MatchedBy = "smoke_seed",
                Notes = "seeded for miner chain artifacts"
            }, ct);
        }

        foreach (var period in createdPeriods.Take(3))
        {
            _ = await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
            {
                CaseId = caseId,
                ChatId = chatId,
                PeriodId = period.Id,
                TimestampStart = period.StartAt.AddHours(5),
                TimestampEnd = period.StartAt.AddHours(7),
                EventType = "meeting",
                Title = "Short in-person meetup",
                UserSummary = "Context adds ambiguity and pacing considerations.",
                EvidenceRefsJson = "[\"offline:smoke-meetup\"]",
                SourceType = "smoke",
                SourceId = "ops_ab_candidates"
            }, ct);
        }
    }
}

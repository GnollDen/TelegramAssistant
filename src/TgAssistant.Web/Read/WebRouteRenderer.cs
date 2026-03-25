using System.Net;
using System.Text;
using System.Text.Json;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebRouteRenderer : IWebRouteRenderer
{
    public static readonly string[] DefaultRoutes =
    [
        "/dashboard",
        "/search",
        "/dossier",
        "/queue",
        "/inbox",
        "/history",
        "/state",
        "/timeline",
        "/network",
        "/profiles",
        "/clarifications",
        "/strategy",
        "/drafts-reviews",
        "/outcomes",
        "/offline-events",
        "/review",
        "/ops-budget",
        "/ops-eval",
        "/ops-ab-candidates"
    ];

    private readonly IWebReadService _webReadService;
    private readonly IWebReviewService _webReviewService;
    private readonly IWebOpsService _webOpsService;
    private readonly IWebSearchService _webSearchService;

    public WebRouteRenderer(
        IWebReadService webReadService,
        IWebReviewService webReviewService,
        IWebOpsService webOpsService,
        IWebSearchService webSearchService)
    {
        _webReadService = webReadService;
        _webReviewService = webReviewService;
        _webOpsService = webOpsService;
        _webSearchService = webSearchService;
    }

    public IReadOnlyList<string> Routes => DefaultRoutes;

    public async Task<WebRenderResult?> RenderAsync(string route, WebReadRequest request, CancellationToken ct = default)
    {
        var (path, query) = ParseRoute(route);
        return path switch
        {
            "/dashboard" => new WebRenderResult
            {
                Route = path,
                Title = "Dashboard",
                Html = RenderDashboard(
                    await _webReadService.GetDashboardAsync(request, ct),
                    await _webOpsService.GetRecentChangesAsync(request, 6, ct),
                    await _webOpsService.GetBudgetOperationalStateAsync(ct),
                    await _webOpsService.GetEvalRunsAsync(limit: 3, ct: ct))
            },
            "/search" => new WebRenderResult { Route = path, Title = "Search", Html = RenderSearch(await ExecuteSearchAsync(request, query, ct)) },
            "/dossier" => new WebRenderResult { Route = path, Title = "Dossier", Html = RenderDossier(await _webSearchService.GetDossierAsync(request, 30, ct)) },
            "/view/blocking" => new WebRenderResult { Route = path, Title = "Saved View: Blocking", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "blocking", 40, ct)) },
            "/view/current-period" => new WebRenderResult { Route = path, Title = "Saved View: Current Period", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "current-period", 10, ct)) },
            "/view/conflicts" => new WebRenderResult { Route = path, Title = "Saved View: Conflicts", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "conflicts", 40, ct)) },
            "/queue" => new WebRenderResult { Route = path, Title = "Case Queue", Html = RenderCaseQueue(await ExecuteCaseQueueReadAsync(request, query, ct), request) },
            "/inbox" => new WebRenderResult { Route = path, Title = "Case Queue", Html = RenderCaseQueue(await ExecuteCaseQueueReadAsync(request, query, ct), request) },
            "/case-detail" => new WebRenderResult { Route = path, Title = "Case Detail", Html = RenderCaseDetail(await ExecuteCaseDetailReadAsync(request, query, ct), request) },
            "/artifact-detail" => new WebRenderResult { Route = path, Title = "Artifact Detail", Html = RenderArtifactDetail(await ExecuteArtifactDetailReadAsync(request, query, ct), request) },
            "/case-action" => new WebRenderResult { Route = path, Title = "Case Action", Html = RenderCaseAction(await ExecuteCaseActionAsync(request, query, ct), request) },
            "/clarification-answer" => new WebRenderResult { Route = path, Title = "Clarification Answer", Html = RenderClarificationAnswer(await ExecuteClarificationAnswerAsync(request, query, ct), request) },
            "/artifact-action" => new WebRenderResult { Route = path, Title = "Artifact Action", Html = RenderArtifactAction(await ExecuteArtifactActionAsync(request, query, ct), request) },
            "/history" => new WebRenderResult { Route = path, Title = "History", Html = RenderHistory(await ExecuteHistoryReadAsync(request, query, ct)) },
            "/history-object" => new WebRenderResult { Route = path, Title = "Object History", Html = RenderObjectHistory(await ExecuteObjectHistoryReadAsync(request, query, ct), request) },
            "/state" => new WebRenderResult { Route = path, Title = "Current State", Html = RenderState(await _webReadService.GetCurrentStateAsync(request, ct)) },
            "/timeline" => new WebRenderResult { Route = path, Title = "Timeline", Html = RenderTimeline(await _webReadService.GetTimelineAsync(request, ct)) },
            "/network" => new WebRenderResult { Route = path, Title = "Network", Html = RenderNetwork(await _webReadService.GetNetworkAsync(request, ct), query) },
            "/profiles" => new WebRenderResult { Route = path, Title = "Profiles", Html = RenderProfiles(await _webReadService.GetProfilesAsync(request, ct)) },
            "/clarifications" => new WebRenderResult { Route = path, Title = "Clarifications", Html = RenderClarifications(await _webReadService.GetClarificationsAsync(request, ct)) },
            "/strategy" => new WebRenderResult { Route = path, Title = "Strategy", Html = RenderStrategy(await _webReadService.GetStrategyAsync(request, ct)) },
            "/drafts-reviews" => new WebRenderResult { Route = path, Title = "Drafts / Reviews", Html = RenderDraftsReviews(await _webReadService.GetDraftsReviewsAsync(request, ct)) },
            "/outcomes" => new WebRenderResult { Route = path, Title = "Outcome Trail", Html = RenderOutcomeTrail(await ExecuteOutcomeTrailReadAsync(request, query, ct)) },
            "/offline-events" => new WebRenderResult { Route = path, Title = "Offline Events", Html = RenderOfflineEvents(await _webReadService.GetOfflineEventsAsync(request, ct)) },
            "/review" => new WebRenderResult { Route = path, Title = "Review", Html = RenderReviewBoard(await _webReviewService.GetBoardAsync(request, ct), request) },
            "/review-action" => new WebRenderResult { Route = path, Title = "Review Action", Html = RenderReviewAction(await ExecuteReviewActionAsync(request, query, ct), request) },
            "/review-edit-period" => new WebRenderResult { Route = path, Title = "Edit Period", Html = RenderReviewAction(await ExecutePeriodEditAsync(request, query, ct), request) },
            "/ops-budget" => new WebRenderResult { Route = path, Title = "Ops Budget", Html = RenderOpsBudget(await ExecuteOpsBudgetReadAsync(query, ct), query) },
            "/ops-eval" => new WebRenderResult { Route = path, Title = "Ops Eval", Html = RenderOpsEval(await ExecuteOpsEvalReadAsync(query, ct), query) },
            "/ops-ab-candidates" => new WebRenderResult { Route = path, Title = "Ops A/B Candidates", Html = RenderOpsAbCandidates(await ExecuteOpsAbCandidatesReadAsync(request, query, ct), query) },
            _ => null
        };
    }

    private async Task<SearchReadModel> ExecuteSearchAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var limit = 120;
        if (int.TryParse(GetQuery(query, "limit"), out var parsedLimit))
        {
            limit = Math.Clamp(parsedLimit, 10, 500);
        }

        return await _webSearchService.SearchAsync(
            request,
            query: EmptyToNull(GetQuery(query, "q")),
            objectType: EmptyToNull(GetQuery(query, "objectType")),
            status: EmptyToNull(GetQuery(query, "status")),
            priority: EmptyToNull(GetQuery(query, "priority")),
            limit: limit,
            ct: ct);
    }

    private async Task<Stage6CaseQueueReadModel> ExecuteCaseQueueReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        return await _webOpsService.GetCaseQueueAsync(
            request,
            status: EmptyToNull(GetQuery(query, "status")) ?? "active",
            priority: EmptyToNull(GetQuery(query, "priority")),
            caseType: EmptyToNull(GetQuery(query, "caseType")),
            artifactType: EmptyToNull(GetQuery(query, "artifactType")),
            query: EmptyToNull(GetQuery(query, "q")),
            ct: ct);
    }

    private async Task<Stage6CaseDetailReadModel> ExecuteCaseDetailReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (!Guid.TryParse(GetQuery(query, "caseId"), out var caseId))
        {
            return new Stage6CaseDetailReadModel
            {
                Exists = false,
                ReasonSummary = "Missing or invalid caseId."
            };
        }

        return await _webOpsService.GetCaseDetailAsync(request, caseId, ct);
    }

    private async Task<Stage6ArtifactDetailReadModel> ExecuteArtifactDetailReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var artifactType = EmptyToNull(GetQuery(query, "artifactType")) ?? Stage6ArtifactTypes.ClarificationState;
        return await _webOpsService.GetArtifactDetailAsync(request, artifactType, ct);
    }

    private async Task<WebStage6CaseActionResult> ExecuteCaseActionAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (!Guid.TryParse(GetQuery(query, "caseId"), out var caseId))
        {
            return new WebStage6CaseActionResult
            {
                Success = false,
                Stage6CaseId = Guid.Empty,
                Action = EmptyToNull(GetQuery(query, "action")) ?? "unknown",
                Message = "Missing or invalid caseId."
            };
        }

        return await _webOpsService.ApplyCaseActionAsync(new WebStage6CaseActionRequest
        {
            ScopeCaseId = request.CaseId,
            ChatId = request.ChatId,
            Stage6CaseId = caseId,
            Action = EmptyToNull(GetQuery(query, "action")) ?? string.Empty,
            Actor = string.IsNullOrWhiteSpace(GetQuery(query, "actor")) ? request.Actor : GetQuery(query, "actor"),
            Reason = EmptyToNull(GetQuery(query, "reason")),
            Note = EmptyToNull(GetQuery(query, "note")),
            FeedbackKind = EmptyToNull(GetQuery(query, "feedbackKind")),
            FeedbackDimension = EmptyToNull(GetQuery(query, "feedbackDimension")),
            IsUseful = ParseNullableBoolean(GetQuery(query, "useful"))
        }, ct);
    }

    private async Task<WebStage6ClarificationAnswerResult> ExecuteClarificationAnswerAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (!Guid.TryParse(GetQuery(query, "caseId"), out var caseId))
        {
            return new WebStage6ClarificationAnswerResult
            {
                Success = false,
                Stage6CaseId = Guid.Empty,
                Message = "Missing or invalid caseId."
            };
        }

        return await _webOpsService.ApplyClarificationAnswerAsync(new WebStage6ClarificationAnswerRequest
        {
            ScopeCaseId = request.CaseId,
            ChatId = request.ChatId,
            Stage6CaseId = caseId,
            AnswerType = EmptyToNull(GetQuery(query, "answerType")) ?? "text",
            AnswerValue = EmptyToNull(GetQuery(query, "answer")) ?? string.Empty,
            MarkResolved = !string.Equals(EmptyToNull(GetQuery(query, "markResolved")), "false", StringComparison.OrdinalIgnoreCase),
            Actor = string.IsNullOrWhiteSpace(GetQuery(query, "actor")) ? request.Actor : GetQuery(query, "actor"),
            Reason = EmptyToNull(GetQuery(query, "reason")),
            IsUseful = ParseNullableBoolean(GetQuery(query, "useful")),
            FeedbackDimension = EmptyToNull(GetQuery(query, "feedbackDimension"))
        }, ct);
    }

    private async Task<WebStage6ArtifactActionResult> ExecuteArtifactActionAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        return await _webOpsService.ApplyArtifactActionAsync(new WebStage6ArtifactActionRequest
        {
            ScopeCaseId = request.CaseId,
            ChatId = request.ChatId,
            ArtifactType = EmptyToNull(GetQuery(query, "artifactType")) ?? string.Empty,
            Action = EmptyToNull(GetQuery(query, "action")) ?? string.Empty,
            Actor = string.IsNullOrWhiteSpace(GetQuery(query, "actor")) ? request.Actor : GetQuery(query, "actor"),
            Reason = EmptyToNull(GetQuery(query, "reason"))
        }, ct);
    }

    private async Task<HistoryReadModel> ExecuteHistoryReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var limit = 40;
        if (int.TryParse(GetQuery(query, "limit"), out var parsedLimit))
        {
            limit = Math.Clamp(parsedLimit, 5, 200);
        }

        return await _webOpsService.GetHistoryAsync(
            request,
            objectType: EmptyToNull(GetQuery(query, "objectType")),
            action: EmptyToNull(GetQuery(query, "action")),
            limit: limit,
            ct: ct);
    }

    private async Task<ObjectHistoryReadModel> ExecuteObjectHistoryReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var objectType = GetQuery(query, "objectType");
        var objectId = GetQuery(query, "objectId");
        if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(objectId))
        {
            return new ObjectHistoryReadModel
            {
                ObjectType = objectType,
                ObjectId = objectId,
                ObjectSummary = "Missing objectType/objectId."
            };
        }

        return await _webOpsService.GetObjectHistoryAsync(request, objectType, objectId, 40, ct);
    }

    private async Task<WebReviewActionResult> ExecuteReviewActionAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        return await _webReviewService.ApplyActionAsync(new WebReviewActionRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ObjectType = GetQuery(query, "objectType"),
            ObjectId = GetQuery(query, "objectId"),
            Action = GetQuery(query, "action"),
            Actor = string.IsNullOrWhiteSpace(GetQuery(query, "actor")) ? request.Actor : GetQuery(query, "actor"),
            Reason = EmptyToNull(GetQuery(query, "reason"))
        }, ct);
    }

    private async Task<WebReviewActionResult> ExecutePeriodEditAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var periodIdText = GetQuery(query, "periodId");
        if (!Guid.TryParse(periodIdText, out var periodId))
        {
            return new WebReviewActionResult
            {
                Success = false,
                ObjectType = "period",
                ObjectId = periodIdText,
                Action = "edit",
                Message = "Invalid periodId."
            };
        }

        short? reviewPriority = null;
        if (short.TryParse(GetQuery(query, "reviewPriority"), out var parsedPriority))
        {
            reviewPriority = parsedPriority;
        }

        bool? isOpen = null;
        if (bool.TryParse(GetQuery(query, "isOpen"), out var parsedIsOpen))
        {
            isOpen = parsedIsOpen;
        }

        return await _webReviewService.EditPeriodAsync(new WebPeriodEditRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            PeriodId = periodId,
            Label = EmptyToNull(GetQuery(query, "label")),
            Summary = EmptyToNull(GetQuery(query, "summary")),
            ReviewPriority = reviewPriority,
            IsOpen = isOpen,
            Actor = string.IsNullOrWhiteSpace(GetQuery(query, "actor")) ? request.Actor : GetQuery(query, "actor"),
            Reason = EmptyToNull(GetQuery(query, "reason"))
        }, ct);
    }

    private async Task<EvalRunsReadModel> ExecuteOpsEvalReadAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var limit = 10;
        if (int.TryParse(GetQuery(query, "limit"), out var parsedLimit))
        {
            limit = Math.Clamp(parsedLimit, 1, 30);
        }

        Guid? runId = null;
        var runIdRaw = EmptyToNull(GetQuery(query, "runId"));
        if (!string.IsNullOrWhiteSpace(runIdRaw) && Guid.TryParse(runIdRaw, out var parsedRunId))
        {
            runId = parsedRunId;
        }

        return await _webOpsService.GetEvalRunsAsync(
            runName: EmptyToNull(GetQuery(query, "runName")),
            runId: runId,
            limit: limit,
            ct: ct);
    }

    private async Task<BudgetOperationalReadModel> ExecuteOpsBudgetReadAsync(IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var model = await _webOpsService.GetBudgetOperationalStateAsync(ct);
        var stateFilter = EmptyToNull(GetQuery(query, "state"));
        var pathFilter = EmptyToNull(GetQuery(query, "path"));
        if (string.IsNullOrWhiteSpace(stateFilter) && string.IsNullOrWhiteSpace(pathFilter))
        {
            return model;
        }

        var filtered = model.States
            .Where(x => MatchesBudgetStateFilter(x, stateFilter))
            .Where(x => string.IsNullOrWhiteSpace(pathFilter)
                        || x.PathKey.Contains(pathFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return BuildBudgetAggregate(filtered, model.GeneratedAtUtc);
    }

    private async Task<AbScenarioCandidatePoolReadModel> ExecuteOpsAbCandidatesReadAsync(
        WebReadRequest request,
        IReadOnlyDictionary<string, string> query,
        CancellationToken ct)
    {
        var target = 30;
        if (int.TryParse(GetQuery(query, "target"), out var parsedTarget))
        {
            target = Math.Clamp(parsedTarget, 20, 40);
        }

        return await _webOpsService.GetAbScenarioCandidatesAsync(
            request,
            targetCount: target,
            bucket: EmptyToNull(GetQuery(query, "bucket")),
            ct: ct);
    }

    private async Task<OutcomeTrailReadModel> ExecuteOutcomeTrailReadAsync(
        WebReadRequest request,
        IReadOnlyDictionary<string, string> query,
        CancellationToken ct)
    {
        var model = await _webReadService.GetOutcomeTrailAsync(request, ct);
        var strategyRecordId = ParseGuidQuery(query, "strategyRecordId");
        var draftId = ParseGuidQuery(query, "draftId");
        var outcomeId = ParseGuidQuery(query, "outcomeId");

        if (!strategyRecordId.HasValue && !draftId.HasValue && !outcomeId.HasValue)
        {
            return model;
        }

        var filteredItems = model.Items
            .Where(x => !strategyRecordId.HasValue || x.StrategyRecordId == strategyRecordId.Value)
            .Where(x => !draftId.HasValue || x.DraftId == draftId.Value)
            .Where(x => !outcomeId.HasValue || x.OutcomeId == outcomeId.Value)
            .ToList();

        return new OutcomeTrailReadModel
        {
            TotalOutcomesScanned = model.TotalOutcomesScanned,
            MissingDraftCount = model.MissingDraftCount,
            MissingStrategyCount = model.MissingStrategyCount,
            Items = filteredItems
        };
    }

    private static string RenderDashboard(
        DashboardReadModel model,
        RecentChangesReadModel recentChanges,
        BudgetOperationalReadModel budgetModel,
        EvalRunsReadModel evalModel)
    {
        var sb = CreateShell("Dashboard");
        sb.AppendLine("<h1>Dashboard</h1>");
        sb.AppendLine($"<section><h2>Current State</h2><p><strong>{E(model.CurrentState.DynamicLabel)}</strong> / {E(model.CurrentState.RelationshipStatus)} (conf {model.CurrentState.Confidence:0.00})</p></section>");
        sb.AppendLine($"<section><h2>Next Step</h2><p>{E(model.Strategy.PrimarySummary)}</p><p>micro-step: {E(model.Strategy.MicroStep)}</p></section>");
        sb.AppendLine($"<section><h2>Open Clarifications</h2><p>open: {model.Clarifications.OpenCount}</p></section>");
        sb.AppendLine("<section><h2>Ops Visibility</h2>");
        sb.AppendLine($"<p>budget health: <strong>{E(budgetModel.OperationalStatus)}</strong></p>");
        sb.AppendLine($"<p>budget paths: total={budgetModel.TotalPaths}, active={budgetModel.ActivePaths}, degraded={budgetModel.DegradedPaths}, paused={budgetModel.PausedPaths}, quota-blocked={budgetModel.QuotaBlockedPaths}</p>");
        sb.AppendLine($"<p>eval health: <strong>{E(evalModel.OperationalStatus)}</strong></p>");
        sb.AppendLine($"<p>latest eval runs: total={evalModel.TotalRuns}, passed={evalModel.PassedRuns}, failed={evalModel.FailedRuns}, comparable-series={evalModel.Comparisons.Count}</p>");
        sb.AppendLine("<p><a href='/ops-budget'>Open budget view</a> | <a href='/ops-budget?state=quota_blocked'>Quota-blocked paths</a> | <a href='/ops-budget?state=hard_paused'>Paused paths</a> | <a href='/ops-eval'>Open eval view</a> | <a href='/ops-eval?status=failed'>Failed eval runs</a> | <a href='/ops-eval?compare=1'>Run comparisons</a> | <a href='/ops-ab-candidates'>A/B scenario candidates</a></p>");
        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Recent Changes</h2>");
        foreach (var evt in recentChanges.Items.Take(5))
        {
            var trail = $"/history-object?objectType={UrlEncode(evt.ObjectType)}&objectId={UrlEncode(evt.ObjectId)}";
            sb.AppendLine($"<div>{E(evt.TimestampLabel)} | {E(evt.ObjectType)} | {E(evt.Action)} | <a href='{E(trail)}'>trail</a></div>");
        }

        sb.AppendLine("</section>");
        return CloseShell(sb);
    }

    private static string RenderSearch(SearchReadModel model)
    {
        var sb = CreateShell("Search");
        sb.AppendLine("<h1>Search</h1>");
        sb.AppendLine($"<p>query={E(model.Query)} objectType={E(model.ObjectTypeFilter ?? "-")} status={E(model.StatusFilter ?? "-")} priority={E(model.PriorityFilter ?? "-")} count={model.Results.Count}</p>");
        foreach (var result in model.Results)
        {
            sb.AppendLine("<article>");
            sb.AppendLine($"<p><strong>{E(result.ObjectType)}</strong>:{E(result.ObjectId)}</p>");
            sb.AppendLine($"<p>{E(result.Title)}</p>");
            sb.AppendLine($"<p>{E(result.Summary)}</p>");
            sb.AppendLine($"<p>status={E(result.Status ?? "-")} priority={E(result.Priority ?? "-")}</p>");
            sb.AppendLine($"<p><a href='{E(result.Link)}'>open</a></p>");
            sb.AppendLine("</article>");
        }

        return CloseShell(sb);
    }

    private static string RenderSavedView(SavedViewReadModel model)
    {
        var sb = CreateShell(model.Title);
        sb.AppendLine($"<h1>{E(model.Title)}</h1>");
        sb.AppendLine($"<p>{E(model.Description)}</p>");
        foreach (var item in model.Items)
        {
            sb.AppendLine($"<div>{E(item.ObjectType)} | {E(item.Title)} | status={E(item.Status ?? "-")} | <a href='{E(item.Link)}'>open</a></div>");
        }

        return CloseShell(sb);
    }

    private static string RenderDossier(DossierReadModel model)
    {
        var sb = CreateShell("Dossier");
        sb.AppendLine("<h1>Dossier</h1>");
        if (!string.IsNullOrWhiteSpace(model.Summary))
        {
            sb.AppendLine($"<p>summary: {E(model.Summary)}</p>");
        }

        RenderDossierInsightSection(sb, "Observed Facts", model.ObservedFacts);
        RenderDossierInsightSection(sb, "Relationship Read", model.RelationshipRead);
        RenderDossierInsightSection(sb, "Notable Events", model.NotableEvents);
        RenderDossierInsightSection(sb, "Likely Interpretation", model.LikelyInterpretation);
        RenderDossierInsightSection(sb, "Uncertainties / Alternative Readings", model.Uncertainties);
        RenderDossierInsightSection(sb, "Missing Information", model.MissingInformation);
        RenderDossierInsightSection(sb, "Practical Interpretation", model.PracticalInterpretation);

        if (model.ObservedFacts.Count == 0
            && model.LikelyInterpretation.Count == 0
            && model.Uncertainties.Count == 0
            && model.MissingInformation.Count == 0)
        {
            RenderDossierSection(sb, "Confirmed", model.Confirmed);
            RenderDossierSection(sb, "Hypotheses", model.Hypotheses);
            RenderDossierSection(sb, "Conflicts", model.Conflicts);
        }

        return CloseShell(sb);
    }

    private static void RenderDossierInsightSection(StringBuilder sb, string title, IReadOnlyCollection<DossierInsightReadModel> rows)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var row in rows)
        {
            sb.AppendLine($"<div><strong>{E(row.SignalStrength)}</strong> | {E(row.Title)} | {E(row.Detail)} | evidence={E(row.Evidence)} | <a href='{E(row.Link)}'>open</a></div>");
        }

        if (rows.Count == 0)
        {
            sb.AppendLine("<p>No items.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static void RenderDossierSection(StringBuilder sb, string title, IReadOnlyCollection<DossierItemReadModel> rows)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var row in rows)
        {
            sb.AppendLine($"<div>{E(row.ObjectType)} | {E(row.Title)} | {E(row.Summary)} | <a href='{E(row.Link)}'>open</a></div>");
        }

        if (rows.Count == 0)
        {
            sb.AppendLine("<p>No items.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static string RenderCaseQueue(Stage6CaseQueueReadModel model, WebReadRequest request)
    {
        var sb = CreateShell("Case Queue");
        var activeLink = BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" });
        var needsInputLink = BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = Stage6CaseStatuses.NeedsUserInput });
        var readyLink = BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = Stage6CaseStatuses.Ready });
        var blockingLink = BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["priority"] = "blocking" });
        var allLink = BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "all" });

        sb.AppendLine("<h1>Stage 6 Queue</h1>");
        sb.AppendLine($"<p>visible={model.VisibleCases} of total={model.TotalCases} | needs-input={model.NeedsInputCases} | ready={model.ReadyCases} | stale={model.StaleCases} | resolved={model.ResolvedCases}</p>");
        sb.AppendLine($"<p>scope: case={request.CaseId}, chat={request.ChatId}</p>");
        sb.AppendLine("<section><h2>Filters</h2>");
        sb.AppendLine("<form method='get' action='/inbox'>");
        sb.AppendLine($"<input type='hidden' name='caseScopeId' value='{E(request.CaseId.ToString())}'>");
        sb.AppendLine($"<input type='hidden' name='chatId' value='{E(request.ChatId.ToString())}'>");
        sb.AppendLine($"<label>status <input name='status' value='{E(model.StatusFilter)}'></label> ");
        sb.AppendLine($"<label>priority <input name='priority' value='{E(model.PriorityFilter ?? string.Empty)}'></label> ");
        sb.AppendLine($"<label>caseType <input name='caseType' value='{E(model.CaseTypeFilter ?? string.Empty)}'></label> ");
        sb.AppendLine($"<label>artifactType <input name='artifactType' value='{E(model.ArtifactTypeFilter ?? string.Empty)}'></label> ");
        sb.AppendLine($"<label>q <input name='q' value='{E(model.Query ?? string.Empty)}'></label> ");
        sb.AppendLine("<button type='submit'>apply</button>");
        sb.AppendLine("</form>");
        sb.AppendLine($"<p>common filters: <a href='{E(activeLink)}'>active</a> | <a href='{E(needsInputLink)}'>needs input</a> | <a href='{E(readyLink)}'>ready</a> | <a href='{E(blockingLink)}'>blocking</a> | <a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.CurrentState }))}'>state work</a> | <a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Draft }))}'>draft work</a> | <a href='{E(allLink)}'>all</a></p>");
        sb.AppendLine($"<p>artifact views: <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Dossier }))}'>dossier</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.CurrentState }))}'>current_state</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Strategy }))}'>strategy</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Draft }))}'>draft</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Review }))}'>review</a></p>");
        sb.AppendLine("</section>");

        var topNeedsInput = model.Cases
            .Where(x => x.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .ToList();
        var topReady = model.Cases
            .Where(x => x.Status.Equals(Stage6CaseStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                        || x.Status.Equals(Stage6CaseStatuses.New, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .ToList();
        var topOther = model.Cases
            .Where(x => !x.Status.Equals(Stage6CaseStatuses.NeedsUserInput, StringComparison.OrdinalIgnoreCase)
                        && !x.Status.Equals(Stage6CaseStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                        && !x.Status.Equals(Stage6CaseStatuses.New, StringComparison.OrdinalIgnoreCase))
            .Take(30)
            .ToList();

        RenderQueueSection(sb, "Needs Input", topNeedsInput, request);
        RenderQueueSection(sb, "Ready / New", topReady, request);
        RenderQueueSection(sb, "Other", topOther, request);

        if (model.Cases.Count == 0)
        {
            sb.AppendLine("<p>No Stage 6 cases matched the current filter.</p>");
        }

        return CloseShell(sb);
    }

    private static string RenderCaseDetail(Stage6CaseDetailReadModel model, WebReadRequest request)
    {
        var sb = CreateShell("Case Detail");
        sb.AppendLine("<h1>Case Detail</h1>");
        if (!model.Exists)
        {
            sb.AppendLine($"<p>{E(model.ReasonSummary)}</p>");
            return CloseShell(sb);
        }

        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>back to queue</a></p>");
        sb.AppendLine($"<p>id={model.Id}</p>");
        sb.AppendLine($"<p>type={E(model.CaseType)} subtype={E(model.CaseSubtype ?? "-")} status={E(model.Status)} priority={E(model.Priority)} conf={FormatConfidence(model.Confidence)}</p>");
        sb.AppendLine($"<p>reason: {E(model.ReasonSummary)}</p>");
        if (!string.IsNullOrWhiteSpace(model.QuestionText))
        {
            sb.AppendLine($"<p>question: {E(model.QuestionText)}</p>");
        }
        sb.AppendLine($"<p>response: mode={E(model.ResponseMode ?? "-")} channel_hint={E(model.ResponseChannelHint ?? "-")}</p>");
        sb.AppendLine($"<p>source: {E(model.SourceObjectType)}:{E(model.SourceObjectId)} {(!string.IsNullOrWhiteSpace(model.SourceLink) ? $"<a href='{E(model.SourceLink)}'>trail</a>" : string.Empty)}</p>");
        if (!string.IsNullOrWhiteSpace(model.SourceSummary))
        {
            sb.AppendLine($"<p>source summary: {E(model.SourceSummary)}</p>");
        }
        sb.AppendLine($"<p>updated={E(model.UpdatedAt.ToString("u"))}</p>");
        sb.AppendLine($"<p>evidence-first summary: {(model.Evidence.Count == 0 ? E(model.ReasonSummary) : E(string.Join(" | ", model.Evidence.Take(4).Select(x => x.Summary))))}</p>");

        sb.AppendLine("<section><h2>Evidence First Context</h2>");
        foreach (var evidence in model.Evidence.Take(12))
        {
            var link = string.IsNullOrWhiteSpace(evidence.Link) ? string.Empty : $" | <a href='{E(evidence.Link)}'>open</a>";
            sb.AppendLine($"<div>{E(evidence.SourceClass)} | {E(evidence.Title)} | {E(evidence.Summary)} | ref={E(evidence.Reference)}{link}</div>");
        }
        if (model.Evidence.Count == 0)
        {
            sb.AppendLine("<p>No explicit evidence refs. Case reason is shown as fallback.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Artifacts</h2>");
        sb.AppendLine($"<p>quick views: <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Dossier }))}'>dossier</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.CurrentState }))}'>current_state</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Strategy }))}'>strategy</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Draft }))}'>draft</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Review }))}'>review</a></p>");
        foreach (var artifact in model.Artifacts)
        {
            var detailLink = BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?>
            {
                ["artifactType"] = artifact.ArtifactType
            });
            var refreshLink = BuildScopedPath("/artifact-action", request, new Dictionary<string, string?>
            {
                ["artifactType"] = artifact.ArtifactType,
                ["action"] = "refresh"
            });
            sb.AppendLine($"<div>{E(artifact.ArtifactType)} | status={E(artifact.Status)} | reason={E(artifact.Reason ?? "-")} | conf={E(artifact.ConfidenceLabel ?? "-")} | {E(artifact.Summary)} | <a href='{E(detailLink)}'>detail</a> | <a href='{E(refreshLink)}'>refresh</a></div>");
        }
        if (model.Artifacts.Count == 0)
        {
            sb.AppendLine("<p>No target artifacts for this case.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Context Notes</h2>");
        foreach (var entry in model.ContextEntries)
        {
            sb.AppendLine($"<div>{E(entry.SourceKind)} via {E(entry.EnteredVia)} | certainty={entry.UserReportedCertainty:0.00} | {E(entry.ContentText)}</div>");
        }
        if (model.ContextEntries.Count == 0)
        {
            sb.AppendLine("<p>No web or bot context notes linked yet.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Actions</h2>");
        var resolve = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = model.Id.ToString(),
            ["action"] = "resolve"
        });
        var reject = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = model.Id.ToString(),
            ["action"] = "reject"
        });
        var refresh = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = model.Id.ToString(),
            ["action"] = "refresh"
        });
        sb.AppendLine($"<p><a href='{E(resolve)}'>resolve</a> | <a href='{E(reject)}'>reject</a> | <a href='{E(refresh)}'>refresh</a></p>");
        sb.AppendLine("<form method='get' action='/case-action'>");
        sb.AppendLine($"<input type='hidden' name='caseScopeId' value='{E(request.CaseId.ToString())}'>");
        sb.AppendLine($"<input type='hidden' name='chatId' value='{E(request.ChatId.ToString())}'>");
        sb.AppendLine($"<input type='hidden' name='caseId' value='{E(model.Id.ToString())}'>");
        sb.AppendLine("<input type='hidden' name='action' value='annotate'>");
        sb.AppendLine("<label>annotation <input name='note' value=''></label> ");
        sb.AppendLine("<label>reason <input name='reason' value=''></label> ");
        sb.AppendLine("<button type='submit'>save note</button>");
        sb.AppendLine("</form>");
        if (model.Clarification != null)
        {
            sb.AppendLine("<h3>Clarification</h3>");
            sb.AppendLine($"<p>{E(model.Clarification.QuestionText)}</p>");
            sb.AppendLine($"<p>why it matters: {E(model.Clarification.WhyItMatters)}</p>");
            sb.AppendLine($"<p>question_type={E(model.Clarification.QuestionType)} priority={E(model.Clarification.Priority)} status={E(model.Clarification.Status)}</p>");
            if (model.Clarification.AnswerOptions.Count > 0)
            {
                sb.AppendLine($"<p>answer options: {E(string.Join(", ", model.Clarification.AnswerOptions))}</p>");
            }
            if (model.Clarification.Answers.Count > 0)
            {
                sb.AppendLine("<p>prior answers:</p>");
                foreach (var answer in model.Clarification.Answers)
                {
                    sb.AppendLine($"<div>{answer.CreatedAt:yyyy-MM-dd HH:mm} | conf={answer.AnswerConfidence:0.00} | {E(answer.AnswerValue)}</div>");
                }
            }
            sb.AppendLine("<form method='get' action='/clarification-answer'>");
            sb.AppendLine($"<input type='hidden' name='caseScopeId' value='{E(request.CaseId.ToString())}'>");
            sb.AppendLine($"<input type='hidden' name='chatId' value='{E(request.ChatId.ToString())}'>");
            sb.AppendLine($"<input type='hidden' name='caseId' value='{E(model.Id.ToString())}'>");
            sb.AppendLine("<label>answer <input name='answer' value=''></label> ");
            sb.AppendLine("<label>reason <input name='reason' value=''></label> ");
            sb.AppendLine("<label>useful <select name='useful'><option value='true'>true</option><option value='false'>false</option></select></label> ");
            sb.AppendLine("<button type='submit'>submit answer</button>");
            sb.AppendLine("</form>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Feedback</h2>");
        foreach (var item in model.Feedback.Take(20))
        {
            sb.AppendLine($"<div>{item.CreatedAt:yyyy-MM-dd HH:mm} | {E(item.FeedbackKind)} | dim={E(item.FeedbackDimension)} | useful={E(item.IsUseful?.ToString() ?? "-")} | {E(item.Note ?? "-")}</div>");
        }
        if (model.Feedback.Count == 0)
        {
            sb.AppendLine("<p>No case feedback recorded yet.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Outcomes</h2>");
        foreach (var item in model.Outcomes.Take(20))
        {
            sb.AppendLine($"<div>{item.CreatedAt:yyyy-MM-dd HH:mm} | {E(item.OutcomeType)} | status={E(item.CaseStatusAfter)} | user_context_material={item.UserContextMaterial} | {E(item.Note ?? "-")}</div>");
        }
        if (model.Outcomes.Count == 0)
        {
            sb.AppendLine("<p>No explicit case outcomes recorded yet.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>History</h2>");
        foreach (var evt in model.History.Take(20))
        {
            sb.AppendLine($"<div>{E(evt.TimestampLabel)} | {E(evt.Action)} | {E(evt.Summary)}</div>");
        }
        sb.AppendLine("</section>");
        return CloseShell(sb);
    }

    private static string RenderArtifactDetail(Stage6ArtifactDetailReadModel model, WebReadRequest request)
    {
        var sb = CreateShell("Artifact Detail");
        sb.AppendLine($"<h1>Artifact Detail: {E(model.ArtifactType)}</h1>");
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>back to queue</a></p>");
        sb.AppendLine($"<p>views: <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Dossier }))}'>dossier</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.CurrentState }))}'>current_state</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Strategy }))}'>strategy</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Draft }))}'>draft</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Review }))}'>review</a></p>");
        sb.AppendLine($"<p>status={E(model.Status)} reason={E(model.Reason ?? "-")}</p>");
        sb.AppendLine($"<p>summary={E(model.Summary)}</p>");
        sb.AppendLine($"<p>confidence={E(model.ConfidenceLabel ?? "-")} payload={E(model.PayloadObjectType ?? "-")}:{E(model.PayloadObjectId ?? "-")}</p>");
        sb.AppendLine($"<p>generated={E(model.GeneratedAt?.ToString("u") ?? "-")} refreshed={E(model.RefreshedAt?.ToString("u") ?? "-")} latest_evidence={E(model.LatestEvidenceAtUtc?.ToString("u") ?? "-")}</p>");
        var refreshLink = BuildScopedPath("/artifact-action", request, new Dictionary<string, string?>
        {
            ["artifactType"] = model.ArtifactType,
            ["action"] = "refresh"
        });
        sb.AppendLine($"<p><a href='{E(refreshLink)}'>refresh artifact</a></p>");
        sb.AppendLine("<section><h2>Evidence Summary</h2>");
        foreach (var evidence in model.Evidence.Take(12))
        {
            sb.AppendLine($"<div>{E(evidence.Title)} | {E(evidence.Summary)}</div>");
        }
        if (model.Evidence.Count == 0)
        {
            sb.AppendLine("<p>No evidence stamp details available.</p>");
        }
        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Linked Cases</h2>");
        foreach (var item in model.LinkedCases)
        {
            var link = BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
            {
                ["caseId"] = item.Id.ToString()
            });
            sb.AppendLine($"<div><a href='{E(link)}'>{item.Id}</a> | {E(item.CaseType)} | {E(item.Status)} | {E(item.ReasonSummary)}</div>");
        }
        if (model.LinkedCases.Count == 0)
        {
            sb.AppendLine("<p>No linked active cases.</p>");
        }
        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Feedback</h2>");
        foreach (var item in model.Feedback.Take(20))
        {
            sb.AppendLine($"<div>{item.CreatedAt:yyyy-MM-dd HH:mm} | {E(item.FeedbackKind)} | dim={E(item.FeedbackDimension)} | useful={E(item.IsUseful?.ToString() ?? "-")} | {E(item.Note ?? "-")}</div>");
        }
        if (model.Feedback.Count == 0)
        {
            sb.AppendLine("<p>No artifact feedback recorded yet.</p>");
        }
        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Payload</h2>");
        sb.AppendLine($"<pre>{E(model.PayloadJson)}</pre>");
        sb.AppendLine("</section>");
        return CloseShell(sb);
    }

    private static string RenderCaseAction(WebStage6CaseActionResult result, WebReadRequest request)
    {
        var sb = CreateShell("Case Action");
        sb.AppendLine("<h1>Case Action</h1>");
        sb.AppendLine($"<p>success={result.Success} action={E(result.Action)} case={result.Stage6CaseId}</p>");
        sb.AppendLine($"<p>{E(result.Message)}</p>");
        if (result.RefreshedArtifactTypes.Count > 0)
        {
            sb.AppendLine($"<p>refreshed artifacts: {E(string.Join(", ", result.RefreshedArtifactTypes))}</p>");
        }
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>open queue (active)</a> | <a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "all" }))}'>open queue (all)</a></p>");
        if (result.Stage6CaseId != Guid.Empty)
        {
            sb.AppendLine($"<p><a href='{E(BuildScopedPath("/case-detail", request, new Dictionary<string, string?> { ["caseId"] = result.Stage6CaseId.ToString() }))}'>back to case detail</a></p>");
        }
        return CloseShell(sb);
    }

    private static string RenderClarificationAnswer(WebStage6ClarificationAnswerResult result, WebReadRequest request)
    {
        var sb = CreateShell("Clarification Answer");
        sb.AppendLine("<h1>Clarification Answer</h1>");
        sb.AppendLine($"<p>success={result.Success} case={result.Stage6CaseId} question={E(result.QuestionId?.ToString() ?? "-")}</p>");
        sb.AppendLine($"<p>{E(result.Message)}</p>");
        if (!string.IsNullOrWhiteSpace(result.QuestionText))
        {
            sb.AppendLine($"<p>question: {E(result.QuestionText)}</p>");
            sb.AppendLine($"<p>answer: {E(result.AnswerValue)}</p>");
        }
        if (result.RecomputeTargets.Count > 0)
        {
            sb.AppendLine($"<p>recompute targets: {E(string.Join(", ", result.RecomputeTargets))}</p>");
        }
        if (result.RefreshedArtifactTypes.Count > 0)
        {
            sb.AppendLine($"<p>artifacts marked stale: {E(string.Join(", ", result.RefreshedArtifactTypes))}</p>");
        }
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = Stage6CaseStatuses.NeedsUserInput }))}'>open queue (needs input)</a> | <a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>open queue (active)</a></p>");
        if (result.Stage6CaseId != Guid.Empty)
        {
            sb.AppendLine($"<p><a href='{E(BuildScopedPath("/case-detail", request, new Dictionary<string, string?> { ["caseId"] = result.Stage6CaseId.ToString() }))}'>back to case detail</a></p>");
        }
        return CloseShell(sb);
    }

    private static string RenderArtifactAction(WebStage6ArtifactActionResult result, WebReadRequest request)
    {
        var sb = CreateShell("Artifact Action");
        sb.AppendLine("<h1>Artifact Action</h1>");
        sb.AppendLine($"<p>success={result.Success} action={E(result.Action)} artifact={E(result.ArtifactType)}</p>");
        sb.AppendLine($"<p>{E(result.Message)}</p>");
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>open queue (active)</a></p>");
        if (!string.IsNullOrWhiteSpace(result.ArtifactType))
        {
            sb.AppendLine($"<p><a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = result.ArtifactType }))}'>open artifact detail</a></p>");
        }
        return CloseShell(sb);
    }

    private static void RenderQueueSection(
        StringBuilder sb,
        string title,
        IReadOnlyCollection<Stage6CaseQueueItemReadModel> items,
        WebReadRequest request)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var item in items)
        {
            RenderQueueItemCard(sb, item, request);
        }

        if (items.Count == 0)
        {
            sb.AppendLine("<p>No cases in this section for current filters.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static void RenderQueueItemCard(StringBuilder sb, Stage6CaseQueueItemReadModel item, WebReadRequest request)
    {
        var objectTrail = BuildScopedPath("/history-object", request, new Dictionary<string, string?>
        {
            ["objectType"] = item.SourceObjectType,
            ["objectId"] = item.SourceObjectId
        });
        var detailLink = BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString()
        });
        var resolveLink = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["action"] = "resolve"
        });
        var rejectLink = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["action"] = "reject"
        });
        var refreshLink = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["action"] = "refresh"
        });

        sb.AppendLine("<article>");
        sb.AppendLine($"<h3><a href='{E(detailLink)}'>{E(item.CaseType)}</a> | type={E(item.CaseType)} | priority={E(item.Priority)} | status={E(item.Status)}</h3>");
        sb.AppendLine($"<p>reason: {E(item.ReasonSummary)}</p>");
        if (!string.IsNullOrWhiteSpace(item.QuestionText))
        {
            sb.AppendLine($"<p>question: {E(item.QuestionText)}</p>");
        }

        sb.AppendLine($"<p>confidence={FormatConfidence(item.Confidence)} evidence_refs={item.EvidenceCount} updated={E(item.UpdatedAt.ToString("u"))}</p>");
        sb.AppendLine($"<p>source=<a href='{E(objectTrail)}'>{E(item.SourceObjectType)}:{E(item.SourceObjectId)}</a></p>");
        sb.AppendLine($"<p>targets: {(item.TargetArtifactTypes.Count == 0 ? "-" : E(string.Join(", ", item.TargetArtifactTypes)))}</p>");

        var detailActions = new List<string>
        {
            $"<a href='{E(detailLink)}'>open case</a>",
            $"<a href='{E(resolveLink)}'>resolve</a>",
            $"<a href='{E(rejectLink)}'>reject</a>",
            $"<a href='{E(refreshLink)}'>refresh</a>"
        };
        if (item.TargetArtifactTypes.Count > 0)
        {
            var artifactLink = BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?>
            {
                ["artifactType"] = item.TargetArtifactTypes[0]
            });
            detailActions.Add($"<a href='{E(artifactLink)}'>open artifact</a>");
        }

        if (item.NeedsAnswer)
        {
            detailActions.Add("needs answer");
        }

        sb.AppendLine($"<p>{string.Join(" | ", detailActions)}</p>");
        sb.AppendLine("</article>");
    }

    private static string RenderInbox(InboxReadModel model, WebReadRequest request)
    {
        var sb = CreateShell("Inbox");
        sb.AppendLine("<h1>Inbox</h1>");
        RenderInboxGroup(sb, "Blocking", model.Blocking, request);
        RenderInboxGroup(sb, "High Impact", model.HighImpact, request);
        RenderInboxGroup(sb, "Everything Else", model.EverythingElse, request);
        return CloseShell(sb);
    }

    private static void RenderInboxGroup(StringBuilder sb, string title, IReadOnlyCollection<InboxItemReadModel> items, WebReadRequest request)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var item in items)
        {
            var trail = $"/history-object?objectType={UrlEncode(item.SourceObjectType)}&objectId={UrlEncode(item.SourceObjectId)}";
            var review = $"/review?case={request.CaseId}&chat={request.ChatId}&objectType={UrlEncode(item.SourceObjectType)}&objectId={UrlEncode(item.SourceObjectId)}";
            sb.AppendLine($"<div>{E(item.Priority)} | {E(item.Summary)} | <a href='{E(trail)}'>trail</a> | <a href='{E(review)}'>review</a></div>");
        }

        if (items.Count == 0)
        {
            sb.AppendLine("<p>No items.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static string RenderHistory(HistoryReadModel model)
    {
        var sb = CreateShell("History");
        sb.AppendLine("<h1>History / Activity</h1>");
        sb.AppendLine("<p><a href='/outcomes'>Outcome trail</a> | <a href='/ops-budget'>Budget visibility</a> | <a href='/ops-eval'>Eval visibility</a></p>");
        sb.AppendLine("<p><a href='/history?action=comparison_completed'>Experiment comparisons</a> | <a href='/history?objectType=draft_outcome'>Outcome events</a> | <a href='/history?objectType=inbox_item'>Inbox events</a></p>");
        foreach (var evt in model.Events)
        {
            var trailLink = $"/history-object?objectType={UrlEncode(evt.ObjectType)}&objectId={UrlEncode(evt.ObjectId)}";
            var chainLink = BuildOutcomeFocusLink(evt.ObjectType, evt.ObjectId);
            sb.AppendLine($"<div>{E(evt.TimestampLabel)} | {E(evt.ObjectType)} | {E(evt.Action)} | <a href='{E(trailLink)}'>trail</a>{RenderOptionalLink(chainLink, "outcome-chain")}</div>");
        }

        if (model.Events.Count == 0)
        {
            sb.AppendLine("<p>No history events yet for this filter.</p>");
        }

        return CloseShell(sb);
    }

    private static string RenderObjectHistory(ObjectHistoryReadModel model, WebReadRequest request)
    {
        var sb = CreateShell("Object History");
        sb.AppendLine("<h1>Object / History Trail</h1>");
        sb.AppendLine($"<p>object: {E(model.ObjectType)}:{E(model.ObjectId)}</p>");
        sb.AppendLine($"<p>summary: {E(model.ObjectSummary)}</p>");
        foreach (var evt in model.Events.OrderByDescending(x => x.CreatedAt))
        {
            sb.AppendLine($"<div>{E(evt.TimestampLabel)} | {E(evt.Action)} | {E(evt.Summary)}</div>");
        }

        if (TryBuildOutcomeTrailLink(model.ObjectType, out var linkedRoute))
        {
            var focused = BuildOutcomeFocusLink(model.ObjectType, model.ObjectId) ?? linkedRoute;
            sb.AppendLine($"<p><a href='{E(focused)}'>open linked outcome trail</a></p>");
        }

        if (model.Events.Count == 0)
        {
            sb.AppendLine("<p>No object history events found yet.</p>");
        }

        sb.AppendLine($"<p>case={request.CaseId}, chat={request.ChatId}</p>");
        return CloseShell(sb);
    }

    private static string RenderState(CurrentStateReadModel model)
    {
        var sb = CreateShell("Current State");
        sb.AppendLine("<h1>Current State</h1>");
        sb.AppendLine($"<p>dynamic: <strong>{E(model.DynamicLabel)}</strong></p>");
        sb.AppendLine($"<p>status: {E(model.RelationshipStatus)}{(string.IsNullOrWhiteSpace(model.AlternativeStatus) ? string.Empty : $" (alt {E(model.AlternativeStatus!)})")}</p>");
        sb.AppendLine($"<p>confidence: {model.Confidence:0.00}</p>");
        sb.AppendLine($"<p>overall signal strength: <strong>{E(model.OverallSignalStrength)}</strong></p>");
        RenderStateInsightSection(sb, "Observed Facts", model.ObservedFacts);
        RenderStateInsightSection(sb, "Likely Interpretation", model.LikelyInterpretation);
        RenderStateInsightSection(sb, "Uncertainties / Alternative Readings", model.Uncertainties);
        RenderStateInsightSection(sb, "Missing Information", model.MissingInformation);
        sb.AppendLine("<h2>Scores</h2>");
        foreach (var kv in model.Scores.OrderBy(x => x.Key))
        {
            sb.AppendLine($"<div>{E(kv.Key)}: {kv.Value:0.00}</div>");
        }

        return CloseShell(sb);
    }

    private static void RenderStateInsightSection(StringBuilder sb, string title, IReadOnlyCollection<StateInsightReadModel> rows)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var row in rows)
        {
            sb.AppendLine($"<div><strong>{E(row.SignalStrength)}</strong> | {E(row.Title)} | {E(row.Detail)} | evidence={E(row.Evidence)}</div>");
        }

        if (rows.Count == 0)
        {
            sb.AppendLine("<p>No items.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static string RenderTimeline(TimelineReadModel model)
    {
        var sb = CreateShell("Timeline");
        sb.AppendLine("<h1>Timeline</h1>");
        if (model.CurrentPeriod != null)
        {
            sb.AppendLine(RenderPeriod(model.CurrentPeriod));
        }

        foreach (var p in model.PriorPeriods)
        {
            sb.AppendLine(RenderPeriod(p));
        }

        foreach (var t in model.Transitions)
        {
            sb.AppendLine($"<div>{E(t.TransitionType)} | resolved={t.IsResolved} | conf={t.Confidence:0.00} | {E(t.Summary)}</div>");
        }

        sb.AppendLine($"<p>unresolved transitions: {model.UnresolvedTransitions}</p>");
        return CloseShell(sb);
    }

    private static string RenderNetwork(NetworkReadModel model, IReadOnlyDictionary<string, string> query)
    {
        var nodeTypeFilter = EmptyToNull(GetQuery(query, "nodeType"));
        var roleFilter = EmptyToNull(GetQuery(query, "role"));
        var selectedNodeId = EmptyToNull(GetQuery(query, "nodeId"));

        var filteredNodes = model.Nodes
            .Where(x => string.IsNullOrWhiteSpace(nodeTypeFilter)
                        || x.NodeType.Equals(nodeTypeFilter, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.IsNullOrWhiteSpace(roleFilter)
                        || x.PrimaryRole.Equals(roleFilter, StringComparison.OrdinalIgnoreCase)
                        || x.AdditionalRoles.Any(role => role.Equals(roleFilter, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.ImportanceScore)
            .ToList();

        var selected = filteredNodes.FirstOrDefault(x =>
                           !string.IsNullOrWhiteSpace(selectedNodeId)
                           && x.NodeId.Equals(selectedNodeId, StringComparison.OrdinalIgnoreCase))
                       ?? filteredNodes.FirstOrDefault();

        var sb = CreateShell("Network");
        sb.AppendLine("<h1>Network</h1>");
        sb.AppendLine($"<p>generated: {model.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine($"<p>filters: nodeType={E(nodeTypeFilter ?? "-")} role={E(roleFilter ?? "-")} nodes={filteredNodes.Count}</p>");
        sb.AppendLine("<section><h2>Nodes</h2>");
        foreach (var node in filteredNodes.Take(40))
        {
            var pick = $"/network?nodeId={UrlEncode(node.NodeId)}";
            sb.AppendLine($"<div><a href='{E(pick)}'>{E(node.DisplayName)}</a> | {E(node.NodeType)} | role={E(node.PrimaryRole)} | importance={node.ImportanceScore:0.00} | conf={node.Confidence:0.00}</div>");
        }

        if (filteredNodes.Count == 0)
        {
            sb.AppendLine("<p>No nodes match current filters.</p>");
        }

        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Influence</h2>");
        foreach (var edge in model.InfluenceEdges.Take(50))
        {
            if (selected != null
                && !edge.FromNodeId.Equals(selected.NodeId, StringComparison.OrdinalIgnoreCase)
                && !edge.ToNodeId.Equals(selected.NodeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine($"<div>{E(edge.FromNodeId)} -> {E(edge.ToNodeId)} | {E(edge.InfluenceType)} | conf={edge.Confidence:0.00}{(edge.IsHypothesis ? " | hypothesis" : string.Empty)}</div>");
        }

        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Information Flow</h2>");
        foreach (var edge in model.InformationFlows.Take(50))
        {
            if (selected != null
                && !edge.FromNodeId.Equals(selected.NodeId, StringComparison.OrdinalIgnoreCase)
                && !edge.ToNodeId.Equals(selected.NodeId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine($"<div>{E(edge.FromNodeId)} => {E(edge.ToNodeId)} | {E(edge.Direction)} | conf={edge.Confidence:0.00}</div>");
        }

        sb.AppendLine("</section>");

        if (selected != null)
        {
            sb.AppendLine("<section><h2>Selected Node</h2>");
            sb.AppendLine($"<p><strong>{E(selected.DisplayName)}</strong> ({E(selected.NodeType)})</p>");
            sb.AppendLine($"<p>primary role: {E(selected.PrimaryRole)}; additional: {E(string.Join(", ", selected.AdditionalRoles))}</p>");
            sb.AppendLine($"<p>global role: {E(selected.GlobalRole)}; focal={selected.IsFocalActor}</p>");
            sb.AppendLine($"<p>linked periods: {E(string.Join(", ", selected.LinkedPeriods))}</p>");
            sb.AppendLine($"<p>linked events: {E(string.Join(", ", selected.LinkedEvents))}</p>");
            sb.AppendLine($"<p>linked clarifications: {E(string.Join(", ", selected.LinkedClarifications))}</p>");
            sb.AppendLine("</section>");
        }

        return CloseShell(sb);
    }

    private static string RenderProfiles(ProfilesReadModel model)
    {
        var sb = CreateShell("Profiles");
        sb.AppendLine("<h1>Profiles</h1>");
        sb.AppendLine(RenderProfileSubject(model.Self));
        sb.AppendLine(RenderProfileSubject(model.Other));
        sb.AppendLine(RenderProfileSubject(model.Pair));
        return CloseShell(sb);
    }

    private static string RenderClarifications(ClarificationsReadModel model)
    {
        var sb = CreateShell("Clarifications");
        sb.AppendLine("<h1>Clarifications</h1>");
        sb.AppendLine($"<p>open questions: {model.OpenCount}</p>");
        foreach (var q in model.TopQuestions)
        {
            var trailLink = $"/history-object?objectType=clarification_question&objectId={UrlEncode(q.Id.ToString())}";
            sb.AppendLine($"<article><h3>{E(q.QuestionText)}</h3><p>why: {E(q.WhyItMatters)}</p><p>{E(q.Priority)} / {E(q.Status)}</p><p><a href='{E(trailLink)}'>history trail</a></p></article>");
        }

        return CloseShell(sb);
    }

    private static string RenderStrategy(StrategyReadModel model)
    {
        var sb = CreateShell("Strategy");
        sb.AppendLine("<h1>Strategy</h1>");
        sb.AppendLine($"<p>record: {E(model.RecordId.ToString())}</p>");
        sb.AppendLine($"<p>confidence: {model.Confidence:0.00}</p>");
        sb.AppendLine($"<h2>Primary</h2><p>{E(model.PrimarySummary)}</p><p>purpose: {E(model.PrimaryPurpose)}</p>");
        sb.AppendLine($"<p>risks: {E(string.Join(", ", model.PrimaryRisks))}</p>");
        sb.AppendLine($"<p>ethics: {E(model.EthicalContractSummary)}</p>");
        sb.AppendLine("<h2>Observed Facts</h2>");
        foreach (var item in model.ObservedFacts)
        {
            sb.AppendLine($"<div><strong>{E(item.SignalStrength)}</strong> | {E(item.Title)} | {E(item.Detail)} | evidence={E(item.Evidence)}</div>");
        }

        sb.AppendLine("<h2>Likely Interpretation</h2>");
        foreach (var item in model.LikelyInterpretation)
        {
            sb.AppendLine($"<div><strong>{E(item.SignalStrength)}</strong> | {E(item.Title)} | {E(item.Detail)} | evidence={E(item.Evidence)}</div>");
        }

        sb.AppendLine("<h2>Uncertainties / Alternative Readings</h2>");
        foreach (var item in model.Uncertainties)
        {
            sb.AppendLine($"<div><strong>{E(item.SignalStrength)}</strong> | {E(item.Title)} | {E(item.Detail)} | evidence={E(item.Evidence)}</div>");
        }

        sb.AppendLine("<h2>Missing Information</h2>");
        foreach (var item in model.MissingInformation)
        {
            sb.AppendLine($"<div><strong>{E(item.SignalStrength)}</strong> | {E(item.Title)} | {E(item.Detail)} | evidence={E(item.Evidence)}</div>");
        }

        sb.AppendLine("<h2>Relational Patterns</h2>");
        foreach (var pattern in model.RelationalPatterns)
        {
            sb.AppendLine($"<div>{E(pattern)}</div>");
        }

        sb.AppendLine("<h2>Alternatives</h2>");
        foreach (var alt in model.Alternatives)
        {
            sb.AppendLine($"<div><strong>{E(alt.ActionType)}</strong> - {E(alt.Summary)} | risks: {E(string.Join(", ", alt.Risks))}</div>");
        }

        sb.AppendLine($"<h2>Micro-step</h2><p>{E(model.MicroStep)}</p>");
        if (model.Horizon.Count > 0)
        {
            sb.AppendLine($"<h2>Horizon</h2><p>{E(string.Join(" -> ", model.Horizon))}</p>");
        }

        sb.AppendLine($"<h2>Why Not</h2><p>{E(model.WhyNotNotes)}</p>");
        var strategyTrail = $"/history-object?objectType=strategy_record&objectId={UrlEncode(model.RecordId.ToString())}";
        var chainFocus = $"/outcomes?strategyRecordId={UrlEncode(model.RecordId.ToString())}";
        sb.AppendLine($"<p><a href='{E(chainFocus)}'>Open outcome trail for this strategy</a> | <a href='{E(strategyTrail)}'>strategy history trail</a></p>");
        return CloseShell(sb);
    }

    private static string RenderDraftsReviews(DraftsReviewsReadModel model)
    {
        var sb = CreateShell("Drafts / Reviews");
        sb.AppendLine("<h1>Drafts / Reviews</h1>");
        if (model.LatestDraft != null)
        {
            sb.AppendLine($"<h2>Latest Draft ({model.LatestDraft.CreatedAt:yyyy-MM-dd HH:mm})</h2>");
            sb.AppendLine($"<p>main: {E(model.LatestDraft.MainDraft)}</p>");
            sb.AppendLine($"<p>softer alternative: {E(model.LatestDraft.SofterAlternative ?? model.LatestDraft.AltDraft1 ?? "-")}</p>");
            sb.AppendLine($"<p>more direct alternative: {E(model.LatestDraft.MoreDirectAlternative ?? model.LatestDraft.AltDraft2 ?? "-")}</p>");
            sb.AppendLine($"<p>style: {E(model.LatestDraft.StyleNotes ?? "-")}</p>");
            var draftTrail = $"/history-object?objectType=draft_record&objectId={UrlEncode(model.LatestDraft.Id.ToString())}";
            var draftChain = $"/outcomes?draftId={UrlEncode(model.LatestDraft.Id.ToString())}";
            sb.AppendLine($"<p><a href='{E(draftTrail)}'>draft history trail</a> | <a href='{E(draftChain)}'>outcome chain for draft</a></p>");
        }
        else
        {
            sb.AppendLine("<p>No drafts yet. Generate or import a draft to build an outcome chain.</p>");
        }

        if (model.LatestReview != null)
        {
            sb.AppendLine("<h2>Latest Review</h2>");
            sb.AppendLine($"<p>assessment: {E(model.LatestReview.Assessment)}</p>");
            sb.AppendLine($"<p>risks: {E(string.Join("; ", model.LatestReview.MainRisks))}</p>");
            sb.AppendLine($"<p>safer rewrite: {E(model.LatestReview.SaferRewrite)}</p>");
            sb.AppendLine($"<p>more natural rewrite: {E(model.LatestReview.NaturalRewrite)}</p>");
        }
        else
        {
            sb.AppendLine("<p>No review snapshot yet.</p>");
        }

        if (model.LatestOutcome != null)
        {
            sb.AppendLine($"<h2>Latest Outcome ({model.LatestOutcome.CreatedAt:yyyy-MM-dd HH:mm})</h2>");
            sb.AppendLine($"<p>strategy: {E(model.LatestOutcome.StrategyRecordId?.ToString() ?? "-")} | draft: {E(model.LatestOutcome.DraftId.ToString())}</p>");
            sb.AppendLine($"<p>actual message: {E(model.LatestOutcome.ActualMessageId?.ToString() ?? "-")} | follow-up: {E(model.LatestOutcome.FollowUpMessageId?.ToString() ?? "-")}</p>");
            sb.AppendLine($"<p>user outcome: {E(model.LatestOutcome.UserOutcomeLabel ?? "-")} | system outcome: {E(model.LatestOutcome.SystemOutcomeLabel ?? "-")}</p>");
            sb.AppendLine($"<p>final merged outcome: {E(model.LatestOutcome.OutcomeLabel)} | conf={(model.LatestOutcome.OutcomeConfidence ?? 0f):0.00}</p>");
            sb.AppendLine($"<p>match: {(model.LatestOutcome.MatchScore ?? 0f):0.00} via {E(model.LatestOutcome.MatchedBy)}</p>");
            sb.AppendLine($"<p>signals: {E(model.LatestOutcome.LearningSignals.Count == 0 ? "no learning signals captured yet" : string.Join("; ", model.LatestOutcome.LearningSignals))}</p>");
            var outcomeTrail = $"/history-object?objectType=draft_outcome&objectId={UrlEncode(model.LatestOutcome.Id.ToString())}";
            var chainFocus = $"/outcomes?outcomeId={UrlEncode(model.LatestOutcome.Id.ToString())}";
            sb.AppendLine($"<p><a href='{E(outcomeTrail)}'>outcome history trail</a> | <a href='{E(chainFocus)}'>focused chain view</a></p>");
        }
        else
        {
            sb.AppendLine("<p>No outcome recorded yet for latest draft/strategy chain.</p>");
        }

        sb.AppendLine("<p><a href='/outcomes'>Open full outcome trail</a></p>");
        return CloseShell(sb);
    }

    private static string RenderOutcomeTrail(OutcomeTrailReadModel model)
    {
        var sb = CreateShell("Outcome Trail");
        sb.AppendLine("<h1>Outcome Trail</h1>");
        sb.AppendLine("<p>Chain visibility: strategy -> draft -> actual action -> outcome.</p>");
        sb.AppendLine($"<p>scanned outcomes: {model.TotalOutcomesScanned} | rendered chain items: {model.Items.Count}</p>");
        if (model.MissingDraftCount > 0 || model.MissingStrategyCount > 0)
        {
            sb.AppendLine($"<p>integrity warnings: missing_draft={model.MissingDraftCount}, missing_strategy={model.MissingStrategyCount}. Some outcomes are not fully linkable yet.</p>");
        }

        foreach (var item in model.Items)
        {
            var strategyId = item.StrategyRecordId?.ToString() ?? "-";
            var strategyTrail = item.StrategyRecordId.HasValue
                ? $"/history-object?objectType=strategy_record&objectId={UrlEncode(item.StrategyRecordId.Value.ToString())}"
                : string.Empty;
            var draftTrail = $"/history-object?objectType=draft_record&objectId={UrlEncode(item.DraftId.ToString())}";
            var outcomeTrail = $"/history-object?objectType=draft_outcome&objectId={UrlEncode(item.OutcomeId.ToString())}";
            var strategyFocus = item.StrategyRecordId.HasValue
                ? $"/outcomes?strategyRecordId={UrlEncode(item.StrategyRecordId.Value.ToString())}"
                : string.Empty;
            var draftFocus = $"/outcomes?draftId={UrlEncode(item.DraftId.ToString())}";

            sb.AppendLine("<article>");
            sb.AppendLine($"<h3>Outcome {E(item.OutcomeId.ToString())}</h3>");
            sb.AppendLine($"<p>time: {item.OutcomeCreatedAt:yyyy-MM-dd HH:mm} | final merged outcome: {E(item.OutcomeLabel)} | confidence: {(item.OutcomeConfidence ?? 0f):0.00}</p>");
            sb.AppendLine($"<p>user outcome: {E(item.UserOutcomeLabel ?? "-")} | system outcome: {E(item.SystemOutcomeLabel ?? "-")}</p>");
            sb.AppendLine($"<p><strong>strategy</strong>: {E(strategyId)} | {E(item.StrategySummary)} {RenderOptionalLink(strategyTrail, "history")} {RenderOptionalLink(strategyFocus, "focus")}</p>");
            sb.AppendLine($"<p><strong>draft</strong>: {E(item.DraftId.ToString())} | {E(item.DraftSnippet)} | <a href='{E(draftTrail)}'>history</a> | <a href='{E(draftFocus)}'>focus</a></p>");
            sb.AppendLine($"<p><strong>actual action</strong>: {E(item.ActualMessageId?.ToString() ?? "-")} | {E(item.ActualMessageSnippet ?? "-")}</p>");
            sb.AppendLine($"<p><strong>follow-up</strong>: {E(item.FollowUpMessageId?.ToString() ?? "-")} | {E(item.FollowUpMessageSnippet ?? "-")}</p>");
            sb.AppendLine($"<p><strong>matching</strong>: {(item.MatchScore ?? 0f):0.00} via {E(item.MatchedBy)}</p>");
            sb.AppendLine($"<p><strong>learning signals</strong>: {E(item.LearningSignals.Count == 0 ? "none" : string.Join("; ", item.LearningSignals))}</p>");
            sb.AppendLine($"<p><a href='{E(outcomeTrail)}'>outcome history</a></p>");
            sb.AppendLine("</article>");
        }

        if (model.Items.Count == 0)
        {
            sb.AppendLine("<p>No outcome chain items matched this view yet.</p>");
            sb.AppendLine("<p>Use <a href='/strategy'>strategy</a>, <a href='/drafts-reviews'>drafts/reviews</a>, or <a href='/history'>history</a> to inspect related chain components.</p>");
        }

        return CloseShell(sb);
    }

    private static string RenderOfflineEvents(OfflineEventsReadModel model)
    {
        var sb = CreateShell("Offline Events");
        sb.AppendLine("<h1>Offline Events</h1>");
        foreach (var e in model.Events)
        {
            sb.AppendLine($"<article><h3>{E(e.Title)}</h3><p>{e.TimestampStart:yyyy-MM-dd HH:mm} | {E(e.EventType)}</p><p>{E(e.UserSummary)}</p><p>period: {E(e.LinkedPeriodId?.ToString() ?? "-")}</p><p>evidence: {E(e.EvidenceSummary)}</p></article>");
        }

        return CloseShell(sb);
    }

    private static string RenderReviewBoard(WebReviewBoardModel model, WebReadRequest request)
    {
        var sb = CreateShell("Review");
        sb.AppendLine("<h1>Review Board</h1>");
        sb.AppendLine("<p>Card pattern: summary, provenance, suggested interpretation, and actions (confirm/reject/defer/edit).</p>");
        if (model.Cards.Count == 0)
        {
            sb.AppendLine("<p>No reviewable objects found.</p>");
            return CloseShell(sb);
        }

        foreach (var card in model.Cards)
        {
            var objectType = UrlEncode(card.ObjectType);
            var objectId = UrlEncode(card.ObjectId);
            var baseQuery = $"case={request.CaseId}&chat={request.ChatId}&objectType={objectType}&objectId={objectId}&actor={UrlEncode(request.Actor)}";
            var confirm = $"/review-action?{baseQuery}&action=confirm";
            var reject = $"/review-action?{baseQuery}&action=reject";
            var defer = $"/review-action?{baseQuery}&action=defer";
            var trail = $"/history-object?objectType={objectType}&objectId={objectId}";

            sb.AppendLine("<article>");
            sb.AppendLine($"<h3>{E(card.ObjectType)}:{E(card.ObjectId)}</h3>");
            sb.AppendLine($"<p><strong>summary:</strong> {E(card.Summary)}</p>");
            sb.AppendLine($"<p><strong>provenance:</strong> {E(card.Provenance)}</p>");
            sb.AppendLine($"<p><strong>suggested:</strong> {E(card.SuggestedInterpretation)}</p>");
            sb.AppendLine($"<p><strong>context:</strong> {E(card.LinkedContext)}</p>");
            sb.AppendLine($"<p><strong>confidence:</strong> {(card.Confidence.HasValue ? card.Confidence.Value.ToString("0.00") : "-")}</p>");
            sb.AppendLine($"<p><a href='{E(confirm)}'>confirm</a> | <a href='{E(reject)}'>reject</a> | <a href='{E(defer)}'>defer</a> | <a href='{E(trail)}'>history trail</a></p>");

            if (card.CanEdit && card.ObjectType == "period")
            {
                var editHref = $"/review-edit-period?case={request.CaseId}&chat={request.ChatId}&periodId={objectId}&label={UrlEncode("edited_label")}&summary={UrlEncode("edited from web review")}&reviewPriority=3&actor={UrlEncode(request.Actor)}";
                sb.AppendLine($"<p><a href='{E(editHref)}'>edit period (sample)</a></p>");
            }

            sb.AppendLine("</article>");
        }

        return CloseShell(sb);
    }

    private static string RenderReviewAction(WebReviewActionResult result, WebReadRequest request)
    {
        var sb = CreateShell("Review Action");
        sb.AppendLine("<h1>Review Action</h1>");
        sb.AppendLine($"<p>success: {result.Success}</p>");
        sb.AppendLine($"<p>object: {E(result.ObjectType)}:{E(result.ObjectId)}</p>");
        sb.AppendLine($"<p>action: {E(result.Action)}</p>");
        sb.AppendLine($"<p>message: {E(result.Message)}</p>");
        sb.AppendLine($"<p><a href='/review'>Back to review board</a> (case={request.CaseId}, chat={request.ChatId})</p>");
        return CloseShell(sb);
    }

    private static string RenderOpsBudget(BudgetOperationalReadModel model, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Ops Budget");
        var stateFilter = EmptyToNull(GetQuery(query, "state"));
        var pathFilter = EmptyToNull(GetQuery(query, "path"));
        sb.AppendLine("<h1>Ops Budget</h1>");
        sb.AppendLine($"<p>generated: {model.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine($"<p>ops status: <strong>{E(model.OperationalStatus)}</strong></p>");
        sb.AppendLine($"<p>paths: total={model.TotalPaths}, active={model.ActivePaths}, degraded={model.DegradedPaths}, paused={model.PausedPaths}, hard-paused={model.HardPausedPaths}, quota-blocked={model.QuotaBlockedPaths}</p>");
        sb.AppendLine($"<p>filters: state={E(stateFilter ?? "-")} path={E(pathFilter ?? "-")}</p>");
        sb.AppendLine("<p><a href='/ops-budget'>All paths</a> | <a href='/ops-budget?state=quota_blocked'>Quota-blocked</a> | <a href='/ops-budget?state=hard_paused'>Hard paused</a> | <a href='/ops-budget?state=soft_limited'>Degraded</a> | <a href='/ops-budget?state=active'>Active</a></p>");

        foreach (var row in model.States)
        {
            var mode = row.VisibilityMode;
            sb.AppendLine("<article>");
            sb.AppendLine($"<h3>{E(row.PathKey)}</h3>");
            sb.AppendLine($"<p>modality={E(row.Modality)} | state={E(row.State)} | mode={E(mode)} | updated={row.UpdatedAt:yyyy-MM-dd HH:mm:ss} UTC</p>");
            sb.AppendLine($"<p>reason: {E(row.Reason)}</p>");
            if (row.Details.Count > 0)
            {
                sb.AppendLine("<p>details:</p>");
                RenderCompactPairs(row.Details, sb);
            }

            sb.AppendLine("</article>");
        }

        if (model.States.Count == 0)
        {
            sb.AppendLine("<p>No budget operational states found.</p>");
        }

        return CloseShell(sb);
    }

    private static string RenderOpsEval(EvalRunsReadModel model, IReadOnlyDictionary<string, string> query)
    {
        var statusFilter = EmptyToNull(GetQuery(query, "status"));
        var compareOnly = string.Equals(GetQuery(query, "compare"), "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetQuery(query, "compare"), "true", StringComparison.OrdinalIgnoreCase);
        var runs = model.Runs
            .Where(x => statusFilter == null
                        || statusFilter.Equals("all", StringComparison.OrdinalIgnoreCase)
                        || (statusFilter.Equals("passed", StringComparison.OrdinalIgnoreCase) && x.Passed)
                        || (statusFilter.Equals("failed", StringComparison.OrdinalIgnoreCase) && !x.Passed))
            .ToList();

        var sb = CreateShell("Ops Eval");
        sb.AppendLine("<h1>Ops Eval</h1>");
        sb.AppendLine($"<p>generated: {model.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine($"<p>ops status: <strong>{E(model.OperationalStatus)}</strong></p>");
        sb.AppendLine($"<p>runs: total={model.TotalRuns}, passed={model.PassedRuns}, failed={model.FailedRuns}, comparisons={model.Comparisons.Count}</p>");
        sb.AppendLine($"<p>filter: status={E(statusFilter ?? "all")} compareOnly={compareOnly}</p>");
        sb.AppendLine("<p><a href='/ops-eval'>All runs</a> | <a href='/ops-eval?status=failed'>Failed runs</a> | <a href='/ops-eval?status=passed'>Passed runs</a> | <a href='/ops-eval?compare=1'>Comparisons</a></p>");

        if (model.Comparisons.Count > 0)
        {
            sb.AppendLine("<section><h2>Run Comparisons</h2>");
            foreach (var comparison in model.Comparisons)
            {
                var currentLink = $"/ops-eval?runId={UrlEncode(comparison.CurrentRunId.ToString())}";
                var previousLink = $"/ops-eval?runId={UrlEncode(comparison.PreviousRunId.ToString())}";
                sb.AppendLine("<article>");
                sb.AppendLine($"<h3>{E(comparison.RunName)} | {E(comparison.StatusTransition)}</h3>");
                sb.AppendLine($"<p>status changed: {comparison.StatusChanged}</p>");
                sb.AppendLine($"<p>scenario pass-rate: {(comparison.PreviousScenarioPassRate * 100):0.0}% -> {(comparison.CurrentScenarioPassRate * 100):0.0}% (delta {(comparison.ScenarioPassRateDelta * 100):+0.0;-0.0;0.0}pp)</p>");
                sb.AppendLine($"<p>duration: {comparison.PreviousDurationSeconds}s -> {comparison.CurrentDurationSeconds}s (delta {comparison.DurationDeltaSeconds:+#;-#;0}s)</p>");
                sb.AppendLine($"<p><a href='{E(currentLink)}'>open current</a> | <a href='{E(previousLink)}'>open previous</a></p>");
                sb.AppendLine("</article>");
            }

            sb.AppendLine("</section>");
        }

        if (!compareOnly)
        {
            sb.AppendLine("<section><h2>Latest Runs</h2>");
            foreach (var run in runs)
            {
                var status = run.Passed ? "PASS" : "FAIL";
                var details = $"/ops-eval?runId={UrlEncode(run.RunId.ToString())}";
                var experimentTrail = run.LinkedExperimentRunId.HasValue
                    ? $"/history-object?objectType=eval_experiment_run&objectId={UrlEncode(run.LinkedExperimentRunId.Value.ToString())}"
                    : string.Empty;
                sb.AppendLine("<article>");
                sb.AppendLine($"<h3>{E(run.RunName)} | {status} | kind={E(run.RunKind)}</h3>");
                sb.AppendLine($"<p>run={E(run.RunId.ToString())}</p>");
                sb.AppendLine($"<p>started={run.StartedAt:yyyy-MM-dd HH:mm:ss} UTC, finished={run.FinishedAt:yyyy-MM-dd HH:mm:ss} UTC, duration={run.DurationSeconds}s</p>");
                sb.AppendLine($"<p>scenarios: total={run.ScenarioCount}, passed={run.ScenarioPassed}, failed={run.ScenarioFailed}</p>");
                sb.AppendLine($"<p>summary: {E(run.Summary)}</p>");
                if (run.Metrics.Count > 0)
                {
                    sb.AppendLine("<p>metrics:</p>");
                    RenderCompactPairs(run.Metrics.Take(8), sb);
                }

                sb.AppendLine($"<p><a href='{E(details)}'>open scenario results</a>{RenderOptionalLink(experimentTrail, "experiment trail")}</p>");
                sb.AppendLine("</article>");
            }

            if (runs.Count == 0)
            {
                sb.AppendLine("<p>No eval runs found for current filter.</p>");
            }

            sb.AppendLine("</section>");
        }

        if (model.SelectedRun != null)
        {
            var selected = model.SelectedRun;
            sb.AppendLine("<section><h2>Selected Run Scenarios</h2>");
            sb.AppendLine($"<p>run={E(selected.RunId.ToString())} | {E(selected.RunName)} | {(selected.Passed ? "PASS" : "FAIL")} | kind={E(selected.RunKind)}</p>");
            foreach (var scenario in model.SelectedScenarios)
            {
                sb.AppendLine("<article>");
                sb.AppendLine($"<h3>{E(scenario.ScenarioName)} | {(scenario.Passed ? "PASS" : "FAIL")}</h3>");
                sb.AppendLine($"<p>created={scenario.CreatedAt:yyyy-MM-dd HH:mm:ss} UTC</p>");
                sb.AppendLine($"<p>{E(scenario.Summary)}</p>");
                if (scenario.Metrics.Count > 0)
                {
                    sb.AppendLine("<p>metrics:</p>");
                    RenderCompactPairs(scenario.Metrics.Take(8), sb);
                }

                sb.AppendLine("</article>");
            }

            if (model.SelectedScenarios.Count == 0)
            {
                sb.AppendLine("<p>No scenario results for selected run.</p>");
            }

            sb.AppendLine("</section>");
        }

        return CloseShell(sb);
    }

    private static string RenderOpsAbCandidates(AbScenarioCandidatePoolReadModel model, IReadOnlyDictionary<string, string> query)
    {
        var bucket = EmptyToNull(GetQuery(query, "bucket"));
        var target = EmptyToNull(GetQuery(query, "target")) ?? model.RequestedCount.ToString();
        var asJson = string.Equals(GetQuery(query, "format"), "json", StringComparison.OrdinalIgnoreCase);

        var sb = CreateShell("Ops A/B Candidates");
        sb.AppendLine("<h1>Ops A/B Scenario Candidates</h1>");
        sb.AppendLine("<p>Semi-automatic candidate miner output for bootstrap dataset; final 8-10 scenarios remain human-selected.</p>");
        sb.AppendLine($"<p>generated: {model.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss} UTC</p>");
        sb.AppendLine($"<p>requested={model.RequestedCount}, produced={model.TotalCandidates}, bucket_filter={E(model.BucketFilter ?? "all")}</p>");
        sb.AppendLine($"<p>buckets: state={model.StateCandidates}, strategy_draft={model.StrategyDraftCandidates}, counterexample={model.CounterexampleCandidates}</p>");
        sb.AppendLine($"<p><a href='/ops-ab-candidates?target=30'>default</a> | <a href='/ops-ab-candidates?bucket=state&target=30'>state</a> | <a href='/ops-ab-candidates?bucket=strategy_draft&target=30'>strategy_draft</a> | <a href='/ops-ab-candidates?bucket=counterexample&target=30'>counterexample</a> | <a href='/ops-ab-candidates?target={E(target)}&bucket={E(bucket ?? string.Empty)}&format=json'>json</a></p>");

        if (asJson)
        {
            var json = JsonSerializer.Serialize(model, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            sb.AppendLine("<section><h2>JSON</h2>");
            sb.AppendLine($"<pre>{E(json)}</pre>");
            sb.AppendLine("</section>");
            return CloseShell(sb);
        }

        foreach (var candidate in model.Candidates)
        {
            sb.AppendLine("<article>");
            sb.AppendLine($"<h3>{E(candidate.Title)}</h3>");
            sb.AppendLine($"<p>candidate_id={E(candidate.CandidateId)} | bucket={E(candidate.Bucket)}</p>");
            sb.AppendLine($"<p>date_range={candidate.DateRange.From:yyyy-MM-dd}..{candidate.DateRange.To:yyyy-MM-dd} | chats={E(string.Join(", ", candidate.ChatIds))}</p>");
            sb.AppendLine($"<p>message_count={candidate.MessageCount} | session_count={candidate.SessionCount}</p>");
            sb.AppendLine($"<p>why_selected: {E(candidate.WhySelected)}</p>");
            sb.AppendLine($"<p>risk_of_misread: {E(candidate.RiskOfMisread)}</p>");
            sb.AppendLine($"<p>suggested_expected_state: {E(candidate.SuggestedExpectedState)}</p>");
            sb.AppendLine($"<p>suggested_expected_risks: {E(string.Join(", ", candidate.SuggestedExpectedRisks))}</p>");
            sb.AppendLine($"<p>source_artifacts: periods={candidate.SourceArtifacts.PeriodIds.Count}, transitions={candidate.SourceArtifacts.TransitionIds.Count}, unresolved={candidate.SourceArtifacts.UnresolvedTransitionIds.Count}, conflicts={candidate.SourceArtifacts.ConflictIds.Count}, clarifications={candidate.SourceArtifacts.ClarificationIds.Count}, snapshots={candidate.SourceArtifacts.StateSnapshotIds.Count}, strategies={candidate.SourceArtifacts.StrategyRecordIds.Count}, drafts={candidate.SourceArtifacts.DraftRecordIds.Count}, outcomes={candidate.SourceArtifacts.OutcomeIds.Count}, offline={candidate.SourceArtifacts.OfflineEventIds.Count}, network_nodes={candidate.SourceArtifacts.NetworkNodeIds.Count}, network_edges={candidate.SourceArtifacts.NetworkEdgeIds.Count}</p>");
            sb.AppendLine("</article>");
        }

        if (model.Candidates.Count == 0)
        {
            sb.AppendLine("<p>No candidates found for current filter. Try /ops-ab-candidates?target=30 without bucket filter.</p>");
        }

        return CloseShell(sb);
    }

    private static string RenderProfileSubject(ProfileSubjectReadModel subject)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<section><h2>{E(subject.SubjectType)} ({E(subject.SubjectId)})</h2>");
        sb.AppendLine($"<p>{E(subject.Summary)}</p>");
        sb.AppendLine($"<p>confidence={subject.Confidence:0.00} stability={subject.Stability:0.00}</p>");
        foreach (var trait in subject.TopTraits)
        {
            sb.AppendLine($"<div>{E(trait.TraitKey)}: {E(trait.ValueLabel)} ({trait.Confidence:0.00}/{trait.Stability:0.00})</div>");
        }

        sb.AppendLine($"<p>what works: {E(subject.WhatWorks)}</p>");
        sb.AppendLine($"<p>what fails: {E(subject.WhatFails)}</p>");
        sb.AppendLine($"<p>participant patterns: {E(subject.ParticipantPatterns)}</p>");
        sb.AppendLine($"<p>pair dynamics: {E(subject.PairDynamics)}</p>");
        sb.AppendLine($"<p>repeated interaction modes: {E(subject.RepeatedInteractionModes)}</p>");
        sb.AppendLine($"<p>changes over time: {E(subject.ChangesOverTime)}</p>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    private static string RenderPeriod(TimelinePeriodReadModel period)
    {
        return $"<article><h3>{E(period.Label)}</h3><p>{period.StartAt:yyyy-MM-dd}..{(period.EndAt?.ToString("yyyy-MM-dd") ?? "now")}, conf={period.InterpretationConfidence:0.00}, open_q={period.OpenQuestionsCount}</p><p>{E(period.Summary)}</p><p>evidence: {E(string.Join("; ", period.EvidenceHooks))}</p></article>";
    }

    private static void RenderCompactPairs(IEnumerable<KeyValuePair<string, string>> pairs, StringBuilder sb)
    {
        foreach (var pair in pairs)
        {
            sb.AppendLine($"<div>{E(pair.Key)}: {E(pair.Value)}</div>");
        }
    }

    private static (string Path, Dictionary<string, string> Query) ParseRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return ("/dashboard", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var trimmed = route.Trim();
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        var queryIndex = trimmed.IndexOf('?');
        var path = queryIndex >= 0 ? trimmed[..queryIndex] : trimmed;
        var rawQuery = queryIndex >= 0 ? trimmed[(queryIndex + 1)..] : string.Empty;
        var query = ParseQuery(rawQuery);
        return (path.ToLowerInvariant(), query);
    }

    private static Dictionary<string, string> ParseQuery(string rawQuery)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return result;
        }

        var parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var split = part.Split('=', 2);
            var key = Uri.UnescapeDataString(split[0]).Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = split.Length > 1 ? Uri.UnescapeDataString(split[1]).Trim() : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static string GetQuery(IReadOnlyDictionary<string, string> query, string key)
    {
        return query.TryGetValue(key, out var value) ? value : string.Empty;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool? ParseNullableBoolean(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static Guid? ParseGuidQuery(IReadOnlyDictionary<string, string> query, string key)
    {
        var raw = EmptyToNull(GetQuery(query, key));
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private static StringBuilder CreateShell(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>" + E(title) + "</title>");
        sb.AppendLine("<style>body{font-family:ui-sans-serif,system-ui;max-width:980px;margin:20px auto;padding:0 12px;color:#1f2937}nav a{margin-right:10px}section,article{border:1px solid #e5e7eb;border-radius:8px;padding:10px;margin:10px 0}h1,h2,h3{margin:6px 0}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<nav><a href='/dashboard'>Dashboard</a><a href='/search'>Search</a><a href='/dossier'>Dossier</a><a href='/view/blocking'>View:Blocking</a><a href='/view/current-period'>View:Current</a><a href='/view/conflicts'>View:Conflicts</a><a href='/inbox'>Queue</a><a href='/history'>History</a><a href='/state'>Current State</a><a href='/timeline'>Timeline</a><a href='/profiles'>Profiles</a><a href='/clarifications'>Clarifications</a><a href='/strategy'>Strategy</a><a href='/drafts-reviews'>Drafts/Reviews</a><a href='/outcomes'>Outcomes</a><a href='/offline-events'>Offline Events</a><a href='/review'>Review</a><a href='/ops-budget'>Ops Budget</a><a href='/ops-eval'>Ops Eval</a><a href='/ops-ab-candidates'>Ops A/B Candidates</a></nav>");
        return sb;
    }

    private static string CloseShell(StringBuilder sb)
    {
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string RenderOptionalLink(string? href, string label)
    {
        return string.IsNullOrWhiteSpace(href)
            ? string.Empty
            : $"| <a href='{E(href)}'>{E(label)}</a>";
    }

    private static bool TryBuildOutcomeTrailLink(string objectType, out string route)
    {
        route = string.Empty;
        if (string.IsNullOrWhiteSpace(objectType))
        {
            return false;
        }

        var normalized = objectType.Trim().ToLowerInvariant();
        if (normalized is "strategy_record" or "draft_record" or "draft_outcome")
        {
            route = "/outcomes";
            return true;
        }

        return false;
    }

    private static string? BuildOutcomeFocusLink(string objectType, string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectType) || string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        var normalizedType = objectType.Trim().ToLowerInvariant();
        return normalizedType switch
        {
            "strategy_record" => $"/outcomes?strategyRecordId={UrlEncode(objectId)}",
            "draft_record" => $"/outcomes?draftId={UrlEncode(objectId)}",
            "draft_outcome" => $"/outcomes?outcomeId={UrlEncode(objectId)}",
            _ => null
        };
    }

    private static bool MatchesBudgetStateFilter(BudgetOperationalStateReadModel state, string? stateFilter)
    {
        if (string.IsNullOrWhiteSpace(stateFilter))
        {
            return true;
        }

        var normalized = stateFilter.Trim().ToLowerInvariant();
        return normalized switch
        {
            "quota_blocked" => state.IsQuotaBlocked,
            "hard_paused" => state.IsHardPaused,
            "paused" => state.IsPaused,
            "soft_limited" or "degraded" => state.IsDegraded,
            "active" => !state.IsPaused && !state.IsDegraded,
            _ => state.State.Equals(normalized, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static BudgetOperationalReadModel BuildBudgetAggregate(IReadOnlyCollection<BudgetOperationalStateReadModel> states, DateTime generatedAtUtc)
    {
        var stateList = states
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
        var paused = stateList.Count(x => x.IsPaused);
        var degraded = stateList.Count(x => x.IsDegraded);
        var quotaBlocked = stateList.Count(x => x.IsQuotaBlocked);
        var hardPaused = stateList.Count(x => x.IsHardPaused);
        var status = quotaBlocked > 0
            ? "quota_blocked"
            : paused > 0
                ? "paused"
                : degraded > 0
                    ? "degraded"
                    : "active";

        return new BudgetOperationalReadModel
        {
            GeneratedAtUtc = generatedAtUtc,
            OperationalStatus = status,
            TotalPaths = stateList.Count,
            PausedPaths = paused,
            HardPausedPaths = hardPaused,
            QuotaBlockedPaths = quotaBlocked,
            DegradedPaths = degraded,
            ActivePaths = stateList.Count(x => !x.IsPaused && !x.IsDegraded),
            States = stateList
        };
    }

    private static string UrlEncode(string value) => Uri.EscapeDataString(value ?? string.Empty);
    private static string FormatConfidence(float? value) => value.HasValue ? value.Value.ToString("0.00") : "-";
    private static string BuildScopedPath(string route, WebReadRequest request, IReadOnlyDictionary<string, string?>? query = null)
    {
        var segments = new List<string>();
        if (request.CaseId > 0)
        {
            segments.Add($"caseScopeId={UrlEncode(request.CaseId.ToString())}");
        }

        if (request.ChatId > 0)
        {
            segments.Add($"chatId={UrlEncode(request.ChatId.ToString())}");
        }

        if (query != null)
        {
            foreach (var pair in query)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                segments.Add($"{UrlEncode(pair.Key)}={UrlEncode(pair.Value)}");
            }
        }

        return segments.Count == 0 ? route : $"{route}?{string.Join("&", segments)}";
    }

    private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

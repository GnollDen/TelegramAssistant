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
        "/case-evidence",
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
                Title = "Панель",
                Html = RenderDashboard(
                    await _webReadService.GetDashboardAsync(request, ct),
                    await _webOpsService.GetRecentChangesAsync(request, 6, ct),
                    await _webOpsService.GetBudgetOperationalStateAsync(ct),
                    await _webOpsService.GetEvalRunsAsync(limit: 3, ct: ct))
            },
            "/search" => new WebRenderResult { Route = path, Title = "Поиск", Html = RenderSearch(await ExecuteSearchAsync(request, query, ct)) },
            "/dossier" => new WebRenderResult { Route = path, Title = "Досье", Html = RenderDossier(await _webSearchService.GetDossierAsync(request, 30, ct)) },
            "/view/blocking" => new WebRenderResult { Route = path, Title = "Сохраненный вид: блокирующие", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "blocking", 40, ct)) },
            "/view/current-period" => new WebRenderResult { Route = path, Title = "Сохраненный вид: текущий период", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "current-period", 10, ct)) },
            "/view/conflicts" => new WebRenderResult { Route = path, Title = "Сохраненный вид: конфликты", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "conflicts", 40, ct)) },
            "/queue" => new WebRenderResult { Route = path, Title = "Очередь кейсов", Html = RenderCaseQueue(await ExecuteCaseQueueReadAsync(request, query, ct), request) },
            "/inbox" => new WebRenderResult { Route = path, Title = "Очередь кейсов", Html = RenderCaseQueue(await ExecuteCaseQueueReadAsync(request, query, ct), request) },
            "/case-detail" => new WebRenderResult { Route = path, Title = "Детали кейса", Html = RenderCaseDetail(await ExecuteCaseDetailReadAsync(request, query, ct), request, query) },
            "/case-evidence" => new WebRenderResult { Route = path, Title = "Доказательства кейса", Html = RenderCaseEvidence(await ExecuteCaseDetailReadAsync(request, query, ct), request, query) },
            "/artifact-detail" => new WebRenderResult { Route = path, Title = "Детали артефакта", Html = RenderArtifactDetail(await ExecuteArtifactDetailReadAsync(request, query, ct), request, query) },
            "/case-action" => new WebRenderResult { Route = path, Title = "Действие по кейсу", Html = RenderCaseAction(await ExecuteCaseActionAsync(request, query, ct), request, query) },
            "/clarification-answer" => new WebRenderResult { Route = path, Title = "Ответ на уточнение", Html = RenderClarificationAnswer(await ExecuteClarificationAnswerAsync(request, query, ct), request, query) },
            "/artifact-action" => new WebRenderResult { Route = path, Title = "Действие по артефакту", Html = RenderArtifactAction(await ExecuteArtifactActionAsync(request, query, ct), request, query) },
            "/history" => new WebRenderResult { Route = path, Title = "История", Html = RenderHistory(await ExecuteHistoryReadAsync(request, query, ct)) },
            "/history-object" => new WebRenderResult { Route = path, Title = "История объекта", Html = RenderObjectHistory(await ExecuteObjectHistoryReadAsync(request, query, ct), request) },
            "/state" => new WebRenderResult { Route = path, Title = "Текущее состояние", Html = RenderState(await _webReadService.GetCurrentStateAsync(request, ct)) },
            "/timeline" => new WebRenderResult { Route = path, Title = "Таймлайн", Html = RenderTimeline(await _webReadService.GetTimelineAsync(request, ct)) },
            "/network" => new WebRenderResult { Route = path, Title = "Network", Html = RenderNetwork(await _webReadService.GetNetworkAsync(request, ct), query) },
            "/profiles" => new WebRenderResult { Route = path, Title = "Профили", Html = RenderProfiles(await _webReadService.GetProfilesAsync(request, ct)) },
            "/clarifications" => new WebRenderResult { Route = path, Title = "Уточнения", Html = RenderClarifications(await _webReadService.GetClarificationsAsync(request, ct)) },
            "/strategy" => new WebRenderResult { Route = path, Title = "Стратегия", Html = RenderStrategy(await _webReadService.GetStrategyAsync(request, ct)) },
            "/drafts-reviews" => new WebRenderResult { Route = path, Title = "Черновики / Ревью", Html = RenderDraftsReviews(await _webReadService.GetDraftsReviewsAsync(request, ct)) },
            "/outcomes" => new WebRenderResult { Route = path, Title = "Траектория исходов", Html = RenderOutcomeTrail(await ExecuteOutcomeTrailReadAsync(request, query, ct)) },
            "/offline-events" => new WebRenderResult { Route = path, Title = "Офлайн-события", Html = RenderOfflineEvents(await _webReadService.GetOfflineEventsAsync(request, ct)) },
            "/review" => new WebRenderResult { Route = path, Title = "Ревью", Html = RenderReviewBoard(await _webReviewService.GetBoardAsync(request, ct), request) },
            "/review-action" => new WebRenderResult { Route = path, Title = "Действие ревью", Html = RenderReviewAction(await ExecuteReviewActionAsync(request, query, ct), request, query) },
            "/review-edit-period" => new WebRenderResult { Route = path, Title = "Редактирование периода", Html = RenderReviewAction(await ExecutePeriodEditAsync(request, query, ct), request, query) },
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
            sortBy: EmptyToNull(GetQuery(query, "sortBy")),
            sortDirection: EmptyToNull(GetQuery(query, "sortDirection")),
            ct: ct);
    }

    private async Task<Stage6CaseDetailReadModel> ExecuteCaseDetailReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (!Guid.TryParse(GetQuery(query, "caseId"), out var caseId))
        {
            return new Stage6CaseDetailReadModel
            {
                Exists = false,
                ReasonSummary = "Не передан или неверный caseId."
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
                Message = "Не передан или неверный caseId."
            };
        }

        var action = EmptyToNull(GetQuery(query, "action")) ?? string.Empty;
        if (RequiresCaseActionConfirmation(action)
            && !IsConfirmationAccepted(query))
        {
            return new WebStage6CaseActionResult
            {
                Success = false,
                Stage6CaseId = caseId,
                Action = action,
                Message = $"Confirmation required for '{action}' action.",
                RequiresConfirmation = true
            };
        }

        return await _webOpsService.ApplyCaseActionAsync(new WebStage6CaseActionRequest
        {
            ScopeCaseId = request.CaseId,
            ChatId = request.ChatId,
            Stage6CaseId = caseId,
            Action = action,
            Actor = string.IsNullOrWhiteSpace(GetQuery(query, "actor")) ? request.Actor : GetQuery(query, "actor"),
            Reason = EmptyToNull(GetQuery(query, "reason")),
            Note = EmptyToNull(GetQuery(query, "note")),
            FeedbackKind = EmptyToNull(GetQuery(query, "feedbackKind")),
            FeedbackDimension = EmptyToNull(GetQuery(query, "feedbackDimension")),
            IsUseful = ParseNullableBoolean(GetQuery(query, "useful")),
            ContextSourceKind = EmptyToNull(GetQuery(query, "contextSourceKind")),
            ContextEntryMode = EmptyToNull(GetQuery(query, "contextEntryMode")),
            CorrectionTargetRef = EmptyToNull(GetQuery(query, "correctionTargetRef")),
            CorrectionSummary = EmptyToNull(GetQuery(query, "correctionSummary")),
            ContextCertainty = ParseNullableSingle(GetQuery(query, "contextCertainty"))
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
                Message = "Не передан или неверный caseId."
            };
        }

        var answer = EmptyToNull(GetQuery(query, "answer")) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(answer)
            && !IsConfirmationAccepted(query))
        {
            return new WebStage6ClarificationAnswerResult
            {
                Success = false,
                Stage6CaseId = caseId,
                Message = "Confirmation required before submitting clarification answer.",
                AnswerValue = answer,
                RequiresConfirmation = true
            };
        }

        return await _webOpsService.ApplyClarificationAnswerAsync(new WebStage6ClarificationAnswerRequest
        {
            ScopeCaseId = request.CaseId,
            ChatId = request.ChatId,
            Stage6CaseId = caseId,
            AnswerType = EmptyToNull(GetQuery(query, "answerType")) ?? "text",
            AnswerValue = answer,
            SourceClass = EmptyToNull(GetQuery(query, "sourceClass")) ?? "operator_web",
            AnswerConfidence = ParseNullableSingle(GetQuery(query, "answerConfidence")) ?? 0.8f,
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
                ObjectSummary = "Не переданы objectType/objectId."
            };
        }

        return await _webOpsService.GetObjectHistoryAsync(request, objectType, objectId, 40, ct);
    }

    private async Task<WebReviewActionResult> ExecuteReviewActionAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        var action = GetQuery(query, "action");
        if (RequiresReviewActionConfirmation(action)
            && !IsConfirmationAccepted(query))
        {
            return new WebReviewActionResult
            {
                Success = false,
                ObjectType = GetQuery(query, "objectType"),
                ObjectId = GetQuery(query, "objectId"),
                Action = action,
                Message = $"Confirmation required for '{action}' action.",
                RequiresConfirmation = true
            };
        }

        return await _webReviewService.ApplyActionAsync(new WebReviewActionRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ObjectType = GetQuery(query, "objectType"),
            ObjectId = GetQuery(query, "objectId"),
            Action = action,
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
        var sb = CreateShell("Поиск");
        sb.AppendLine("<h1>Поиск</h1>");
        sb.AppendLine($"<p>Запрос: {E(model.Query)}. Найдено: {model.Results.Count}.</p>");
        sb.AppendLine($"<p>Фильтры: тип объекта — {E(ToRuSearchObjectType(model.ObjectTypeFilter))}; статус — {E(ToRuWorkflowStatus(model.StatusFilter))}; приоритет — {E(ToRuWorkflowPriority(model.PriorityFilter))}.</p>");
        foreach (var result in model.Results)
        {
            sb.AppendLine("<article>");
            sb.AppendLine($"<h3>{E(ToRuSearchObjectType(result.ObjectType))}</h3>");
            if (!string.IsNullOrWhiteSpace(result.Title))
            {
                sb.AppendLine($"<p><strong>{E(result.Title)}</strong></p>");
            }
            sb.AppendLine($"<p>{E(result.Summary)}</p>");
            sb.AppendLine($"<p>Статус: {E(ToRuWorkflowStatus(result.Status))}. Приоритет: {E(ToRuWorkflowPriority(result.Priority))}. Обновлено: {result.UpdatedAt:yyyy-MM-dd HH:mm} UTC.</p>");
            sb.AppendLine($"<p><a href='{E(result.Link)}'>Открыть объект</a></p>");
            sb.AppendLine("<details><summary>Технические детали</summary>");
            sb.AppendLine($"<p>Тип: {E(result.ObjectType)}; ID: {E(result.ObjectId)}</p>");
            sb.AppendLine("</details>");
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
        var sb = CreateShell("Досье");
        sb.AppendLine("<h1>Досье</h1>");
        if (!string.IsNullOrWhiteSpace(model.Summary))
        {
            sb.AppendLine($"<p><strong>Сводка:</strong> {E(model.Summary)}</p>");
        }

        var brief = BuildDossierBrief(model);
        if (brief.Count > 0)
        {
            sb.AppendLine("<section><h2>Коротко для оператора</h2>");
            foreach (var item in brief)
            {
                sb.AppendLine($"<div><strong>{E(item.Label)}:</strong> {E(item.Value)}</div>");
            }
            sb.AppendLine("</section>");
        }

        RenderDossierInsightSection(sb, "Что важно сейчас", model.PracticalInterpretation, promoteStrongSignals: true);
        RenderDossierInsightSection(sb, "Подтвержденные наблюдения", model.ObservedFacts, promoteStrongSignals: true);
        RenderDossierInsightSection(sb, "Картина взаимодействия", model.RelationshipRead);
        RenderDossierInsightSection(sb, "Заметные события", model.NotableEvents);
        RenderDossierInsightSection(sb, "Вероятная интерпретация", model.LikelyInterpretation, promoteStrongSignals: true);
        RenderDossierInsightSection(sb, "Неопределенности и альтернативы", model.Uncertainties, promoteStrongSignals: true);
        RenderDossierInsightSection(sb, "Чего не хватает", model.MissingInformation);

        if (model.ObservedFacts.Count == 0
            && model.LikelyInterpretation.Count == 0
            && model.Uncertainties.Count == 0
            && model.MissingInformation.Count == 0)
        {
            RenderDossierSection(sb, "Подтверждено", model.Confirmed);
            RenderDossierSection(sb, "Гипотезы", model.Hypotheses);
            RenderDossierSection(sb, "Конфликты", model.Conflicts);
        }

        return CloseShell(sb);
    }

    private static void RenderDossierInsightSection(
        StringBuilder sb,
        string title,
        IReadOnlyCollection<DossierInsightReadModel> rows,
        bool promoteStrongSignals = false)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var ranked = rows
            .OrderByDescending(x => SignalRank(x.SignalStrength))
            .ThenByDescending(x => x.UpdatedAt)
            .ToList();
        var primaryRows = ranked
            .Where(x => !IsLowValueDossierInsight(x))
            .Take(4)
            .ToList();
        var hiddenRows = ranked
            .Where(x => !primaryRows.Any(y => y == x))
            .ToList();

        if (promoteStrongSignals && primaryRows.Count == 0)
        {
            primaryRows = ranked.Take(2).ToList();
            hiddenRows = ranked.Skip(primaryRows.Count).ToList();
        }

        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var row in primaryRows)
        {
            RenderDossierInsightRow(sb, row);
        }

        if (hiddenRows.Count > 0)
        {
            sb.AppendLine($"<details><summary>Дополнительно: {hiddenRows.Count} менее значимых пунктов</summary>");
            foreach (var row in hiddenRows)
            {
                RenderDossierInsightRow(sb, row);
            }
            sb.AppendLine("</details>");
        }

        sb.AppendLine("</section>");
    }

    private static void RenderDossierInsightRow(StringBuilder sb, DossierInsightReadModel row)
    {
        sb.AppendLine("<article>");
        sb.AppendLine($"<h3>{E(row.Title)}</h3>");
        sb.AppendLine($"<p>{E(CleanOperatorText(row.Detail))}</p>");
        sb.AppendLine($"<p>Сила сигнала: <strong>{E(ToRuSignalStrength(row.SignalStrength))}</strong>.</p>");
        if (!string.IsNullOrWhiteSpace(row.Link))
        {
            sb.AppendLine($"<p><a href='{E(row.Link)}'>Открыть источник</a></p>");
        }
        if (!string.IsNullOrWhiteSpace(row.SourceObjectType) || !string.IsNullOrWhiteSpace(row.SourceObjectId))
        {
            sb.AppendLine("<details><summary>Технические детали</summary>");
            sb.AppendLine($"<p>Источник: {E(row.SourceObjectType ?? "-")}:{E(row.SourceObjectId ?? "-")}</p>");
            sb.AppendLine("</details>");
        }
        sb.AppendLine("</article>");
    }

    private static void RenderDossierSection(StringBuilder sb, string title, IReadOnlyCollection<DossierItemReadModel> rows)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var row in rows)
        {
            sb.AppendLine($"<div>{E(ToRuSearchObjectType(row.ObjectType))} | {E(row.Title)} | {E(row.Summary)} | <a href='{E(row.Link)}'>Открыть</a></div>");
        }

        if (rows.Count == 0)
        {
            sb.AppendLine("<p>Записей пока нет.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static string RenderCaseQueue(Stage6CaseQueueReadModel model, WebReadRequest request)
    {
        var sb = CreateShell("Очередь кейсов");
        var currentQueuePath = BuildQueueScopedPath(
            request,
            model.StatusFilter,
            model.PriorityFilter,
            model.CaseTypeFilter,
            model.ArtifactTypeFilter,
            model.Query,
            model.SortBy,
            model.SortDirection);
        var activeLink = BuildQueueScopedPath(request, "active", null, null, null, null, model.SortBy, model.SortDirection);
        var needsInputLink = BuildQueueScopedPath(request, Stage6CaseStatuses.NeedsUserInput, null, null, null, null, model.SortBy, model.SortDirection);
        var readyLink = BuildQueueScopedPath(request, Stage6CaseStatuses.Ready, null, null, null, null, model.SortBy, model.SortDirection);
        var blockingLink = BuildQueueScopedPath(request, model.StatusFilter, "blocking", null, null, null, model.SortBy, model.SortDirection);
        var allLink = BuildQueueScopedPath(request, "active", null, null, null, null, "priority", "desc");

        sb.AppendLine("<h1>Очередь кейсов</h1>");
        sb.AppendLine("<p><em>Операционная очередь Stage 6</em></p>");
        sb.AppendLine($"<p>Сортировка: {E(ToRuSortBy(model.SortBy))}, {E(ToRuSortDirection(model.SortDirection))}.</p>");
        sb.AppendLine($"<p>Показано: {model.VisibleCases}. Активных: {model.TotalCases}. Требуют ответа: {model.NeedsInputCases}. Готовы: {model.ReadyCases}.</p>");
        sb.AppendLine("<section><h2>Фильтры</h2>");
        sb.AppendLine("<form method='get' action='/inbox'>");
        sb.AppendLine($"<input type='hidden' name='caseScopeId' value='{E(request.CaseId.ToString())}'>");
        sb.AppendLine($"<input type='hidden' name='chatId' value='{E(request.ChatId.ToString())}'>");
        sb.AppendLine("<label>Статус <select name='status'>");
        RenderOption(sb, "active", model.StatusFilter);
        RenderOption(sb, "all", model.StatusFilter);
        RenderOption(sb, Stage6CaseStatuses.NeedsUserInput, model.StatusFilter);
        RenderOption(sb, Stage6CaseStatuses.Ready, model.StatusFilter);
        RenderOption(sb, Stage6CaseStatuses.New, model.StatusFilter);
        RenderOption(sb, Stage6CaseStatuses.Stale, model.StatusFilter);
        RenderOption(sb, Stage6CaseStatuses.Resolved, model.StatusFilter);
        RenderOption(sb, Stage6CaseStatuses.Rejected, model.StatusFilter);
        sb.AppendLine("</select></label> ");
        sb.AppendLine("<label>Приоритет <select name='priority'>");
        RenderOption(sb, string.Empty, "все", model.PriorityFilter);
        RenderOption(sb, "blocking", "блокирующий", model.PriorityFilter);
        RenderOption(sb, "important", "важный", model.PriorityFilter);
        RenderOption(sb, "optional", "необязательный", model.PriorityFilter);
        sb.AppendLine("</select></label> ");
        sb.AppendLine($"<label>Тип кейса <input name='caseType' value='{E(model.CaseTypeFilter ?? string.Empty)}' placeholder='например: clarification_missing_data'></label> ");
        sb.AppendLine("<label>Артефакт <select name='artifactType'>");
        RenderOption(sb, string.Empty, "все", model.ArtifactTypeFilter);
        RenderOption(sb, Stage6ArtifactTypes.Dossier, "досье", model.ArtifactTypeFilter);
        RenderOption(sb, Stage6ArtifactTypes.CurrentState, "текущее состояние", model.ArtifactTypeFilter);
        RenderOption(sb, Stage6ArtifactTypes.Strategy, "стратегия", model.ArtifactTypeFilter);
        RenderOption(sb, Stage6ArtifactTypes.Draft, "черновик", model.ArtifactTypeFilter);
        RenderOption(sb, Stage6ArtifactTypes.Review, "ревью", model.ArtifactTypeFilter);
        sb.AppendLine("</select></label> ");
        sb.AppendLine($"<label>Поиск <input name='q' value='{E(model.Query ?? string.Empty)}' placeholder='текст'></label> ");
        sb.AppendLine("<label>Сортировка <select name='sortBy'>");
        RenderOption(sb, "priority", "приоритет", model.SortBy);
        RenderOption(sb, "updated", "обновление", model.SortBy);
        RenderOption(sb, "status", "статус", model.SortBy);
        RenderOption(sb, "confidence", "уверенность", model.SortBy);
        sb.AppendLine("</select></label> ");
        sb.AppendLine("<label>Направление <select name='sortDirection'>");
        RenderOption(sb, "desc", "сначала новые", model.SortDirection);
        RenderOption(sb, "asc", "сначала старые", model.SortDirection);
        sb.AppendLine("</select></label> ");
        sb.AppendLine("<button type='submit'>Применить</button>");
        sb.AppendLine($" <a href='{E(allLink)}'>Сбросить</a>");
        sb.AppendLine("</form>");
        RenderStateCallout(
            sb,
            "info",
            "Текущий срез",
            $"Статус: {ToRuCaseStatus(model.StatusFilter)}. Приоритет: {ToRuPriority(model.PriorityFilter)}. Тип кейса: {E(model.CaseTypeFilter ?? "все")}. Артефакт: {ToRuArtifactType(model.ArtifactTypeFilter)}.",
            ("Сбросить фильтры", allLink));
        sb.AppendLine($"<p>Быстрые фильтры: <a href='{E(activeLink)}'>активные</a> | <a href='{E(needsInputLink)}'>требуют ответа</a> | <a href='{E(readyLink)}'>готовые</a> | <a href='{E(blockingLink)}'>блокирующие</a> | <a href='{E(BuildQueueScopedPath(request, model.StatusFilter, null, null, Stage6ArtifactTypes.CurrentState, null, model.SortBy, model.SortDirection))}'>по состоянию</a> | <a href='{E(BuildQueueScopedPath(request, model.StatusFilter, null, null, Stage6ArtifactTypes.Draft, null, model.SortBy, model.SortDirection))}'>по черновикам</a> | <a href='{E(allLink)}'>по умолчанию</a></p>");
        sb.AppendLine($"<p>Артефакты: <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Dossier, ["returnTo"] = currentQueuePath }))}'>досье</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.CurrentState, ["returnTo"] = currentQueuePath }))}'>текущее состояние</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Strategy, ["returnTo"] = currentQueuePath }))}'>стратегия</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Draft, ["returnTo"] = currentQueuePath }))}'>черновик</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Review, ["returnTo"] = currentQueuePath }))}'>ревью</a></p>");
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

        if (model.TotalCases == 0)
        {
            RenderStateCallout(
                sb,
                "empty",
                "Очередь пока пуста",
                "По текущей области еще нет кейсов для обработки. Обычно это значит, что апстрим-обработка еще не сформировала рабочие кейсы.",
                ("Открыть панель", BuildScopedPath("/dashboard", request)));
        }
        else if (model.Cases.Count == 0)
        {
            RenderStateCallout(
                sb,
                "empty",
                "Ничего не найдено по фильтрам",
                "Сбросьте часть фильтров или вернитесь к базовой очереди.",
                ("Сбросить фильтры", allLink),
                ("Открыть активные", activeLink));
            sb.AppendLine("<p>По этому набору фильтров кейсов нет.</p>");
        }
        else
        {
            RenderQueueSection(sb, $"Требуют ответа ({topNeedsInput.Count})", topNeedsInput, request, currentQueuePath);
            RenderQueueSection(sb, $"Готовые / Новые ({topReady.Count})", topReady, request, currentQueuePath);
            RenderQueueSection(sb, $"Остальные ({topOther.Count})", topOther, request, currentQueuePath);
        }

        return CloseShell(sb);
    }

    private static string RenderCaseDetail(Stage6CaseDetailReadModel model, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Детали кейса");
        sb.AppendLine("<h1>Детали кейса</h1>");
        if (!model.Exists)
        {
            sb.AppendLine("<h2>Case Not Found</h2>");
            sb.AppendLine("<section><h2>Кейс не найден</h2>");
            sb.AppendLine($"<p>{E(model.ReasonSummary)}</p>");
            sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>Назад в очередь</a></p>");
            sb.AppendLine("</section>");
            return CloseShell(sb);
        }

        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>Назад в очередь</a></p>");
        sb.AppendLine($"<p><strong>{E(ToRuCaseType(model.CaseType))}</strong> | {E(ToRuCaseStatus(model.Status))} | {E(ToRuPriority(model.Priority))} | уверенность: {E(ToRuConfidenceLabel(model.Confidence))}</p>");
        sb.AppendLine($"<p>{E(model.ReasonSummary)}</p>");
        if (!string.IsNullOrWhiteSpace(model.QuestionText))
        {
            sb.AppendLine($"<p><strong>Вопрос:</strong> {E(model.QuestionText)}</p>");
        }
        sb.AppendLine($"<p>Канал ответа: {E(model.ResponseMode ?? "-")}</p>");
        sb.AppendLine($"<p>Источник: {E(ToRuObjectType(model.SourceObjectType))} {(!string.IsNullOrWhiteSpace(model.SourceLink) ? $"<a href='{E(model.SourceLink)}'>история</a>" : string.Empty)}</p>");
        if (!string.IsNullOrWhiteSpace(model.SourceSummary))
        {
            sb.AppendLine($"<p>{E(model.SourceSummary)}</p>");
        }
        sb.AppendLine($"<p>Обновлено: {E(model.UpdatedAt.ToString("u"))}</p>");
        sb.AppendLine($"<p>Ключевая суть: {(model.Evidence.Count == 0 ? E(model.ReasonSummary) : E(string.Join(" | ", model.Evidence.Take(4).Select(x => x.Summary))))}</p>");
        sb.AppendLine("<section><h2>Evidence First Context</h2><h2>Контекст</h2>");
        sb.AppendLine($"<p>lifecycle: created={E(model.CreatedAt.ToString("u"))} | ready={E(model.ReadyAt?.ToString("u") ?? "-")} | resolved={E(model.ResolvedAt?.ToString("u") ?? "-")} | rejected={E(model.RejectedAt?.ToString("u") ?? "-")} | stale={E(model.StaleAt?.ToString("u") ?? "-")}</p>");
        sb.AppendLine($"<p>evidence window: from={E(model.EarliestEvidenceAtUtc?.ToString("u") ?? "-")} to={E(model.LatestEvidenceAtUtc?.ToString("u") ?? "-")}</p>");
        sb.AppendLine($"<p>participants: {(model.EvidenceParticipants.Count == 0 ? "-" : E(string.Join(" | ", model.EvidenceParticipants.Select(x => $"{x.SenderName} (msgs={x.MessageCount}, direct={x.EvidenceMessageCount})"))))}</p>");
        sb.AppendLine($"<p>subject refs: {(model.SubjectRefs.Count == 0 ? "-" : E(string.Join(", ", model.SubjectRefs)))} </p>");
        sb.AppendLine($"<p>reopen triggers: {(model.ReopenTriggers.Count == 0 ? "-" : E(string.Join("; ", model.ReopenTriggers)))} </p>");
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/case-evidence", request, new Dictionary<string, string?> { ["caseId"] = model.Id.ToString() }))}'>Открыть доказательства</a> | <a href='{E(BuildScopedPath("/timeline", request))}'>Таймлайн</a> | <a href='{E(BuildScopedPath("/network", request))}'>Сеть участников</a> | <a href='{E(BuildScopedPath("/history", request, new Dictionary<string, string?> { ["objectType"] = "stage6_case" }))}'>История кейса</a></p>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Основания</h2>");
        foreach (var evidence in model.Evidence.Take(12))
        {
            var link = string.IsNullOrWhiteSpace(evidence.Link) ? string.Empty : $" | <a href='{E(evidence.Link)}'>открыть</a>";
            var time = evidence.TimestampUtc.HasValue ? evidence.TimestampUtc.Value.ToString("yyyy-MM-dd HH:mm") : "-";
            var refTrail = BuildScopedPath("/history-object", request, new Dictionary<string, string?>
            {
                ["objectType"] = "stage6_case",
                ["objectId"] = model.Id.ToString()
            });
            sb.AppendLine($"<div>{E(ToRuEvidenceClass(evidence.SourceClass))} | {E(evidence.Title)} | {E(evidence.Summary)} | {E(time)}{link} | <a href='{E(refTrail)}'>история кейса</a></div>");
        }
        if (model.Evidence.Count == 0)
        {
            sb.AppendLine("<p>Явные основания не найдены. Показана причина кейса.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Evidence Drill-Down: Message Window</h2>");
        foreach (var row in model.EvidenceMessages)
        {
            var messageLink = BuildScopedPath("/search", request, new Dictionary<string, string?>
            {
                ["objectType"] = "message",
                ["q"] = row.MessageId.ToString()
            });
            sb.AppendLine($"<div>{(row.IsDirectEvidence ? "<strong>evidence</strong>" : "context")} | {E(row.TimestampUtc.ToString("yyyy-MM-dd HH:mm"))} | {E(row.SenderName)} | msg#{row.MessageId} | {E(row.TextSnippet)} | <a href='{E(messageLink)}'>open message</a></div>");
        }
        if (model.EvidenceMessages.Count == 0)
        {
            sb.AppendLine("<p>No message-level evidence window available for this case.</p>");
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

        sb.AppendLine("<section><h2>Linked Cases and Objects</h2>");
        foreach (var linkedCase in model.LinkedCases)
        {
            var linkedCaseLink = BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
            {
                ["caseId"] = linkedCase.Id.ToString()
            });
            sb.AppendLine($"<div>case <a href='{E(linkedCaseLink)}'>{linkedCase.Id}</a> | {E(linkedCase.CaseType)} | {E(linkedCase.Status)} | {E(linkedCase.ReasonSummary)}</div>");
        }
        foreach (var linked in model.LinkedObjects)
        {
            var target = linked.Link ?? BuildScopedPath("/history-object", request, new Dictionary<string, string?>
            {
                ["objectType"] = linked.LinkedObjectType,
                ["objectId"] = linked.LinkedObjectId
            });
            sb.AppendLine($"<div>{E(linked.LinkRole)} | {E(linked.LinkedObjectType)}:{E(linked.LinkedObjectId)} | {E(linked.Summary)} | <a href='{E(target)}'>open</a></div>");
        }
        if (model.LinkedCases.Count == 0 && model.LinkedObjects.Count == 0)
        {
            sb.AppendLine("<p>No linked cases/objects were resolved for this case.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Context Notes</h2>");
        foreach (var entry in model.ContextEntries)
        {
            sb.AppendLine($"<div>{E(entry.SourceKind)} via {E(entry.EnteredVia)} | source={E(entry.SourceType)}:{E(entry.SourceId)} | certainty={entry.UserReportedCertainty:0.00} | at={E(entry.CreatedAt.ToString("yyyy-MM-dd HH:mm"))}</div>");
            sb.AppendLine($"<div>{E(entry.ContentText)}</div>");
            if (entry.AppliesToRefs.Count > 0)
            {
                sb.AppendLine($"<div>applies_to: {E(string.Join(", ", entry.AppliesToRefs))}</div>");
            }
            if (entry.ConflictsWithRefs.Count > 0)
            {
                sb.AppendLine($"<div>conflicts: {E(string.Join(", ", entry.ConflictsWithRefs))}</div>");
            }
            if (entry.SupersedesContextEntryId.HasValue)
            {
                sb.AppendLine($"<div>supersedes: {E(entry.SupersedesContextEntryId.Value.ToString())}</div>");
            }
            if (!string.IsNullOrWhiteSpace(entry.StructuredPayloadJson))
            {
                sb.AppendLine($"<details><summary>structured payload</summary><pre>{E(entry.StructuredPayloadJson!)}</pre></details>");
            }
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
        sb.AppendLine("<h3>Long-form Context / Correction</h3>");
        sb.AppendLine("<form method='get' action='/case-action'>");
        sb.AppendLine($"<input type='hidden' name='caseScopeId' value='{E(request.CaseId.ToString())}'>");
        sb.AppendLine($"<input type='hidden' name='chatId' value='{E(request.ChatId.ToString())}'>");
        sb.AppendLine($"<input type='hidden' name='caseId' value='{E(model.Id.ToString())}'>");
        sb.AppendLine("<input type='hidden' name='action' value='annotate'>");
        sb.AppendLine("<p><label>mode <select name='contextEntryMode'><option value='context'>context</option><option value='correction'>correction</option></select></label> ");
        sb.AppendLine("<label>source kind <select name='contextSourceKind'><option value='long_form_context'>long_form_context</option><option value='user_context_correction'>user_context_correction</option><option value='operator_annotation'>operator_annotation</option></select></label></p>");
        sb.AppendLine("<p><label>context/correction text<br><textarea name='note' rows='6' cols='110'></textarea></label></p>");
        sb.AppendLine("<p><label>correction target ref <input name='correctionTargetRef' value=''></label> ");
        sb.AppendLine("<label>correction summary <input name='correctionSummary' value=''></label></p>");
        sb.AppendLine("<p><label>certainty 0..1 <input name='contextCertainty' value='0.85'></label> ");
        sb.AppendLine("<label>reason <input name='reason' value=''></label></p>");
        sb.AppendLine("<button type='submit'>save long-form context</button>");
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
            sb.AppendLine("<p><label>answer type <select name='answerType'><option value='text'>text</option><option value='long_form'>long_form</option><option value='correction'>correction</option></select></label> ");
            sb.AppendLine("<label>source class <select name='sourceClass'><option value='operator_web'>operator_web</option><option value='user_reported_context'>user_reported_context</option><option value='user_context_correction'>user_context_correction</option></select></label></p>");
            sb.AppendLine("<p><label>answer<br><textarea name='answer' rows='5' cols='100'></textarea></label></p>");
            sb.AppendLine("<label>reason <input name='reason' value=''></label> ");
            sb.AppendLine("<label>useful <select name='useful'><option value='true'>true</option><option value='false'>false</option></select></label> ");
            sb.AppendLine("<button type='submit'>submit answer</button>");
            sb.AppendLine("</form>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Deep Review Snapshot</h2><h2>Feedback</h2>");
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

    private static string RenderArtifactDetail(Stage6ArtifactDetailReadModel model, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Детали артефакта");
        sb.AppendLine("<h1>Artifact Detail</h1>");
        sb.AppendLine($"<h1>Артефакт: {E(ToRuArtifactType(model.ArtifactType))}</h1>");
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" }))}'>Назад в очередь</a></p>");
        sb.AppendLine($"<p>Виды: <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Dossier }))}'>досье</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.CurrentState }))}'>текущее состояние</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Strategy }))}'>стратегия</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Draft }))}'>черновик</a> | <a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = Stage6ArtifactTypes.Review }))}'>ревью</a></p>");
        sb.AppendLine($"<p>Статус: {E(ToRuArtifactStatus(model.Status))}{(string.IsNullOrWhiteSpace(model.Reason) ? string.Empty : $". Причина: {E(model.Reason)}")}</p>");
        sb.AppendLine($"<p>{E(model.Summary)}</p>");
        if (!model.Exists)
        {
            sb.AppendLine("<section><h2>Артефакт пока недоступен</h2>");
            sb.AppendLine("<p>Artifact Unavailable</p>");
            sb.AppendLine("<p>Данных для этого артефакта пока нет. Вернитесь в очередь или обновите кейс после новых сообщений.</p>");
            sb.AppendLine("</section>");
            return CloseShell(sb);
        }
        sb.AppendLine($"<p>Уверенность: {E(ToRuConfidenceLabel(model.ConfidenceLabel))}</p>");
        sb.AppendLine($"<p>Сформирован: {E(model.GeneratedAt?.ToString("u") ?? "-")} | Обновлен: {E(model.RefreshedAt?.ToString("u") ?? "-")} | Последние данные: {E(model.LatestEvidenceAtUtc?.ToString("u") ?? "-")}</p>");
        var refreshLink = BuildScopedPath("/artifact-action", request, new Dictionary<string, string?>
        {
            ["artifactType"] = model.ArtifactType,
            ["action"] = "refresh"
        });
        sb.AppendLine($"<p><a href='{E(refreshLink)}'>Обновить артефакт</a></p>");
        sb.AppendLine("<section><h2>Ключевые основания</h2>");
        foreach (var evidence in model.Evidence.Take(12))
        {
            var open = string.IsNullOrWhiteSpace(evidence.Link) ? string.Empty : $" | <a href='{E(evidence.Link)}'>открыть</a>";
            sb.AppendLine($"<div>{E(ToRuEvidenceClass(evidence.SourceClass))} | {E(evidence.Title)} | {E(evidence.Summary)}{open}</div>");
        }
        if (model.Evidence.Count == 0)
        {
            sb.AppendLine("<p>Пока нет подробностей по основаниям.</p>");
        }
        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Связанные кейсы</h2>");
        foreach (var item in model.LinkedCases)
        {
            var link = BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
            {
                ["caseId"] = item.Id.ToString()
            });
            var drilldown = BuildScopedPath("/case-evidence", request, new Dictionary<string, string?>
            {
                ["caseId"] = item.Id.ToString()
            });
            sb.AppendLine($"<div><a href='{E(link)}'>Кейс {E(ShortId(item.Id.ToString()))}</a> | {E(ToRuCaseType(item.CaseType))} | {E(ToRuCaseStatus(item.Status))} | {E(item.ReasonSummary)} | <a href='{E(drilldown)}'>доказательства</a></div>");
        }
        if (model.LinkedCases.Count == 0)
        {
            sb.AppendLine("<p>Связанные активные кейсы не найдены.</p>");
        }
        sb.AppendLine("</section>");
        sb.AppendLine("<section><h2>Обратная связь</h2>");
        foreach (var item in model.Feedback.Take(20))
        {
            sb.AppendLine($"<div>{item.CreatedAt:yyyy-MM-dd HH:mm} | {E(ToRuFeedbackKind(item.FeedbackKind))} | полезно: {E(item.IsUseful?.ToString() ?? "-")} | {E(item.Note ?? "-")}</div>");
        }
        if (model.Feedback.Count == 0)
        {
            sb.AppendLine("<p>Обратная связь по артефакту пока не добавлена.</p>");
        }
        sb.AppendLine("</section>");
        sb.AppendLine("<details><summary>Технические детали</summary>");
        sb.AppendLine($"<p>payload: {E(model.PayloadObjectType ?? "-")}:{E(model.PayloadObjectId ?? "-")}</p>");
        sb.AppendLine($"<pre>{E(model.PayloadJson)}</pre>");
        sb.AppendLine("</details>");
        return CloseShell(sb);
    }

    private static string RenderCaseEvidence(Stage6CaseDetailReadModel model, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Case Evidence");
        sb.AppendLine("<h1>Case Evidence Drill-Down</h1>");
        if (!model.Exists)
        {
            sb.AppendLine($"<p>{E(model.ReasonSummary)}</p>");
            return CloseShell(sb);
        }

        sb.AppendLine($"<p>case={model.Id} | type={E(model.CaseType)} | status={E(model.Status)}</p>");
        sb.AppendLine($"<p><a href='{E(BuildScopedPath("/case-detail", request, new Dictionary<string, string?> { ["caseId"] = model.Id.ToString() }))}'>back to case detail</a></p>");
        sb.AppendLine($"<p>evidence window: from={E(model.EarliestEvidenceAtUtc?.ToString("u") ?? "-")} to={E(model.LatestEvidenceAtUtc?.ToString("u") ?? "-")}</p>");

        sb.AppendLine("<section><h2>Evidence Refs</h2>");
        foreach (var evidence in model.Evidence)
        {
            var open = string.IsNullOrWhiteSpace(evidence.Link) ? "-" : $"<a href='{E(evidence.Link)}'>open</a>";
            var at = evidence.TimestampUtc.HasValue ? evidence.TimestampUtc.Value.ToString("yyyy-MM-dd HH:mm") : "-";
            sb.AppendLine($"<div>{E(evidence.SourceClass)} | {E(evidence.Reference)} | {E(evidence.Title)} | at={E(at)} | {E(evidence.Summary)} | {open}</div>");
        }
        if (model.Evidence.Count == 0)
        {
            sb.AppendLine("<p>No evidence refs.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Timeline / Messages</h2>");
        foreach (var message in model.EvidenceMessages)
        {
            var messageLink = BuildScopedPath("/search", request, new Dictionary<string, string?>
            {
                ["objectType"] = "message",
                ["q"] = message.MessageId.ToString()
            });
            sb.AppendLine($"<div>{(message.IsDirectEvidence ? "<strong>evidence</strong>" : "context")} | {E(message.TimestampUtc.ToString("yyyy-MM-dd HH:mm"))} | {E(message.SenderName)} | msg#{message.MessageId} | {E(message.TextSnippet)} | <a href='{E(messageLink)}'>open message</a></div>");
        }
        if (model.EvidenceMessages.Count == 0)
        {
            sb.AppendLine("<p>No message timeline context for this case.</p>");
        }
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>People Context</h2>");
        foreach (var person in model.EvidenceParticipants)
        {
            sb.AppendLine($"<div>{E(person.SenderName)} | sender_id={person.SenderId} | messages={person.MessageCount} | direct_evidence={person.EvidenceMessageCount}</div>");
        }
        if (model.EvidenceParticipants.Count == 0)
        {
            sb.AppendLine("<p>No participant summary available.</p>");
        }
        sb.AppendLine("</section>");
        return CloseShell(sb);
    }

    private static string RenderCaseAction(WebStage6CaseActionResult result, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Действие по кейсу");
        var returnTo = ResolveReturnToPath(query, request);
        var detailLink = result.Stage6CaseId == Guid.Empty
            ? string.Empty
            : BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
            {
                ["caseId"] = result.Stage6CaseId.ToString(),
                ["returnTo"] = returnTo
            });
        sb.AppendLine("<h1>Действие по кейсу</h1>");
        sb.AppendLine("<h1>Case Action</h1>");
        if (result.RequiresConfirmation)
        {
            var proceed = BuildPathFromCurrentQuery("/case-action", query, new Dictionary<string, string?> { ["confirm"] = "1" });
            sb.AppendLine("<p>Confirmation Required</p>");
            RenderStateCallout(
                sb,
                "warning",
                "Требуется подтверждение",
                string.IsNullOrWhiteSpace(result.Message) ? "Это действие изменит статус кейса." : result.Message,
                ("Подтвердить", proceed),
                ("Отмена", string.IsNullOrWhiteSpace(detailLink) ? returnTo : detailLink));
            return CloseShell(sb);
        }

        RenderStateCallout(
            sb,
            result.Success ? "success" : "error",
            result.Success ? "Действие выполнено" : "Действие не выполнено",
            $"Кейс: {ShortId(result.Stage6CaseId.ToString())}. {result.Message}",
            ("Назад в очередь", returnTo));
        sb.AppendLine($"<p>success={result.Success}</p>");
        if (result.RefreshedArtifactTypes.Count > 0)
        {
            sb.AppendLine($"<p>Обновлены артефакты: {E(string.Join(", ", result.RefreshedArtifactTypes.Select(ToRuArtifactType)))}</p>");
        }
        if (!string.IsNullOrWhiteSpace(detailLink))
        {
            sb.AppendLine($"<p><a href='{E(detailLink)}'>К деталям кейса</a></p>");
        }
        return CloseShell(sb);
    }

    private static string RenderClarificationAnswer(WebStage6ClarificationAnswerResult result, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Ответ на уточнение");
        var returnTo = ResolveReturnToPath(query, request);
        var detailLink = result.Stage6CaseId == Guid.Empty
            ? string.Empty
            : BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
            {
                ["caseId"] = result.Stage6CaseId.ToString(),
                ["returnTo"] = returnTo
            });
        sb.AppendLine("<h1>Ответ на уточнение</h1>");
        sb.AppendLine("<h1>Clarification Answer</h1>");
        if (result.RequiresConfirmation)
        {
            var proceed = BuildPathFromCurrentQuery("/clarification-answer", query, new Dictionary<string, string?> { ["confirm"] = "1" });
            RenderStateCallout(
                sb,
                "warning",
                "Отправить ответ?",
                "Ответ будет сохранен, а кейс может быть закрыт.",
                ("Подтвердить ответ", proceed),
                ("Отмена", string.IsNullOrWhiteSpace(detailLink) ? returnTo : detailLink));
            sb.AppendLine($"<p>Предпросмотр ответа: {E(BuildSnippet(result.AnswerValue, 220))}</p>");
            return CloseShell(sb);
        }

        RenderStateCallout(
            sb,
            result.Success ? "success" : "error",
            result.Success ? "Ответ сохранен" : "Не удалось сохранить ответ",
            $"Кейс: {ShortId(result.Stage6CaseId.ToString())}. {result.Message}",
            ("Назад в очередь", returnTo));
        if (!string.IsNullOrWhiteSpace(result.QuestionText))
        {
            sb.AppendLine($"<p>Вопрос: {E(result.QuestionText)}</p>");
            sb.AppendLine($"<p>Ответ: {E(result.AnswerValue)}</p>");
        }
        if (result.RecomputeTargets.Count > 0)
        {
            sb.AppendLine($"<p>Пересчитаны цели: {E(string.Join(", ", result.RecomputeTargets))}</p>");
        }
        if (result.RefreshedArtifactTypes.Count > 0)
        {
            sb.AppendLine($"<p>Помечены на обновление: {E(string.Join(", ", result.RefreshedArtifactTypes.Select(ToRuArtifactType)))}</p>");
        }
        if (!string.IsNullOrWhiteSpace(detailLink))
        {
            sb.AppendLine($"<p><a href='{E(detailLink)}'>К деталям кейса</a></p>");
        }
        return CloseShell(sb);
    }

    private static string RenderArtifactAction(WebStage6ArtifactActionResult result, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Действие по артефакту");
        var returnTo = ResolveReturnToPath(query, request);
        sb.AppendLine("<h1>Действие по артефакту</h1>");
        RenderStateCallout(
            sb,
            result.Success ? "success" : "error",
            result.Success ? "Действие выполнено" : "Действие не выполнено",
            $"Артефакт: {E(ToRuArtifactType(result.ArtifactType))}. {result.Message}",
            ("Назад в очередь", returnTo));
        if (!string.IsNullOrWhiteSpace(result.ArtifactType))
        {
            sb.AppendLine($"<p><a href='{E(BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?> { ["artifactType"] = result.ArtifactType, ["returnTo"] = returnTo }))}'>Открыть артефакт</a></p>");
        }
        return CloseShell(sb);
    }

    private static void RenderQueueSection(
        StringBuilder sb,
        string title,
        IReadOnlyCollection<Stage6CaseQueueItemReadModel> items,
        WebReadRequest request,
        string currentQueuePath)
    {
        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var item in items)
        {
            RenderQueueItemCard(sb, item, request, currentQueuePath);
        }

        if (items.Count == 0)
        {
            sb.AppendLine("<p>В этом разделе нет кейсов по текущим фильтрам.</p>");
        }

        sb.AppendLine("</section>");
    }

    private static void RenderQueueItemCard(StringBuilder sb, Stage6CaseQueueItemReadModel item, WebReadRequest request, string currentQueuePath)
    {
        var objectTrail = BuildScopedPath("/history-object", request, new Dictionary<string, string?>
        {
            ["objectType"] = item.SourceObjectType,
            ["objectId"] = item.SourceObjectId
        });
        var detailLink = BuildScopedPath("/case-detail", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["returnTo"] = currentQueuePath
        });
        var resolveLink = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["action"] = "resolve",
            ["returnTo"] = currentQueuePath
        });
        var rejectLink = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["action"] = "reject",
            ["returnTo"] = currentQueuePath
        });
        var refreshLink = BuildScopedPath("/case-action", request, new Dictionary<string, string?>
        {
            ["caseId"] = item.Id.ToString(),
            ["action"] = "refresh",
            ["returnTo"] = currentQueuePath
        });

        sb.AppendLine("<article>");
        sb.AppendLine($"<h3><a href='{E(detailLink)}'>{E(ToRuCaseType(item.CaseType))}</a> | {E(ToRuPriority(item.Priority))} | {E(ToRuCaseStatus(item.Status))}</h3>");
        sb.AppendLine($"<p>{E(item.ReasonSummary)}</p>");
        sb.AppendLine($"<p>Статус: {E(ToRuCaseStatus(item.Status))}. Приоритет: {E(ToRuPriority(item.Priority))}. Уверенность: {E(ToRuConfidenceLabel(item.Confidence))}.</p>");
        if (!string.IsNullOrWhiteSpace(item.QuestionText))
        {
            sb.AppendLine($"<p><strong>Вопрос:</strong> {E(item.QuestionText)}</p>");
        }

        sb.AppendLine($"<p>Уверенность: {E(ToRuConfidenceLabel(item.Confidence))}. Доказательств: {item.EvidenceCount}. Обновлено: {E(item.UpdatedAt.ToString("u"))}</p>");
        sb.AppendLine($"<p>Источник: <a href='{E(objectTrail)}'>{E(ToRuObjectType(item.SourceObjectType))}</a></p>");
        sb.AppendLine("<details><summary>Технические детали</summary>");
        sb.AppendLine($"<p>ID кейса: {E(item.Id.ToString())}; тип: {E(item.CaseType)}; статус: {E(item.Status)}; приоритет: {E(item.Priority)}</p>");
        sb.AppendLine("</details>");
        if (item.TargetArtifactTypes.Count > 0)
        {
            sb.AppendLine($"<p>Связанные артефакты: {E(string.Join(", ", item.TargetArtifactTypes.Select(ToRuArtifactType)))}</p>");
        }

        var detailActions = new List<string>
        {
            $"<a href='{E(detailLink)}'>Открыть</a>",
            $"<a href='{E(resolveLink)}'>Закрыть</a>",
            $"<a href='{E(rejectLink)}'>Отклонить</a>",
            $"<a href='{E(refreshLink)}'>Обновить</a>"
        };
        if (item.TargetArtifactTypes.Count > 0)
        {
            var artifactLink = BuildScopedPath("/artifact-detail", request, new Dictionary<string, string?>
            {
                ["artifactType"] = item.TargetArtifactTypes[0],
                ["returnTo"] = currentQueuePath
            });
            detailActions.Add($"<a href='{E(artifactLink)}'>Открыть артефакт</a>");
        }

        if (item.NeedsAnswer)
        {
            detailActions.Add("требуется ответ");
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

        sb.AppendLine("<details><summary>Технические детали</summary><p>Параметры scope доступны в URL этой страницы.</p></details>");
        return CloseShell(sb);
    }

    private static string RenderState(CurrentStateReadModel model)
    {
        var sb = CreateShell("Текущее состояние");
        sb.AppendLine("<h1>Текущее состояние</h1>");
        sb.AppendLine($"<p>dynamic: <strong>{E(model.DynamicLabel)}</strong></p>");
        sb.AppendLine($"<p>Динамика: <strong>{E(model.DynamicLabel)}</strong></p>");
        sb.AppendLine($"<p>Статус: {E(model.RelationshipStatus)}{(string.IsNullOrWhiteSpace(model.AlternativeStatus) ? string.Empty : $" (альтернатива: {E(model.AlternativeStatus!)})")}</p>");
        sb.AppendLine($"<p>Уверенность: {E(ToRuConfidenceLabel(model.Confidence))}</p>");
        sb.AppendLine($"<p>overall signal strength: <strong>{E(ToRuSignalStrength(model.OverallSignalStrength))}</strong></p>");
        sb.AppendLine($"<p>Сила сигнала: <strong>{E(ToRuSignalStrength(model.OverallSignalStrength))}</strong></p>");
        RenderStateInsightSection(sb, "Observed Facts / Подтвержденные факты", model.ObservedFacts);
        RenderStateInsightSection(sb, "Вероятная интерпретация", model.LikelyInterpretation);
        RenderStateInsightSection(sb, "Uncertainties / Alternative Readings / Неопределенности", model.Uncertainties);
        RenderStateInsightSection(sb, "Чего не хватает", model.MissingInformation);
        if (model.Scores.Count > 0)
        {
            sb.AppendLine("<details><summary>Технические скоры</summary>");
            foreach (var kv in model.Scores.OrderBy(x => x.Key))
            {
                sb.AppendLine($"<div>{E(kv.Key)}: {kv.Value:0.00}</div>");
            }

            sb.AppendLine("</details>");
        }

        return CloseShell(sb);
    }

    private static void RenderStateInsightSection(StringBuilder sb, string title, IReadOnlyCollection<StateInsightReadModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        sb.AppendLine($"<section><h2>{E(title)}</h2>");
        foreach (var row in rows)
        {
            sb.AppendLine($"<div><strong>{E(ToRuSignalStrength(row.SignalStrength))}</strong> | {E(row.Title)} | {E(row.Detail)}</div>");
        }

        sb.AppendLine("</section>");
    }

    private static string RenderTimeline(TimelineReadModel model)
    {
        var sb = CreateShell("Таймлайн");
        sb.AppendLine("<h1>Таймлайн</h1>");
        if (model.CurrentPeriod != null)
        {
            sb.AppendLine("<section><h2>Текущий период</h2>");
            sb.AppendLine(RenderPeriod(model.CurrentPeriod, emphasizeCurrent: true));
            sb.AppendLine("</section>");
        }

        if (model.PriorPeriods.Count > 0)
        {
            sb.AppendLine("<section><h2>Значимые прошлые периоды</h2>");
            foreach (var p in model.PriorPeriods)
            {
                sb.AppendLine(RenderPeriod(p));
            }
            if (model.HiddenLowValuePeriods > 0)
            {
                sb.AppendLine($"<p><em>Скрыто низкоприоритетных периодов: {model.HiddenLowValuePeriods}. Полная история остается доступной через /history и object trail.</em></p>");
            }
            sb.AppendLine("</section>");
        }

        if (model.Transitions.Count > 0)
        {
            sb.AppendLine("<section><h2>Переходы между периодами</h2>");
            foreach (var t in model.Transitions)
            {
                var status = t.IsResolved ? "подтвержден" : "требует проверки";
                sb.AppendLine($"<div>{E(ToRuTransitionType(t.TransitionType))} | {E(status)} | уверенность: {t.Confidence:0.00} | {E(CleanOperatorText(t.Summary))}</div>");
            }
            sb.AppendLine("</section>");
        }

        sb.AppendLine($"<p>Неподтвержденных переходов: {model.UnresolvedTransitions}</p>");
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
        var sb = CreateShell("Профили");
        sb.AppendLine("<h1>Профили</h1>");
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
        var sb = CreateShell("Стратегия");
        sb.AppendLine("<h1>Стратегия</h1>");
        sb.AppendLine("<h1>Strategy</h1>");
        sb.AppendLine($"<p>Уверенность: {E(ToRuConfidenceLabel(model.Confidence))}</p>");
        sb.AppendLine($"<h2>Основной ход</h2><p>{E(model.PrimarySummary)}</p><p><strong>Зачем:</strong> {E(model.PrimaryPurpose)}</p>");
        if (model.PrimaryRisks.Count > 0)
        {
            sb.AppendLine($"<p><strong>Риски:</strong> {E(string.Join(", ", model.PrimaryRisks.Select(ToRuRiskLabel)))}</p>");
        }
        sb.AppendLine($"<p>ethics: {E(model.EthicalContractSummary)}</p>");
        sb.AppendLine($"<p><strong>Этическая рамка:</strong> {E(model.EthicalContractSummary)}</p>");
        RenderStateInsightSection(sb, "Observed Facts / Подтвержденные факты", model.ObservedFacts);
        RenderStateInsightSection(sb, "Likely Interpretation / Вероятная интерпретация", model.LikelyInterpretation);
        RenderStateInsightSection(sb, "Неопределенности", model.Uncertainties);
        RenderStateInsightSection(sb, "Чего не хватает", model.MissingInformation);

        sb.AppendLine("<h2>Relational Patterns</h2>");
        sb.AppendLine("<h2>Паттерны взаимодействия</h2>");
        foreach (var pattern in model.RelationalPatterns)
        {
            sb.AppendLine($"<div>{E(pattern)}</div>");
        }

        sb.AppendLine("<h2>Альтернативы</h2>");
        foreach (var alt in model.Alternatives)
        {
            sb.AppendLine($"<div><strong>{E(alt.ActionType)}</strong> — {E(alt.Summary)}{(alt.Risks.Count == 0 ? string.Empty : $" | риски: {E(string.Join(", ", alt.Risks.Select(ToRuRiskLabel)))}")}</div>");
        }

        sb.AppendLine($"<h2>Ближайший шаг</h2><p>{E(model.MicroStep)}</p>");
        if (model.Horizon.Count > 0)
        {
            sb.AppendLine($"<h2>Горизонт</h2><p>{E(string.Join(" -> ", model.Horizon))}</p>");
        }

        sb.AppendLine($"<h2>Почему не другие варианты</h2><p>{E(model.WhyNotNotes)}</p>");
        var strategyTrail = $"/history-object?objectType=strategy_record&objectId={UrlEncode(model.RecordId.ToString())}";
        var chainFocus = $"/outcomes?strategyRecordId={UrlEncode(model.RecordId.ToString())}";
        sb.AppendLine($"<p><a href='{E(chainFocus)}'>Открыть цепочку исходов по стратегии</a> | <a href='{E(strategyTrail)}'>История стратегии</a></p>");
        sb.AppendLine("<details><summary>Технические детали</summary>");
        sb.AppendLine($"<p>strategy_record_id: {E(model.RecordId.ToString())}</p>");
        sb.AppendLine("</details>");
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
            sb.AppendLine($"<p><a href='{E(confirm)}'>confirm (confirm)</a> | <a href='{E(reject)}'>reject (confirm)</a> | <a href='{E(defer)}'>defer</a> | <a href='{E(trail)}'>history trail</a></p>");

            if (card.CanEdit && card.ObjectType == "period")
            {
                var editHref = $"/review-edit-period?case={request.CaseId}&chat={request.ChatId}&periodId={objectId}&label={UrlEncode("edited_label")}&summary={UrlEncode("edited from web review")}&reviewPriority=3&actor={UrlEncode(request.Actor)}";
                sb.AppendLine($"<p><a href='{E(editHref)}'>edit period (sample)</a></p>");
            }

            sb.AppendLine("</article>");
        }

        return CloseShell(sb);
    }

    private static string RenderReviewAction(WebReviewActionResult result, WebReadRequest request, IReadOnlyDictionary<string, string> query)
    {
        var sb = CreateShell("Review Action");
        sb.AppendLine("<h1>Review Action</h1>");
        if (result.RequiresConfirmation)
        {
            var proceed = BuildScopedPath("/review-action", request, new Dictionary<string, string?>
            {
                ["objectType"] = result.ObjectType,
                ["objectId"] = result.ObjectId,
                ["action"] = result.Action,
                ["confirm"] = "1"
            });
            sb.AppendLine("<section><h2>Confirmation Required</h2>");
            sb.AppendLine($"<p>{E(result.Message)}</p>");
            sb.AppendLine($"<p><a href='{E(proceed)}'>confirm and continue</a> | <a href='/review'>cancel</a></p>");
            sb.AppendLine("</section>");
            return CloseShell(sb);
        }

        sb.AppendLine($"<p>success: {result.Success}</p>");
        sb.AppendLine($"<p>object: {E(result.ObjectType)}:{E(result.ObjectId)}</p>");
        sb.AppendLine($"<p>action: {E(result.Action)}</p>");
        sb.AppendLine($"<p>message: {E(result.Message)}</p>");
        sb.AppendLine("<p><a href='/review'>Back to review board</a></p>");
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
        sb.AppendLine($"<section><h2>{E(ToRuProfileSubjectType(subject.SubjectType))}</h2>");
        sb.AppendLine($"<p>{E(subject.Summary)}</p>");
        sb.AppendLine($"<p>Уверенность: {E(ToRuConfidenceLabel(subject.Confidence))}. Стабильность: {E(ToRuConfidenceLabel(subject.Stability))}.</p>");
        sb.AppendLine("<h3>Ключевые характеристики</h3>");
        foreach (var trait in subject.TopTraits)
        {
            sb.AppendLine($"<div>{E(ToRuProfileTraitKey(trait.TraitKey))}: {E(trait.ValueLabel)} ({trait.Confidence:0.00}/{trait.Stability:0.00})</div>");
        }

        sb.AppendLine($"<p><strong>Что работает:</strong> {E(subject.WhatWorks)}</p>");
        sb.AppendLine($"<p><strong>Что не работает:</strong> {E(subject.WhatFails)}</p>");
        sb.AppendLine($"<p><strong>Паттерны участников:</strong> {E(subject.ParticipantPatterns)}</p>");
        sb.AppendLine($"<p><strong>Динамика пары:</strong> {E(subject.PairDynamics)}</p>");
        sb.AppendLine($"<p><strong>Повторяющиеся режимы взаимодействия:</strong> {E(subject.RepeatedInteractionModes)}</p>");
        sb.AppendLine($"<p><strong>Изменения во времени:</strong> {E(subject.ChangesOverTime)}</p>");
        sb.AppendLine("<details><summary>Технические детали</summary>");
        sb.AppendLine($"<p>subject_type: {E(subject.SubjectType)}; subject_id: {E(subject.SubjectId)}</p>");
        sb.AppendLine("</details>");
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    private static string RenderPeriod(TimelinePeriodReadModel period, bool emphasizeCurrent = false)
    {
        var heading = emphasizeCurrent
            ? $"{ToRuPeriodLabel(period.Label)} (актуально)"
            : ToRuPeriodLabel(period.Label);
        var summary = CleanOperatorText(period.Summary);
        var hooks = period.EvidenceHooks
            .Select(CleanEvidenceHook)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(3)
            .ToList();
        var evidenceSummary = hooks.Count == 0 ? "нет явных ссылок, ориентир по периоду остается рабочим" : string.Join("; ", hooks);
        return $"<article><h3>{E(heading)}</h3><p>{period.StartAt:yyyy-MM-dd}..{(period.EndAt?.ToString("yyyy-MM-dd") ?? "н.в.")} | уверенность: {period.InterpretationConfidence:0.00} | открытых вопросов: {period.OpenQuestionsCount}</p><p>{E(summary)}</p><p><strong>Основания:</strong> {E(evidenceSummary)}</p><details><summary>Техническая метка периода</summary><p>{E(period.Label)}</p></details></article>";
    }

    private static List<(string Label, string Value)> BuildDossierBrief(DossierReadModel model)
    {
        var brief = new List<(string Label, string Value)>();
        var known = PickTopInsight(model.ObservedFacts.Concat(model.NotableEvents));
        if (!string.IsNullOrWhiteSpace(known.Detail))
        {
            brief.Add(("Что известно", CleanOperatorText(known.Detail)));
        }

        var matters = PickTopInsight(model.PracticalInterpretation.Concat(model.LikelyInterpretation));
        if (!string.IsNullOrWhiteSpace(matters.Detail))
        {
            brief.Add(("Что важно", CleanOperatorText(matters.Detail)));
        }

        var uncertain = PickTopInsight(model.Uncertainties.Concat(model.MissingInformation));
        if (!string.IsNullOrWhiteSpace(uncertain.Detail))
        {
            brief.Add(("Что неясно", CleanOperatorText(uncertain.Detail)));
        }

        return brief;
    }

    private static DossierInsightReadModel PickTopInsight(IEnumerable<DossierInsightReadModel> rows)
    {
        return rows
                   .Where(x => !string.IsNullOrWhiteSpace(x.Detail))
                   .OrderByDescending(x => SignalRank(x.SignalStrength))
                   .ThenByDescending(x => x.UpdatedAt)
                   .FirstOrDefault()
               ?? new DossierInsightReadModel();
    }

    private static int SignalRank(string? signal)
    {
        return (signal ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "contradictory" => 4,
            "strong" => 3,
            "medium" => 2,
            _ => 1
        };
    }

    private static bool IsLowValueDossierInsight(DossierInsightReadModel row)
    {
        var detail = row.Detail ?? string.Empty;
        if (SignalRank(row.SignalStrength) >= 2)
        {
            return false;
        }

        return detail.Length < 45
               || detail.Contains("routine monitoring", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("limited", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("sparse", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("without explicit", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanOperatorText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Детали пока не зафиксированы.";
        }

        return value
            .Replace("Current data has few explicit hypotheses, so interpretation remains provisional.", "Явных гипотез пока немного, интерпретация остается рабочей.")
            .Replace("Open clarification without explicit rationale.", "Вопрос открыт и требует уточнения перед финальными выводами.")
            .Replace("No high-impact contradiction is visible in this dossier slice; continue with routine monitoring.", "Критичных противоречий сейчас не видно, достаточно штатного мониторинга.")
            .Trim();
    }

    private static string CleanEvidenceHook(string hook)
    {
        if (string.IsNullOrWhiteSpace(hook))
        {
            return string.Empty;
        }

        return hook
            .Replace("period:", "период ")
            .Replace("message:", "сообщение ")
            .Replace("conflict:", "конфликт ")
            .Replace("clarification_question:", "уточнение ")
            .Replace("state_snapshot:", "снимок состояния ")
            .Replace("hypothesis:", "гипотеза ")
            .Trim();
    }

    private static void RenderCompactPairs(IEnumerable<KeyValuePair<string, string>> pairs, StringBuilder sb)
    {
        foreach (var pair in pairs)
        {
            sb.AppendLine($"<div>{E(pair.Key)}: {E(pair.Value)}</div>");
        }
    }

    private static void RenderOption(StringBuilder sb, string value, string? selected)
    {
        var isSelected = value.Equals(selected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var selectedAttribute = isSelected ? " selected" : string.Empty;
        sb.AppendLine($"<option value='{E(value)}'{selectedAttribute}>{E(value)}</option>");
    }

    private static void RenderOption(StringBuilder sb, string value, string label, string? selected)
    {
        var isSelected = value.Equals(selected ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var selectedAttribute = isSelected ? " selected" : string.Empty;
        sb.AppendLine($"<option value='{E(value)}'{selectedAttribute}>{E(label)}</option>");
    }

    private static string BuildQueueScopedPath(
        WebReadRequest request,
        string? status,
        string? priority,
        string? caseType,
        string? artifactType,
        string? query,
        string? sortBy,
        string? sortDirection)
    {
        return BuildScopedPath("/inbox", request, new Dictionary<string, string?>
        {
            ["status"] = EmptyToNull(status),
            ["priority"] = EmptyToNull(priority),
            ["caseType"] = EmptyToNull(caseType),
            ["artifactType"] = EmptyToNull(artifactType),
            ["q"] = EmptyToNull(query),
            ["sortBy"] = EmptyToNull(sortBy),
            ["sortDirection"] = EmptyToNull(sortDirection)
        });
    }

    private static string ResolveReturnToPath(IReadOnlyDictionary<string, string> query, WebReadRequest request)
    {
        var returnTo = EmptyToNull(GetQuery(query, "returnTo"));
        if (!string.IsNullOrWhiteSpace(returnTo) && returnTo.StartsWith('/'))
        {
            return returnTo;
        }

        return BuildScopedPath("/inbox", request, new Dictionary<string, string?> { ["status"] = "active" });
    }

    private static string BuildPathFromCurrentQuery(
        string route,
        IReadOnlyDictionary<string, string> currentQuery,
        IReadOnlyDictionary<string, string?> overrides)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in currentQuery)
        {
            merged[pair.Key] = pair.Value;
        }

        foreach (var pair in overrides)
        {
            merged[pair.Key] = pair.Value;
        }

        var parts = merged
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => $"{UrlEncode(x.Key)}={UrlEncode(x.Value!)}")
            .ToList();

        return parts.Count == 0 ? route : $"{route}?{string.Join("&", parts)}";
    }

    private static string BuildSnippet(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..Math.Max(0, maxLength - 1)] + "…";
    }

    private static void RenderStateCallout(
        StringBuilder sb,
        string tone,
        string title,
        string message,
        params (string Label, string Href)[] actions)
    {
        var background = tone switch
        {
            "success" => "#e9f8ef",
            "warning" => "#fff7e8",
            "error" => "#fdeeee",
            "empty" => "#f6f8fc",
            _ => "#eef3fb"
        };

        sb.AppendLine($"<section style='background:{background}'><h2>{E(title)}</h2><p>{E(message)}</p>");
        if (actions.Length > 0)
        {
            var links = actions
                .Where(x => !string.IsNullOrWhiteSpace(x.Href))
                .Select(x => $"<a href='{E(x.Href)}'>{E(x.Label)}</a>")
                .ToList();
            if (links.Count > 0)
            {
                sb.AppendLine($"<p>{string.Join(" | ", links)}</p>");
            }
        }

        sb.AppendLine("</section>");
    }

    private static bool RequiresCaseActionConfirmation(string action)
    {
        return action.Trim().ToLowerInvariant() is "resolve" or "reject";
    }

    private static bool RequiresReviewActionConfirmation(string action)
    {
        return action.Trim().ToLowerInvariant() is "confirm" or "reject";
    }

    private static bool IsConfirmationAccepted(IReadOnlyDictionary<string, string> query)
    {
        var raw = EmptyToNull(GetQuery(query, "confirm"));
        return raw is not null
               && (raw.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                   || raw.Equals("true", StringComparison.OrdinalIgnoreCase));
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

    private static string? EmptyToNull(string? value)
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

    private static float? ParseNullableSingle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return float.TryParse(value, out var parsed) ? Math.Clamp(parsed, 0f, 1f) : null;
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
        sb.AppendLine("<style>body{font-family:ui-sans-serif,system-ui;background:#f4f6fa;max-width:1060px;margin:16px auto;padding:0 14px 20px;color:#1f2937;line-height:1.45}nav{display:flex;flex-wrap:wrap;gap:8px;padding:10px;background:#e9edf5;border:1px solid #d5dde9;border-radius:10px}nav a{font-size:.92rem;text-decoration:none;color:#1d3557;background:#fff;border:1px solid #ccd7ea;border-radius:8px;padding:4px 8px}section,article{background:#fff;border:1px solid #e1e7f0;border-radius:10px;padding:10px 12px;margin:10px 0}h1,h2,h3{margin:6px 0}code{background:#eef2f8;padding:1px 5px;border-radius:5px}form{display:flex;flex-wrap:wrap;gap:8px;align-items:center}input,select,textarea,button{font:inherit}textarea{max-width:100%}details{margin:10px 0}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<nav><a href='/dashboard'>Панель</a><a href='/search'>Поиск</a><a href='/dossier'>Досье</a><a href='/view/blocking'>Вид: блокирующие</a><a href='/view/current-period'>Вид: текущий</a><a href='/view/conflicts'>Вид: конфликты</a><a href='/inbox'>Очередь</a><a href='/history'>История</a><a href='/state'>Состояние</a><a href='/timeline'>Таймлайн</a><a href='/profiles'>Профили</a><a href='/clarifications'>Уточнения</a><a href='/strategy'>Стратегия</a><a href='/drafts-reviews'>Черновики/Ревью</a><a href='/outcomes'>Исходы</a><a href='/offline-events'>Офлайн</a><a href='/review'>Ревью</a><a href='/ops-budget'>Ops Бюджет</a><a href='/ops-eval'>Ops Eval</a><a href='/ops-ab-candidates'>Ops A/B</a></nav>");
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

    private static string ToRuSortBy(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "priority" => "приоритет",
            "updated" => "время обновления",
            "status" => "статус",
            "confidence" => "уверенность",
            _ => string.IsNullOrWhiteSpace(value) ? "приоритет" : value
        };
    }

    private static string ToRuSortDirection(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "asc" => "сначала старые",
            "desc" => "сначала новые",
            _ => string.IsNullOrWhiteSpace(value) ? "сначала новые" : value
        };
    }

    private static string ToRuSearchObjectType(string? objectType)
    {
        var normalized = (objectType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => "все",
            "stage6_case" => "кейс Stage 6",
            "inbox_item" => "элемент очереди",
            "clarification_question" => "вопрос на уточнение",
            "clarification_answer" => "ответ на уточнение",
            "period" => "период",
            "period_transition" => "переход периода",
            "hypothesis" => "гипотеза",
            "conflict_record" => "конфликт",
            "strategy_record" => "стратегия",
            "strategy_option" => "вариант стратегии",
            "draft_record" => "черновик",
            "draft_outcome" => "исход черновика",
            "profile_snapshot" => "снимок профиля",
            "profile_trait" => "характеристика профиля",
            "message" => "сообщение",
            _ => objectType ?? "объект"
        };
    }

    private static string ToRuWorkflowStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => "все",
            "open" => "открыт",
            "in_progress" => "в работе",
            "review" => "на проверке",
            "resolved" => "закрыт",
            "rejected" => "отклонен",
            "active" => "активный",
            "ready" => "готов",
            "new" => "новый",
            "needs_user_input" => "требует ответа",
            "stale" => "устарел",
            "closed" => "закрыт",
            _ => status ?? "все"
        };
    }

    private static string ToRuWorkflowPriority(string? priority)
    {
        var normalized = (priority ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => "все",
            "blocking" => "блокирующий",
            "important" => "важный",
            "optional" => "необязательный",
            "critical" => "критичный",
            "high" => "высокий",
            "medium" => "средний",
            "low" => "низкий",
            "primary" => "основной",
            "alternative" => "альтернативный",
            "sensitive" => "чувствительный",
            "standard" => "стандартный",
            _ => priority ?? "все"
        };
    }

    private static string ToRuPeriodLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "Период";
        }

        var normalized = label.Trim();
        if (normalized.StartsWith("period_", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized["period_".Length..];
            return int.TryParse(suffix, out var number)
                ? $"Период #{number}"
                : $"Период ({normalized})";
        }

        return normalized;
    }

    private static string ToRuTransitionType(string? transitionType)
    {
        var normalized = (transitionType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "continuation" => "продолжение",
            "shift" => "сдвиг",
            "rupture" => "разрыв",
            "de_escalation" => "снижение напряжения",
            "escalation" => "эскалация",
            _ => string.IsNullOrWhiteSpace(transitionType) ? "переход" : transitionType
        };
    }

    private static string ToRuProfileSubjectType(string? subjectType)
    {
        return (subjectType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "self" => "Профиль оператора",
            "other" => "Профиль собеседника",
            "pair" => "Профиль пары",
            "profile" => "Профиль",
            _ => string.IsNullOrWhiteSpace(subjectType) ? "Профиль" : subjectType
        };
    }

    private static string ToRuProfileTraitKey(string? traitKey)
    {
        return (traitKey ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "communication_style" => "Стиль коммуникации",
            "initiative" => "Инициативность",
            "responsiveness" => "Откликаемость",
            "openness" => "Открытость",
            "warmth" => "Теплота",
            "reciprocity" => "Взаимность",
            "ambiguity" => "Неопределенность",
            "avoidance_risk" => "Риск избегания",
            "external_pressure" => "Внешнее давление",
            "escalation_readiness" => "Готовность к эскалации",
            "tone_stability" => "Стабильность тона",
            "what_works" => "Что работает",
            "what_fails" => "Что не работает",
            "participant_patterns" => "Паттерны участников",
            "pair_dynamics" => "Динамика пары",
            "repeated_interaction_modes" => "Повторяющиеся режимы",
            "changes_over_time" => "Изменения во времени",
            _ => string.IsNullOrWhiteSpace(traitKey) ? "характеристика" : traitKey
        };
    }

    private static string ToRuCaseStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "active" => "активные",
            "all" => "все",
            "new" => "новый",
            "ready" => "готов",
            "needs_user_input" => "требует ответа",
            "resolved" => "закрыт",
            "rejected" => "отклонен",
            "stale" => "устарел",
            _ => string.IsNullOrWhiteSpace(status) ? "все" : status
        };
    }

    private static string ToRuPriority(string? priority)
    {
        var normalized = (priority ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "blocking" => "блокирующий",
            "important" => "важный",
            "optional" => "необязательный",
            "" => "все",
            _ => priority ?? "все"
        };
    }

    private static string ToRuArtifactType(string? artifactType)
    {
        var normalized = (artifactType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "" => "все",
            "dossier" => "досье",
            "current_state" => "текущее состояние",
            "strategy" => "стратегия",
            "draft" => "черновик",
            "review" => "ревью",
            "clarification_state" => "уточнения",
            _ => artifactType ?? "все"
        };
    }

    private static string ToRuArtifactStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "current" => "актуален",
            "stale" => "нуждается в обновлении",
            "missing" => "нет данных",
            _ => string.IsNullOrWhiteSpace(status) ? "-" : status
        };
    }

    private static string ToRuCaseType(string? caseType)
    {
        if (string.IsNullOrWhiteSpace(caseType))
        {
            return "кейс";
        }

        return caseType
            .Replace("clarification_", "уточнение: ", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ');
    }

    private static string ToRuObjectType(string? objectType)
    {
        if (string.IsNullOrWhiteSpace(objectType))
        {
            return "объект";
        }

        return objectType switch
        {
            "clarification_question" => "вопрос на уточнение",
            "stage6_case" => "кейс",
            "message" => "сообщение",
            "strategy_record" => "стратегия",
            "draft_record" => "черновик",
            "draft_outcome" => "исход черновика",
            "profile_trait" => "характеристика профиля",
            "profile_snapshot" => "снимок профиля",
            "conflict_record" => "конфликт",
            "clarification_answer" => "ответ на уточнение",
            "period_transition" => "переход периода",
            _ => objectType
        };
    }

    private static string ToRuEvidenceClass(string? sourceClass)
    {
        return sourceClass switch
        {
            "system_inference" => "системный вывод",
            "user_reported_context" => "контекст от пользователя",
            _ => string.IsNullOrWhiteSpace(sourceClass) ? "источник" : sourceClass
        };
    }

    private static string ToRuFeedbackKind(string? kind)
    {
        return kind switch
        {
            "accept_useful" => "принято как полезное",
            "reject_not_useful" => "отклонено как неполезное",
            "correction_note" => "корректирующая заметка",
            "refresh_requested" => "запрошено обновление",
            _ => string.IsNullOrWhiteSpace(kind) ? "-" : kind
        };
    }

    private static string ToRuRiskLabel(string risk)
    {
        return risk switch
        {
            "overpressure" => "избыточное давление",
            "premature_escalation" => "преждевременная эскалация",
            "ambiguity" => "высокая неопределенность",
            _ => risk
        };
    }

    private static string ToRuSignalStrength(string signal)
    {
        return signal switch
        {
            "strong" => "сильный сигнал",
            "medium" => "средний сигнал",
            "weak" => "слабый сигнал",
            "contradictory" => "противоречие",
            _ => signal
        };
    }

    private static string ToRuConfidenceLabel(float? value)
    {
        if (!value.HasValue)
        {
            return "нет оценки";
        }

        if (value.Value >= 0.75f)
        {
            return $"высокая ({value.Value:0.00})";
        }

        if (value.Value >= 0.45f)
        {
            return $"средняя ({value.Value:0.00})";
        }

        return $"низкая ({value.Value:0.00})";
    }

    private static string ToRuConfidenceLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "нет оценки";
        }

        return label
            .Replace("high", "высокая", StringComparison.OrdinalIgnoreCase)
            .Replace("medium", "средняя", StringComparison.OrdinalIgnoreCase)
            .Replace("low", "низкая", StringComparison.OrdinalIgnoreCase);
    }

    private static string ShortId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Length <= 8 ? value : value[..8];
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

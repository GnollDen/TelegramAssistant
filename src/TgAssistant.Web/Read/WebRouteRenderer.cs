using System.Net;
using System.Text;

namespace TgAssistant.Web.Read;

public class WebRouteRenderer : IWebRouteRenderer
{
    public static readonly string[] DefaultRoutes =
    [
        "/dashboard",
        "/search",
        "/dossier",
        "/inbox",
        "/history",
        "/state",
        "/timeline",
        "/network",
        "/profiles",
        "/clarifications",
        "/strategy",
        "/drafts-reviews",
        "/offline-events",
        "/review"
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
                    await _webOpsService.GetRecentChangesAsync(request, 6, ct))
            },
            "/search" => new WebRenderResult { Route = path, Title = "Search", Html = RenderSearch(await ExecuteSearchAsync(request, query, ct)) },
            "/dossier" => new WebRenderResult { Route = path, Title = "Dossier", Html = RenderDossier(await _webSearchService.GetDossierAsync(request, 30, ct)) },
            "/view/blocking" => new WebRenderResult { Route = path, Title = "Saved View: Blocking", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "blocking", 40, ct)) },
            "/view/current-period" => new WebRenderResult { Route = path, Title = "Saved View: Current Period", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "current-period", 10, ct)) },
            "/view/conflicts" => new WebRenderResult { Route = path, Title = "Saved View: Conflicts", Html = RenderSavedView(await _webSearchService.GetSavedViewAsync(request, "conflicts", 40, ct)) },
            "/inbox" => new WebRenderResult { Route = path, Title = "Inbox", Html = RenderInbox(await ExecuteInboxReadAsync(request, query, ct), request) },
            "/history" => new WebRenderResult { Route = path, Title = "History", Html = RenderHistory(await ExecuteHistoryReadAsync(request, query, ct)) },
            "/history-object" => new WebRenderResult { Route = path, Title = "Object History", Html = RenderObjectHistory(await ExecuteObjectHistoryReadAsync(request, query, ct), request) },
            "/state" => new WebRenderResult { Route = path, Title = "Current State", Html = RenderState(await _webReadService.GetCurrentStateAsync(request, ct)) },
            "/timeline" => new WebRenderResult { Route = path, Title = "Timeline", Html = RenderTimeline(await _webReadService.GetTimelineAsync(request, ct)) },
            "/network" => new WebRenderResult { Route = path, Title = "Network", Html = RenderNetwork(await _webReadService.GetNetworkAsync(request, ct), query) },
            "/profiles" => new WebRenderResult { Route = path, Title = "Profiles", Html = RenderProfiles(await _webReadService.GetProfilesAsync(request, ct)) },
            "/clarifications" => new WebRenderResult { Route = path, Title = "Clarifications", Html = RenderClarifications(await _webReadService.GetClarificationsAsync(request, ct)) },
            "/strategy" => new WebRenderResult { Route = path, Title = "Strategy", Html = RenderStrategy(await _webReadService.GetStrategyAsync(request, ct)) },
            "/drafts-reviews" => new WebRenderResult { Route = path, Title = "Drafts / Reviews", Html = RenderDraftsReviews(await _webReadService.GetDraftsReviewsAsync(request, ct)) },
            "/offline-events" => new WebRenderResult { Route = path, Title = "Offline Events", Html = RenderOfflineEvents(await _webReadService.GetOfflineEventsAsync(request, ct)) },
            "/review" => new WebRenderResult { Route = path, Title = "Review", Html = RenderReviewBoard(await _webReviewService.GetBoardAsync(request, ct), request) },
            "/review-action" => new WebRenderResult { Route = path, Title = "Review Action", Html = RenderReviewAction(await ExecuteReviewActionAsync(request, query, ct), request) },
            "/review-edit-period" => new WebRenderResult { Route = path, Title = "Edit Period", Html = RenderReviewAction(await ExecutePeriodEditAsync(request, query, ct), request) },
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

    private async Task<InboxReadModel> ExecuteInboxReadAsync(WebReadRequest request, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        bool? blocking = null;
        if (bool.TryParse(GetQuery(query, "blocking"), out var parsedBlocking))
        {
            blocking = parsedBlocking;
        }

        return await _webOpsService.GetInboxAsync(
            request,
            group: EmptyToNull(GetQuery(query, "group")),
            status: EmptyToNull(GetQuery(query, "status")) ?? "open",
            priority: EmptyToNull(GetQuery(query, "priority")),
            blocking: blocking,
            ct: ct);
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

    private static string RenderDashboard(DashboardReadModel model, RecentChangesReadModel recentChanges)
    {
        var sb = CreateShell("Dashboard");
        sb.AppendLine("<h1>Dashboard</h1>");
        sb.AppendLine($"<section><h2>Current State</h2><p><strong>{E(model.CurrentState.DynamicLabel)}</strong> / {E(model.CurrentState.RelationshipStatus)} (conf {model.CurrentState.Confidence:0.00})</p></section>");
        sb.AppendLine($"<section><h2>Next Step</h2><p>{E(model.Strategy.PrimarySummary)}</p><p>micro-step: {E(model.Strategy.MicroStep)}</p></section>");
        sb.AppendLine($"<section><h2>Open Clarifications</h2><p>open: {model.Clarifications.OpenCount}</p></section>");
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
        RenderDossierSection(sb, "Confirmed", model.Confirmed);
        RenderDossierSection(sb, "Hypotheses", model.Hypotheses);
        RenderDossierSection(sb, "Conflicts", model.Conflicts);
        return CloseShell(sb);
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
        foreach (var evt in model.Events)
        {
            var trailLink = $"/history-object?objectType={UrlEncode(evt.ObjectType)}&objectId={UrlEncode(evt.ObjectId)}";
            sb.AppendLine($"<div>{E(evt.TimestampLabel)} | {E(evt.ObjectType)} | {E(evt.Action)} | <a href='{E(trailLink)}'>trail</a></div>");
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
        sb.AppendLine("<h2>Scores</h2>");
        foreach (var kv in model.Scores.OrderBy(x => x.Key))
        {
            sb.AppendLine($"<div>{E(kv.Key)}: {kv.Value:0.00}</div>");
        }

        return CloseShell(sb);
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
        sb.AppendLine($"<p>confidence: {model.Confidence:0.00}</p>");
        sb.AppendLine($"<h2>Primary</h2><p>{E(model.PrimarySummary)}</p><p>purpose: {E(model.PrimaryPurpose)}</p>");
        sb.AppendLine($"<p>risks: {E(string.Join(", ", model.PrimaryRisks))}</p>");
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
            sb.AppendLine($"<p>alt 1: {E(model.LatestDraft.AltDraft1 ?? "-")}</p>");
            sb.AppendLine($"<p>alt 2: {E(model.LatestDraft.AltDraft2 ?? "-")}</p>");
            sb.AppendLine($"<p>style: {E(model.LatestDraft.StyleNotes ?? "-")}</p>");
        }

        if (model.LatestReview != null)
        {
            sb.AppendLine("<h2>Latest Review</h2>");
            sb.AppendLine($"<p>assessment: {E(model.LatestReview.Assessment)}</p>");
            sb.AppendLine($"<p>risks: {E(string.Join("; ", model.LatestReview.MainRisks))}</p>");
            sb.AppendLine($"<p>safer rewrite: {E(model.LatestReview.SaferRewrite)}</p>");
            sb.AppendLine($"<p>more natural rewrite: {E(model.LatestReview.NaturalRewrite)}</p>");
        }

        if (model.LatestOutcome != null)
        {
            sb.AppendLine($"<h2>Latest Outcome ({model.LatestOutcome.CreatedAt:yyyy-MM-dd HH:mm})</h2>");
            sb.AppendLine($"<p>strategy: {E(model.LatestOutcome.StrategyRecordId?.ToString() ?? "-")} | draft: {E(model.LatestOutcome.DraftId.ToString())}</p>");
            sb.AppendLine($"<p>actual message: {E(model.LatestOutcome.ActualMessageId?.ToString() ?? "-")} | follow-up: {E(model.LatestOutcome.FollowUpMessageId?.ToString() ?? "-")}</p>");
            sb.AppendLine($"<p>label: {E(model.LatestOutcome.OutcomeLabel)} (user={E(model.LatestOutcome.UserOutcomeLabel ?? "-")}, system={E(model.LatestOutcome.SystemOutcomeLabel ?? "-")}, conf={(model.LatestOutcome.OutcomeConfidence ?? 0f):0.00})</p>");
            sb.AppendLine($"<p>match: {(model.LatestOutcome.MatchScore ?? 0f):0.00} via {E(model.LatestOutcome.MatchedBy)}</p>");
            sb.AppendLine($"<p>signals: {E(string.Join("; ", model.LatestOutcome.LearningSignals))}</p>");
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
        sb.AppendLine("</section>");
        return sb.ToString();
    }

    private static string RenderPeriod(TimelinePeriodReadModel period)
    {
        return $"<article><h3>{E(period.Label)}</h3><p>{period.StartAt:yyyy-MM-dd}..{(period.EndAt?.ToString("yyyy-MM-dd") ?? "now")}, conf={period.InterpretationConfidence:0.00}, open_q={period.OpenQuestionsCount}</p><p>{E(period.Summary)}</p><p>evidence: {E(string.Join("; ", period.EvidenceHooks))}</p></article>";
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

    private static StringBuilder CreateShell(string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html><head><meta charset='utf-8'><title>" + E(title) + "</title>");
        sb.AppendLine("<style>body{font-family:ui-sans-serif,system-ui;max-width:980px;margin:20px auto;padding:0 12px;color:#1f2937}nav a{margin-right:10px}section,article{border:1px solid #e5e7eb;border-radius:8px;padding:10px;margin:10px 0}h1,h2,h3{margin:6px 0}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine("<nav><a href='/dashboard'>Dashboard</a><a href='/search'>Search</a><a href='/dossier'>Dossier</a><a href='/view/blocking'>View:Blocking</a><a href='/view/current-period'>View:Current</a><a href='/view/conflicts'>View:Conflicts</a><a href='/inbox'>Inbox</a><a href='/history'>History</a><a href='/state'>Current State</a><a href='/timeline'>Timeline</a><a href='/profiles'>Profiles</a><a href='/clarifications'>Clarifications</a><a href='/strategy'>Strategy</a><a href='/drafts-reviews'>Drafts/Reviews</a><a href='/offline-events'>Offline Events</a><a href='/review'>Review</a></nav>");
        return sb;
    }

    private static string CloseShell(StringBuilder sb)
    {
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string UrlEncode(string value) => Uri.EscapeDataString(value ?? string.Empty);
    private static string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

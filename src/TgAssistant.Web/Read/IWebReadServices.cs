namespace TgAssistant.Web.Read;

public interface IWebReadService
{
    Task<DashboardReadModel> GetDashboardAsync(WebReadRequest request, CancellationToken ct = default);
    Task<CurrentStateReadModel> GetCurrentStateAsync(WebReadRequest request, CancellationToken ct = default);
    Task<TimelineReadModel> GetTimelineAsync(WebReadRequest request, CancellationToken ct = default);
    Task<NetworkReadModel> GetNetworkAsync(WebReadRequest request, CancellationToken ct = default);
    Task<ProfilesReadModel> GetProfilesAsync(WebReadRequest request, CancellationToken ct = default);
    Task<ClarificationsReadModel> GetClarificationsAsync(WebReadRequest request, CancellationToken ct = default);
    Task<StrategyReadModel> GetStrategyAsync(WebReadRequest request, CancellationToken ct = default);
    Task<DraftsReviewsReadModel> GetDraftsReviewsAsync(WebReadRequest request, CancellationToken ct = default);
    Task<OutcomeTrailReadModel> GetOutcomeTrailAsync(WebReadRequest request, CancellationToken ct = default);
    Task<OfflineEventsReadModel> GetOfflineEventsAsync(WebReadRequest request, CancellationToken ct = default);
}

public interface IWebRouteRenderer
{
    IReadOnlyList<string> Routes { get; }
    Task<WebRenderResult?> RenderAsync(string route, WebReadRequest request, CancellationToken ct = default);
}

public interface IWebReviewService
{
    Task<WebReviewBoardModel> GetBoardAsync(WebReadRequest request, CancellationToken ct = default);
    Task<WebReviewActionResult> ApplyActionAsync(WebReviewActionRequest request, CancellationToken ct = default);
    Task<WebReviewActionResult> EditPeriodAsync(WebPeriodEditRequest request, CancellationToken ct = default);
}

public interface IWebOpsService
{
    Task<Stage6CaseQueueReadModel> GetCaseQueueAsync(
        WebReadRequest request,
        string? status = "active",
        string? priority = null,
        string? caseType = null,
        string? artifactType = null,
        string? query = null,
        CancellationToken ct = default);

    Task<Stage6CaseDetailReadModel> GetCaseDetailAsync(
        WebReadRequest request,
        Guid stage6CaseId,
        CancellationToken ct = default);

    Task<Stage6ArtifactDetailReadModel> GetArtifactDetailAsync(
        WebReadRequest request,
        string artifactType,
        CancellationToken ct = default);

    Task<WebStage6CaseActionResult> ApplyCaseActionAsync(
        WebStage6CaseActionRequest request,
        CancellationToken ct = default);

    Task<WebStage6ClarificationAnswerResult> ApplyClarificationAnswerAsync(
        WebStage6ClarificationAnswerRequest request,
        CancellationToken ct = default);

    Task<WebStage6ArtifactActionResult> ApplyArtifactActionAsync(
        WebStage6ArtifactActionRequest request,
        CancellationToken ct = default);

    Task<InboxReadModel> GetInboxAsync(
        WebReadRequest request,
        string? group = null,
        string? status = "open",
        string? priority = null,
        bool? blocking = null,
        CancellationToken ct = default);

    Task<HistoryReadModel> GetHistoryAsync(
        WebReadRequest request,
        string? objectType = null,
        string? action = null,
        int limit = 50,
        CancellationToken ct = default);

    Task<ObjectHistoryReadModel> GetObjectHistoryAsync(
        WebReadRequest request,
        string objectType,
        string objectId,
        int limit = 30,
        CancellationToken ct = default);

    Task<RecentChangesReadModel> GetRecentChangesAsync(
        WebReadRequest request,
        int limit = 8,
        CancellationToken ct = default);

    Task<BudgetOperationalReadModel> GetBudgetOperationalStateAsync(CancellationToken ct = default);

    Task<EvalRunsReadModel> GetEvalRunsAsync(
        string? runName = null,
        Guid? runId = null,
        int limit = 10,
        CancellationToken ct = default);

    Task<AbScenarioCandidatePoolReadModel> GetAbScenarioCandidatesAsync(
        WebReadRequest request,
        int targetCount = 30,
        string? bucket = null,
        CancellationToken ct = default);
}

public interface IWebSearchService
{
    Task<SearchReadModel> SearchAsync(
        WebReadRequest request,
        string? query,
        string? objectType = null,
        string? status = null,
        string? priority = null,
        int limit = 100,
        CancellationToken ct = default);

    Task<SavedViewReadModel> GetSavedViewAsync(
        WebReadRequest request,
        string viewKey,
        int limit = 50,
        CancellationToken ct = default);

    Task<DossierReadModel> GetDossierAsync(
        WebReadRequest request,
        int limit = 50,
        CancellationToken ct = default);
}

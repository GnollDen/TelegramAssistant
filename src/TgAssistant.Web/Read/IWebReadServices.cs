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

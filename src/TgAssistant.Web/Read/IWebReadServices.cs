namespace TgAssistant.Web.Read;

public interface IWebReadService
{
    Task<DashboardReadModel> GetDashboardAsync(WebReadRequest request, CancellationToken ct = default);
    Task<CurrentStateReadModel> GetCurrentStateAsync(WebReadRequest request, CancellationToken ct = default);
    Task<TimelineReadModel> GetTimelineAsync(WebReadRequest request, CancellationToken ct = default);
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

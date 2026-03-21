using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebReadVerificationService
{
    private readonly IWebReadService _webReadService;
    private readonly IWebRouteRenderer _webRouteRenderer;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    public WebReadVerificationService(
        IWebReadService webReadService,
        IWebRouteRenderer webRouteRenderer,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository)
    {
        _webReadService = webReadService;
        _webRouteRenderer = webRouteRenderer;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var baseId = 9_300_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L);
        var caseId = baseId;
        var chatId = baseId;

        await SeedMessagesAsync(chatId, now, ct);
        await SeedSessionsAsync(chatId, now, ct);
        await SeedClarificationAsync(caseId, chatId, ct);
        await SeedOfflineEventAsync(caseId, chatId, now, ct);

        var request = new WebReadRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "web_smoke"
        };

        var dashboardModel = await _webReadService.GetDashboardAsync(request, ct);
        if (string.IsNullOrWhiteSpace(dashboardModel.CurrentState.DynamicLabel))
        {
            throw new InvalidOperationException("Web smoke failed: dashboard current state is empty.");
        }

        if (string.IsNullOrWhiteSpace(dashboardModel.Strategy.PrimarySummary))
        {
            throw new InvalidOperationException("Web smoke failed: dashboard strategy summary is empty.");
        }

        if (dashboardModel.Clarifications.OpenCount <= 0)
        {
            throw new InvalidOperationException("Web smoke failed: dashboard clarification summary is empty.");
        }

        var stateModel = await _webReadService.GetCurrentStateAsync(request, ct);
        if (stateModel.Scores.Count < 5)
        {
            throw new InvalidOperationException("Web smoke failed: state scores are incomplete.");
        }

        var timelineModel = await _webReadService.GetTimelineAsync(request, ct);
        if (timelineModel.CurrentPeriod == null && timelineModel.PriorPeriods.Count == 0)
        {
            throw new InvalidOperationException("Web smoke failed: timeline is empty.");
        }

        var profilesModel = await _webReadService.GetProfilesAsync(request, ct);
        if (string.IsNullOrWhiteSpace(profilesModel.Self.Summary)
            || string.IsNullOrWhiteSpace(profilesModel.Other.Summary)
            || string.IsNullOrWhiteSpace(profilesModel.Pair.Summary))
        {
            throw new InvalidOperationException("Web smoke failed: profiles are empty.");
        }

        var strategyModel = await _webReadService.GetStrategyAsync(request, ct);
        if (string.IsNullOrWhiteSpace(strategyModel.MicroStep))
        {
            throw new InvalidOperationException("Web smoke failed: strategy micro-step is empty.");
        }

        var rendered = new Dictionary<string, WebRenderResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in _webRouteRenderer.Routes)
        {
            var page = await _webRouteRenderer.RenderAsync(route, request, ct)
                ?? throw new InvalidOperationException($"Web smoke failed: route '{route}' did not resolve.");
            if (string.IsNullOrWhiteSpace(page.Html) || !page.Html.Contains("<h1>", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Web smoke failed: route '{route}' did not render usable content.");
            }

            rendered[route] = page;
        }

        AssertNonEmptyRoute(rendered, "/dashboard", "Current State");
        AssertNonEmptyRoute(rendered, "/state", "dynamic:");
        AssertNonEmptyRoute(rendered, "/timeline", "Timeline");
        AssertNonEmptyRoute(rendered, "/network", "Network");
        AssertNonEmptyRoute(rendered, "/profiles", "Profiles");
        AssertNonEmptyRoute(rendered, "/strategy", "Strategy");

        _ = rendered.Count;
    }

    private async Task SeedMessagesAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var telegramId = 930_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 100_000_000L);
        var rows = new List<Message>();
        for (var i = 0; i < 44; i++)
        {
            var ts = now.AddDays(-28).AddHours(i * 8);
            rows.Add(new Message
            {
                TelegramMessageId = telegramId + i,
                ChatId = chatId,
                SenderId = i % 2 == 0 ? 1 : 2,
                SenderName = i % 2 == 0 ? "Self" : "Other",
                Timestamp = ts,
                Text = i % 7 == 0
                    ? "After yesterday I feel warmer, but I still need a little space."
                    : (i % 5 == 0 ? "Let's keep contact light this week and avoid pressure." : "Thanks, this rhythm works better for me."),
                ProcessingStatus = ProcessingStatus.Processed,
                Source = MessageSource.Archive,
                CreatedAt = DateTime.UtcNow
            });
        }

        _ = await _messageRepository.SaveBatchAsync(rows, ct);
    }

    private async Task SeedSessionsAsync(long chatId, DateTime now, CancellationToken ct)
    {
        var baseIndex = (int)(Math.Abs(now.Ticks) % 100_000);
        var sessions = new[]
        {
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 1,
                StartDate = now.AddDays(-28),
                EndDate = now.AddDays(-22),
                LastMessageAt = now.AddDays(-22),
                Summary = "high uncertainty reopening",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 2,
                StartDate = now.AddDays(-18),
                EndDate = now.AddDays(-12),
                LastMessageAt = now.AddDays(-12),
                Summary = "cooling then stabilizing",
                IsFinalized = true,
                IsAnalyzed = true
            },
            new ChatSession
            {
                ChatId = chatId,
                SessionIndex = baseIndex + 3,
                StartDate = now.AddDays(-8),
                EndDate = now.AddDays(-2),
                LastMessageAt = now.AddDays(-2),
                Summary = "clearer tone and lower pressure",
                IsFinalized = true,
                IsAnalyzed = true
            }
        };

        foreach (var session in sessions)
        {
            await _chatSessionRepository.UpsertAsync(session, ct);
        }
    }

    private async Task SeedClarificationAsync(long caseId, long chatId, CancellationToken ct)
    {
        await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = null,
            QuestionText = "Did the offline meetup reduce pressure for this week?",
            QuestionType = "state_strategy",
            Priority = "important",
            Status = "open",
            WhyItMatters = "Affects short-term strategy pressure calibration",
            ExpectedGain = 0.71f,
            RelatedHypothesisId = null,
            AnswerOptionsJson = "[\"yes\",\"no\",\"unclear\"]",
            AffectedOutputsJson = "[\"state\",\"strategy\"]",
            SourceType = "smoke",
            SourceId = "web"
        }, ct);
    }

    private async Task SeedOfflineEventAsync(long caseId, long chatId, DateTime now, CancellationToken ct)
    {
        await _offlineEventRepository.CreateOfflineEventAsync(new OfflineEvent
        {
            CaseId = caseId,
            ChatId = chatId,
            TimestampStart = now.AddDays(-5),
            TimestampEnd = now.AddDays(-5).AddHours(2),
            EventType = "meeting",
            Title = "Coffee walk",
            UserSummary = "Talked calmly and agreed to slower pacing for this week.",
            EvidenceRefsJson = "[\"offline:coffee-walk\",\"note:agreed-slower-pacing\"]",
            SourceType = "smoke",
            SourceId = "web"
        }, ct);
    }

    private static void AssertNonEmptyRoute(
        IReadOnlyDictionary<string, WebRenderResult> rendered,
        string route,
        string mustContain)
    {
        if (!rendered.TryGetValue(route, out var page))
        {
            throw new InvalidOperationException($"Web smoke failed: route '{route}' was not rendered.");
        }

        if (!page.Html.Contains(mustContain, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Web smoke failed: route '{route}' does not contain required marker '{mustContain}'.");
        }
    }
}

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebOpsVerificationService
{
    private readonly IWebRouteRenderer _webRouteRenderer;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IInboxConflictRepository _inboxConflictRepository;

    public WebOpsVerificationService(
        IWebRouteRenderer webRouteRenderer,
        IClarificationRepository clarificationRepository,
        IInboxConflictRepository inboxConflictRepository)
    {
        _webRouteRenderer = webRouteRenderer;
        _clarificationRepository = clarificationRepository;
        _inboxConflictRepository = inboxConflictRepository;
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

        var request = new WebReadRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "ops_web_smoke"
        };

        var inboxPage = await _webRouteRenderer.RenderAsync("/inbox", request, ct)
            ?? throw new InvalidOperationException("Ops web smoke failed: /inbox route did not resolve.");
        if (string.IsNullOrWhiteSpace(inboxPage.Html)
            || !inboxPage.Html.Contains("Inbox", StringComparison.OrdinalIgnoreCase)
            || !inboxPage.Html.Contains("Blocking clarification", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ops web smoke failed: /inbox route does not show seeded inbox items.");
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
    }
}

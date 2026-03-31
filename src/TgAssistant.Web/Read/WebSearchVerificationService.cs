using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Web.Read;

public class WebSearchVerificationService
{
    private readonly IWebRouteRenderer _webRouteRenderer;
    private readonly IInboxConflictRepository _inboxConflictRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;

    public WebSearchVerificationService(
        IWebRouteRenderer webRouteRenderer,
        IInboxConflictRepository inboxConflictRepository,
        IClarificationRepository clarificationRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository)
    {
        _webRouteRenderer = webRouteRenderer;
        _inboxConflictRepository = inboxConflictRepository;
        _clarificationRepository = clarificationRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var baseId = 9_800_000_000_000L + (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L);
        var caseId = baseId;
        var chatId = baseId;
        var token = "s13-alpha";

        var question = await _clarificationRepository.CreateQuestionAsync(new ClarificationQuestion
        {
            CaseId = caseId,
            ChatId = chatId,
            QuestionText = $"{token} clarification question",
            QuestionType = "state",
            Priority = "blocking",
            Status = "open",
            WhyItMatters = "search seed",
            ExpectedGain = 0.75f,
            SourceType = "smoke",
            SourceId = "search"
        }, ct);

        _ = await _clarificationRepository.ApplyAnswerAsync(
            question.Id,
            new ClarificationAnswer
            {
                QuestionId = question.Id,
                AnswerType = "free_text",
                AnswerValue = $"{token} confirmed answer",
                AnswerConfidence = 0.82f,
                SourceClass = "user_assertion",
                SourceType = "smoke",
                SourceId = "search"
            },
            markResolved: true,
            actor: "search_smoke",
            reason: "seed confirmed",
            ct: ct);

        var period = await _periodRepository.CreatePeriodAsync(new Period
        {
            CaseId = caseId,
            ChatId = chatId,
            Label = $"{token} period",
            StartAt = DateTime.UtcNow.AddDays(-8),
            EndAt = null,
            IsOpen = true,
            Summary = "searchable current period summary",
            ReviewPriority = 3,
            InterpretationConfidence = 0.66f,
            BoundaryConfidence = 0.61f,
            SourceType = "smoke",
            SourceId = "search"
        }, ct);

        _ = await _periodRepository.CreateHypothesisAsync(new Hypothesis
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            HypothesisType = "trajectory",
            SubjectType = "pair",
            SubjectId = "1:2",
            Statement = $"{token} hypothesis statement",
            Confidence = 0.57f,
            Status = "open",
            SourceType = "smoke",
            SourceId = "search"
        }, ct);

        var conflict = await _inboxConflictRepository.CreateConflictRecordAsync(new ConflictRecord
        {
            CaseId = caseId,
            ChatId = chatId,
            ConflictType = "answer_vs_state",
            ObjectAType = "clarification_answer",
            ObjectAId = question.Id.ToString(),
            ObjectBType = "state_snapshot",
            ObjectBId = "seed",
            Summary = $"{token} conflict summary",
            Severity = "high",
            Status = "open",
            LastActor = "search_smoke",
            LastReason = "seed"
        }, ct);

        _ = await _inboxConflictRepository.CreateInboxItemAsync(new InboxItem
        {
            CaseId = caseId,
            ChatId = chatId,
            ItemType = "clarification",
            SourceObjectType = "clarification_question",
            SourceObjectId = question.Id.ToString(),
            Priority = "blocking",
            IsBlocking = true,
            Title = $"{token} inbox",
            Summary = "blocking retrieval seed",
            Status = "open",
            LastActor = "search_smoke",
            LastReason = "seed"
        }, ct);

        var profile = await _stateProfileRepository.CreateProfileSnapshotAsync(new ProfileSnapshot
        {
            SubjectType = "self",
            SubjectId = "1",
            CaseId = caseId,
            ChatId = chatId,
            Summary = $"{token} profile snapshot",
            Confidence = 0.61f,
            Stability = 0.43f
        }, ct);

        _ = await _stateProfileRepository.CreateProfileTraitAsync(new ProfileTrait
        {
            ProfileSnapshotId = profile.Id,
            TraitKey = "communication_style",
            ValueLabel = $"{token} concise-warm",
            Confidence = 0.58f,
            Stability = 0.40f,
            IsSensitive = false,
            EvidenceRefsJson = "[]"
        }, ct);

        var strategy = await _strategyDraftRepository.CreateStrategyRecordAsync(new StrategyRecord
        {
            CaseId = caseId,
            ChatId = chatId,
            PeriodId = period.Id,
            StrategyConfidence = 0.62f,
            RecommendedGoal = $"{token} hold calm contact",
            WhyNotOthers = "seed",
            MicroStep = "seed micro-step"
        }, ct);

        _ = await _strategyDraftRepository.CreateStrategyOptionAsync(new StrategyOption
        {
            StrategyRecordId = strategy.Id,
            ActionType = "check_in",
            Summary = $"{token} option",
            Purpose = "seed",
            Risk = "{}",
            WhenToUse = "seed",
            SuccessSigns = "seed",
            FailureSigns = "seed",
            IsPrimary = true
        }, ct);

        _ = await _strategyDraftRepository.CreateDraftRecordAsync(new DraftRecord
        {
            StrategyRecordId = strategy.Id,
            MainDraft = $"{token} draft",
            AltDraft1 = "a",
            AltDraft2 = "b",
            StyleNotes = "seed",
            Confidence = 0.6f
        }, ct);

        var request = new WebReadRequest
        {
            CaseId = caseId,
            ChatId = chatId,
            Actor = "search_smoke"
        };

        var searchPage = await _webRouteRenderer.RenderAsync($"/search?q={token}", request, ct)
            ?? throw new InvalidOperationException("Search smoke failed: /search route did not resolve.");
        if (string.IsNullOrWhiteSpace(searchPage.Html)
            || !searchPage.Html.Contains("Поиск", StringComparison.OrdinalIgnoreCase)
            || !searchPage.Html.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Search smoke failed: seeded artifacts are missing in /search.");
        }

        if (!searchPage.Html.Contains("/outcomes?", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Search smoke failed: outcome-chain deep links are missing in /search.");
        }

        var filtered = await _webRouteRenderer.RenderAsync($"/search?q={token}&objectType=conflict_record", request, ct)
            ?? throw new InvalidOperationException("Search smoke failed: filtered /search route did not resolve.");
        if (!filtered.Html.Contains("conflict_record", StringComparison.OrdinalIgnoreCase)
            || filtered.Html.Contains("clarification_question", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Search smoke failed: objectType filter does not affect results.");
        }

        var blockingView = await _webRouteRenderer.RenderAsync("/view/blocking", request, ct)
            ?? throw new InvalidOperationException("Search smoke failed: /view/blocking did not resolve.");
        var currentPeriodView = await _webRouteRenderer.RenderAsync("/view/current-period", request, ct)
            ?? throw new InvalidOperationException("Search smoke failed: /view/current-period did not resolve.");
        var conflictsView = await _webRouteRenderer.RenderAsync("/view/conflicts", request, ct)
            ?? throw new InvalidOperationException("Search smoke failed: /view/conflicts did not resolve.");

        if (!blockingView.Html.Contains("<h1>", StringComparison.OrdinalIgnoreCase)
            || !currentPeriodView.Html.Contains("<h1>", StringComparison.OrdinalIgnoreCase)
            || !conflictsView.Html.Contains("<h1>", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Search smoke failed: saved views do not render correctly.");
        }

        var dossier = await _webRouteRenderer.RenderAsync("/dossier", request, ct)
            ?? throw new InvalidOperationException("Search smoke failed: /dossier route did not resolve.");
        if (!dossier.Html.Contains("<h1>Досье</h1>", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Search smoke failed: dossier page did not render.");
        }

        if (!dossier.Html.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Search smoke failed: seeded dossier items are missing.");
        }

        _ = conflict.Id;
    }
}

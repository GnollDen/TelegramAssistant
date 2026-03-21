using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Drafts;

public class DraftEngine : IDraftEngine
{
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IDraftGenerator _draftGenerator;
    private readonly IDraftStyleAdapter _styleAdapter;
    private readonly IDraftStrategyChecker _strategyChecker;
    private readonly IDraftPackagingService _packagingService;
    private readonly ILogger<DraftEngine> _logger;

    public DraftEngine(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IStrategyEngine strategyEngine,
        IDraftGenerator draftGenerator,
        IDraftStyleAdapter styleAdapter,
        IDraftStrategyChecker strategyChecker,
        IDraftPackagingService packagingService,
        ILogger<DraftEngine> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _strategyEngine = strategyEngine;
        _draftGenerator = draftGenerator;
        _styleAdapter = styleAdapter;
        _strategyChecker = strategyChecker;
        _packagingService = packagingService;
        _logger = logger;
    }

    public async Task<DraftEngineResult> RunAsync(DraftEngineRequest request, CancellationToken ct = default)
    {
        var asOf = request.AsOfUtc ?? DateTime.UtcNow;
        var context = await LoadContextAsync(request, asOf, ct);

        var generated = await _draftGenerator.GenerateAsync(context, ct);
        var styled = _styleAdapter.ApplyStyle(context, generated);
        var conflict = _strategyChecker.Evaluate(context, styled);
        var record = request.Persist
            ? await _packagingService.PersistAsync(context, styled, conflict, ct)
            : new DraftRecord
            {
                StrategyRecordId = context.StrategyRecord.Id,
                MainDraft = styled.MainDraft,
                AltDraft1 = styled.AltDraft1,
                AltDraft2 = conflict.RiskyIntentAlternative ?? styled.AltDraft2,
                StyleNotes = conflict.HasConflict ? $"{styled.StyleNotes}; conflict={conflict.Reason}" : styled.StyleNotes,
                Confidence = conflict.HasConflict ? Math.Clamp(styled.Confidence - 0.1f, 0f, 1f) : styled.Confidence
            };

        _logger.LogInformation(
            "Draft generated: case_id={CaseId}, chat_id={ChatId}, strategy_record={StrategyRecordId}, confidence={Confidence:0.00}, conflict={Conflict}",
            request.CaseId,
            request.ChatId,
            context.StrategyRecord.Id,
            record.Confidence,
            conflict.HasConflict);

        return new DraftEngineResult
        {
            Record = record,
            HasIntentConflict = conflict.HasConflict,
            ConflictReason = conflict.Reason
        };
    }

    private async Task<DraftGenerationContext> LoadContextAsync(DraftEngineRequest request, DateTime asOf, CancellationToken ct)
    {
        // case_id is analysis scope; chat_id anchors canonical message/session provenance.
        var strategyRecord = await ResolveStrategyRecordAsync(request, ct);
        var strategyOptions = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(strategyRecord.Id, ct);
        var primaryOption = strategyOptions.FirstOrDefault(x => x.IsPrimary) ?? strategyOptions.FirstOrDefault();
        if (primaryOption == null)
        {
            throw new InvalidOperationException($"Draft engine cannot proceed: strategy options missing for record {strategyRecord.Id}.");
        }

        var states = await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 20, ct);
        var currentState = states
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.AsOf <= asOf)
            .OrderByDescending(x => x.AsOf)
            .FirstOrDefault();

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.StartAt <= asOf)
            .OrderByDescending(x => x.StartAt)
            .ToList();
        var currentPeriod = periods.FirstOrDefault(x => x.IsOpen) ?? periods.FirstOrDefault();

        var messages = (await _messageRepository.GetByChatAndPeriodAsync(request.ChatId, asOf.AddDays(-45), asOf, 1200, ct))
            .OrderBy(x => x.Timestamp)
            .ToList();

        var senderCounts = messages
            .Where(x => x.SenderId > 0)
            .GroupBy(x => x.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        var selfSenderId = request.SelfSenderId ?? senderCounts.FirstOrDefault()?.SenderId ?? 1L;
        var otherSenderId = senderCounts.Where(x => x.SenderId != selfSenderId).Select(x => x.SenderId).FirstOrDefault();
        if (otherSenderId <= 0)
        {
            otherSenderId = selfSenderId + 1;
        }

        var profileTraits = new List<ProfileTrait>();
        await LoadProfileTraitsAsync(request.CaseId, "self", selfSenderId.ToString(), profileTraits, ct);
        await LoadProfileTraitsAsync(request.CaseId, "other", otherSenderId.ToString(), profileTraits, ct);
        await LoadProfileTraitsAsync(request.CaseId, "pair", $"{selfSenderId}:{otherSenderId}", profileTraits, ct);

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, [])
            .Where(x => x.EndDate <= asOf)
            .OrderByDescending(x => x.EndDate)
            .Take(10)
            .ToList();

        return new DraftGenerationContext
        {
            AsOfUtc = asOf,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = selfSenderId,
            OtherSenderId = otherSenderId,
            StrategyRecord = strategyRecord,
            StrategyOptions = strategyOptions,
            PrimaryOption = primaryOption,
            CurrentState = currentState,
            CurrentPeriod = currentPeriod,
            ProfileTraits = profileTraits,
            RecentMessages = messages,
            RecentSessions = sessions,
            UserNotes = request.UserNotes,
            DesiredTone = request.DesiredTone
        };
    }

    private async Task LoadProfileTraitsAsync(
        long caseId,
        string subjectType,
        string subjectId,
        ICollection<ProfileTrait> sink,
        CancellationToken ct)
    {
        var snapshots = await _stateProfileRepository.GetProfileSnapshotsByCaseAsync(caseId, subjectType, subjectId, ct);
        var latest = snapshots
            .OrderBy(x => x.PeriodId.HasValue ? 1 : 0)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (latest == null)
        {
            return;
        }

        foreach (var trait in await _stateProfileRepository.GetProfileTraitsBySnapshotIdAsync(latest.Id, ct))
        {
            sink.Add(trait);
        }
    }

    private async Task<StrategyRecord> ResolveStrategyRecordAsync(DraftEngineRequest request, CancellationToken ct)
    {
        if (request.StrategyRecordId.HasValue)
        {
            var byId = await _strategyDraftRepository.GetStrategyRecordByIdAsync(request.StrategyRecordId.Value, ct);
            if (byId == null)
            {
                throw new InvalidOperationException($"Draft engine cannot find strategy record '{request.StrategyRecordId.Value}'.");
            }

            return byId;
        }

        var latest = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);
        if (latest != null)
        {
            return latest;
        }

        if (!request.AllowStrategyAutogeneration)
        {
            throw new InvalidOperationException("Draft engine requires strategy record when auto-generation is disabled.");
        }

        var strategyResult = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = request.SelfSenderId,
            Actor = request.Actor,
            SourceType = "draft_engine",
            SourceId = "autogenerated_strategy_for_draft",
            AsOfUtc = request.AsOfUtc,
            Persist = true
        }, ct);

        return strategyResult.Record;
    }
}

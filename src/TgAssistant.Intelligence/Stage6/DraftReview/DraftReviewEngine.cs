// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.DraftReview;

public class DraftReviewEngine : IDraftReviewEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IStrategyDraftRepository _strategyDraftRepository;
    private readonly IStrategyEngine _strategyEngine;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IStage6ArtifactRepository _stage6ArtifactRepository;
    private readonly IStage6ArtifactFreshnessService _stage6ArtifactFreshnessService;
    private readonly IDraftRiskAssessor _riskAssessor;
    private readonly IDraftStrategyFitChecker _strategyFitChecker;
    private readonly ISaferRewriteGenerator _saferRewriteGenerator;
    private readonly INaturalRewriteGenerator _naturalRewriteGenerator;
    private readonly ILogger<DraftReviewEngine> _logger;

    public DraftReviewEngine(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IStateProfileRepository stateProfileRepository,
        IStrategyDraftRepository strategyDraftRepository,
        IStrategyEngine strategyEngine,
        IDomainReviewEventRepository domainReviewEventRepository,
        IStage6ArtifactRepository stage6ArtifactRepository,
        IStage6ArtifactFreshnessService stage6ArtifactFreshnessService,
        IDraftRiskAssessor riskAssessor,
        IDraftStrategyFitChecker strategyFitChecker,
        ISaferRewriteGenerator saferRewriteGenerator,
        INaturalRewriteGenerator naturalRewriteGenerator,
        ILogger<DraftReviewEngine> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _stateProfileRepository = stateProfileRepository;
        _strategyDraftRepository = strategyDraftRepository;
        _strategyEngine = strategyEngine;
        _domainReviewEventRepository = domainReviewEventRepository;
        _stage6ArtifactRepository = stage6ArtifactRepository;
        _stage6ArtifactFreshnessService = stage6ArtifactFreshnessService;
        _riskAssessor = riskAssessor;
        _strategyFitChecker = strategyFitChecker;
        _saferRewriteGenerator = saferRewriteGenerator;
        _naturalRewriteGenerator = naturalRewriteGenerator;
        _logger = logger;
    }

    public async Task<DraftReviewResult> RunAsync(DraftReviewRequest request, CancellationToken ct = default)
    {
        var asOf = request.AsOfUtc ?? DateTime.UtcNow;
        var context = await LoadContextAsync(request, asOf, ct);

        var strategyFit = _strategyFitChecker.Evaluate(context);
        var assessment = _riskAssessor.Assess(context, strategyFit);
        var saferRewrite = _saferRewriteGenerator.Generate(context, assessment, strategyFit);
        var naturalRewrite = _naturalRewriteGenerator.Generate(context, assessment);

        var result = new DraftReviewResult
        {
            ReviewId = context.ReviewId,
            Assessment = assessment.Summary,
            MainRisks = assessment.MainRisks.Take(2).ToList(),
            RiskLabels = assessment.RiskLabels.ToList(),
            SaferRewrite = saferRewrite,
            NaturalRewrite = naturalRewrite,
            StrategyConflictDetected = strategyFit.HasMaterialConflict,
            StrategyConflictNote = strategyFit.ConflictNote,
            SourceDraftRecordId = context.SourceDraftRecordId
        };

        if (request.Persist)
        {
            await PersistReviewAsync(request, context, result, ct);
        }

        _logger.LogInformation(
            "Draft review completed: case_id={CaseId}, chat_id={ChatId}, review_id={ReviewId}, source_draft_id={SourceDraftId}, risk_labels={RiskCount}, strategy_conflict={StrategyConflict}",
            request.CaseId,
            request.ChatId,
            result.ReviewId,
            result.SourceDraftRecordId,
            result.RiskLabels.Count,
            result.StrategyConflictDetected);

        return result;
    }

    private async Task<DraftReviewContext> LoadContextAsync(DraftReviewRequest request, DateTime asOf, CancellationToken ct)
    {
        DraftRecord? draftRecord = null;
        if (request.DraftRecordId.HasValue)
        {
            draftRecord = await _strategyDraftRepository.GetDraftRecordByIdAsync(request.DraftRecordId.Value, ct);
            if (draftRecord == null)
            {
                throw new InvalidOperationException($"Draft review cannot find draft record '{request.DraftRecordId.Value}'.");
            }
        }

        var strategyRecord = await ResolveStrategyRecordAsync(request, draftRecord, ct);
        var strategyOptions = await _strategyDraftRepository.GetStrategyOptionsByRecordIdAsync(strategyRecord.Id, ct);
        var primaryOption = strategyOptions.FirstOrDefault(x => x.IsPrimary) ?? strategyOptions.FirstOrDefault();
        if (primaryOption == null)
        {
            throw new InvalidOperationException($"Draft review cannot proceed: strategy options missing for record {strategyRecord.Id}.");
        }

        var candidateText = (request.CandidateText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidateText))
        {
            candidateText = draftRecord?.MainDraft?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(candidateText))
        {
            throw new InvalidOperationException("Draft review requires candidate text or a valid draft record with non-empty main draft.");
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

        var traits = new List<ProfileTrait>();
        await LoadProfileTraitsAsync(request.CaseId, "self", selfSenderId.ToString(), traits, ct);
        await LoadProfileTraitsAsync(request.CaseId, "other", otherSenderId.ToString(), traits, ct);
        await LoadProfileTraitsAsync(request.CaseId, "pair", $"{selfSenderId}:{otherSenderId}", traits, ct);

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, [])
            .Where(x => x.EndDate <= asOf)
            .OrderByDescending(x => x.EndDate)
            .Take(10)
            .ToList();

        return new DraftReviewContext
        {
            ReviewId = Guid.NewGuid(),
            AsOfUtc = asOf,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = selfSenderId,
            OtherSenderId = otherSenderId,
            CandidateText = candidateText,
            SourceDraftRecordId = draftRecord?.Id,
            StrategyRecord = strategyRecord,
            PrimaryOption = primaryOption,
            CurrentState = currentState,
            CurrentPeriod = currentPeriod,
            ProfileTraits = traits,
            RecentMessages = messages,
            RecentSessions = sessions
        };
    }

    private async Task<StrategyRecord> ResolveStrategyRecordAsync(
        DraftReviewRequest request,
        DraftRecord? draftRecord,
        CancellationToken ct)
    {
        if (request.StrategyRecordId.HasValue)
        {
            var byId = await _strategyDraftRepository.GetStrategyRecordByIdAsync(request.StrategyRecordId.Value, ct);
            if (byId == null)
            {
                throw new InvalidOperationException($"Draft review cannot find strategy record '{request.StrategyRecordId.Value}'.");
            }

            return byId;
        }

        if (draftRecord != null)
        {
            var byDraft = await _strategyDraftRepository.GetStrategyRecordByIdAsync(draftRecord.StrategyRecordId, ct);
            if (byDraft != null)
            {
                return byDraft;
            }
        }

        var latest = (await _strategyDraftRepository.GetStrategyRecordsByCaseAsync(request.CaseId, ct))
            .FirstOrDefault(x => x.ChatId == null || x.ChatId == request.ChatId);
        if (latest != null)
        {
            return latest;
        }

        if (!request.AllowStrategyAutogeneration)
        {
            throw new InvalidOperationException("Draft review requires strategy record when auto-generation is disabled.");
        }

        var strategyResult = await _strategyEngine.RunAsync(new StrategyEngineRequest
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = request.SelfSenderId,
            Actor = request.Actor,
            SourceType = "draft_review_engine",
            SourceId = "autogenerated_strategy_for_review",
            AsOfUtc = request.AsOfUtc,
            Persist = true
        }, ct);

        return strategyResult.Record;
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

    private async Task PersistReviewAsync(
        DraftReviewRequest request,
        DraftReviewContext context,
        DraftReviewResult result,
        CancellationToken ct)
    {
        await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
        {
            ObjectType = "draft_review",
            ObjectId = result.ReviewId.ToString(),
            Action = "review_generated",
            NewValueRef = JsonSerializer.Serialize(new
            {
                request.CaseId,
                request.ChatId,
                source_draft_record_id = result.SourceDraftRecordId,
                strategy_record_id = context.StrategyRecord.Id,
                strategy_option = context.PrimaryOption.ActionType,
                assessment = result.Assessment,
                main_risks = result.MainRisks,
                risk_labels = result.RiskLabels,
                strategy_conflict = result.StrategyConflictDetected,
                strategy_conflict_note = result.StrategyConflictNote,
                safer_rewrite = result.SaferRewrite,
                natural_rewrite = result.NaturalRewrite
            }, JsonOptions),
            Reason = request.SourceId,
            Actor = request.Actor,
            CreatedAt = DateTime.UtcNow
        }, ct);

        if (result.SourceDraftRecordId.HasValue)
        {
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "draft_record",
                ObjectId = result.SourceDraftRecordId.Value.ToString(),
                Action = "reviewed",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    review_id = result.ReviewId,
                    strategy_conflict = result.StrategyConflictDetected,
                    main_risks = result.MainRisks
                }, JsonOptions),
                Reason = request.SourceId,
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        var evidence = await _stage6ArtifactFreshnessService.BuildEvidenceStampAsync(
            request.CaseId,
            request.ChatId,
            Stage6ArtifactTypes.Review,
            ct);
        _ = await _stage6ArtifactRepository.UpsertCurrentAsync(new Stage6ArtifactRecord
        {
            ArtifactType = Stage6ArtifactTypes.Review,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            ScopeKey = Stage6ArtifactTypes.ChatScope(request.ChatId),
            PayloadObjectType = "draft_review",
            PayloadObjectId = result.ReviewId.ToString(),
            PayloadJson = JsonSerializer.Serialize(result, JsonOptions),
            FreshnessBasisHash = evidence.BasisHash,
            FreshnessBasisJson = evidence.BasisJson,
            GeneratedAt = DateTime.UtcNow,
            RefreshedAt = DateTime.UtcNow,
            StaleAt = DateTime.UtcNow.Add(_stage6ArtifactFreshnessService.ResolveTtl(Stage6ArtifactTypes.Review)),
            IsStale = false,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            SourceMessageId = null,
            SourceSessionId = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }, ct);
    }
}

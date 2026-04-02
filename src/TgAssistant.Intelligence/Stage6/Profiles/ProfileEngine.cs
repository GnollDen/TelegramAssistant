// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class ProfileEngine : IProfileEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IPeriodRepository _periodRepository;
    private readonly IClarificationRepository _clarificationRepository;
    private readonly IOfflineEventRepository _offlineEventRepository;
    private readonly IIntelligenceRepository _intelligenceRepository;
    private readonly IStateProfileRepository _stateProfileRepository;
    private readonly IDomainReviewEventRepository _domainReviewEventRepository;
    private readonly IProfileTraitExtractor _traitExtractor;
    private readonly IPairProfileSynthesizer _pairProfileSynthesizer;
    private readonly IProfileConfidenceEvaluator _confidenceEvaluator;
    private readonly IPatternSynthesisService _patternSynthesisService;
    private readonly ILogger<ProfileEngine> _logger;

    public ProfileEngine(
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IPeriodRepository periodRepository,
        IClarificationRepository clarificationRepository,
        IOfflineEventRepository offlineEventRepository,
        IIntelligenceRepository intelligenceRepository,
        IStateProfileRepository stateProfileRepository,
        IDomainReviewEventRepository domainReviewEventRepository,
        IProfileTraitExtractor traitExtractor,
        IPairProfileSynthesizer pairProfileSynthesizer,
        IProfileConfidenceEvaluator confidenceEvaluator,
        IPatternSynthesisService patternSynthesisService,
        ILogger<ProfileEngine> logger)
    {
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _periodRepository = periodRepository;
        _clarificationRepository = clarificationRepository;
        _offlineEventRepository = offlineEventRepository;
        _intelligenceRepository = intelligenceRepository;
        _stateProfileRepository = stateProfileRepository;
        _domainReviewEventRepository = domainReviewEventRepository;
        _traitExtractor = traitExtractor;
        _pairProfileSynthesizer = pairProfileSynthesizer;
        _confidenceEvaluator = confidenceEvaluator;
        _patternSynthesisService = patternSynthesisService;
        _logger = logger;
    }

    public async Task<ProfileEngineResult> RunAsync(ProfileEngineRequest request, CancellationToken ct = default)
    {
        var asOf = request.AsOfUtc ?? DateTime.UtcNow;
        var context = await LoadContextAsync(request, asOf, ct);
        var result = new ProfileEngineResult
        {
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = context.SelfSenderId,
            OtherSenderId = context.OtherSenderId
        };

        var periodSlices = context.Periods
            .OrderByDescending(x => x.StartAt)
            .Take(Math.Max(1, request.MaxPeriodSlices))
            .ToList();

        await BuildSubjectAsync("self", context.SelfSenderId.ToString(), request, context, periodSlices, result, ct);
        await BuildSubjectAsync("other", context.OtherSenderId.ToString(), request, context, periodSlices, result, ct);
        await BuildPairAsync(request, context, periodSlices, result, ct);

        _logger.LogInformation(
            "Profile engine run completed: case_id={CaseId}, chat_id={ChatId}, snapshots={SnapshotCount}, traits={TraitCount}, patterns={PatternCount}",
            request.CaseId,
            request.ChatId,
            result.Snapshots.Count,
            result.Traits.Count,
            result.Patterns.Count);

        return result;
    }

    private async Task BuildSubjectAsync(
        string subjectType,
        string subjectId,
        ProfileEngineRequest request,
        ProfileEvidenceContext context,
        IReadOnlyList<Period> periodSlices,
        ProfileEngineResult result,
        CancellationToken ct)
    {
        var globalTraits = await _traitExtractor.ExtractAsync(subjectType, subjectId, context, null, ct);
        var globalPatterns = await _patternSynthesisService.BuildPatternsAsync(subjectType, subjectId, context, null, ct);
        var globalAssessment = _confidenceEvaluator.Evaluate(subjectType, globalTraits, context, null);

        var globalSnapshot = await PersistSnapshotBundleAsync(
            request,
            context,
            subjectType,
            subjectId,
            period: null,
            globalAssessment,
            globalTraits,
            globalPatterns,
            ct);

        result.Snapshots.Add(globalSnapshot.Snapshot);
        result.Traits.AddRange(globalSnapshot.Traits);
        result.Patterns.AddRange(globalPatterns);

        foreach (var period in periodSlices)
        {
            var periodTraits = await _traitExtractor.ExtractAsync(subjectType, subjectId, context, period, ct);
            var periodPatterns = await _patternSynthesisService.BuildPatternsAsync(subjectType, subjectId, context, period, ct);
            var periodAssessment = _confidenceEvaluator.Evaluate(subjectType, periodTraits, context, period);

            var periodSnapshot = await PersistSnapshotBundleAsync(
                request,
                context,
                subjectType,
                subjectId,
                period,
                periodAssessment,
                periodTraits,
                periodPatterns,
                ct);

            result.Snapshots.Add(periodSnapshot.Snapshot);
            result.Traits.AddRange(periodSnapshot.Traits);
            result.Patterns.AddRange(periodPatterns);
        }
    }

    private async Task BuildPairAsync(
        ProfileEngineRequest request,
        ProfileEvidenceContext context,
        IReadOnlyList<Period> periodSlices,
        ProfileEngineResult result,
        CancellationToken ct)
    {
        var subjectType = "pair";
        var subjectId = $"{context.SelfSenderId}:{context.OtherSenderId}";

        var globalTraits = await _pairProfileSynthesizer.SynthesizeAsync(context, null, ct);
        var globalPatterns = await _patternSynthesisService.BuildPatternsAsync(subjectType, subjectId, context, null, ct);
        var globalAssessment = _confidenceEvaluator.Evaluate(subjectType, globalTraits, context, null);

        var globalSnapshot = await PersistSnapshotBundleAsync(
            request,
            context,
            subjectType,
            subjectId,
            period: null,
            globalAssessment,
            globalTraits,
            globalPatterns,
            ct);

        result.Snapshots.Add(globalSnapshot.Snapshot);
        result.Traits.AddRange(globalSnapshot.Traits);
        result.Patterns.AddRange(globalPatterns);

        foreach (var period in periodSlices)
        {
            var periodTraits = await _pairProfileSynthesizer.SynthesizeAsync(context, period, ct);
            var periodPatterns = await _patternSynthesisService.BuildPatternsAsync(subjectType, subjectId, context, period, ct);
            var periodAssessment = _confidenceEvaluator.Evaluate(subjectType, periodTraits, context, period);

            var periodSnapshot = await PersistSnapshotBundleAsync(
                request,
                context,
                subjectType,
                subjectId,
                period,
                periodAssessment,
                periodTraits,
                periodPatterns,
                ct);

            result.Snapshots.Add(periodSnapshot.Snapshot);
            result.Traits.AddRange(periodSnapshot.Traits);
            result.Patterns.AddRange(periodPatterns);
        }
    }

    private async Task<(ProfileSnapshot Snapshot, List<ProfileTrait> Traits)> PersistSnapshotBundleAsync(
        ProfileEngineRequest request,
        ProfileEvidenceContext context,
        string subjectType,
        string subjectId,
        Period? period,
        ProfileAssessment assessment,
        IReadOnlyList<ProfileTraitDraft> traitDrafts,
        IReadOnlyList<ProfilePatternRecord> patternRecords,
        CancellationToken ct)
    {
        var latestMessage = context.Messages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
        var latestSession = context.Sessions.OrderByDescending(x => x.EndDate).FirstOrDefault();

        var snapshot = new ProfileSnapshot
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            PeriodId = period?.Id,
            Summary = BuildSnapshotSummary(assessment, traitDrafts, patternRecords),
            Confidence = assessment.Confidence,
            Stability = assessment.Stability,
            SourceSessionId = latestSession?.Id,
            SourceMessageId = latestMessage?.Id,
            CreatedAt = DateTime.UtcNow
        };

        if (request.Persist)
        {
            snapshot = await _stateProfileRepository.CreateProfileSnapshotAsync(snapshot, ct);
        }

        var traits = new List<ProfileTrait>();
        foreach (var draft in traitDrafts)
        {
            var trait = new ProfileTrait
            {
                ProfileSnapshotId = snapshot.Id,
                TraitKey = draft.TraitKey,
                ValueLabel = draft.ValueLabel,
                Confidence = draft.Confidence,
                Stability = draft.Stability,
                IsSensitive = draft.IsSensitive,
                EvidenceRefsJson = JsonSerializer.Serialize(draft.EvidenceRefs, JsonOptions),
                SourceSessionId = latestSession?.Id,
                SourceMessageId = latestMessage?.Id,
                CreatedAt = DateTime.UtcNow
            };

            if (request.Persist)
            {
                trait = await _stateProfileRepository.CreateProfileTraitAsync(trait, ct);
            }

            traits.Add(trait);
        }

        foreach (var pattern in patternRecords)
        {
            var trait = new ProfileTrait
            {
                ProfileSnapshotId = snapshot.Id,
                TraitKey = pattern.PatternType,
                ValueLabel = pattern.Summary,
                Confidence = pattern.Confidence,
                Stability = Math.Clamp(assessment.Stability - 0.05f, 0.2f, 0.9f),
                IsSensitive = false,
                EvidenceRefsJson = JsonSerializer.Serialize(pattern.EvidenceRefs, JsonOptions),
                SourceSessionId = latestSession?.Id,
                SourceMessageId = latestMessage?.Id,
                CreatedAt = DateTime.UtcNow
            };

            if (request.Persist)
            {
                trait = await _stateProfileRepository.CreateProfileTraitAsync(trait, ct);
            }

            traits.Add(trait);
        }

        if (request.Persist)
        {
            await _domainReviewEventRepository.AddAsync(new DomainReviewEvent
            {
                ObjectType = "profile_snapshot",
                ObjectId = snapshot.Id.ToString(),
                Action = "profile_computed",
                NewValueRef = JsonSerializer.Serialize(new
                {
                    snapshot.SubjectType,
                    snapshot.SubjectId,
                    snapshot.PeriodId,
                    snapshot.Confidence,
                    snapshot.Stability,
                    traits = traitDrafts.Select(x => new
                    {
                        x.TraitKey,
                        x.ValueLabel,
                        x.Confidence,
                        x.Stability,
                        x.IsSensitive,
                        x.IsPeriodSpecific,
                        x.IsTemporary
                    })
                }, JsonOptions),
                Reason = request.SourceId,
                Actor = request.Actor,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }

        return (snapshot, traits);
    }

    private async Task<ProfileEvidenceContext> LoadContextAsync(ProfileEngineRequest request, DateTime asOf, CancellationToken ct)
    {
        var fromUtc = asOf.AddDays(-180);
        var messages = await _messageRepository.GetByChatAndPeriodAsync(request.ChatId, fromUtc, asOf, 6000, ct);
        var senderCounts = messages
            .Where(x => x.SenderId > 0)
            .GroupBy(x => x.SenderId)
            .Select(g => new { SenderId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (senderCounts.Count < 2)
        {
            throw new InvalidOperationException("Profile engine requires at least two active senders to build self/other/pair profiles.");
        }

        var selfSenderId = request.SelfSenderId ?? senderCounts[0].SenderId;
        var otherSenderId = senderCounts
            .Where(x => x.SenderId != selfSenderId)
            .Select(x => x.SenderId)
            .FirstOrDefault();

        if (otherSenderId <= 0)
        {
            otherSenderId = senderCounts.First(x => x.SenderId != selfSenderId).SenderId;
        }

        var sessionsByChat = await _chatSessionRepository.GetByChatsAsync([request.ChatId], ct);
        var sessions = sessionsByChat.GetValueOrDefault(request.ChatId, [])
            .Where(x => x.EndDate <= asOf)
            .OrderByDescending(x => x.EndDate)
            .Take(16)
            .ToList();

        var periods = (await _periodRepository.GetPeriodsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.StartAt <= asOf)
            .ToList();

        var questions = await _clarificationRepository.GetQuestionsAsync(request.CaseId, null, null, ct);
        questions = questions
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.CreatedAt <= asOf)
            .ToList();

        var answers = new List<ClarificationAnswer>();
        foreach (var question in questions.Take(100))
        {
            var byQuestion = await _clarificationRepository.GetAnswersByQuestionIdAsync(question.Id, ct);
            answers.AddRange(byQuestion.Where(x => x.CreatedAt <= asOf));
        }

        var offlineEvents = (await _offlineEventRepository.GetOfflineEventsByCaseAsync(request.CaseId, ct))
            .Where(x => x.ChatId == null || x.ChatId == request.ChatId)
            .Where(x => x.TimestampStart <= asOf)
            .OrderByDescending(x => x.TimestampStart)
            .Take(80)
            .ToList();

        var claims = await _intelligenceRepository.GetClaimsByChatAndPeriodAsync(
            request.ChatId,
            fromUtc,
            asOf,
            limit: 12000,
            ct);
        var profileSignalClaims = claims
            .Where(x => x.ClaimType.Equals("profile_signal", StringComparison.OrdinalIgnoreCase))
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToList();

        var stateSnapshots = await _stateProfileRepository.GetStateSnapshotsByCaseAsync(request.CaseId, 30, ct);

        _logger.LogInformation(
            "Profile engine evidence loaded: chat_id={ChatId}, messages={MessageCount}, profile_signal_claims={ProfileSignalCount}",
            request.ChatId,
            messages.Count,
            profileSignalClaims.Count);

        return new ProfileEvidenceContext
        {
            AsOfUtc = asOf,
            CaseId = request.CaseId,
            ChatId = request.ChatId,
            SelfSenderId = selfSenderId,
            OtherSenderId = otherSenderId,
            Messages = messages.OrderBy(x => x.Timestamp).ToList(),
            Sessions = sessions,
            Periods = periods,
            ClarificationQuestions = questions,
            ClarificationAnswers = answers.OrderByDescending(x => x.CreatedAt).ToList(),
            OfflineEvents = offlineEvents,
            StateSnapshots = stateSnapshots.Where(x => x.AsOf <= asOf).OrderByDescending(x => x.AsOf).ToList(),
            ProfileSignalClaims = profileSignalClaims
        };
    }

    private static string BuildSnapshotSummary(
        ProfileAssessment assessment,
        IReadOnlyList<ProfileTraitDraft> traitDrafts,
        IReadOnlyList<ProfilePatternRecord> patterns)
    {
        var traitSlice = string.Join(", ", traitDrafts.Take(3).Select(x => $"{x.TraitKey}={x.ValueLabel}"));
        var works = patterns.FirstOrDefault(x => x.PatternType == "what_works")?.Summary ?? "n/a";
        var fails = patterns.FirstOrDefault(x => x.PatternType == "what_fails")?.Summary ?? "n/a";

        return $"{assessment.Summary} Traits: {traitSlice}. Works: {works} Fails: {fails}";
    }
}

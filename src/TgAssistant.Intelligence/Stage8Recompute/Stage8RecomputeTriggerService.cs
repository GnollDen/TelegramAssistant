using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Intelligence.Stage8Recompute;

public class Stage8RecomputeTriggerService : IStage8RecomputeTriggerService
{
    private static readonly IReadOnlyList<string> Stage7Families =
    [
        Stage8RecomputeTargetFamilies.DossierProfile,
        Stage8RecomputeTargetFamilies.PairDynamics,
        Stage8RecomputeTargetFamilies.TimelineObjects
    ];

    private static readonly HashSet<string> HighSignalActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "periodization_created",
        "state_computed",
        "strategy_generated",
        "outcome_recorded",
        "external_archive_ingested",
        "period_proposal_created"
    };

    private static readonly HashSet<string> HighSignalObjectTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "period",
        "period_transition",
        "state_snapshot",
        "profile_snapshot",
        "profile_trait",
        "strategy_record",
        "draft_record",
        "draft_outcome",
        "external_archive_batch"
    };

    private readonly IStage8RecomputeQueueService _queueService;
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<Stage8RecomputeTriggerService> _logger;

    public Stage8RecomputeTriggerService(
        IStage8RecomputeQueueService queueService,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<Stage8RecomputeTriggerService> logger)
    {
        _queueService = queueService;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task HandleDomainReviewEventAsync(DomainReviewEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var scopeKey = await ResolveScopeKeyAsync(evt, ct);
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            _logger.LogDebug(
                "Stage8 trigger ignored: unable to resolve scope for domain review event id={EventId}, object_type={ObjectType}, object_id={ObjectId}, action={Action}",
                evt.Id,
                evt.ObjectType,
                evt.ObjectId,
                evt.Action);
            return;
        }

        await HandleSignalAsync(new Stage8RecomputeTriggerSignal
        {
            ScopeKey = scopeKey,
            ObjectType = evt.ObjectType ?? string.Empty,
            Action = evt.Action ?? string.Empty,
            TriggerSource = "domain_review_event",
            TriggerRef = $"domain_review_event:{evt.Id:D}"
        }, ct);
    }

    public async Task HandleSignalAsync(Stage8RecomputeTriggerSignal signal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var scopeKey = signal.ScopeKey?.Trim();
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            _logger.LogDebug(
                "Stage8 trigger signal ignored: missing scope key. source={TriggerSource}, object_type={ObjectType}, action={Action}, trigger_ref={TriggerRef}",
                signal.TriggerSource,
                signal.ObjectType,
                signal.Action,
                signal.TriggerRef);
            return;
        }

        var plan = BuildTriggerPlan(signal);
        if (plan == null)
        {
            return;
        }

        var triggerRef = string.IsNullOrWhiteSpace(signal.TriggerRef)
            ? $"{(string.IsNullOrWhiteSpace(signal.TriggerSource) ? "direct_signal" : signal.TriggerSource)}:{Guid.NewGuid():N}"
            : signal.TriggerRef.Trim();
        var priority = signal.Priority ?? plan.Priority;
        foreach (var family in plan.TargetFamilies)
        {
            _ = await _queueService.EnqueueAsync(new Stage8RecomputeQueueRequest
            {
                ScopeKey = scopeKey,
                PersonId = signal.PersonId,
                TargetFamily = family,
                TriggerKind = plan.TriggerKind,
                TriggerRef = triggerRef,
                Priority = priority
            }, ct);
        }

        _logger.LogInformation(
            "Stage8 trigger ingested: source={TriggerSource}, trigger_ref={TriggerRef}, trigger_kind={TriggerKind}, scope_key={ScopeKey}, person_id={PersonId}, target_families={TargetFamilies}",
            string.IsNullOrWhiteSpace(signal.TriggerSource) ? "direct_signal" : signal.TriggerSource,
            triggerRef,
            plan.TriggerKind,
            scopeKey,
            signal.PersonId,
            string.Join(",", plan.TargetFamilies));
    }

    private static TriggerPlan? BuildTriggerPlan(Stage8RecomputeTriggerSignal signal)
    {
        if (signal.TargetFamilies.Count > 0)
        {
            var explicitFamilies = signal.TargetFamilies
                .Where(family => Stage8RecomputeTargetFamilies.All.Contains(family, StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (explicitFamilies.Count > 0)
            {
                return new TriggerPlan(
                    string.IsNullOrWhiteSpace(signal.TriggerSource) ? "direct_signal" : signal.TriggerSource.Trim(),
                    50,
                    explicitFamilies);
            }
        }

        var action = signal.Action?.Trim() ?? string.Empty;
        var objectType = signal.ObjectType?.Trim() ?? string.Empty;

        if (string.Equals(objectType, "clarification_answer", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(objectType, "clarification_question", StringComparison.OrdinalIgnoreCase)
                && string.Equals(action, "applied", StringComparison.OrdinalIgnoreCase)))
        {
            return new TriggerPlan("clarification_answer", 20, Stage7Families);
        }

        if (action.Contains("delete", StringComparison.OrdinalIgnoreCase)
            || action.Contains("remove", StringComparison.OrdinalIgnoreCase))
        {
            return new TriggerPlan("delete", 30, Stage7Families);
        }

        if (action.Contains("edit", StringComparison.OrdinalIgnoreCase))
        {
            return new TriggerPlan("edit", 40, Stage7Families);
        }

        if (HighSignalActions.Contains(action)
            || (HighSignalObjectTypes.Contains(objectType)
                && (action.Contains("created", StringComparison.OrdinalIgnoreCase)
                    || action.Contains("computed", StringComparison.OrdinalIgnoreCase)
                    || action.Contains("generated", StringComparison.OrdinalIgnoreCase)
                    || action.Contains("recorded", StringComparison.OrdinalIgnoreCase)
                    || action.Contains("ingested", StringComparison.OrdinalIgnoreCase))))
        {
            return new TriggerPlan("high_signal_data", 60, Stage7Families);
        }

        return null;
    }

    private async Task<string?> ResolveScopeKeyAsync(DomainReviewEvent evt, CancellationToken ct)
    {
        if (TryParseDirectScopeKey(evt.ObjectId, out var directScope))
        {
            return directScope;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await ResolveScopeKeyViaLookupAsync(db, evt, ct);
    }

    private static bool TryParseDirectScopeKey(string? objectId, out string? scopeKey)
    {
        scopeKey = null;
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return false;
        }

        var trimmed = objectId.Trim();
        if (trimmed.StartsWith("chat:", StringComparison.OrdinalIgnoreCase))
        {
            scopeKey = $"chat:{trimmed[5..]}";
            return true;
        }

        var segments = trimmed.Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && long.TryParse(segments[1], out var chatId))
        {
            scopeKey = $"chat:{chatId}";
            return true;
        }

        return false;
    }

    private static async Task<string?> ResolveScopeKeyViaLookupAsync(
        TgAssistantDbContext db,
        DomainReviewEvent evt,
        CancellationToken ct)
    {
        var objectType = evt.ObjectType?.Trim() ?? string.Empty;
        var objectId = evt.ObjectId?.Trim() ?? string.Empty;

        switch (objectType.ToLowerInvariant())
        {
            case "clarification_answer":
                if (Guid.TryParse(objectId, out var clarificationAnswerId))
                {
                    var chatId = await (
                        from answer in db.ClarificationAnswers.AsNoTracking()
                        join question in db.ClarificationQuestions.AsNoTracking() on answer.QuestionId equals question.Id
                        where answer.Id == clarificationAnswerId
                        select question.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "clarification_question":
                if (Guid.TryParse(objectId, out var clarificationQuestionId))
                {
                    var chatId = await db.ClarificationQuestions.AsNoTracking()
                        .Where(x => x.Id == clarificationQuestionId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "period":
                if (Guid.TryParse(objectId, out var periodId))
                {
                    var chatId = await db.Periods.AsNoTracking()
                        .Where(x => x.Id == periodId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "period_transition":
                if (Guid.TryParse(objectId, out var transitionId))
                {
                    var chatId = await (
                        from transition in db.PeriodTransitions.AsNoTracking()
                        join period in db.Periods.AsNoTracking() on transition.FromPeriodId equals period.Id
                        where transition.Id == transitionId
                        select period.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "state_snapshot":
                if (Guid.TryParse(objectId, out var stateSnapshotId))
                {
                    var chatId = await db.StateSnapshots.AsNoTracking()
                        .Where(x => x.Id == stateSnapshotId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "profile_snapshot":
                if (Guid.TryParse(objectId, out var profileSnapshotId))
                {
                    var chatId = await db.ProfileSnapshots.AsNoTracking()
                        .Where(x => x.Id == profileSnapshotId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "profile_trait":
                if (Guid.TryParse(objectId, out var profileTraitId))
                {
                    var chatId = await (
                        from trait in db.ProfileTraits.AsNoTracking()
                        join profile in db.ProfileSnapshots.AsNoTracking() on trait.ProfileSnapshotId equals profile.Id
                        where trait.Id == profileTraitId
                        select profile.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "strategy_record":
                if (Guid.TryParse(objectId, out var strategyRecordId))
                {
                    var chatId = await db.StrategyRecords.AsNoTracking()
                        .Where(x => x.Id == strategyRecordId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "strategy_option":
                if (Guid.TryParse(objectId, out var strategyOptionId))
                {
                    var chatId = await (
                        from option in db.StrategyOptions.AsNoTracking()
                        join strategy in db.StrategyRecords.AsNoTracking() on option.StrategyRecordId equals strategy.Id
                        where option.Id == strategyOptionId
                        select strategy.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "draft_record":
                if (Guid.TryParse(objectId, out var draftRecordId))
                {
                    var chatId = await (
                        from draft in db.DraftRecords.AsNoTracking()
                        join strategy in db.StrategyRecords.AsNoTracking() on draft.StrategyRecordId equals strategy.Id
                        where draft.Id == draftRecordId
                        select strategy.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "draft_outcome":
                if (Guid.TryParse(objectId, out var draftOutcomeId))
                {
                    var chatId = await (
                        from outcome in db.DraftOutcomes.AsNoTracking()
                        join draft in db.DraftRecords.AsNoTracking() on outcome.DraftId equals draft.Id
                        join strategy in db.StrategyRecords.AsNoTracking() on draft.StrategyRecordId equals strategy.Id
                        where outcome.Id == draftOutcomeId
                        select strategy.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "inbox_item":
                if (Guid.TryParse(objectId, out var inboxItemId))
                {
                    var chatId = await db.InboxItems.AsNoTracking()
                        .Where(x => x.Id == inboxItemId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "conflict_record":
                if (Guid.TryParse(objectId, out var conflictId))
                {
                    var chatId = await db.ConflictRecords.AsNoTracking()
                        .Where(x => x.Id == conflictId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;

            case "stage6_case":
                if (Guid.TryParse(objectId, out var stage6CaseId))
                {
                    var chatId = await db.Stage6Cases.AsNoTracking()
                        .Where(x => x.Id == stage6CaseId)
                        .Select(x => x.ChatId)
                        .FirstOrDefaultAsync(ct);
                    return chatId.HasValue ? $"chat:{chatId.Value}" : null;
                }

                break;
        }

        return null;
    }

    private sealed class TriggerPlan
    {
        public TriggerPlan(string triggerKind, int priority, IReadOnlyList<string> targetFamilies)
        {
            TriggerKind = triggerKind;
            Priority = priority;
            TargetFamilies = targetFamilies;
        }

        public string TriggerKind { get; }
        public int Priority { get; }
        public IReadOnlyList<string> TargetFamilies { get; }
    }
}

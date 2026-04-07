using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class CurrentWorldApproximationReadService : ICurrentWorldApproximationReadService
{
    private const string ProfileStatusFactType = TemporalSingleValuedFactFamilies.ProfileStatus;
    private const string RelationshipStateFactType = TemporalSingleValuedFactFamilies.RelationshipState;
    private const string TimelinePrimaryActivityFactType = TemporalSingleValuedFactFamilies.TimelinePrimaryActivity;

    private readonly ITemporalPersonStateRepository _temporalPersonStateRepository;
    private readonly IConditionalKnowledgeRepository _conditionalKnowledgeRepository;
    private readonly IStage7PairDynamicsRepository _stage7PairDynamicsRepository;
    private readonly IStage7TimelineRepository _stage7TimelineRepository;

    public CurrentWorldApproximationReadService(
        ITemporalPersonStateRepository temporalPersonStateRepository,
        IConditionalKnowledgeRepository conditionalKnowledgeRepository,
        IStage7PairDynamicsRepository stage7PairDynamicsRepository,
        IStage7TimelineRepository stage7TimelineRepository)
    {
        _temporalPersonStateRepository = temporalPersonStateRepository;
        _conditionalKnowledgeRepository = conditionalKnowledgeRepository;
        _stage7PairDynamicsRepository = stage7PairDynamicsRepository;
        _stage7TimelineRepository = stage7TimelineRepository;
    }

    public async Task<CurrentWorldApproximationSnapshot> BuildSnapshotAsync(
        CurrentWorldApproximationReadRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = NormalizeRequired(request.ScopeKey, nameof(request.ScopeKey));
        if (request.TrackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(request));
        }

        var asOfUtc = request.AsOfUtc == default ? DateTime.UtcNow : request.AsOfUtc;
        var snapshotId = Guid.NewGuid();

        var allScopedStates = await _temporalPersonStateRepository.QueryScopedAsync(
            new TemporalPersonStateScopeQuery
            {
                ScopeKey = scopeKey,
                TrackedPersonId = request.TrackedPersonId,
                Limit = 500
            },
            ct);

        var openStates = allScopedStates
            .Where(x => TemporalPersonStateContract.IsCurrentOpenState(x, asOfUtc))
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .ToList();

        var pairSurface = await _stage7PairDynamicsRepository.GetCurrentWorldReadSurfaceAsync(scopeKey, request.TrackedPersonId, ct);
        var timelineSurface = await _stage7TimelineRepository.GetCurrentWorldReadSurfaceAsync(scopeKey, request.TrackedPersonId, ct);
        var openConditionalStates = await _conditionalKnowledgeRepository.QueryOpenScopedAsync(scopeKey, request.TrackedPersonId, asOfUtc, ct);

        var snapshot = new CurrentWorldApproximationSnapshot
        {
            SnapshotId = snapshotId,
            ScopeKey = scopeKey,
            TrackedPersonId = request.TrackedPersonId,
            AsOfUtc = asOfUtc,
            ReadState = CurrentWorldApproximationReadStates.RecomputedOnRead
        };

        PopulatePeopleRows(snapshot, openStates);
        PopulateRelationshipRows(snapshot, openStates, pairSurface);
        PopulateConditionRows(snapshot, openStates, openConditionalStates, timelineSurface);
        PopulateRecentChangeRows(snapshot, allScopedStates, asOfUtc);

        var disagreementReasons = CollectDisagreementReasons(snapshot, pairSurface, timelineSurface);
        foreach (var reason in disagreementReasons)
        {
            snapshot.UncertaintyRefs.Add(reason);
        }

        var hasPublishableRows = snapshot.ActivePersonRows.Count > 0
            || snapshot.InactivePersonRows.Count > 0
            || snapshot.RelationshipStateRows.Count > 0
            || snapshot.ActiveConditionRows.Count > 0
            || snapshot.RecentChangeRows.Count > 0;

        if (!hasPublishableRows)
        {
            snapshot.PublicationState = CurrentWorldApproximationPublicationStates.InsufficientEvidence;
            snapshot.ReadState = CurrentWorldApproximationReadStates.NoPublishableContent;
            snapshot.UncertaintyRefs.Add("current_world:insufficient_evidence:no_publishable_content");
        }
        else if (snapshot.UncertaintyRefs.Count > 0)
        {
            snapshot.PublicationState = CurrentWorldApproximationPublicationStates.Unresolved;
        }
        else
        {
            snapshot.PublicationState = CurrentWorldApproximationPublicationStates.Publishable;
        }

        ApplyPublicationState(snapshot);
        ApplyConditionalPublicationState(snapshot);
        return snapshot;
    }

    private static void PopulatePeopleRows(
        CurrentWorldApproximationSnapshot snapshot,
        IReadOnlyCollection<TemporalPersonState> openStates)
    {
        foreach (var group in openStates.GroupBy(x => x.SubjectRef, StringComparer.Ordinal))
        {
            var statusState = group
                .Where(x => string.Equals(x.FactType, ProfileStatusFactType, StringComparison.Ordinal))
                .OrderByDescending(x => x.ValidFromUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefault();

            var subjectStates = group.OrderByDescending(x => x.ValidFromUtc).ThenByDescending(x => x.Id).ToList();
            var sourceRefIds = subjectStates
                .SelectMany(GetSourceRefs)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var evidenceRefs = subjectStates
                .SelectMany(x => x.EvidenceRefs)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var validFrom = statusState?.ValidFromUtc ?? subjectStates.Min(x => x.ValidFromUtc);
            var validTo = statusState?.ValidToUtc;
            var statusLabel = NormalizeOptional(statusState?.Value) ?? "active";
            var isInactive = IsInactiveStatus(statusLabel);
            var isDroppedOut = IsDroppedOutStatus(statusLabel);

            if (isInactive)
            {
                snapshot.InactivePersonRows.Add(new InactivePersonRow
                {
                    InactivePersonRowId = Guid.NewGuid(),
                    SnapshotId = snapshot.SnapshotId,
                    SubjectRef = group.Key,
                    InactiveReason = statusLabel,
                    DroppedOutFlag = isDroppedOut,
                    LastSeenUtc = subjectStates.Max(x => x.ValidToUtc ?? x.ValidFromUtc),
                    ValidFromUtc = validFrom,
                    ValidToUtc = validTo,
                    EvidenceRefs = evidenceRefs,
                    SourceRefIds = sourceRefIds
                });
                continue;
            }

            snapshot.ActivePersonRows.Add(new ActivePersonRow
            {
                PersonRowId = Guid.NewGuid(),
                SnapshotId = snapshot.SnapshotId,
                SubjectRef = group.Key,
                StateLabel = statusLabel,
                ValidFromUtc = validFrom,
                ValidToUtc = validTo,
                EvidenceRefs = evidenceRefs,
                SourceRefIds = sourceRefIds
            });
        }

        snapshot.ActivePersonRows = snapshot.ActivePersonRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
        snapshot.InactivePersonRows = snapshot.InactivePersonRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
    }

    private static void PopulateRelationshipRows(
        CurrentWorldApproximationSnapshot snapshot,
        IReadOnlyCollection<TemporalPersonState> openStates,
        CurrentWorldPairDynamicsReadSurface? pairSurface)
    {
        var relationshipStates = openStates
            .Where(x => string.Equals(x.FactType, RelationshipStateFactType, StringComparison.Ordinal))
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .ToList();

        foreach (var state in relationshipStates)
        {
            var sourceRefIds = GetSourceRefs(state).Concat(pairSurface?.SourceRefIds ?? []).Distinct(StringComparer.Ordinal).ToList();
            var evidenceRefs = state.EvidenceRefs.Concat(pairSurface?.EvidenceRefs ?? []).Distinct(StringComparer.Ordinal).ToList();
            snapshot.RelationshipStateRows.Add(new RelationshipStateRow
            {
                RelationshipRowId = Guid.NewGuid(),
                SnapshotId = snapshot.SnapshotId,
                SubjectRef = state.SubjectRef,
                RelatedSubjectRef = pairSurface?.RelatedPersonId is Guid relatedPersonId
                    ? $"person:{relatedPersonId:D}"
                    : "person:unknown",
                RelationshipState = state.Value,
                ValidFromUtc = state.ValidFromUtc,
                ValidToUtc = state.ValidToUtc,
                EvidenceRefs = evidenceRefs,
                SourceRefIds = sourceRefIds
            });
        }

        snapshot.RelationshipStateRows = snapshot.RelationshipStateRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
    }

    private static void PopulateConditionRows(
        CurrentWorldApproximationSnapshot snapshot,
        IReadOnlyCollection<TemporalPersonState> openStates,
        IReadOnlyCollection<ConditionalKnowledgeState> openConditionalStates,
        CurrentWorldTimelineReadSurface? timelineSurface)
    {
        var conditionStates = openStates
            .Where(x => string.Equals(x.FactType, TimelinePrimaryActivityFactType, StringComparison.Ordinal)
                || string.Equals(x.FactCategory, TemporalPersonStateFactCategories.EventConditioned, StringComparison.Ordinal))
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .ToList();

        foreach (var state in conditionStates)
        {
            var sourceRefIds = GetSourceRefs(state).Concat(timelineSurface?.SourceRefIds ?? []).Distinct(StringComparer.Ordinal).ToList();
            var evidenceRefs = state.EvidenceRefs.Concat(timelineSurface?.EvidenceRefs ?? []).Distinct(StringComparer.Ordinal).ToList();
            snapshot.ActiveConditionRows.Add(new ActiveConditionRow
            {
                ConditionRowId = Guid.NewGuid(),
                SnapshotId = snapshot.SnapshotId,
                SubjectRef = state.SubjectRef,
                ConditionType = state.FactType,
                ConditionValue = state.Value,
                ValidFromUtc = state.ValidFromUtc,
                ValidToUtc = state.ValidToUtc,
                EvidenceRefs = evidenceRefs,
                SourceRefIds = sourceRefIds
            });
        }

        foreach (var conditionalState in openConditionalStates
                     .OrderByDescending(x => x.ValidFromUtc)
                     .ThenByDescending(x => x.Id))
        {
            var evidenceRefs = conditionalState.EvidenceRefs
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var sourceRefIds = conditionalState.SourceRefIds
                .Concat(GetSourceRefs(conditionalState))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var linkedTemporalStateIds = conditionalState.LinkedTemporalStateIds
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            snapshot.ActiveConditionRows.Add(new ActiveConditionRow
            {
                ConditionRowId = Guid.NewGuid(),
                SnapshotId = snapshot.SnapshotId,
                SubjectRef = conditionalState.SubjectRef,
                ConditionType = $"conditional:{conditionalState.FactFamily}:{conditionalState.RuleKind}",
                ConditionValue = BuildConditionalConditionValue(conditionalState),
                ValidFromUtc = conditionalState.ValidFromUtc,
                ValidToUtc = conditionalState.ValidToUtc,
                EvidenceRefs = evidenceRefs,
                SourceRefIds = sourceRefIds
            });

            snapshot.ActiveNowConditionalRows.Add(new ActiveNowConditionalRow
            {
                ActiveNowConditionalRowId = Guid.NewGuid(),
                SnapshotId = snapshot.SnapshotId,
                ConditionalStateId = conditionalState.Id,
                RuleKind = conditionalState.RuleKind,
                RuleId = conditionalState.RuleId,
                ParentRuleId = conditionalState.ParentRuleId,
                SubjectRef = conditionalState.SubjectRef,
                FactFamily = conditionalState.FactFamily,
                ResolvedValue = BuildConditionalConditionValue(conditionalState),
                ValidFromUtc = conditionalState.ValidFromUtc,
                ValidToUtc = conditionalState.ValidToUtc,
                Confidence = conditionalState.Confidence,
                EvidenceRefs = evidenceRefs,
                SourceRefIds = sourceRefIds,
                LinkedTemporalStateIds = linkedTemporalStateIds
            });

            if (string.Equals(conditionalState.RuleKind, ConditionalKnowledgeRuleKinds.BaselineRule, StringComparison.Ordinal))
            {
                snapshot.ConditionalBaselineRuleRows.Add(new ConditionalBaselineRuleRow
                {
                    RuleRowId = Guid.NewGuid(),
                    SnapshotId = snapshot.SnapshotId,
                    ConditionalStateId = conditionalState.Id,
                    RuleId = conditionalState.RuleId,
                    SubjectRef = conditionalState.SubjectRef,
                    FactFamily = conditionalState.FactFamily,
                    BaselineValue = NormalizeOptional(conditionalState.BaselineValue) ?? string.Empty,
                    ValidFromUtc = conditionalState.ValidFromUtc,
                    ValidToUtc = conditionalState.ValidToUtc,
                    Confidence = conditionalState.Confidence,
                    EvidenceRefs = evidenceRefs,
                    SourceRefIds = sourceRefIds,
                    LinkedTemporalStateIds = linkedTemporalStateIds
                });
            }
            else if (string.Equals(conditionalState.RuleKind, ConditionalKnowledgeRuleKinds.ExceptionRule, StringComparison.Ordinal))
            {
                snapshot.ConditionalExceptionRuleRows.Add(new ConditionalExceptionRuleRow
                {
                    ExceptionRowId = Guid.NewGuid(),
                    SnapshotId = snapshot.SnapshotId,
                    ConditionalStateId = conditionalState.Id,
                    ExceptionId = conditionalState.Id,
                    RuleId = conditionalState.RuleId,
                    SubjectRef = conditionalState.SubjectRef,
                    FactFamily = conditionalState.FactFamily,
                    ExceptionValue = NormalizeOptional(conditionalState.ExceptionValue) ?? string.Empty,
                    ConditionClauseIds = conditionalState.ConditionClauseIds
                        .Where(x => x != Guid.Empty)
                        .Distinct()
                        .ToList(),
                    ValidFromUtc = conditionalState.ValidFromUtc,
                    ValidToUtc = conditionalState.ValidToUtc,
                    Confidence = conditionalState.Confidence,
                    EvidenceRefs = evidenceRefs,
                    SourceRefIds = sourceRefIds,
                    LinkedTemporalStateIds = linkedTemporalStateIds
                });
            }
            else if (string.Equals(conditionalState.RuleKind, ConditionalKnowledgeRuleKinds.PhaseMarker, StringComparison.Ordinal))
            {
                snapshot.ConditionalPhaseMarkerRows.Add(new ConditionalPhaseMarkerRow
                {
                    PhaseMarkerRowId = Guid.NewGuid(),
                    SnapshotId = snapshot.SnapshotId,
                    ConditionalStateId = conditionalState.Id,
                    PhaseMarkerId = conditionalState.Id,
                    SubjectRef = conditionalState.SubjectRef,
                    FactFamily = conditionalState.FactFamily,
                    PhaseLabel = NormalizeOptional(conditionalState.PhaseLabel) ?? string.Empty,
                    PhaseReason = NormalizeOptional(conditionalState.PhaseReason) ?? string.Empty,
                    ValidFromUtc = conditionalState.ValidFromUtc,
                    ValidToUtc = conditionalState.ValidToUtc,
                    Confidence = conditionalState.Confidence,
                    EvidenceRefs = evidenceRefs,
                    SourceRefIds = sourceRefIds,
                    LinkedTemporalStateIds = linkedTemporalStateIds
                });
            }
        }

        snapshot.ActiveConditionRows = snapshot.ActiveConditionRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
        snapshot.ActiveNowConditionalRows = snapshot.ActiveNowConditionalRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
        snapshot.ConditionalBaselineRuleRows = snapshot.ConditionalBaselineRuleRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
        snapshot.ConditionalExceptionRuleRows = snapshot.ConditionalExceptionRuleRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
        snapshot.ConditionalPhaseMarkerRows = snapshot.ConditionalPhaseMarkerRows
            .OrderBy(x => x.SubjectRef, StringComparer.Ordinal)
            .ThenByDescending(x => x.ValidFromUtc)
            .ToList();
    }

    private static void PopulateRecentChangeRows(
        CurrentWorldApproximationSnapshot snapshot,
        IReadOnlyCollection<TemporalPersonState> allScopedStates,
        DateTime asOfUtc)
    {
        var rows = allScopedStates
            .Where(x => x.ValidFromUtc <= asOfUtc)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .Select(state => new RecentChangeRow
            {
                ChangeRowId = Guid.NewGuid(),
                SnapshotId = snapshot.SnapshotId,
                SubjectRef = state.SubjectRef,
                ChangeType = state.FactType,
                ChangeSummary = $"{state.FactType}:{state.Value}",
                ChangedAtUtc = state.UpdatedAtUtc,
                EvidenceRefs = state.EvidenceRefs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList(),
                SourceRefIds = GetSourceRefs(state).Distinct(StringComparer.Ordinal).ToList()
            })
            .ToList();

        snapshot.RecentChangeRows = rows;
    }

    private static List<string> CollectDisagreementReasons(
        CurrentWorldApproximationSnapshot snapshot,
        CurrentWorldPairDynamicsReadSurface? pairSurface,
        CurrentWorldTimelineReadSurface? timelineSurface)
    {
        var reasons = new List<string>();

        if (pairSurface?.HasContradictionMarkers == true)
        {
            reasons.Add("current_world:unresolved:pair_dynamics_contradiction_markers_present");
        }

        if (timelineSurface?.HasContradictionMarkers == true)
        {
            reasons.Add("current_world:unresolved:timeline_contradiction_markers_present");
        }

        var relationshipRow = snapshot.RelationshipStateRows.FirstOrDefault();
        if (relationshipRow != null && !string.IsNullOrWhiteSpace(pairSurface?.RelationshipStateHint))
        {
            var relationshipStateNorm = NormalizeComparisonValue(relationshipRow.RelationshipState);
            var pairHintNorm = NormalizeComparisonValue(pairSurface.RelationshipStateHint!);
            if (!string.Equals(relationshipStateNorm, pairHintNorm, StringComparison.Ordinal))
            {
                reasons.Add("current_world:unresolved:temporal_vs_pair_relationship_disagreement");
            }
        }

        var activityRow = snapshot.ActiveConditionRows
            .FirstOrDefault(x => string.Equals(x.ConditionType, TimelinePrimaryActivityFactType, StringComparison.Ordinal));
        if (activityRow != null && !string.IsNullOrWhiteSpace(timelineSurface?.TimelinePrimaryActivityHint))
        {
            var activityNorm = NormalizeComparisonValue(activityRow.ConditionValue);
            var timelineHintNorm = NormalizeComparisonValue(timelineSurface.TimelinePrimaryActivityHint!);
            if (!activityNorm.Contains(timelineHintNorm, StringComparison.Ordinal)
                && !timelineHintNorm.Contains(activityNorm, StringComparison.Ordinal))
            {
                reasons.Add("current_world:unresolved:temporal_vs_timeline_activity_disagreement");
            }
        }

        return reasons.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void ApplyPublicationState(CurrentWorldApproximationSnapshot snapshot)
    {
        var publicationState = snapshot.PublicationState;

        foreach (var row in snapshot.ActivePersonRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.InactivePersonRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.RelationshipStateRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.ActiveConditionRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.RecentChangeRows)
        {
            row.PublicationState = publicationState;
        }
    }

    private static void ApplyConditionalPublicationState(CurrentWorldApproximationSnapshot snapshot)
    {
        var publicationState = ResolveWsB5PublicationState(snapshot);
        foreach (var row in snapshot.ConditionalBaselineRuleRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.ConditionalExceptionRuleRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.ActiveNowConditionalRows)
        {
            row.PublicationState = publicationState;
        }

        foreach (var row in snapshot.ConditionalPhaseMarkerRows)
        {
            row.PublicationState = publicationState;
        }
    }

    private static string ResolveWsB5PublicationState(CurrentWorldApproximationSnapshot snapshot)
    {
        if (string.Equals(snapshot.PublicationState, CurrentWorldApproximationPublicationStates.Publishable, StringComparison.Ordinal))
        {
            return WsB5ResponsePublicationStates.Publishable;
        }

        if (string.Equals(snapshot.PublicationState, CurrentWorldApproximationPublicationStates.InsufficientEvidence, StringComparison.Ordinal))
        {
            return WsB5ResponsePublicationStates.InsufficientEvidence;
        }

        var requiresEscalation = snapshot.UncertaintyRefs.Any(x =>
            x.Contains("contradiction", StringComparison.Ordinal)
            || x.Contains("disagreement", StringComparison.Ordinal));
        return requiresEscalation
            ? WsB5ResponsePublicationStates.EscalationOnly
            : WsB5ResponsePublicationStates.ManualReviewRequired;
    }

    private static bool IsInactiveStatus(string statusValue)
    {
        var normalized = NormalizeComparisonValue(statusValue);
        return normalized.Contains("inactive", StringComparison.Ordinal)
            || normalized.Contains("dropped", StringComparison.Ordinal)
            || normalized.Contains("dropout", StringComparison.Ordinal)
            || normalized.Contains("absent", StringComparison.Ordinal)
            || normalized.Contains("left", StringComparison.Ordinal)
            || normalized.Contains("deceased", StringComparison.Ordinal);
    }

    private static bool IsDroppedOutStatus(string statusValue)
    {
        var normalized = NormalizeComparisonValue(statusValue);
        return normalized.Contains("dropped", StringComparison.Ordinal)
            || normalized.Contains("dropout", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetSourceRefs(TemporalPersonState state)
    {
        if (!string.IsNullOrWhiteSpace(state.TriggerRef))
        {
            yield return state.TriggerRef.Trim();
        }

        if (state.TriggerModelPassRunId.HasValue)
        {
            yield return $"model_pass:{state.TriggerModelPassRunId.Value:D}";
        }

        yield return $"temporal_state:{state.Id:D}";
    }

    private static IEnumerable<string> GetSourceRefs(ConditionalKnowledgeState state)
    {
        if (!string.IsNullOrWhiteSpace(state.TriggerRef))
        {
            yield return state.TriggerRef.Trim();
        }

        if (state.TriggerModelPassRunId.HasValue)
        {
            yield return $"model_pass:{state.TriggerModelPassRunId.Value:D}";
        }

        yield return $"conditional_state:{state.Id:D}";
    }

    private static string BuildConditionalConditionValue(ConditionalKnowledgeState state)
    {
        if (string.Equals(state.RuleKind, ConditionalKnowledgeRuleKinds.BaselineRule, StringComparison.Ordinal))
        {
            return NormalizeOptional(state.BaselineValue) ?? "n/a";
        }

        if (string.Equals(state.RuleKind, ConditionalKnowledgeRuleKinds.ExceptionRule, StringComparison.Ordinal))
        {
            return NormalizeOptional(state.ExceptionValue) ?? "n/a";
        }

        if (string.Equals(state.RuleKind, ConditionalKnowledgeRuleKinds.StyleDrift, StringComparison.Ordinal))
        {
            return NormalizeOptional(state.StyleLabel) ?? "n/a";
        }

        if (string.Equals(state.RuleKind, ConditionalKnowledgeRuleKinds.PhaseMarker, StringComparison.Ordinal))
        {
            var phase = NormalizeOptional(state.PhaseLabel) ?? "unknown";
            var reason = NormalizeOptional(state.PhaseReason) ?? "unspecified";
            return $"{phase} ({reason})";
        }

        return "n/a";
    }

    private static string NormalizeRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeComparisonValue(string value)
    {
        return string.Concat(value
            .Trim()
            .ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch)));
    }
}

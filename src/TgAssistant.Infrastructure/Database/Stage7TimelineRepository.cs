using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage7TimelineRepository : IStage7TimelineRepository
{
    private const string ActiveStatus = "active";
    private const string PendingPromotionState = "pending";
    private const string EventEvidenceLinkRole = "event_supporting";
    private const string TimelineEpisodeEvidenceLinkRole = "timeline_episode_supporting";
    private const string StoryArcEvidenceLinkRole = "story_arc_supporting";
    private const string TemporalTriggerKind = "stage7_timeline";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ITemporalPersonStateRepository _temporalPersonStateRepository;
    private readonly ILogger<Stage7TimelineRepository> _logger;

    public Stage7TimelineRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ITemporalPersonStateRepository temporalPersonStateRepository,
        ILogger<Stage7TimelineRepository> logger)
    {
        _dbFactory = dbFactory;
        _temporalPersonStateRepository = temporalPersonStateRepository;
        _logger = logger;
    }

    public async Task<Stage7TimelineFormationResult> UpsertAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        ArgumentNullException.ThrowIfNull(bootstrapResult);

        if (!string.Equals(auditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            || bootstrapResult.TrackedPerson == null
            || string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey))
        {
            throw new InvalidOperationException("Stage7 durable timeline persistence requires a ready bootstrap result with tracked person context.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var trackedPerson = bootstrapResult.TrackedPerson;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var scopeKey = bootstrapResult.ScopeKey;
        var evidenceItemIds = CollectEvidenceItemIds(auditRecord, bootstrapResult);
        var eventConfidence = AverageConfidence(
            auditRecord.Normalization.NormalizedPayload.Facts.Select(x => x.Confidence)
                .Concat(auditRecord.Normalization.NormalizedPayload.Conflicts.Select(x => x.Confidence)),
            0.65f);
        var timelineConfidence = AverageConfidence(
            auditRecord.Normalization.NormalizedPayload.Inferences.Select(x => x.Confidence)
                .Concat(auditRecord.Normalization.NormalizedPayload.Hypotheses.Select(x => x.Confidence)),
            0.62f);
        var storyArcConfidence = AverageConfidence(
            auditRecord.Normalization.NormalizedPayload.Inferences.Select(x => x.Confidence)
                .Concat(auditRecord.Normalization.NormalizedPayload.Conflicts.Select(x => x.Confidence)),
            0.60f);
        var eventBoundaryConfidence = ComputeBoundaryConfidence(bootstrapResult, 0.88f);
        var episodeBoundaryConfidence = ComputeBoundaryConfidence(bootstrapResult, 0.83f);
        var storyArcBoundaryConfidence = ComputeBoundaryConfidence(bootstrapResult, 0.78f);
        var stability = ComputeStability(bootstrapResult.ContradictionOutputs.Count);
        var eventDecayPolicy = DurableDecayPolicyCatalog.Resolve(Stage7DurableObjectFamilies.Event);
        var timelineDecayPolicy = DurableDecayPolicyCatalog.Resolve(Stage7DurableObjectFamilies.TimelineEpisode);
        var storyArcDecayPolicy = DurableDecayPolicyCatalog.Resolve(Stage7DurableObjectFamilies.StoryArc);
        var eventFreshness = DurableDecayPolicyCatalog.ComputeFreshness(Stage7DurableObjectFamilies.Event, bootstrapResult.LatestEvidenceAtUtc, now);
        var timelineFreshness = DurableDecayPolicyCatalog.ComputeFreshness(Stage7DurableObjectFamilies.TimelineEpisode, bootstrapResult.LatestEvidenceAtUtc, now);
        var storyArcFreshness = DurableDecayPolicyCatalog.ComputeFreshness(Stage7DurableObjectFamilies.StoryArc, bootstrapResult.LatestEvidenceAtUtc, now);
        var contradictionMarkersJson = BuildContradictionMarkersJson(bootstrapResult);
        var eventClosureState = ResolveEventClosureState(bootstrapResult);
        var episodeClosureState = ResolveTimelineEpisodeClosureState(bootstrapResult);
        var storyArcClosureState = ResolveStoryArcClosureState(bootstrapResult);

        var eventMetadata = await UpsertMetadataAsync(
            db,
            scopeKey,
            Stage7DurableObjectFamilies.Event,
            BuildEventObjectKey(trackedPerson.PersonId),
            trackedPerson.PersonId,
            operatorPerson?.PersonId,
            auditRecord,
            eventConfidence,
            Math.Clamp(auditRecord.Normalization.NormalizedPayload.Facts.Count / 4f, 0.25f, 1.0f),
            eventFreshness,
            stability,
            eventDecayPolicy,
            contradictionMarkersJson,
            BuildMetadataJson(bootstrapResult, Stage7DurableObjectFamilies.Event, eventClosureState, eventBoundaryConfidence),
            now,
            ct);
        var timelineMetadata = await UpsertMetadataAsync(
            db,
            scopeKey,
            Stage7DurableObjectFamilies.TimelineEpisode,
            BuildTimelineEpisodeObjectKey(trackedPerson.PersonId),
            trackedPerson.PersonId,
            operatorPerson?.PersonId,
            auditRecord,
            timelineConfidence,
            Math.Clamp(
                (auditRecord.Normalization.NormalizedPayload.Inferences.Count + bootstrapResult.SliceOutputs.Count) / 4f,
                0.25f,
                1.0f),
            timelineFreshness,
            stability,
            timelineDecayPolicy,
            contradictionMarkersJson,
            BuildMetadataJson(bootstrapResult, Stage7DurableObjectFamilies.TimelineEpisode, episodeClosureState, episodeBoundaryConfidence),
            now,
            ct);
        var storyArcMetadata = await UpsertMetadataAsync(
            db,
            scopeKey,
            Stage7DurableObjectFamilies.StoryArc,
            BuildStoryArcObjectKey(operatorPerson?.PersonId, trackedPerson.PersonId),
            trackedPerson.PersonId,
            operatorPerson?.PersonId,
            auditRecord,
            storyArcConfidence,
            Math.Clamp(
                (auditRecord.Normalization.NormalizedPayload.Inferences.Count
                + auditRecord.Normalization.NormalizedPayload.Hypotheses.Count
                + bootstrapResult.SliceOutputs.Count) / 5f,
                0.25f,
                1.0f),
            storyArcFreshness,
            stability,
            storyArcDecayPolicy,
            contradictionMarkersJson,
            BuildMetadataJson(bootstrapResult, Stage7DurableObjectFamilies.StoryArc, storyArcClosureState, storyArcBoundaryConfidence),
            now,
            ct);
        await db.SaveChangesAsync(ct);

        var eventRow = await UpsertEventAsync(
            db,
            bootstrapResult,
            auditRecord,
            eventMetadata.Id,
            eventClosureState,
            eventBoundaryConfidence,
            eventConfidence,
            now,
            ct);
        var timelineRow = await UpsertTimelineEpisodeAsync(
            db,
            bootstrapResult,
            auditRecord,
            timelineMetadata.Id,
            episodeClosureState,
            episodeBoundaryConfidence,
            now,
            ct);
        var storyArcRow = await UpsertStoryArcAsync(
            db,
            bootstrapResult,
            auditRecord,
            storyArcMetadata.Id,
            storyArcClosureState,
            storyArcBoundaryConfidence,
            now,
            ct);
        await db.SaveChangesAsync(ct);
        var eventRevision = await UpsertEventRevisionAsync(
            db,
            eventRow,
            auditRecord,
            eventConfidence,
            eventFreshness,
            stability,
            eventBoundaryConfidence,
            eventClosureState,
            contradictionMarkersJson,
            eventRow.SummaryJson,
            eventRow.PayloadJson,
            now,
            ct);
        var timelineRevision = await UpsertTimelineEpisodeRevisionAsync(
            db,
            timelineRow,
            auditRecord,
            timelineConfidence,
            timelineFreshness,
            stability,
            episodeBoundaryConfidence,
            episodeClosureState,
            contradictionMarkersJson,
            timelineRow.SummaryJson,
            timelineRow.PayloadJson,
            now,
            ct);
        var storyArcRevision = await UpsertStoryArcRevisionAsync(
            db,
            storyArcRow,
            auditRecord,
            storyArcConfidence,
            storyArcFreshness,
            stability,
            storyArcBoundaryConfidence,
            storyArcClosureState,
            contradictionMarkersJson,
            storyArcRow.SummaryJson,
            storyArcRow.PayloadJson,
            now,
            ct);
        eventRow.CurrentRevisionNumber = eventRevision.RevisionNumber;
        eventRow.CurrentRevisionHash = eventRevision.RevisionHash;
        eventRow.SummaryJson = eventRevision.SummaryJson;
        eventRow.PayloadJson = eventRevision.PayloadJson;
        eventRow.UpdatedAt = now;
        timelineRow.CurrentRevisionNumber = timelineRevision.RevisionNumber;
        timelineRow.CurrentRevisionHash = timelineRevision.RevisionHash;
        timelineRow.SummaryJson = timelineRevision.SummaryJson;
        timelineRow.PayloadJson = timelineRevision.PayloadJson;
        timelineRow.UpdatedAt = now;
        storyArcRow.CurrentRevisionNumber = storyArcRevision.RevisionNumber;
        storyArcRow.CurrentRevisionHash = storyArcRevision.RevisionHash;
        storyArcRow.SummaryJson = storyArcRevision.SummaryJson;
        storyArcRow.PayloadJson = storyArcRevision.PayloadJson;
        storyArcRow.UpdatedAt = now;

        await SyncEvidenceLinksAsync(db, eventMetadata.Id, scopeKey, evidenceItemIds, EventEvidenceLinkRole, now, ct);
        await SyncEvidenceLinksAsync(db, timelineMetadata.Id, scopeKey, evidenceItemIds, TimelineEpisodeEvidenceLinkRole, now, ct);
        await SyncEvidenceLinksAsync(db, storyArcMetadata.Id, scopeKey, evidenceItemIds, StoryArcEvidenceLinkRole, now, ct);

        await db.SaveChangesAsync(ct);
        await UpsertTimelineTemporalStateAsync(auditRecord, bootstrapResult, episodeClosureState, evidenceItemIds, now, ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Stage7 durable timeline persisted: scope_key={ScopeKey}, tracked_person_id={TrackedPersonId}, event_id={EventId}, episode_id={EpisodeId}, story_arc_id={StoryArcId}",
            scopeKey,
            trackedPerson.PersonId,
            eventRow.Id,
            timelineRow.Id,
            storyArcRow.Id);

        return new Stage7TimelineFormationResult
        {
            AuditRecord = auditRecord,
            Formed = true,
            TrackedPerson = trackedPerson,
            OperatorPerson = operatorPerson,
            Event = MapEvent(eventRow),
            TimelineEpisode = MapTimelineEpisode(timelineRow),
            StoryArc = MapStoryArc(storyArcRow),
            CurrentEventRevision = MapEventRevision(eventRevision),
            CurrentTimelineEpisodeRevision = MapTimelineEpisodeRevision(timelineRevision),
            CurrentStoryArcRevision = MapStoryArcRevision(storyArcRevision),
            EvidenceItemIds = [.. evidenceItemIds.OrderBy(x => x)]
        };
    }

    private async Task UpsertTimelineTemporalStateAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string episodeClosureState,
        IReadOnlyCollection<Guid> evidenceItemIds,
        DateTime validFromUtc,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson;
        if (trackedPerson == null || trackedPerson.PersonId == Guid.Empty || string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey))
        {
            return;
        }

        var scopeKey = bootstrapResult.ScopeKey;
        var subjectRef = $"person:{trackedPerson.PersonId:D}:timeline_episode";
        var value = $"bootstrap_episode:{episodeClosureState}";
        var evidenceRefs = evidenceItemIds.Select(x => $"evidence:{x:D}").ToArray();
        var openState = await _temporalPersonStateRepository.GetOpenStateAsync(
            scopeKey,
            subjectRef,
            Stage7TimelineTemporalFactTypes.TimelinePrimaryActivity,
            ct);
        if (openState != null && string.Equals(openState.Value, value, StringComparison.Ordinal))
        {
            return;
        }

        var inserted = await _temporalPersonStateRepository.InsertAsync(
            new TemporalPersonStateWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPerson.PersonId,
                SubjectRef = subjectRef,
                FactType = Stage7TimelineTemporalFactTypes.TimelinePrimaryActivity,
                FactCategory = TemporalPersonStateFactCategories.EventConditioned,
                Value = value,
                ValidFromUtc = validFromUtc,
                Confidence = null,
                EvidenceRefs = evidenceRefs,
                StateStatus = TemporalPersonStateStatuses.Open,
                SupersedesStateId = openState?.Id,
                TriggerKind = TemporalTriggerKind,
                TriggerRef = $"{TemporalTriggerKind}:{auditRecord.ModelPassRunId:D}:{Stage7TimelineTemporalFactTypes.TimelinePrimaryActivity}",
                TriggerModelPassRunId = auditRecord.ModelPassRunId
            },
            ct);

        if (openState == null)
        {
            return;
        }

        _ = await _temporalPersonStateRepository.UpdateSupersessionAsync(
            new TemporalPersonStateSupersessionUpdateRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPerson.PersonId,
                PreviousStateId = openState.Id,
                SupersededByStateId = inserted.Id,
                SupersededAtUtc = validFromUtc,
                NextStatus = TemporalPersonStateStatuses.Superseded
            },
            ct);
    }

    private static async Task<DbDurableObjectMetadata> UpsertMetadataAsync(
        TgAssistantDbContext db,
        string scopeKey,
        string objectFamily,
        string objectKey,
        Guid ownerPersonId,
        Guid? relatedPersonId,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float coverage,
        float freshness,
        float stability,
        DurableDecayPolicySnapshot decayPolicy,
        string contradictionMarkersJson,
        string metadataJson,
        DateTime now,
        CancellationToken ct)
    {
        var row = await db.DurableObjectMetadata.FirstOrDefaultAsync(
            x => x.ObjectFamily == objectFamily && x.ObjectKey == objectKey,
            ct);
        if (row == null)
        {
            row = new DbDurableObjectMetadata
            {
                Id = Guid.NewGuid(),
                ObjectFamily = objectFamily,
                ObjectKey = objectKey,
                CreatedAt = now,
                CreatedByModelPassRunId = auditRecord.ModelPassRunId
            };
            db.DurableObjectMetadata.Add(row);
        }

        row.ScopeKey = scopeKey;
        row.Status = ActiveStatus;
        row.TruthLayer = ModelNormalizationTruthLayers.DerivedButDurable;
        row.PromotionState = PendingPromotionState;
        row.OwnerPersonId = ownerPersonId;
        row.RelatedPersonId = relatedPersonId;
        row.LastNormalizationRunId = auditRecord.NormalizationRunId;
        row.Confidence = confidence;
        row.Coverage = coverage;
        row.Freshness = freshness;
        row.Stability = stability;
        row.DecayClass = decayPolicy.DecayClass;
        row.DecayPolicyJson = JsonSerializer.Serialize(decayPolicy);
        row.ContradictionMarkersJson = contradictionMarkersJson;
        row.MetadataJson = metadataJson;
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurableEvent> UpsertEventAsync(
        TgAssistantDbContext db,
        Stage6BootstrapGraphResult bootstrapResult,
        ModelPassAuditRecord auditRecord,
        Guid durableObjectMetadataId,
        string closureState,
        float boundaryConfidence,
        float eventConfidence,
        DateTime now,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var row = await db.DurableEvents.FirstOrDefaultAsync(
            x => x.ScopeKey == bootstrapResult.ScopeKey
                && x.PersonId == trackedPerson.PersonId
                && x.EventType == Stage7EventTypes.BootstrapAnchorEvent,
            ct);
        if (row == null)
        {
            row = new DbDurableEvent
            {
                Id = Guid.NewGuid(),
                ScopeKey = bootstrapResult.ScopeKey,
                PersonId = trackedPerson.PersonId,
                EventType = Stage7EventTypes.BootstrapAnchorEvent,
                CreatedAt = now
            };
            db.DurableEvents.Add(row);
        }

        row.RelatedPersonId = operatorPerson?.PersonId;
        row.DurableObjectMetadataId = durableObjectMetadataId;
        row.LastModelPassRunId = auditRecord.ModelPassRunId;
        row.Status = ActiveStatus;
        row.BoundaryConfidence = boundaryConfidence;
        row.EventConfidence = eventConfidence;
        row.ClosureState = closureState;
        row.OccurredFromUtc = bootstrapResult.LatestEvidenceAtUtc?.AddHours(-6);
        row.OccurredToUtc = bootstrapResult.LatestEvidenceAtUtc;
        row.SummaryJson = BuildEventSummaryJson(auditRecord, bootstrapResult, closureState, boundaryConfidence, eventConfidence);
        row.PayloadJson = BuildEventPayloadJson(auditRecord, bootstrapResult, closureState, boundaryConfidence, eventConfidence);
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurableTimelineEpisode> UpsertTimelineEpisodeAsync(
        TgAssistantDbContext db,
        Stage6BootstrapGraphResult bootstrapResult,
        ModelPassAuditRecord auditRecord,
        Guid durableObjectMetadataId,
        string closureState,
        float boundaryConfidence,
        DateTime now,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var row = await db.DurableTimelineEpisodes.FirstOrDefaultAsync(
            x => x.ScopeKey == bootstrapResult.ScopeKey
                && x.PersonId == trackedPerson.PersonId
                && x.EpisodeType == Stage7TimelineEpisodeTypes.BootstrapEpisode,
            ct);
        if (row == null)
        {
            row = new DbDurableTimelineEpisode
            {
                Id = Guid.NewGuid(),
                ScopeKey = bootstrapResult.ScopeKey,
                PersonId = trackedPerson.PersonId,
                EpisodeType = Stage7TimelineEpisodeTypes.BootstrapEpisode,
                CreatedAt = now
            };
            db.DurableTimelineEpisodes.Add(row);
        }

        row.RelatedPersonId = operatorPerson?.PersonId;
        row.DurableObjectMetadataId = durableObjectMetadataId;
        row.LastModelPassRunId = auditRecord.ModelPassRunId;
        row.Status = ActiveStatus;
        row.BoundaryConfidence = boundaryConfidence;
        row.ClosureState = closureState;
        row.StartedAtUtc = bootstrapResult.LatestEvidenceAtUtc?.AddDays(-2);
        row.EndedAtUtc = bootstrapResult.LatestEvidenceAtUtc;
        row.SummaryJson = BuildTimelineEpisodeSummaryJson(auditRecord, bootstrapResult, closureState, boundaryConfidence);
        row.PayloadJson = BuildTimelineEpisodePayloadJson(auditRecord, bootstrapResult, closureState, boundaryConfidence);
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurableStoryArc> UpsertStoryArcAsync(
        TgAssistantDbContext db,
        Stage6BootstrapGraphResult bootstrapResult,
        ModelPassAuditRecord auditRecord,
        Guid durableObjectMetadataId,
        string closureState,
        float boundaryConfidence,
        DateTime now,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var row = await db.DurableStoryArcs.FirstOrDefaultAsync(
            x => x.ScopeKey == bootstrapResult.ScopeKey
                && x.PersonId == trackedPerson.PersonId
                && x.ArcType == Stage7StoryArcTypes.OperatorTrackedArc,
            ct);
        if (row == null)
        {
            row = new DbDurableStoryArc
            {
                Id = Guid.NewGuid(),
                ScopeKey = bootstrapResult.ScopeKey,
                PersonId = trackedPerson.PersonId,
                ArcType = Stage7StoryArcTypes.OperatorTrackedArc,
                CreatedAt = now
            };
            db.DurableStoryArcs.Add(row);
        }

        row.RelatedPersonId = operatorPerson?.PersonId;
        row.DurableObjectMetadataId = durableObjectMetadataId;
        row.LastModelPassRunId = auditRecord.ModelPassRunId;
        row.Status = ActiveStatus;
        row.BoundaryConfidence = boundaryConfidence;
        row.ClosureState = closureState;
        row.OpenedAtUtc = bootstrapResult.LatestEvidenceAtUtc?.AddDays(-7);
        row.ClosedAtUtc = string.Equals(closureState, Stage7ClosureStates.Closed, StringComparison.Ordinal)
            ? bootstrapResult.LatestEvidenceAtUtc
            : null;
        row.SummaryJson = BuildStoryArcSummaryJson(auditRecord, bootstrapResult, closureState, boundaryConfidence);
        row.PayloadJson = BuildStoryArcPayloadJson(auditRecord, bootstrapResult, closureState, boundaryConfidence);
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurableEventRevision> UpsertEventRevisionAsync(
        TgAssistantDbContext db,
        DbDurableEvent eventRow,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float freshness,
        float stability,
        float boundaryConfidence,
        string closureState,
        string contradictionMarkersJson,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var revisionHash = ComputeTimelineRevisionHash(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            confidence,
            freshness,
            stability,
            boundaryConfidence,
            closureState,
            eventRow.EventConfidence);
        var existing = await db.DurableEventRevisions.FirstOrDefaultAsync(
            x => x.DurableEventId == eventRow.Id && x.RevisionHash == revisionHash,
            ct);
        if (existing != null)
        {
            existing.ModelPassRunId = auditRecord.ModelPassRunId;
            return existing;
        }

        var nextRevisionNumber = await db.DurableEventRevisions
            .Where(x => x.DurableEventId == eventRow.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(ct) ?? 0;

        var row = new DbDurableEventRevision
        {
            Id = Guid.NewGuid(),
            DurableEventId = eventRow.Id,
            RevisionNumber = nextRevisionNumber + 1,
            RevisionHash = revisionHash,
            ModelPassRunId = auditRecord.ModelPassRunId,
            Confidence = confidence,
            Freshness = freshness,
            Stability = stability,
            BoundaryConfidence = boundaryConfidence,
            EventConfidence = eventRow.EventConfidence,
            ClosureState = closureState,
            ContradictionMarkersJson = contradictionMarkersJson,
            SummaryJson = summaryJson,
            PayloadJson = payloadJson,
            CreatedAt = now
        };
        db.DurableEventRevisions.Add(row);
        return row;
    }

    private static async Task<DbDurableTimelineEpisodeRevision> UpsertTimelineEpisodeRevisionAsync(
        TgAssistantDbContext db,
        DbDurableTimelineEpisode episodeRow,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float freshness,
        float stability,
        float boundaryConfidence,
        string closureState,
        string contradictionMarkersJson,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var revisionHash = ComputeTimelineRevisionHash(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            confidence,
            freshness,
            stability,
            boundaryConfidence,
            closureState);
        var existing = await db.DurableTimelineEpisodeRevisions.FirstOrDefaultAsync(
            x => x.DurableTimelineEpisodeId == episodeRow.Id && x.RevisionHash == revisionHash,
            ct);
        if (existing != null)
        {
            existing.ModelPassRunId = auditRecord.ModelPassRunId;
            return existing;
        }

        var nextRevisionNumber = await db.DurableTimelineEpisodeRevisions
            .Where(x => x.DurableTimelineEpisodeId == episodeRow.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(ct) ?? 0;

        var row = new DbDurableTimelineEpisodeRevision
        {
            Id = Guid.NewGuid(),
            DurableTimelineEpisodeId = episodeRow.Id,
            RevisionNumber = nextRevisionNumber + 1,
            RevisionHash = revisionHash,
            ModelPassRunId = auditRecord.ModelPassRunId,
            Confidence = confidence,
            Freshness = freshness,
            Stability = stability,
            BoundaryConfidence = boundaryConfidence,
            ClosureState = closureState,
            ContradictionMarkersJson = contradictionMarkersJson,
            SummaryJson = summaryJson,
            PayloadJson = payloadJson,
            CreatedAt = now
        };
        db.DurableTimelineEpisodeRevisions.Add(row);
        return row;
    }

    private static async Task<DbDurableStoryArcRevision> UpsertStoryArcRevisionAsync(
        TgAssistantDbContext db,
        DbDurableStoryArc storyArcRow,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float freshness,
        float stability,
        float boundaryConfidence,
        string closureState,
        string contradictionMarkersJson,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var revisionHash = ComputeTimelineRevisionHash(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            confidence,
            freshness,
            stability,
            boundaryConfidence,
            closureState);
        var existing = await db.DurableStoryArcRevisions.FirstOrDefaultAsync(
            x => x.DurableStoryArcId == storyArcRow.Id && x.RevisionHash == revisionHash,
            ct);
        if (existing != null)
        {
            existing.ModelPassRunId = auditRecord.ModelPassRunId;
            return existing;
        }

        var nextRevisionNumber = await db.DurableStoryArcRevisions
            .Where(x => x.DurableStoryArcId == storyArcRow.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(ct) ?? 0;

        var row = new DbDurableStoryArcRevision
        {
            Id = Guid.NewGuid(),
            DurableStoryArcId = storyArcRow.Id,
            RevisionNumber = nextRevisionNumber + 1,
            RevisionHash = revisionHash,
            ModelPassRunId = auditRecord.ModelPassRunId,
            Confidence = confidence,
            Freshness = freshness,
            Stability = stability,
            BoundaryConfidence = boundaryConfidence,
            ClosureState = closureState,
            ContradictionMarkersJson = contradictionMarkersJson,
            SummaryJson = summaryJson,
            PayloadJson = payloadJson,
            CreatedAt = now
        };
        db.DurableStoryArcRevisions.Add(row);
        return row;
    }

    private static async Task SyncEvidenceLinksAsync(
        TgAssistantDbContext db,
        Guid durableMetadataId,
        string scopeKey,
        IReadOnlyCollection<Guid> evidenceItemIds,
        string linkRole,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await db.DurableObjectEvidenceLinks
            .Where(x => x.DurableObjectMetadataId == durableMetadataId && x.LinkRole == linkRole)
            .ToListAsync(ct);
        var expected = evidenceItemIds.ToHashSet();

        foreach (var row in existing.Where(x => !expected.Contains(x.EvidenceItemId)))
        {
            db.DurableObjectEvidenceLinks.Remove(row);
        }

        var existingIds = existing.Select(x => x.EvidenceItemId).ToHashSet();
        foreach (var evidenceItemId in expected.Where(x => !existingIds.Contains(x)))
        {
            db.DurableObjectEvidenceLinks.Add(new DbDurableObjectEvidenceLink
            {
                DurableObjectMetadataId = durableMetadataId,
                ScopeKey = scopeKey,
                EvidenceItemId = evidenceItemId,
                LinkRole = linkRole,
                CreatedAt = now
            });
        }
    }

    private static HashSet<Guid> CollectEvidenceItemIds(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult)
    {
        var evidenceIds = new HashSet<Guid>();

        foreach (var sourceRef in auditRecord.Envelope.SourceRefs)
        {
            if (sourceRef.EvidenceItemId != null)
            {
                evidenceIds.Add(sourceRef.EvidenceItemId.Value);
            }
        }

        AddEvidenceRefs(evidenceIds, auditRecord.Envelope.TruthSummary.CanonicalRefs);
        AddEvidenceRefs(evidenceIds, auditRecord.Normalization.NormalizedPayload.Facts.SelectMany(x => x.EvidenceRefs));
        AddEvidenceRefs(evidenceIds, auditRecord.Normalization.NormalizedPayload.Inferences.SelectMany(x => x.EvidenceRefs));
        AddEvidenceRefs(evidenceIds, auditRecord.Normalization.NormalizedPayload.Hypotheses.SelectMany(x => x.EvidenceRefs));
        AddEvidenceRefs(evidenceIds, auditRecord.Normalization.NormalizedPayload.Conflicts.SelectMany(x => x.EvidenceRefs));

        if (evidenceIds.Count == 0 && bootstrapResult.AuditRecord.Envelope.EvidenceItemId != null)
        {
            evidenceIds.Add(bootstrapResult.AuditRecord.Envelope.EvidenceItemId.Value);
        }

        return evidenceIds;
    }

    private static void AddEvidenceRefs(HashSet<Guid> evidenceIds, IEnumerable<string> evidenceRefs)
    {
        foreach (var evidenceRef in evidenceRefs)
        {
            if (!evidenceRef.StartsWith("evidence:", StringComparison.Ordinal)
                || !Guid.TryParse(evidenceRef["evidence:".Length..], out var evidenceId))
            {
                continue;
            }

            evidenceIds.Add(evidenceId);
        }
    }

    private static string BuildMetadataJson(
        Stage6BootstrapGraphResult bootstrapResult,
        string family,
        string closureState,
        float boundaryConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            family,
            closure_state = closureState,
            boundary_confidence = boundaryConfidence,
            ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count,
            slice_count = bootstrapResult.SliceOutputs.Count,
            evidence_count = bootstrapResult.EvidenceCount,
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O")
        });
    }

    private static string BuildEventSummaryJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string closureState,
        float boundaryConfidence,
        float eventConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            event_type = Stage7EventTypes.BootstrapAnchorEvent,
            tracked_person = bootstrapResult.TrackedPerson?.DisplayName,
            operator_root = bootstrapResult.OperatorPerson?.DisplayName,
            fact_count = auditRecord.Normalization.NormalizedPayload.Facts.Count,
            closure_state = closureState,
            boundary_confidence = boundaryConfidence,
            event_confidence = eventConfidence
        });
    }

    private static string BuildEventPayloadJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string closureState,
        float boundaryConfidence,
        float eventConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            event_type = Stage7EventTypes.BootstrapAnchorEvent,
            tracked_person = bootstrapResult.TrackedPerson,
            operator_root = bootstrapResult.OperatorPerson,
            occurred_from_utc = bootstrapResult.LatestEvidenceAtUtc?.AddHours(-6).ToUniversalTime().ToString("O"),
            occurred_to_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O"),
            closure_state = closureState,
            boundary_confidence = boundaryConfidence,
            event_confidence = eventConfidence,
            facts = auditRecord.Normalization.NormalizedPayload.Facts.Select(x => new
            {
                category = x.Category,
                key = x.Key,
                value = x.Value,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            })
        });
    }

    private static string BuildTimelineEpisodeSummaryJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string closureState,
        float boundaryConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            episode_type = Stage7TimelineEpisodeTypes.BootstrapEpisode,
            tracked_person = bootstrapResult.TrackedPerson?.DisplayName,
            inference_count = auditRecord.Normalization.NormalizedPayload.Inferences.Count,
            slice_count = bootstrapResult.SliceOutputs.Count,
            closure_state = closureState,
            boundary_confidence = boundaryConfidence
        });
    }

    private static string BuildTimelineEpisodePayloadJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string closureState,
        float boundaryConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            episode_type = Stage7TimelineEpisodeTypes.BootstrapEpisode,
            tracked_person = bootstrapResult.TrackedPerson,
            operator_root = bootstrapResult.OperatorPerson,
            started_at_utc = bootstrapResult.LatestEvidenceAtUtc?.AddDays(-2).ToUniversalTime().ToString("O"),
            ended_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O"),
            closure_state = closureState,
            boundary_confidence = boundaryConfidence,
            inferences = auditRecord.Normalization.NormalizedPayload.Inferences.Select(x => new
            {
                inference_type = x.InferenceType,
                summary = x.Summary,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            }),
            bootstrap_slices = bootstrapResult.SliceOutputs.Select(x => new
            {
                output_key = x.OutputKey,
                output_type = x.OutputType
            })
        });
    }

    private static string BuildStoryArcSummaryJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string closureState,
        float boundaryConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            arc_type = Stage7StoryArcTypes.OperatorTrackedArc,
            tracked_person = bootstrapResult.TrackedPerson?.DisplayName,
            operator_root = bootstrapResult.OperatorPerson?.DisplayName,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count,
            hypothesis_count = auditRecord.Normalization.NormalizedPayload.Hypotheses.Count,
            closure_state = closureState,
            boundary_confidence = boundaryConfidence
        });
    }

    private static string BuildStoryArcPayloadJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        string closureState,
        float boundaryConfidence)
    {
        return JsonSerializer.Serialize(new
        {
            arc_type = Stage7StoryArcTypes.OperatorTrackedArc,
            tracked_person = bootstrapResult.TrackedPerson,
            operator_root = bootstrapResult.OperatorPerson,
            opened_at_utc = bootstrapResult.LatestEvidenceAtUtc?.AddDays(-7).ToUniversalTime().ToString("O"),
            closed_at_utc = string.Equals(closureState, Stage7ClosureStates.Closed, StringComparison.Ordinal)
                ? bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O")
                : null,
            closure_state = closureState,
            boundary_confidence = boundaryConfidence,
            hypotheses = auditRecord.Normalization.NormalizedPayload.Hypotheses.Select(x => new
            {
                hypothesis_type = x.HypothesisType,
                statement = x.Statement,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            }),
            conflicts = auditRecord.Normalization.NormalizedPayload.Conflicts.Select(x => new
            {
                conflict_type = x.ConflictType,
                summary = x.Summary,
                related_object_ref = x.RelatedObjectRef,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            })
        });
    }

    private static string BuildContradictionMarkersJson(Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(
            bootstrapResult.ContradictionOutputs.Select(x => new
            {
                output_key = x.OutputKey,
                output_type = x.OutputType,
                relationship_edge_anchor_id = x.RelationshipEdgeAnchorId
            }));
    }

    private static float AverageConfidence(IEnumerable<float> confidences, float fallback)
    {
        var values = confidences
            .Where(x => !float.IsNaN(x) && !float.IsInfinity(x))
            .ToArray();
        return values.Length == 0
            ? fallback
            : Math.Clamp(values.Average(), 0.30f, 0.98f);
    }

    private static float ComputeStability(int contradictionCount)
        => Math.Clamp(1.0f - (contradictionCount * 0.15f), 0.35f, 1.0f);

    private static float ComputeBoundaryConfidence(Stage6BootstrapGraphResult bootstrapResult, float baseline)
    {
        var confidence = baseline;
        confidence += Math.Min(0.08f, bootstrapResult.SliceOutputs.Count * 0.03f);
        confidence -= bootstrapResult.AmbiguityOutputs.Count * 0.05f;
        confidence -= bootstrapResult.ContradictionOutputs.Count * 0.08f;
        return Math.Clamp(confidence, 0.25f, 0.95f);
    }

    private static string ResolveEventClosureState(Stage6BootstrapGraphResult bootstrapResult)
    {
        if (bootstrapResult.ContradictionOutputs.Count == 0 && bootstrapResult.AmbiguityOutputs.Count == 0)
        {
            return Stage7ClosureStates.Closed;
        }

        if (bootstrapResult.ContradictionOutputs.Count == 0)
        {
            return Stage7ClosureStates.SemiClosed;
        }

        return Stage7ClosureStates.Open;
    }

    private static string ResolveTimelineEpisodeClosureState(Stage6BootstrapGraphResult bootstrapResult)
    {
        if (bootstrapResult.SliceOutputs.Count == 0)
        {
            return Stage7ClosureStates.Open;
        }

        if (bootstrapResult.ContradictionOutputs.Count == 0)
        {
            return Stage7ClosureStates.Closed;
        }

        return Stage7ClosureStates.SemiClosed;
    }

    private static string ResolveStoryArcClosureState(Stage6BootstrapGraphResult bootstrapResult)
    {
        if (bootstrapResult.ContradictionOutputs.Count > 0)
        {
            return Stage7ClosureStates.Open;
        }

        if (bootstrapResult.AmbiguityOutputs.Count > 0)
        {
            return Stage7ClosureStates.SemiClosed;
        }

        return Stage7ClosureStates.Closed;
    }

    private static string BuildEventObjectKey(Guid trackedPersonId)
        => $"person:{trackedPersonId:D}:event:{Stage7EventTypes.BootstrapAnchorEvent}";

    private static string BuildTimelineEpisodeObjectKey(Guid trackedPersonId)
        => $"person:{trackedPersonId:D}:timeline_episode:{Stage7TimelineEpisodeTypes.BootstrapEpisode}";

    private static string BuildStoryArcObjectKey(Guid? operatorPersonId, Guid trackedPersonId)
        => operatorPersonId == null
            ? $"person:{trackedPersonId:D}:story_arc:{Stage7StoryArcTypes.OperatorTrackedArc}"
            : $"pair:{operatorPersonId:D}:{trackedPersonId:D}:story_arc:{Stage7StoryArcTypes.OperatorTrackedArc}";

    private static string ComputeTimelineRevisionHash(
        string summaryJson,
        string payloadJson,
        string contradictionMarkersJson,
        float confidence,
        float freshness,
        float stability,
        float boundaryConfidence,
        string closureState,
        float? eventConfidence = null)
    {
        return Stage7RevisionHashHelper.Compute(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            Stage7RevisionHashHelper.FormatFloat(confidence),
            Stage7RevisionHashHelper.FormatFloat(freshness),
            Stage7RevisionHashHelper.FormatFloat(stability),
            Stage7RevisionHashHelper.FormatFloat(boundaryConfidence),
            eventConfidence == null ? string.Empty : Stage7RevisionHashHelper.FormatFloat(eventConfidence.Value),
            closureState);
    }

    private static Stage7DurableEvent MapEvent(DbDurableEvent row)
    {
        return new Stage7DurableEvent
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            RelatedPersonId = row.RelatedPersonId,
            DurableObjectMetadataId = row.DurableObjectMetadataId,
            LastModelPassRunId = row.LastModelPassRunId,
            EventType = row.EventType,
            Status = row.Status,
            CurrentRevisionNumber = row.CurrentRevisionNumber,
            CurrentRevisionHash = row.CurrentRevisionHash,
            BoundaryConfidence = row.BoundaryConfidence,
            EventConfidence = row.EventConfidence,
            ClosureState = row.ClosureState,
            OccurredFromUtc = row.OccurredFromUtc,
            OccurredToUtc = row.OccurredToUtc,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage7DurableTimelineEpisode MapTimelineEpisode(DbDurableTimelineEpisode row)
    {
        return new Stage7DurableTimelineEpisode
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            RelatedPersonId = row.RelatedPersonId,
            DurableObjectMetadataId = row.DurableObjectMetadataId,
            LastModelPassRunId = row.LastModelPassRunId,
            EpisodeType = row.EpisodeType,
            Status = row.Status,
            CurrentRevisionNumber = row.CurrentRevisionNumber,
            CurrentRevisionHash = row.CurrentRevisionHash,
            BoundaryConfidence = row.BoundaryConfidence,
            ClosureState = row.ClosureState,
            StartedAtUtc = row.StartedAtUtc,
            EndedAtUtc = row.EndedAtUtc,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage7DurableStoryArc MapStoryArc(DbDurableStoryArc row)
    {
        return new Stage7DurableStoryArc
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            RelatedPersonId = row.RelatedPersonId,
            DurableObjectMetadataId = row.DurableObjectMetadataId,
            LastModelPassRunId = row.LastModelPassRunId,
            ArcType = row.ArcType,
            Status = row.Status,
            CurrentRevisionNumber = row.CurrentRevisionNumber,
            CurrentRevisionHash = row.CurrentRevisionHash,
            BoundaryConfidence = row.BoundaryConfidence,
            ClosureState = row.ClosureState,
            OpenedAtUtc = row.OpenedAtUtc,
            ClosedAtUtc = row.ClosedAtUtc,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage7DurableEventRevision MapEventRevision(DbDurableEventRevision row)
    {
        return new Stage7DurableEventRevision
        {
            Id = row.Id,
            DurableEventId = row.DurableEventId,
            RevisionNumber = row.RevisionNumber,
            RevisionHash = row.RevisionHash,
            ModelPassRunId = row.ModelPassRunId,
            Confidence = row.Confidence,
            Freshness = row.Freshness,
            Stability = row.Stability,
            BoundaryConfidence = row.BoundaryConfidence,
            EventConfidence = row.EventConfidence,
            ClosureState = row.ClosureState,
            ContradictionMarkersJson = row.ContradictionMarkersJson,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson,
            CreatedAt = row.CreatedAt
        };
    }

    private static Stage7DurableTimelineEpisodeRevision MapTimelineEpisodeRevision(DbDurableTimelineEpisodeRevision row)
    {
        return new Stage7DurableTimelineEpisodeRevision
        {
            Id = row.Id,
            DurableTimelineEpisodeId = row.DurableTimelineEpisodeId,
            RevisionNumber = row.RevisionNumber,
            RevisionHash = row.RevisionHash,
            ModelPassRunId = row.ModelPassRunId,
            Confidence = row.Confidence,
            Freshness = row.Freshness,
            Stability = row.Stability,
            BoundaryConfidence = row.BoundaryConfidence,
            ClosureState = row.ClosureState,
            ContradictionMarkersJson = row.ContradictionMarkersJson,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson,
            CreatedAt = row.CreatedAt
        };
    }

    private static Stage7DurableStoryArcRevision MapStoryArcRevision(DbDurableStoryArcRevision row)
    {
        return new Stage7DurableStoryArcRevision
        {
            Id = row.Id,
            DurableStoryArcId = row.DurableStoryArcId,
            RevisionNumber = row.RevisionNumber,
            RevisionHash = row.RevisionHash,
            ModelPassRunId = row.ModelPassRunId,
            Confidence = row.Confidence,
            Freshness = row.Freshness,
            Stability = row.Stability,
            BoundaryConfidence = row.BoundaryConfidence,
            ClosureState = row.ClosureState,
            ContradictionMarkersJson = row.ContradictionMarkersJson,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson,
            CreatedAt = row.CreatedAt
        };
    }
}

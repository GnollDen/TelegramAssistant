using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage7PairDynamicsRepository : IStage7PairDynamicsRepository
{
    private const string ActiveStatus = "active";
    private const string PendingPromotionState = "pending";
    private const string EvidenceLinkRole = "pair_dynamics_supporting";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<Stage7PairDynamicsRepository> _logger;

    public Stage7PairDynamicsRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<Stage7PairDynamicsRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<Stage7PairDynamicsFormationResult> UpsertAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        ArgumentNullException.ThrowIfNull(bootstrapResult);

        if (!string.Equals(auditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            || bootstrapResult.TrackedPerson == null
            || bootstrapResult.OperatorPerson == null
            || string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey))
        {
            throw new InvalidOperationException("Stage7 durable pair-dynamics persistence requires a ready bootstrap result with tracked and operator context.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;
        var trackedPerson = bootstrapResult.TrackedPerson;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var scopeKey = bootstrapResult.ScopeKey;
        var evidenceItemIds = CollectEvidenceItemIds(auditRecord, bootstrapResult);
        var confidence = AverageConfidence(
            auditRecord.Normalization.NormalizedPayload.Inferences.Select(x => x.Confidence)
                .Concat(auditRecord.Normalization.NormalizedPayload.Hypotheses.Select(x => x.Confidence))
                .Concat(auditRecord.Normalization.NormalizedPayload.Conflicts.Select(x => x.Confidence)),
            0.62f);
        var coverage = Math.Clamp(
            (auditRecord.Normalization.NormalizedPayload.Facts.Count
            + auditRecord.Normalization.NormalizedPayload.Inferences.Count
            + auditRecord.Normalization.NormalizedPayload.Hypotheses.Count) / 6f,
            0.25f,
            1.0f);
        var stability = ComputeStability(bootstrapResult.ContradictionOutputs.Count);
        var decayPolicy = DurableDecayPolicyCatalog.Resolve(Stage7DurableObjectFamilies.PairDynamics);
        var freshness = DurableDecayPolicyCatalog.ComputeFreshness(Stage7DurableObjectFamilies.PairDynamics, bootstrapResult.LatestEvidenceAtUtc, now);
        var contradictionMarkersJson = BuildContradictionMarkersJson(bootstrapResult);
        var summaryJson = BuildSummaryJson(auditRecord, bootstrapResult);
        var payloadJson = BuildPayloadJson(auditRecord, bootstrapResult);

        var metadata = await UpsertMetadataAsync(
            db,
            scopeKey,
            trackedPerson.PersonId,
            operatorPerson.PersonId,
            auditRecord,
            confidence,
            coverage,
            freshness,
            stability,
            decayPolicy,
            contradictionMarkersJson,
            BuildMetadataJson(bootstrapResult),
            now,
            ct);
        await db.SaveChangesAsync(ct);

        var pairRow = await UpsertPairDynamicsRowAsync(
            db,
            auditRecord,
            bootstrapResult,
            metadata.Id,
            summaryJson,
            payloadJson,
            now,
            ct);
        await db.SaveChangesAsync(ct);

        var revision = await UpsertRevisionAsync(
            db,
            pairRow,
            auditRecord,
            confidence,
            freshness,
            stability,
            contradictionMarkersJson,
            summaryJson,
            payloadJson,
            now,
            ct);

        pairRow.CurrentRevisionNumber = revision.RevisionNumber;
        pairRow.CurrentRevisionHash = revision.RevisionHash;
        pairRow.SummaryJson = revision.SummaryJson;
        pairRow.PayloadJson = revision.PayloadJson;
        pairRow.UpdatedAt = now;

        await SyncEvidenceLinksAsync(db, metadata.Id, scopeKey, evidenceItemIds, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Stage7 durable pair dynamics persisted: scope_key={ScopeKey}, operator_person_id={OperatorPersonId}, tracked_person_id={TrackedPersonId}, revision_number={RevisionNumber}",
            scopeKey,
            operatorPerson.PersonId,
            trackedPerson.PersonId,
            revision.RevisionNumber);

        return new Stage7PairDynamicsFormationResult
        {
            AuditRecord = auditRecord,
            Formed = true,
            TrackedPerson = trackedPerson,
            OperatorPerson = operatorPerson,
            PairDynamics = MapPairDynamics(pairRow),
            CurrentRevision = MapRevision(revision),
            EvidenceItemIds = [.. evidenceItemIds.OrderBy(x => x)]
        };
    }

    private static async Task<DbDurableObjectMetadata> UpsertMetadataAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid trackedPersonId,
        Guid operatorPersonId,
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
        var objectKey = BuildPairObjectKey(operatorPersonId, trackedPersonId);
        var row = await db.DurableObjectMetadata.FirstOrDefaultAsync(
            x => x.ObjectFamily == Stage7DurableObjectFamilies.PairDynamics && x.ObjectKey == objectKey,
            ct);
        if (row == null)
        {
            row = new DbDurableObjectMetadata
            {
                Id = Guid.NewGuid(),
                ObjectFamily = Stage7DurableObjectFamilies.PairDynamics,
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
        row.OwnerPersonId = trackedPersonId;
        row.RelatedPersonId = operatorPersonId;
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

    private static async Task<DbDurablePairDynamics> UpsertPairDynamicsRowAsync(
        TgAssistantDbContext db,
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        Guid durableObjectMetadataId,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var operatorPerson = bootstrapResult.OperatorPerson!;
        var row = await db.DurablePairDynamics.FirstOrDefaultAsync(
            x => x.ScopeKey == bootstrapResult.ScopeKey
                && x.LeftPersonId == operatorPerson.PersonId
                && x.RightPersonId == trackedPerson.PersonId
                && x.PairDynamicsType == Stage7PairDynamicsTypes.OperatorTrackedPair,
            ct);
        if (row == null)
        {
            row = new DbDurablePairDynamics
            {
                Id = Guid.NewGuid(),
                ScopeKey = bootstrapResult.ScopeKey,
                LeftPersonId = operatorPerson.PersonId,
                RightPersonId = trackedPerson.PersonId,
                PairDynamicsType = Stage7PairDynamicsTypes.OperatorTrackedPair,
                CreatedAt = now
            };
            db.DurablePairDynamics.Add(row);
        }

        row.DurableObjectMetadataId = durableObjectMetadataId;
        row.LastModelPassRunId = auditRecord.ModelPassRunId;
        row.Status = ActiveStatus;
        row.SummaryJson = summaryJson;
        row.PayloadJson = payloadJson;
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurablePairDynamicsRevision> UpsertRevisionAsync(
        TgAssistantDbContext db,
        DbDurablePairDynamics pairRow,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float freshness,
        float stability,
        string contradictionMarkersJson,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var revisionHash = ComputeRevisionHash(summaryJson, payloadJson, contradictionMarkersJson, confidence, freshness, stability);
        var existing = await db.DurablePairDynamicsRevisions.FirstOrDefaultAsync(
            x => x.DurablePairDynamicsId == pairRow.Id && x.RevisionHash == revisionHash,
            ct);
        if (existing != null)
        {
            existing.ModelPassRunId = auditRecord.ModelPassRunId;
            return existing;
        }

        var nextRevisionNumber = await db.DurablePairDynamicsRevisions
            .Where(x => x.DurablePairDynamicsId == pairRow.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(ct) ?? 0;

        var row = new DbDurablePairDynamicsRevision
        {
            Id = Guid.NewGuid(),
            DurablePairDynamicsId = pairRow.Id,
            RevisionNumber = nextRevisionNumber + 1,
            RevisionHash = revisionHash,
            ModelPassRunId = auditRecord.ModelPassRunId,
            Confidence = confidence,
            Freshness = freshness,
            Stability = stability,
            ContradictionMarkersJson = contradictionMarkersJson,
            SummaryJson = summaryJson,
            PayloadJson = payloadJson,
            CreatedAt = now
        };
        db.DurablePairDynamicsRevisions.Add(row);
        return row;
    }

    private static async Task SyncEvidenceLinksAsync(
        TgAssistantDbContext db,
        Guid durableMetadataId,
        string scopeKey,
        IReadOnlyCollection<Guid> evidenceItemIds,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await db.DurableObjectEvidenceLinks
            .Where(x => x.DurableObjectMetadataId == durableMetadataId && x.LinkRole == EvidenceLinkRole)
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
                LinkRole = EvidenceLinkRole,
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

    private static string BuildMetadataJson(Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            family = Stage7DurableObjectFamilies.PairDynamics,
            pair_dynamics_type = Stage7PairDynamicsTypes.OperatorTrackedPair,
            linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
            candidate_identity_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal)),
            mention_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.Ordinal)),
            ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count,
            slice_count = bootstrapResult.SliceOutputs.Count,
            evidence_count = bootstrapResult.EvidenceCount,
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O")
        });
    }

    private static string BuildSummaryJson(ModelPassAuditRecord auditRecord, Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            pair_dynamics_type = Stage7PairDynamicsTypes.OperatorTrackedPair,
            operator_root = bootstrapResult.OperatorPerson?.DisplayName,
            tracked_person = bootstrapResult.TrackedPerson?.DisplayName,
            fact_count = auditRecord.Normalization.NormalizedPayload.Facts.Count,
            inference_count = auditRecord.Normalization.NormalizedPayload.Inferences.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count
        });
    }

    private static string BuildPayloadJson(ModelPassAuditRecord auditRecord, Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            pair_dynamics_type = Stage7PairDynamicsTypes.OperatorTrackedPair,
            left_person = bootstrapResult.OperatorPerson,
            right_person = bootstrapResult.TrackedPerson,
            dimensions = auditRecord.Normalization.NormalizedPayload.Facts.Select(x => new
            {
                category = x.Category,
                key = x.Key,
                value = x.Value,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            }),
            inferences = auditRecord.Normalization.NormalizedPayload.Inferences.Select(x => new
            {
                inference_type = x.InferenceType,
                summary = x.Summary,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            }),
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
            }),
            bootstrap_pressure = new
            {
                ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
                contradiction_count = bootstrapResult.ContradictionOutputs.Count,
                slice_count = bootstrapResult.SliceOutputs.Count
            }
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

    private static string BuildPairObjectKey(Guid operatorPersonId, Guid trackedPersonId)
        => $"pair:{operatorPersonId:D}:{trackedPersonId:D}:{Stage7PairDynamicsTypes.OperatorTrackedPair}";

    private static string ComputeRevisionHash(
        string summaryJson,
        string payloadJson,
        string contradictionMarkersJson,
        float confidence,
        float freshness,
        float stability)
    {
        return Stage7RevisionHashHelper.Compute(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            Stage7RevisionHashHelper.FormatFloat(confidence),
            Stage7RevisionHashHelper.FormatFloat(freshness),
            Stage7RevisionHashHelper.FormatFloat(stability));
    }

    private static Stage7DurablePairDynamics MapPairDynamics(DbDurablePairDynamics row)
    {
        return new Stage7DurablePairDynamics
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            LeftPersonId = row.LeftPersonId,
            RightPersonId = row.RightPersonId,
            DurableObjectMetadataId = row.DurableObjectMetadataId,
            LastModelPassRunId = row.LastModelPassRunId,
            PairDynamicsType = row.PairDynamicsType,
            Status = row.Status,
            CurrentRevisionNumber = row.CurrentRevisionNumber,
            CurrentRevisionHash = row.CurrentRevisionHash,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage7DurablePairDynamicsRevision MapRevision(DbDurablePairDynamicsRevision row)
    {
        return new Stage7DurablePairDynamicsRevision
        {
            Id = row.Id,
            DurablePairDynamicsId = row.DurablePairDynamicsId,
            RevisionNumber = row.RevisionNumber,
            RevisionHash = row.RevisionHash,
            ModelPassRunId = row.ModelPassRunId,
            Confidence = row.Confidence,
            Freshness = row.Freshness,
            Stability = row.Stability,
            ContradictionMarkersJson = row.ContradictionMarkersJson,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson,
            CreatedAt = row.CreatedAt
        };
    }
}

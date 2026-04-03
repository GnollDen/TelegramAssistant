using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage7DossierProfileRepository : IStage7DossierProfileRepository
{
    private const string ActiveStatus = "active";
    private const string PendingPromotionState = "pending";
    private const string DossierEvidenceLinkRole = "dossier_supporting";
    private const string ProfileEvidenceLinkRole = "profile_supporting";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<Stage7DossierProfileRepository> _logger;

    public Stage7DossierProfileRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<Stage7DossierProfileRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<Stage7DossierProfileFormationResult> UpsertAsync(
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
            throw new InvalidOperationException("Stage7 durable dossier/profile persistence requires a ready bootstrap result.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var trackedPerson = bootstrapResult.TrackedPerson;
        var scopeKey = bootstrapResult.ScopeKey;
        var evidenceItemIds = CollectEvidenceItemIds(auditRecord, bootstrapResult);
        var dossierConfidence = AverageConfidence(auditRecord.Normalization.NormalizedPayload.Facts.Select(x => x.Confidence), fallback: 0.70f);
        var profileConfidence = AverageConfidence(
            auditRecord.Normalization.NormalizedPayload.Inferences.Select(x => x.Confidence)
                .Concat(auditRecord.Normalization.NormalizedPayload.Hypotheses.Select(x => x.Confidence)),
            fallback: dossierConfidence);
        var dossierCoverage = Math.Clamp(auditRecord.Normalization.NormalizedPayload.Facts.Count / 5f, 0.25f, 1.0f);
        var profileCoverage = Math.Clamp(
            (auditRecord.Normalization.NormalizedPayload.Inferences.Count + auditRecord.Normalization.NormalizedPayload.Hypotheses.Count) / 3f,
            0.25f,
            1.0f);
        var freshness = ComputeFreshness(bootstrapResult.LatestEvidenceAtUtc);
        var stability = ComputeStability(bootstrapResult.ContradictionOutputs.Count);
        var contradictionMarkersJson = JsonSerializer.Serialize(
            bootstrapResult.ContradictionOutputs
                .Select(x => new
                {
                    output_key = x.OutputKey,
                    output_type = x.OutputType,
                    relationship_edge_anchor_id = x.RelationshipEdgeAnchorId
                }));

        var dossierMetadata = await UpsertMetadataAsync(
            db,
            scopeKey,
            Stage7DurableObjectFamilies.Dossier,
            BuildDossierObjectKey(trackedPerson.PersonId),
            trackedPerson.PersonId,
            auditRecord,
            dossierConfidence,
            dossierCoverage,
            freshness,
            stability,
            contradictionMarkersJson,
            BuildMetadataJson(bootstrapResult, "dossier"),
            now,
            ct);
        var profileMetadata = await UpsertMetadataAsync(
            db,
            scopeKey,
            Stage7DurableObjectFamilies.Profile,
            BuildProfileObjectKey(trackedPerson.PersonId),
            trackedPerson.PersonId,
            auditRecord,
            profileConfidence,
            profileCoverage,
            freshness,
            stability,
            contradictionMarkersJson,
            BuildMetadataJson(bootstrapResult, "profile"),
            now,
            ct);

        var dossierRow = await UpsertDossierAsync(db, dossierMetadata.Id, auditRecord, bootstrapResult, now, ct);
        var profileRow = await UpsertProfileAsync(db, profileMetadata.Id, auditRecord, bootstrapResult, now, ct);
        await SyncEvidenceLinksAsync(db, dossierMetadata.Id, scopeKey, evidenceItemIds, DossierEvidenceLinkRole, now, ct);
        await SyncEvidenceLinksAsync(db, profileMetadata.Id, scopeKey, evidenceItemIds, ProfileEvidenceLinkRole, now, ct);

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Stage7 durable dossier/profile persisted: scope_key={ScopeKey}, tracked_person_id={TrackedPersonId}, evidence_count={EvidenceCount}",
            scopeKey,
            trackedPerson.PersonId,
            evidenceItemIds.Count);

        return new Stage7DossierProfileFormationResult
        {
            AuditRecord = auditRecord,
            Formed = true,
            TrackedPerson = trackedPerson,
            Dossier = MapDossier(dossierRow),
            Profile = MapProfile(profileRow),
            EvidenceItemIds = [.. evidenceItemIds.OrderBy(x => x)]
        };
    }

    private static async Task<DbDurableObjectMetadata> UpsertMetadataAsync(
        TgAssistantDbContext db,
        string scopeKey,
        string objectFamily,
        string objectKey,
        Guid ownerPersonId,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float coverage,
        float freshness,
        float stability,
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
        row.RelatedPersonId = null;
        row.LastNormalizationRunId = auditRecord.NormalizationRunId;
        row.Confidence = confidence;
        row.Coverage = coverage;
        row.Freshness = freshness;
        row.Stability = stability;
        row.ContradictionMarkersJson = contradictionMarkersJson;
        row.MetadataJson = metadataJson;
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurableDossier> UpsertDossierAsync(
        TgAssistantDbContext db,
        Guid durableMetadataId,
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        DateTime now,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var row = await db.DurableDossiers.FirstOrDefaultAsync(
            x => x.ScopeKey == bootstrapResult.ScopeKey
                && x.PersonId == trackedPerson.PersonId
                && x.DossierType == Stage7DossierTypes.PersonDossier,
            ct);
        if (row == null)
        {
            row = new DbDurableDossier
            {
                Id = Guid.NewGuid(),
                ScopeKey = bootstrapResult.ScopeKey,
                PersonId = trackedPerson.PersonId,
                DossierType = Stage7DossierTypes.PersonDossier,
                CreatedAt = now
            };
            db.DurableDossiers.Add(row);
        }

        row.DurableObjectMetadataId = durableMetadataId;
        row.LastModelPassRunId = auditRecord.ModelPassRunId;
        row.Status = ActiveStatus;
        row.SummaryJson = BuildDossierSummaryJson(auditRecord, bootstrapResult);
        row.PayloadJson = BuildDossierPayloadJson(auditRecord, bootstrapResult);
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbDurableProfile> UpsertProfileAsync(
        TgAssistantDbContext db,
        Guid durableMetadataId,
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        DateTime now,
        CancellationToken ct)
    {
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var row = await db.DurableProfiles.FirstOrDefaultAsync(
            x => x.ScopeKey == bootstrapResult.ScopeKey
                && x.PersonId == trackedPerson.PersonId
                && x.ProfileScope == Stage7ProfileScopes.Global,
            ct);
        if (row == null)
        {
            row = new DbDurableProfile
            {
                Id = Guid.NewGuid(),
                ScopeKey = bootstrapResult.ScopeKey,
                PersonId = trackedPerson.PersonId,
                ProfileScope = Stage7ProfileScopes.Global,
                CreatedAt = now
            };
            db.DurableProfiles.Add(row);
        }

        row.DurableObjectMetadataId = durableMetadataId;
        row.LastModelPassRunId = auditRecord.ModelPassRunId;
        row.Status = ActiveStatus;
        row.SummaryJson = BuildProfileSummaryJson(auditRecord, bootstrapResult);
        row.PayloadJson = BuildProfilePayloadJson(auditRecord, bootstrapResult);
        row.UpdatedAt = now;
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

        foreach (var evidenceRef in auditRecord.Envelope.TruthSummary.CanonicalRefs)
        {
            if (!evidenceRef.StartsWith("evidence:", StringComparison.Ordinal)
                || !Guid.TryParse(evidenceRef["evidence:".Length..], out var evidenceId))
            {
                continue;
            }

            evidenceIds.Add(evidenceId);
        }

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

    private static string BuildMetadataJson(Stage6BootstrapGraphResult bootstrapResult, string family)
    {
        return JsonSerializer.Serialize(new
        {
            family,
            linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
            candidate_identity_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal)),
            mention_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.Ordinal)),
            ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count,
            evidence_count = bootstrapResult.EvidenceCount,
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O")
        });
    }

    private static string BuildDossierSummaryJson(ModelPassAuditRecord auditRecord, Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            tracked_person = bootstrapResult.TrackedPerson?.DisplayName,
            operator_root = bootstrapResult.OperatorPerson?.DisplayName,
            fact_count = auditRecord.Normalization.NormalizedPayload.Facts.Count,
            linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O")
        });
    }

    private static string BuildDossierPayloadJson(ModelPassAuditRecord auditRecord, Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            dossier_type = Stage7DossierTypes.PersonDossier,
            tracked_person = bootstrapResult.TrackedPerson,
            operator_root = bootstrapResult.OperatorPerson,
            fields = auditRecord.Normalization.NormalizedPayload.Facts.Select(x => new
            {
                category = x.Category,
                key = x.Key,
                value = x.Value,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            }),
            bootstrap_outputs = new
            {
                linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
                candidate_identity_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal)),
                mention_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.Ordinal))
            }
        });
    }

    private static string BuildProfileSummaryJson(ModelPassAuditRecord auditRecord, Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            profile_scope = Stage7ProfileScopes.Global,
            inference_count = auditRecord.Normalization.NormalizedPayload.Inferences.Count,
            hypothesis_count = auditRecord.Normalization.NormalizedPayload.Hypotheses.Count,
            ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count
        });
    }

    private static string BuildProfilePayloadJson(ModelPassAuditRecord auditRecord, Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            profile_scope = Stage7ProfileScopes.Global,
            tracked_person = bootstrapResult.TrackedPerson,
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
            bootstrap_pressure = new
            {
                ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
                contradiction_count = bootstrapResult.ContradictionOutputs.Count,
                slice_count = bootstrapResult.SliceOutputs.Count
            }
        });
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

    private static float ComputeFreshness(DateTime? latestEvidenceAtUtc)
    {
        if (latestEvidenceAtUtc == null)
        {
            return 0.25f;
        }

        var age = DateTime.UtcNow - latestEvidenceAtUtc.Value;
        if (age <= TimeSpan.FromDays(2))
        {
            return 1.0f;
        }

        if (age <= TimeSpan.FromDays(7))
        {
            return 0.85f;
        }

        if (age <= TimeSpan.FromDays(30))
        {
            return 0.65f;
        }

        return 0.40f;
    }

    private static float ComputeStability(int contradictionCount)
        => Math.Clamp(1.0f - (contradictionCount * 0.15f), 0.35f, 1.0f);

    private static string BuildDossierObjectKey(Guid personId)
        => $"person:{personId:D}:dossier:{Stage7DossierTypes.PersonDossier}";

    private static string BuildProfileObjectKey(Guid personId)
        => $"person:{personId:D}:profile:{Stage7ProfileScopes.Global}";

    private static Stage7DurableDossier MapDossier(DbDurableDossier row)
    {
        return new Stage7DurableDossier
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            DurableObjectMetadataId = row.DurableObjectMetadataId,
            LastModelPassRunId = row.LastModelPassRunId,
            DossierType = row.DossierType,
            Status = row.Status,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage7DurableProfile MapProfile(DbDurableProfile row)
    {
        return new Stage7DurableProfile
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            DurableObjectMetadataId = row.DurableObjectMetadataId,
            LastModelPassRunId = row.LastModelPassRunId,
            ProfileScope = row.ProfileScope,
            Status = row.Status,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }
}

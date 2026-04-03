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
        var dossierDecayPolicy = DurableDecayPolicyCatalog.Resolve(Stage7DurableObjectFamilies.Dossier);
        var profileDecayPolicy = DurableDecayPolicyCatalog.Resolve(Stage7DurableObjectFamilies.Profile);
        var contradictionMarkersJson = JsonSerializer.Serialize(
            bootstrapResult.ContradictionOutputs
                .Select(x => new
                {
                    output_key = x.OutputKey,
                    output_type = x.OutputType,
                    relationship_edge_anchor_id = x.RelationshipEdgeAnchorId
                }));
        var dossierFieldRegistry = await SyncDossierFieldRegistryAsync(db, auditRecord, now, ct);

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
            dossierDecayPolicy,
            contradictionMarkersJson,
            BuildDossierMetadataJson(bootstrapResult, dossierFieldRegistry),
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
            profileDecayPolicy,
            contradictionMarkersJson,
            BuildProfileMetadataJson(bootstrapResult),
            now,
            ct);

        var dossierRow = await UpsertDossierAsync(db, dossierMetadata.Id, auditRecord, bootstrapResult, dossierFieldRegistry, now, ct);
        var profileRow = await UpsertProfileAsync(db, profileMetadata.Id, auditRecord, bootstrapResult, now, ct);
        var dossierRevision = await UpsertDossierRevisionAsync(
            db,
            dossierRow,
            auditRecord,
            dossierConfidence,
            dossierCoverage,
            freshness,
            stability,
            contradictionMarkersJson,
            dossierRow.SummaryJson,
            dossierRow.PayloadJson,
            now,
            ct);
        var profileRevision = await UpsertProfileRevisionAsync(
            db,
            profileRow,
            auditRecord,
            profileConfidence,
            profileCoverage,
            freshness,
            stability,
            contradictionMarkersJson,
            profileRow.SummaryJson,
            profileRow.PayloadJson,
            now,
            ct);
        dossierRow.CurrentRevisionNumber = dossierRevision.RevisionNumber;
        dossierRow.CurrentRevisionHash = dossierRevision.RevisionHash;
        dossierRow.SummaryJson = dossierRevision.SummaryJson;
        dossierRow.PayloadJson = dossierRevision.PayloadJson;
        dossierRow.UpdatedAt = now;
        profileRow.CurrentRevisionNumber = profileRevision.RevisionNumber;
        profileRow.CurrentRevisionHash = profileRevision.RevisionHash;
        profileRow.SummaryJson = profileRevision.SummaryJson;
        profileRow.PayloadJson = profileRevision.PayloadJson;
        profileRow.UpdatedAt = now;
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
            CurrentDossierRevision = MapDossierRevision(dossierRevision),
            CurrentProfileRevision = MapProfileRevision(profileRevision),
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
        row.RelatedPersonId = null;
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

    private static async Task<DbDurableDossier> UpsertDossierAsync(
        TgAssistantDbContext db,
        Guid durableMetadataId,
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        DossierFieldRegistrySnapshot dossierFieldRegistry,
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
        row.SummaryJson = BuildDossierSummaryJson(auditRecord, bootstrapResult, dossierFieldRegistry);
        row.PayloadJson = BuildDossierPayloadJson(auditRecord, bootstrapResult, dossierFieldRegistry);
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

    private static async Task<DbDurableDossierRevision> UpsertDossierRevisionAsync(
        TgAssistantDbContext db,
        DbDurableDossier dossierRow,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float coverage,
        float freshness,
        float stability,
        string contradictionMarkersJson,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var revisionHash = ComputeDossierProfileRevisionHash(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            confidence,
            coverage,
            freshness,
            stability);
        var existing = await db.DurableDossierRevisions.FirstOrDefaultAsync(
            x => x.DurableDossierId == dossierRow.Id && x.RevisionHash == revisionHash,
            ct);
        if (existing != null)
        {
            existing.ModelPassRunId = auditRecord.ModelPassRunId;
            return existing;
        }

        var nextRevisionNumber = await db.DurableDossierRevisions
            .Where(x => x.DurableDossierId == dossierRow.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(ct) ?? 0;

        var row = new DbDurableDossierRevision
        {
            Id = Guid.NewGuid(),
            DurableDossierId = dossierRow.Id,
            RevisionNumber = nextRevisionNumber + 1,
            RevisionHash = revisionHash,
            ModelPassRunId = auditRecord.ModelPassRunId,
            Confidence = confidence,
            Coverage = coverage,
            Freshness = freshness,
            Stability = stability,
            ContradictionMarkersJson = contradictionMarkersJson,
            SummaryJson = summaryJson,
            PayloadJson = payloadJson,
            CreatedAt = now
        };
        db.DurableDossierRevisions.Add(row);
        return row;
    }

    private static async Task<DbDurableProfileRevision> UpsertProfileRevisionAsync(
        TgAssistantDbContext db,
        DbDurableProfile profileRow,
        ModelPassAuditRecord auditRecord,
        float confidence,
        float coverage,
        float freshness,
        float stability,
        string contradictionMarkersJson,
        string summaryJson,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var revisionHash = ComputeDossierProfileRevisionHash(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            confidence,
            coverage,
            freshness,
            stability);
        var existing = await db.DurableProfileRevisions.FirstOrDefaultAsync(
            x => x.DurableProfileId == profileRow.Id && x.RevisionHash == revisionHash,
            ct);
        if (existing != null)
        {
            existing.ModelPassRunId = auditRecord.ModelPassRunId;
            return existing;
        }

        var nextRevisionNumber = await db.DurableProfileRevisions
            .Where(x => x.DurableProfileId == profileRow.Id)
            .Select(x => (int?)x.RevisionNumber)
            .MaxAsync(ct) ?? 0;

        var row = new DbDurableProfileRevision
        {
            Id = Guid.NewGuid(),
            DurableProfileId = profileRow.Id,
            RevisionNumber = nextRevisionNumber + 1,
            RevisionHash = revisionHash,
            ModelPassRunId = auditRecord.ModelPassRunId,
            Confidence = confidence,
            Coverage = coverage,
            Freshness = freshness,
            Stability = stability,
            ContradictionMarkersJson = contradictionMarkersJson,
            SummaryJson = summaryJson,
            PayloadJson = payloadJson,
            CreatedAt = now
        };
        db.DurableProfileRevisions.Add(row);
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

    private static string BuildDossierMetadataJson(
        Stage6BootstrapGraphResult bootstrapResult,
        DossierFieldRegistrySnapshot dossierFieldRegistry)
    {
        return JsonSerializer.Serialize(new
        {
            family = "dossier",
            linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
            candidate_identity_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal)),
            mention_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.Ordinal)),
            ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count,
            evidence_count = bootstrapResult.EvidenceCount,
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O"),
            canonical_field_registry = new
            {
                approved_family_count = dossierFieldRegistry.ApprovedFamilyCount,
                proposal_family_count = dossierFieldRegistry.ProposalFamilyCount,
                alias_count = dossierFieldRegistry.AliasCount
            }
        });
    }

    private static string BuildProfileMetadataJson(Stage6BootstrapGraphResult bootstrapResult)
    {
        return JsonSerializer.Serialize(new
        {
            family = "profile",
            linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
            candidate_identity_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal)),
            mention_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.Ordinal)),
            ambiguity_count = bootstrapResult.AmbiguityOutputs.Count,
            contradiction_count = bootstrapResult.ContradictionOutputs.Count,
            evidence_count = bootstrapResult.EvidenceCount,
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O")
        });
    }

    private static string BuildDossierSummaryJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        DossierFieldRegistrySnapshot dossierFieldRegistry)
    {
        var writePlan = DossierFieldRegistryCatalog.BuildDurableWritePlan(
            auditRecord.Normalization.NormalizedPayload.Facts,
            dossierFieldRegistry);

        return JsonSerializer.Serialize(new
        {
            tracked_person = bootstrapResult.TrackedPerson?.DisplayName,
            operator_root = bootstrapResult.OperatorPerson?.DisplayName,
            fact_count = auditRecord.Normalization.NormalizedPayload.Facts.Count,
            durable_field_count = writePlan.ApprovedFields.Count,
            proposal_only_field_count = writePlan.ProposalOnlyFields.Count,
            linked_person_count = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal)),
            latest_evidence_at_utc = bootstrapResult.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O"),
            canonical_field_family_count = dossierFieldRegistry.ApprovedFamilyCount,
            proposal_field_family_count = dossierFieldRegistry.ProposalFamilyCount
        });
    }

    private static string BuildDossierPayloadJson(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapGraphResult bootstrapResult,
        DossierFieldRegistrySnapshot dossierFieldRegistry)
    {
        var writePlan = DossierFieldRegistryCatalog.BuildDurableWritePlan(
            auditRecord.Normalization.NormalizedPayload.Facts,
            dossierFieldRegistry);

        return JsonSerializer.Serialize(new
        {
            dossier_type = Stage7DossierTypes.PersonDossier,
            tracked_person = bootstrapResult.TrackedPerson,
            operator_root = bootstrapResult.OperatorPerson,
            fields = writePlan.ApprovedFields.Select(x => new
            {
                category = x.CanonicalCategory,
                key = x.CanonicalKey,
                canonical_category = x.CanonicalCategory,
                canonical_key = x.CanonicalKey,
                family_key = x.FamilyKey,
                approval_state = x.ApprovalState,
                value = x.PrimaryValue,
                observed_category = x.ObservedCategory,
                observed_key = x.ObservedKey,
                normalized_duplicate_count = Math.Max(0, x.ObservedInputs.Count - 1),
                observed_inputs = x.ObservedInputs.Select(input => new
                {
                    observed_category = input.ObservedCategory,
                    observed_key = input.ObservedKey,
                    value = input.Value,
                    confidence = input.Confidence,
                    evidence_refs = input.EvidenceRefs
                }),
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs
            }),
            proposal_fields = writePlan.ProposalOnlyFields.Select(x => new
            {
                category = x.ObservedCategory,
                key = x.ObservedKey,
                canonical_category = x.CanonicalCategory,
                canonical_key = x.CanonicalKey,
                family_key = x.FamilyKey,
                approval_state = x.ApprovalState,
                value = x.PrimaryValue,
                confidence = x.Confidence,
                evidence_refs = x.EvidenceRefs,
                blocked_reason = "unapproved_family"
            }),
            field_registry = new
            {
                approved_family_count = dossierFieldRegistry.ApprovedFamilyCount,
                proposal_family_count = dossierFieldRegistry.ProposalFamilyCount,
                alias_count = dossierFieldRegistry.AliasCount,
                durable_field_count = writePlan.ApprovedFields.Count,
                proposal_only_field_count = writePlan.ProposalOnlyFields.Count,
                collapsed_duplicate_count = writePlan.CollapsedApprovedDuplicateCount,
                observed_mappings = dossierFieldRegistry.FieldMappings.Select(x => new
                {
                    observed_category = x.ObservedCategory,
                    observed_key = x.ObservedKey,
                    canonical_category = x.CanonicalCategory,
                    canonical_key = x.CanonicalKey,
                    family_key = x.FamilyKey,
                    approval_state = x.ApprovalState,
                    is_seeded = x.IsSeeded
                })
            },
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

    private async Task<DossierFieldRegistrySnapshot> SyncDossierFieldRegistryAsync(
        TgAssistantDbContext db,
        ModelPassAuditRecord auditRecord,
        DateTime now,
        CancellationToken ct)
    {
        var observedFacts = auditRecord.Normalization.NormalizedPayload.Facts
            .Select(x => new ObservedDossierField
            {
                ObservedCategory = DossierFieldRegistryCatalog.NormalizeToken(x.Category),
                ObservedKey = DossierFieldRegistryCatalog.NormalizeToken(x.Key)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.ObservedCategory) && !string.IsNullOrWhiteSpace(x.ObservedKey))
            .GroupBy(x => x.AliasToken, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
        var observedTokens = observedFacts.Select(x => x.AliasToken).ToArray();

        var existingAliasRows = observedTokens.Length == 0
            ? new List<DbDossierFieldAlias>()
            : await db.DossierFieldAliases
                .Where(x => observedTokens.Contains(x.AliasToken))
                .ToListAsync(ct);
        var directFamilyRows = observedTokens.Length == 0
            ? new List<DbDossierFieldFamily>()
            : await db.DossierFieldFamilies
                .Where(x => observedTokens.Contains(x.FamilyKey))
                .ToListAsync(ct);
        var existingFamilyIds = existingAliasRows.Select(x => x.DossierFieldFamilyId)
            .Concat(directFamilyRows.Select(x => x.Id))
            .Distinct()
            .ToArray();
        var existingFamiliesById = existingFamilyIds.Length == 0
            ? new Dictionary<Guid, DbDossierFieldFamily>()
            : await db.DossierFieldFamilies
                .Where(x => existingFamilyIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, ct);
        var existingAliasesByToken = existingAliasRows
            .GroupBy(x => x.AliasToken, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var directFamiliesByKey = directFamilyRows
            .GroupBy(x => x.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var observedMappings = observedFacts
            .Select(observed => ResolveObservedMapping(
                observed,
                existingAliasesByToken,
                existingFamiliesById,
                directFamiliesByKey))
            .ToList();

        var desiredFamilies = DossierFieldRegistryCatalog.SeededEntries
            .Select(BuildDesiredFamily)
            .Concat(observedMappings.Where(x => !x.IsSeeded).Select(BuildDesiredFamily))
            .GroupBy(x => x.FamilyKey, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
        var desiredFamilyKeys = desiredFamilies.Select(f => f.FamilyKey).ToArray();

        var existingFamilies = await db.DossierFieldFamilies
            .Where(x => desiredFamilyKeys.Contains(x.FamilyKey))
            .ToDictionaryAsync(x => x.FamilyKey, StringComparer.Ordinal, ct);
        var familyRows = new Dictionary<string, DbDossierFieldFamily>(StringComparer.Ordinal);

        foreach (var family in desiredFamilies)
        {
            if (!existingFamilies.TryGetValue(family.FamilyKey, out var row))
            {
                row = new DbDossierFieldFamily
                {
                    Id = Guid.NewGuid(),
                    FamilyKey = family.FamilyKey,
                    CreatedAt = now
                };
                db.DossierFieldFamilies.Add(row);
                existingFamilies[family.FamilyKey] = row;
            }

            row.CanonicalCategory = family.CanonicalCategory;
            row.CanonicalKey = family.CanonicalKey;
            row.ApprovalState = PromoteApprovalState(row.ApprovalState, family.ApprovalState);
            row.IsSeeded = row.IsSeeded || family.IsSeeded;
            row.MetadataJson = JsonSerializer.Serialize(new
            {
                source = family.IsSeeded ? "seed_catalog" : "observed_dossier_payload",
                proposal_only = string.Equals(family.ApprovalState, DossierFieldApprovalStates.ProposalOnly, StringComparison.Ordinal)
            });
            row.UpdatedAt = now;
            familyRows[family.FamilyKey] = row;
        }

        var desiredAliases = desiredFamilies
            .SelectMany(family => family.Aliases.Select(alias => new DesiredDossierFieldAlias
            {
                FamilyKey = family.FamilyKey,
                AliasCategory = alias.Category,
                AliasKey = alias.Key,
                AliasToken = DossierFieldRegistryCatalog.ComposeAliasToken(alias.Category, alias.Key),
                ApprovalState = family.ApprovalState
            }))
            .GroupBy(x => x.AliasToken, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
        var desiredAliasTokens = desiredAliases.Select(alias => alias.AliasToken).ToArray();
        var existingAliases = await db.DossierFieldAliases
            .Where(x => desiredAliasTokens.Contains(x.AliasToken))
            .ToDictionaryAsync(x => x.AliasToken, StringComparer.Ordinal, ct);

        foreach (var alias in desiredAliases)
        {
            var familyRow = familyRows[alias.FamilyKey];
            if (existingAliases.TryGetValue(alias.AliasToken, out var row))
            {
                if (row.DossierFieldFamilyId != familyRow.Id)
                {
                    _logger.LogWarning(
                        "Dossier field alias conflict retained existing family binding: alias_token={AliasToken}, expected_family={ExpectedFamilyKey}",
                        alias.AliasToken,
                        alias.FamilyKey);
                    continue;
                }

                row.ApprovalState = PromoteApprovalState(row.ApprovalState, alias.ApprovalState);
                row.UpdatedAt = now;
                continue;
            }

            row = new DbDossierFieldAlias
            {
                DossierFieldFamilyId = familyRow.Id,
                AliasCategory = alias.AliasCategory,
                AliasKey = alias.AliasKey,
                AliasToken = alias.AliasToken,
                ApprovalState = alias.ApprovalState,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.DossierFieldAliases.Add(row);
            existingAliases[alias.AliasToken] = row;
        }

        return new DossierFieldRegistrySnapshot
        {
            FieldMappings = observedMappings,
            ApprovedFamilyCount = familyRows.Values.Count(x => string.Equals(x.ApprovalState, DossierFieldApprovalStates.Approved, StringComparison.Ordinal)),
            ProposalFamilyCount = familyRows.Values.Count(x => string.Equals(x.ApprovalState, DossierFieldApprovalStates.ProposalOnly, StringComparison.Ordinal)),
            AliasCount = existingAliases.Count
        };
    }

    private static DesiredDossierFieldFamily BuildDesiredFamily(DossierFieldRegistryEntry entry)
        => new()
        {
            FamilyKey = entry.FamilyKey,
            CanonicalCategory = entry.CanonicalCategory,
            CanonicalKey = entry.CanonicalKey,
            ApprovalState = entry.ApprovalState,
            IsSeeded = entry.IsSeeded,
            Aliases =
            [
                new DossierFieldAliasDefinition
                {
                    Category = entry.CanonicalCategory,
                    Key = entry.CanonicalKey
                },
                .. entry.Aliases
            ]
        };

    private static DesiredDossierFieldFamily BuildDesiredFamily(DossierFieldRegistryResolution resolution)
        => new()
        {
            FamilyKey = resolution.FamilyKey,
            CanonicalCategory = resolution.CanonicalCategory,
            CanonicalKey = resolution.CanonicalKey,
            ApprovalState = resolution.ApprovalState,
            IsSeeded = resolution.IsSeeded,
            Aliases =
            [
                new DossierFieldAliasDefinition
                {
                    Category = resolution.ObservedCategory,
                    Key = resolution.ObservedKey
                }
            ]
        };

    private static string PromoteApprovalState(string current, string desired)
        => string.Equals(current, DossierFieldApprovalStates.Approved, StringComparison.Ordinal)
            || string.Equals(desired, DossierFieldApprovalStates.Approved, StringComparison.Ordinal)
            ? DossierFieldApprovalStates.Approved
            : DossierFieldApprovalStates.ProposalOnly;

    private static DossierFieldRegistryResolution ResolveObservedMapping(
        ObservedDossierField observed,
        IReadOnlyDictionary<string, DbDossierFieldAlias> aliasesByToken,
        IReadOnlyDictionary<Guid, DbDossierFieldFamily> familiesById,
        IReadOnlyDictionary<string, DbDossierFieldFamily> familiesByKey)
    {
        if (aliasesByToken.TryGetValue(observed.AliasToken, out var alias)
            && familiesById.TryGetValue(alias.DossierFieldFamilyId, out var aliasFamily))
        {
            return new DossierFieldRegistryResolution
            {
                ObservedCategory = observed.ObservedCategory,
                ObservedKey = observed.ObservedKey,
                FamilyKey = aliasFamily.FamilyKey,
                CanonicalCategory = aliasFamily.CanonicalCategory,
                CanonicalKey = aliasFamily.CanonicalKey,
                ApprovalState = PromoteApprovalState(aliasFamily.ApprovalState, alias.ApprovalState),
                IsSeeded = aliasFamily.IsSeeded
            };
        }

        if (familiesByKey.TryGetValue(observed.AliasToken, out var family))
        {
            return new DossierFieldRegistryResolution
            {
                ObservedCategory = observed.ObservedCategory,
                ObservedKey = observed.ObservedKey,
                FamilyKey = family.FamilyKey,
                CanonicalCategory = family.CanonicalCategory,
                CanonicalKey = family.CanonicalKey,
                ApprovalState = family.ApprovalState,
                IsSeeded = family.IsSeeded
            };
        }

        return DossierFieldRegistryCatalog.Resolve(observed.ObservedCategory, observed.ObservedKey);
    }

    private sealed class DesiredDossierFieldFamily
    {
        public string FamilyKey { get; init; } = string.Empty;
        public string CanonicalCategory { get; init; } = string.Empty;
        public string CanonicalKey { get; init; } = string.Empty;
        public string ApprovalState { get; init; } = DossierFieldApprovalStates.Approved;
        public bool IsSeeded { get; init; }
        public List<DossierFieldAliasDefinition> Aliases { get; init; } = [];
    }

    private sealed class DesiredDossierFieldAlias
    {
        public string FamilyKey { get; init; } = string.Empty;
        public string AliasCategory { get; init; } = string.Empty;
        public string AliasKey { get; init; } = string.Empty;
        public string AliasToken { get; init; } = string.Empty;
        public string ApprovalState { get; init; } = DossierFieldApprovalStates.Approved;
    }

    private sealed class ObservedDossierField
    {
        public string ObservedCategory { get; init; } = string.Empty;
        public string ObservedKey { get; init; } = string.Empty;
        public string AliasToken => DossierFieldRegistryCatalog.ComposeAliasToken(ObservedCategory, ObservedKey);
    }

    private static string ComputeDossierProfileRevisionHash(
        string summaryJson,
        string payloadJson,
        string contradictionMarkersJson,
        float confidence,
        float coverage,
        float freshness,
        float stability)
    {
        return Stage7RevisionHashHelper.Compute(
            summaryJson,
            payloadJson,
            contradictionMarkersJson,
            Stage7RevisionHashHelper.FormatFloat(confidence),
            Stage7RevisionHashHelper.FormatFloat(coverage),
            Stage7RevisionHashHelper.FormatFloat(freshness),
            Stage7RevisionHashHelper.FormatFloat(stability));
    }

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
            CurrentRevisionNumber = row.CurrentRevisionNumber,
            CurrentRevisionHash = row.CurrentRevisionHash,
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
            CurrentRevisionNumber = row.CurrentRevisionNumber,
            CurrentRevisionHash = row.CurrentRevisionHash,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage7DurableDossierRevision MapDossierRevision(DbDurableDossierRevision row)
    {
        return new Stage7DurableDossierRevision
        {
            Id = row.Id,
            DurableDossierId = row.DurableDossierId,
            RevisionNumber = row.RevisionNumber,
            RevisionHash = row.RevisionHash,
            ModelPassRunId = row.ModelPassRunId,
            Confidence = row.Confidence,
            Coverage = row.Coverage,
            Freshness = row.Freshness,
            Stability = row.Stability,
            ContradictionMarkersJson = row.ContradictionMarkersJson,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson,
            CreatedAt = row.CreatedAt
        };
    }

    private static Stage7DurableProfileRevision MapProfileRevision(DbDurableProfileRevision row)
    {
        return new Stage7DurableProfileRevision
        {
            Id = row.Id,
            DurableProfileId = row.DurableProfileId,
            RevisionNumber = row.RevisionNumber,
            RevisionHash = row.RevisionHash,
            ModelPassRunId = row.ModelPassRunId,
            Confidence = row.Confidence,
            Coverage = row.Coverage,
            Freshness = row.Freshness,
            Stability = row.Stability,
            ContradictionMarkersJson = row.ContradictionMarkersJson,
            SummaryJson = row.SummaryJson,
            PayloadJson = row.PayloadJson,
            CreatedAt = row.CreatedAt
        };
    }
}

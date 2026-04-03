using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage7Formation;

public class Stage7DossierProfileFormationService : IStage7DossierProfileService
{
    private readonly IStage7DossierProfileRepository _repository;
    private readonly IModelPassAuditService _auditService;
    private readonly ILogger<Stage7DossierProfileFormationService> _logger;

    public Stage7DossierProfileFormationService(
        IStage7DossierProfileRepository repository,
        IModelPassAuditService auditService,
        ILogger<Stage7DossierProfileFormationService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Stage7DossierProfileFormationResult> FormAsync(
        Stage7DossierProfileFormationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.BootstrapResult);

        var bootstrapResult = request.BootstrapResult;
        var envelope = BuildEnvelope(request, bootstrapResult);
        var auditRecord = await _auditService.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = envelope,
            RawModelOutput = BuildRawModelOutput(bootstrapResult)
        }, ct);

        if (!string.Equals(auditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Stage7 dossier/profile formation did not materialize durable output: scope_key={ScopeKey}, status={Status}",
                auditRecord.Envelope.ScopeKey,
                auditRecord.Envelope.ResultStatus);

            return new Stage7DossierProfileFormationResult
            {
                AuditRecord = auditRecord,
                Formed = false,
                TrackedPerson = bootstrapResult.TrackedPerson
            };
        }

        var result = await _repository.UpsertAsync(auditRecord, bootstrapResult, ct);
        _logger.LogInformation(
            "Stage7 dossier/profile formation materialized durable outputs: scope_key={ScopeKey}, tracked_person_id={TrackedPersonId}, evidence_count={EvidenceCount}",
            result.AuditRecord.Envelope.ScopeKey,
            result.TrackedPerson?.PersonId,
            result.EvidenceItemIds.Count);
        return result;
    }

    private static ModelPassEnvelope BuildEnvelope(
        Stage7DossierProfileFormationRequest request,
        Stage6BootstrapGraphResult bootstrapResult)
    {
        var bootstrapEnvelope = bootstrapResult.AuditRecord.Envelope;
        var trackedPerson = bootstrapResult.TrackedPerson;
        var scopeKey = string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey)
            ? bootstrapEnvelope.ScopeKey
            : bootstrapResult.ScopeKey;
        var resultStatus = ResolveResultStatus(bootstrapResult);

        return new ModelPassEnvelope
        {
            Stage = "stage7_durable_formation",
            PassFamily = "dossier_profile",
            RunKind = string.IsNullOrWhiteSpace(request.RunKind) ? "manual" : request.RunKind.Trim(),
            ScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "stage7_durable_formation:unresolved" : scopeKey,
            Scope = new ModelPassScope
            {
                ScopeType = "person_scope",
                ScopeRef = trackedPerson?.PersonRef
                    ?? bootstrapEnvelope.Target.TargetRef
                    ?? "person:unresolved",
                AdditionalRefs = bootstrapResult.OperatorPerson == null
                    ? []
                    : [bootstrapResult.OperatorPerson.PersonRef]
            },
            Target = new ModelPassTarget
            {
                TargetType = "person",
                TargetRef = trackedPerson?.PersonRef
                    ?? bootstrapEnvelope.Target.TargetRef
                    ?? "person:unresolved"
            },
            PersonId = trackedPerson?.PersonId ?? bootstrapEnvelope.PersonId,
            SourceObjectId = bootstrapEnvelope.SourceObjectId,
            EvidenceItemId = bootstrapEnvelope.EvidenceItemId,
            RequestedModel = string.IsNullOrWhiteSpace(request.RequestedModel) ? null : request.RequestedModel.Trim(),
            TriggerKind = string.IsNullOrWhiteSpace(request.TriggerKind) ? "manual" : request.TriggerKind.Trim(),
            TriggerRef = string.IsNullOrWhiteSpace(request.TriggerRef) ? null : request.TriggerRef.Trim(),
            SourceRefs =
            [
                .. bootstrapEnvelope.SourceRefs.Select(x => new ModelPassSourceRef
                {
                    SourceType = x.SourceType,
                    SourceRef = x.SourceRef,
                    SourceObjectId = x.SourceObjectId,
                    EvidenceItemId = x.EvidenceItemId,
                })
            ],
            TruthSummary = new ModelPassTruthSummary
            {
                TruthLayer = ModelNormalizationTruthLayers.DerivedButDurable,
                Summary = BuildTruthSummary(bootstrapResult),
                CanonicalRefs = [.. bootstrapEnvelope.TruthSummary.CanonicalRefs]
            },
            Conflicts =
            [
                .. bootstrapResult.ContradictionOutputs.Select(x => new ModelPassConflict
                {
                    ConflictType = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                    Summary = $"Bootstrap contradiction output '{x.OutputKey}' remains unresolved before durable formation.",
                    RelatedObjectRef = x.RelationshipEdgeAnchorId == null
                        ? null
                        : $"relationship_edge_anchor:{x.RelationshipEdgeAnchorId:D}"
                })
            ],
            Unknowns =
            [
                .. bootstrapEnvelope.Unknowns.Select(x => new ModelPassUnknown
                {
                    UnknownType = x.UnknownType,
                    Summary = x.Summary,
                    RequiredAction = x.RequiredAction
                })
            ],
            ResultStatus = resultStatus,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = BuildOutputSummary(resultStatus, bootstrapResult),
                BlockedReason = string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
                    ? "Stage7 dossier/profile formation requires a ready Stage6 bootstrap result with tracked person context."
                    : null
            },
            StartedAtUtc = DateTime.UtcNow,
            FinishedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildRawModelOutput(Stage6BootstrapGraphResult bootstrapResult)
    {
        var resultStatus = ResolveResultStatus(bootstrapResult);
        if (!string.Equals(resultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            if (string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
            {
                return JsonSerializer.Serialize(new
                {
                    result_status = resultStatus,
                    output_summary = new
                    {
                        blocked_reason = "Stage7 dossier/profile formation requires a ready bootstrap result."
                    }
                });
            }

            return JsonSerializer.Serialize(new
            {
                result_status = resultStatus,
                facts = Array.Empty<object>(),
                inferences = Array.Empty<object>(),
                hypotheses = Array.Empty<object>(),
                conflicts = Array.Empty<object>()
            });
        }

        var evidenceRefs = BuildEvidenceRefs(bootstrapResult);
        var trackedPerson = bootstrapResult.TrackedPerson!;
        var linkedPersonCount = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal));
        var candidateIdentityCount = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal));
        var mentionCount = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.Ordinal));
        var ambiguityCount = bootstrapResult.AmbiguityOutputs.Count;
        var contradictionCount = bootstrapResult.ContradictionOutputs.Count;
        var dossierConfidence = NormalizeConfidence(bootstrapResult.EvidenceCount, ambiguityCount, contradictionCount);
        var profileConfidence = Math.Max(0.45f, dossierConfidence - 0.1f);

        return JsonSerializer.Serialize(new
        {
            result_status = ModelPassResultStatuses.ResultReady,
            facts = new object[]
            {
                new
                {
                    category = "identity",
                    key = "tracked_person_name",
                    value = trackedPerson.DisplayName,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = dossierConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "graph_context",
                    key = "operator_attachment",
                    value = bootstrapResult.OperatorPerson?.DisplayName ?? "unresolved",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = dossierConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "bootstrap_discovery",
                    key = "linked_person_count",
                    value = linkedPersonCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = dossierConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "bootstrap_discovery",
                    key = "candidate_identity_count",
                    value = candidateIdentityCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = dossierConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "bootstrap_discovery",
                    key = "mention_count",
                    value = mentionCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = dossierConfidence,
                    evidence_refs = evidenceRefs
                }
            },
            inferences = new object[]
            {
                new
                {
                    inference_type = "profile_context",
                    subject_type = "person",
                    subject_ref = trackedPerson.PersonRef,
                    summary = $"Bootstrap established an operator-centered graph seed with {linkedPersonCount} linked people, {candidateIdentityCount} candidate identities, and {mentionCount} mentions.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = profileConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    inference_type = "bootstrap_pressure",
                    subject_type = "person",
                    subject_ref = trackedPerson.PersonRef,
                    summary = $"Bootstrap pressure is ambiguity={ambiguityCount}, contradiction={contradictionCount}, evidence_count={bootstrapResult.EvidenceCount}.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.40f, profileConfidence - 0.05f),
                    evidence_refs = evidenceRefs
                }
            },
            hypotheses = ambiguityCount + contradictionCount == 0
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        hypothesis_type = "review_pressure",
                        subject_type = "person",
                        subject_ref = trackedPerson.PersonRef,
                        statement = $"Bootstrap still carries {ambiguityCount} ambiguity pools and {contradictionCount} contradiction pools that need later review.",
                        truth_layer = ModelNormalizationTruthLayers.ProposalLayer,
                        confidence = Math.Max(0.35f, profileConfidence - 0.15f),
                        evidence_refs = evidenceRefs
                    }
                },
            conflicts = contradictionCount == 0
                ? Array.Empty<object>()
                : bootstrapResult.ContradictionOutputs.Select(x => new
                {
                    conflict_type = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                    summary = $"Bootstrap contradiction output '{x.OutputKey}' remains unresolved.",
                    truth_layer = ModelNormalizationTruthLayers.ConflictedOrObsolete,
                    related_object_ref = x.RelationshipEdgeAnchorId == null
                        ? null
                        : $"relationship_edge_anchor:{x.RelationshipEdgeAnchorId:D}",
                    confidence = 0.50f,
                    evidence_refs = evidenceRefs
                }).ToArray()
        });
    }

    private static string[] BuildEvidenceRefs(Stage6BootstrapGraphResult bootstrapResult)
    {
        var evidenceRefs = bootstrapResult.AuditRecord.Envelope.TruthSummary.CanonicalRefs
            .Where(x => x.StartsWith("evidence:", StringComparison.Ordinal))
            .Concat(
                bootstrapResult.AuditRecord.Envelope.SourceRefs
                    .Where(x => x.EvidenceItemId != null)
                    .Select(x => $"evidence:{x.EvidenceItemId:D}"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return evidenceRefs.Length == 0
            ? ["evidence:unresolved"]
            : evidenceRefs;
    }

    private static string BuildTruthSummary(Stage6BootstrapGraphResult bootstrapResult)
    {
        if (bootstrapResult.TrackedPerson == null)
        {
            return "Stage7 dossier/profile formation could not resolve tracked person context.";
        }

        return $"Durable dossier/profile formation summarized tracked person '{bootstrapResult.TrackedPerson.DisplayName}' from bootstrap evidence count {bootstrapResult.EvidenceCount}.";
    }

    private static string BuildOutputSummary(string resultStatus, Stage6BootstrapGraphResult bootstrapResult)
    {
        return resultStatus switch
        {
            ModelPassResultStatuses.ResultReady => "Stage7 dossier/profile formation produced durable dossier and global profile outputs.",
            ModelPassResultStatuses.NeedMoreData => bootstrapResult.AuditRecord.Envelope.OutputSummary.Summary,
            ModelPassResultStatuses.NeedOperatorClarification => bootstrapResult.AuditRecord.Envelope.OutputSummary.Summary,
            _ => "Stage7 dossier/profile formation was blocked by incomplete bootstrap context."
        };
    }

    private static string ResolveResultStatus(Stage6BootstrapGraphResult bootstrapResult)
    {
        if (bootstrapResult == null)
        {
            return ModelPassResultStatuses.BlockedInvalidInput;
        }

        if (!string.Equals(bootstrapResult.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            return bootstrapResult.AuditRecord.Envelope.ResultStatus;
        }

        return bootstrapResult.TrackedPerson == null || string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey)
            ? ModelPassResultStatuses.BlockedInvalidInput
            : ModelPassResultStatuses.ResultReady;
    }

    private static float NormalizeConfidence(int evidenceCount, int ambiguityCount, int contradictionCount)
    {
        var baseline = evidenceCount switch
        {
            <= 1 => 0.60f,
            2 => 0.72f,
            3 => 0.82f,
            _ => 0.90f
        };

        var penalty = (ambiguityCount * 0.05f) + (contradictionCount * 0.08f);
        return Math.Clamp(baseline - penalty, 0.35f, 0.95f);
    }
}

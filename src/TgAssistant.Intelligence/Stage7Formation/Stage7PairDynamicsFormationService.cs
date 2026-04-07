using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage7Formation;

public class Stage7PairDynamicsFormationService : IStage7PairDynamicsService
{
    private readonly IStage7PairDynamicsRepository _repository;
    private readonly IModelPassAuditService _auditService;
    private readonly ILogger<Stage7PairDynamicsFormationService> _logger;

    public Stage7PairDynamicsFormationService(
        IStage7PairDynamicsRepository repository,
        IModelPassAuditService auditService,
        ILogger<Stage7PairDynamicsFormationService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Stage7PairDynamicsFormationResult> FormAsync(
        Stage7PairDynamicsFormationRequest request,
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
                "Stage7 pair-dynamics formation did not materialize durable output: scope_key={ScopeKey}, status={Status}",
                auditRecord.Envelope.ScopeKey,
                auditRecord.Envelope.ResultStatus);

            return new Stage7PairDynamicsFormationResult
            {
                AuditRecord = auditRecord,
                Formed = false,
                TrackedPerson = bootstrapResult.TrackedPerson,
                OperatorPerson = bootstrapResult.OperatorPerson
            };
        }

        var result = await _repository.UpsertAsync(auditRecord, bootstrapResult, ct);
        _logger.LogInformation(
            "Stage7 pair-dynamics formation materialized durable output: scope_key={ScopeKey}, tracked_person_id={TrackedPersonId}, operator_person_id={OperatorPersonId}, revision_number={RevisionNumber}",
            result.AuditRecord.Envelope.ScopeKey,
            result.TrackedPerson?.PersonId,
            result.OperatorPerson?.PersonId,
            result.CurrentRevision?.RevisionNumber);
        return result;
    }

    private static ModelPassEnvelope BuildEnvelope(
        Stage7PairDynamicsFormationRequest request,
        Stage6BootstrapGraphResult bootstrapResult)
    {
        var bootstrapEnvelope = bootstrapResult.AuditRecord.Envelope;
        var trackedPerson = bootstrapResult.TrackedPerson;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var scopeKey = string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey)
            ? bootstrapEnvelope.ScopeKey
            : bootstrapResult.ScopeKey;
        var contractViolationReason = ResolveContractViolationReason(bootstrapResult, StageSemanticOwnedOutputFamilies.Stage7PairDynamics);
        var resultStatus = ResolveResultStatus(bootstrapResult, contractViolationReason);
        var pairTargetRef = BuildPairTargetRef(operatorPerson, trackedPerson);

        return new ModelPassEnvelope
        {
            Stage = StageSemanticRuntimeSeams.Stage7DurableFormationStage,
            PassFamily = StageSemanticRuntimeSeams.Stage7PairDynamicsPassFamily,
            RunKind = string.IsNullOrWhiteSpace(request.RunKind) ? "manual" : request.RunKind.Trim(),
            ScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "stage7_pair_dynamics:unresolved" : scopeKey,
            Scope = new ModelPassScope
            {
                ScopeType = "pair_scope",
                ScopeRef = pairTargetRef,
                AdditionalRefs = trackedPerson == null || operatorPerson == null
                    ? []
                    : [trackedPerson.PersonRef, operatorPerson.PersonRef]
            },
            Target = new ModelPassTarget
            {
                TargetType = "pair_dynamics",
                TargetRef = pairTargetRef
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
                    EvidenceItemId = x.EvidenceItemId
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
                    Summary = $"Bootstrap contradiction output '{x.OutputKey}' remains unresolved before pair-dynamics formation.",
                    RelatedObjectRef = x.RelationshipEdgeAnchorId == null
                        ? null
                        : $"relationship_edge_anchor:{x.RelationshipEdgeAnchorId:D}"
                })
            ],
            Unknowns = BuildUnknowns(resultStatus, bootstrapEnvelope),
            Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                ModelPassBudgetCatalog.Create(
                    StageSemanticRuntimeSeams.Stage7DurableFormationStage,
                    StageSemanticRuntimeSeams.Stage7PairDynamicsPassFamily)),
            ResultStatus = resultStatus,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = BuildOutputSummary(resultStatus, bootstrapResult, contractViolationReason),
                BlockedReason = string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
                    ? contractViolationReason ?? "Stage7 pair-dynamics formation requires a ready Stage6 bootstrap result with operator and tracked person context."
                    : null
            },
            StartedAtUtc = DateTime.UtcNow,
            FinishedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildRawModelOutput(Stage6BootstrapGraphResult bootstrapResult)
    {
        var contractViolationReason = ResolveContractViolationReason(bootstrapResult, StageSemanticOwnedOutputFamilies.Stage7PairDynamics);
        var resultStatus = ResolveResultStatus(bootstrapResult, contractViolationReason);
        if (!string.Equals(resultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            if (string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
            {
                return JsonSerializer.Serialize(new
                {
                    result_status = resultStatus,
                    output_summary = new
                    {
                        blocked_reason = "Stage7 pair-dynamics formation requires tracked and operator bootstrap context."
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

        var trackedPerson = bootstrapResult.TrackedPerson!;
        var operatorPerson = bootstrapResult.OperatorPerson!;
        var evidenceRefs = BuildEvidenceRefs(bootstrapResult);
        var ambiguityCount = bootstrapResult.AmbiguityOutputs.Count;
        var contradictionCount = bootstrapResult.ContradictionOutputs.Count;
        var linkedPersonCount = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal));
        var candidateIdentityCount = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.CandidateIdentity, StringComparison.Ordinal));
        var baseConfidence = NormalizeConfidence(bootstrapResult.EvidenceCount, ambiguityCount, contradictionCount);
        var pairRef = BuildPairTargetRef(operatorPerson, trackedPerson);

        return JsonSerializer.Serialize(new
        {
            result_status = ModelPassResultStatuses.ResultReady,
            facts = new object[]
            {
                new
                {
                    category = "pair_dynamics",
                    key = "initiative_balance",
                    value = ambiguityCount > contradictionCount ? "operator_anchor_with_open_response" : "operator_anchor_with_stable_response",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = baseConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "pair_dynamics",
                    key = "response_rhythm",
                    value = bootstrapResult.EvidenceCount >= 3 ? "evidence_backed_exchange" : "low_signal_exchange",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = baseConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "pair_dynamics",
                    key = "conflict_repair_cycle",
                    value = contradictionCount == 0 ? "repair_pressure_low" : "repair_pressure_present",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.40f, baseConfidence - 0.05f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "pair_dynamics",
                    key = "emotional_safety",
                    value = contradictionCount == 0 && ambiguityCount <= 1 ? "cautiously_safe" : "needs_review",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.35f, baseConfidence - 0.10f),
                    evidence_refs = evidenceRefs
                }
            },
            inferences = new object[]
            {
                new
                {
                    inference_type = "pair_summary",
                    subject_type = "pair",
                    subject_ref = pairRef,
                    summary = $"Operator '{operatorPerson.DisplayName}' and tracked person '{trackedPerson.DisplayName}' currently show bootstrap-backed pair dynamics with linked_people={linkedPersonCount}, candidate_identities={candidateIdentityCount}, ambiguity={ambiguityCount}, contradiction={contradictionCount}.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.45f, baseConfidence - 0.05f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    inference_type = "coordination_signal",
                    subject_type = "pair",
                    subject_ref = pairRef,
                    summary = $"Bootstrap retained {bootstrapResult.SliceOutputs.Count} bootstrap slices and {bootstrapResult.EvidenceCount} evidence-backed observations for this pair scope.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.40f, baseConfidence - 0.08f),
                    evidence_refs = evidenceRefs
                }
            },
            hypotheses = ambiguityCount == 0
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        hypothesis_type = "pair_ambiguity_pressure",
                        subject_type = "pair",
                        subject_ref = pairRef,
                        statement = $"Pair dynamics still carry {ambiguityCount} ambiguity pools that may change the reading of closeness and response rhythm.",
                        truth_layer = ModelNormalizationTruthLayers.ProposalLayer,
                        confidence = Math.Max(0.35f, baseConfidence - 0.15f),
                        evidence_refs = evidenceRefs
                    }
                },
            conflicts = contradictionCount == 0
                ? Array.Empty<object>()
                : bootstrapResult.ContradictionOutputs.Select(x => new
                {
                    conflict_type = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                    summary = $"Pair contradiction output '{x.OutputKey}' remains unresolved.",
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
        if (bootstrapResult.TrackedPerson == null || bootstrapResult.OperatorPerson == null)
        {
            return "Stage7 pair-dynamics formation could not resolve operator/tracked pair context.";
        }

        return $"Durable pair-dynamics formation summarized operator '{bootstrapResult.OperatorPerson.DisplayName}' and tracked person '{bootstrapResult.TrackedPerson.DisplayName}' from bootstrap evidence count {bootstrapResult.EvidenceCount}.";
    }

    private static string BuildOutputSummary(string resultStatus, Stage6BootstrapGraphResult bootstrapResult, string? contractViolationReason)
    {
        return resultStatus switch
        {
            ModelPassResultStatuses.ResultReady
                => $"Stage7 pair-dynamics formation is ready for pair '{BuildPairTargetRef(bootstrapResult.OperatorPerson, bootstrapResult.TrackedPerson)}'.",
            ModelPassResultStatuses.NeedMoreData
                => "Stage7 pair-dynamics formation needs more bootstrap evidence or operator attachment data.",
            ModelPassResultStatuses.NeedOperatorClarification
                => "Stage7 pair-dynamics formation requires clarification before durable pair output can be formed.",
            _ => contractViolationReason ?? "Stage7 pair-dynamics formation is blocked because bootstrap context is invalid."
        };
    }

    private static string ResolveResultStatus(Stage6BootstrapGraphResult bootstrapResult, string? contractViolationReason)
    {
        if (!string.IsNullOrWhiteSpace(contractViolationReason))
        {
            return ModelPassResultStatuses.BlockedInvalidInput;
        }

        if (!string.Equals(bootstrapResult.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            return bootstrapResult.AuditRecord.Envelope.ResultStatus;
        }

        if (bootstrapResult.TrackedPerson == null
            || bootstrapResult.OperatorPerson == null
            || string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey))
        {
            return ModelPassResultStatuses.BlockedInvalidInput;
        }

        return ModelPassResultStatuses.ResultReady;
    }

    private static float NormalizeConfidence(int evidenceCount, int ambiguityCount, int contradictionCount)
    {
        var confidence = 0.45f + Math.Min(0.35f, evidenceCount * 0.08f);
        confidence -= ambiguityCount * 0.05f;
        confidence -= contradictionCount * 0.08f;
        return Math.Clamp(confidence, 0.30f, 0.92f);
    }

    private static string BuildPairTargetRef(Stage6BootstrapPersonRef? operatorPerson, Stage6BootstrapPersonRef? trackedPerson)
        => operatorPerson == null || trackedPerson == null
            ? "pair:unresolved"
            : $"pair:{operatorPerson.PersonId:D}:{trackedPerson.PersonId:D}";

    private static List<ModelPassUnknown> BuildUnknowns(string resultStatus, ModelPassEnvelope bootstrapEnvelope)
    {
        var unknowns = bootstrapEnvelope.Unknowns
            .Select(x => new ModelPassUnknown
            {
                UnknownType = x.UnknownType,
                Summary = x.Summary,
                RequiredAction = x.RequiredAction
            })
            .ToList();

        if (unknowns.Count == 0
            && (string.Equals(resultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
                || string.Equals(resultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)))
        {
            unknowns.Add(new ModelPassUnknown
            {
                UnknownType = "pair_context_gap",
                Summary = "Stage7 pair-dynamics formation requires additional Stage6 pair context before durable output can be formed.",
                RequiredAction = string.Equals(resultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)
                    ? "operator_clarification"
                    : "collect_more_bootstrap_evidence"
            });
        }

        return unknowns;
    }

    private static string? ResolveContractViolationReason(Stage6BootstrapGraphResult bootstrapResult, string stage7OwnedOutputFamily)
    {
        var envelope = bootstrapResult.AuditRecord.Envelope;
        if (!StageSemanticContract.TryMapRuntimeStageAndPassFamilyToSemanticOutputFamily(
                envelope.Stage,
                envelope.PassFamily,
                out var stage6OwnedOutputFamily))
        {
            return StageSemanticHandoffReasons.StageContractViolation;
        }

        if (!string.Equals(stage6OwnedOutputFamily, StageSemanticOwnedOutputFamilies.Stage6BootstrapGraph, StringComparison.Ordinal))
        {
            return StageSemanticHandoffReasons.StageContractViolation;
        }

        var bootstrapGraphHandoff = StageSemanticContract.ValidateStage6ToStage7Handoff(
            StageSemanticOwnedOutputFamilies.Stage6BootstrapGraph,
            StageSemanticAcceptedInputFamilies.Stage6BootstrapGraph,
            StageSemanticHandoffReasons.BootstrapComplete);
        if (!bootstrapGraphHandoff.IsValid)
        {
            return bootstrapGraphHandoff.Reason ?? StageSemanticHandoffReasons.StageContractViolation;
        }

        var discoveryPoolHandoff = StageSemanticContract.ValidateStage6ToStage7Handoff(
            StageSemanticOwnedOutputFamilies.Stage6DiscoveryPool,
            StageSemanticAcceptedInputFamilies.Stage6DiscoveryPool,
            StageSemanticHandoffReasons.BootstrapComplete);
        if (!discoveryPoolHandoff.IsValid)
        {
            return discoveryPoolHandoff.Reason ?? StageSemanticHandoffReasons.StageContractViolation;
        }

        return StageSemanticContract.OwnsOutputFamily(StageSemanticStages.Stage7, stage7OwnedOutputFamily)
            ? null
            : StageSemanticHandoffReasons.StageContractViolation;
    }
}

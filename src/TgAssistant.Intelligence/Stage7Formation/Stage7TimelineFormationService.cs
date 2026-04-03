using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage7Formation;

public class Stage7TimelineFormationService : IStage7TimelineService
{
    private readonly IStage7TimelineRepository _repository;
    private readonly IModelPassAuditService _auditService;
    private readonly ILogger<Stage7TimelineFormationService> _logger;

    public Stage7TimelineFormationService(
        IStage7TimelineRepository repository,
        IModelPassAuditService auditService,
        ILogger<Stage7TimelineFormationService> logger)
    {
        _repository = repository;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Stage7TimelineFormationResult> FormAsync(
        Stage7TimelineFormationRequest request,
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
                "Stage7 timeline formation did not materialize durable output: scope_key={ScopeKey}, status={Status}",
                auditRecord.Envelope.ScopeKey,
                auditRecord.Envelope.ResultStatus);

            return new Stage7TimelineFormationResult
            {
                AuditRecord = auditRecord,
                Formed = false,
                TrackedPerson = bootstrapResult.TrackedPerson,
                OperatorPerson = bootstrapResult.OperatorPerson
            };
        }

        var result = await _repository.UpsertAsync(auditRecord, bootstrapResult, ct);
        _logger.LogInformation(
            "Stage7 timeline formation materialized durable outputs: scope_key={ScopeKey}, tracked_person_id={TrackedPersonId}, event_id={EventId}, episode_id={EpisodeId}, story_arc_id={StoryArcId}",
            result.AuditRecord.Envelope.ScopeKey,
            result.TrackedPerson?.PersonId,
            result.Event?.Id,
            result.TimelineEpisode?.Id,
            result.StoryArc?.Id);
        return result;
    }

    private static ModelPassEnvelope BuildEnvelope(
        Stage7TimelineFormationRequest request,
        Stage6BootstrapGraphResult bootstrapResult)
    {
        var bootstrapEnvelope = bootstrapResult.AuditRecord.Envelope;
        var trackedPerson = bootstrapResult.TrackedPerson;
        var operatorPerson = bootstrapResult.OperatorPerson;
        var scopeKey = string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey)
            ? bootstrapEnvelope.ScopeKey
            : bootstrapResult.ScopeKey;
        var resultStatus = ResolveResultStatus(bootstrapResult);

        return new ModelPassEnvelope
        {
            Stage = "stage7_durable_formation",
            PassFamily = "timeline_objects",
            RunKind = string.IsNullOrWhiteSpace(request.RunKind) ? "manual" : request.RunKind.Trim(),
            ScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "stage7_timeline:unresolved" : scopeKey,
            Scope = new ModelPassScope
            {
                ScopeType = "timeline_scope",
                ScopeRef = trackedPerson?.PersonRef ?? "person:unresolved",
                AdditionalRefs = operatorPerson == null ? [] : [operatorPerson.PersonRef]
            },
            Target = new ModelPassTarget
            {
                TargetType = "timeline_bundle",
                TargetRef = trackedPerson?.PersonRef ?? "person:unresolved"
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
                    Summary = $"Bootstrap contradiction output '{x.OutputKey}' remains unresolved before durable timeline formation.",
                    RelatedObjectRef = x.RelationshipEdgeAnchorId == null
                        ? null
                        : $"relationship_edge_anchor:{x.RelationshipEdgeAnchorId:D}"
                })
            ],
            Unknowns = BuildUnknowns(resultStatus, bootstrapEnvelope),
            ResultStatus = resultStatus,
            OutputSummary = new ModelPassOutputSummary
            {
                Summary = BuildOutputSummary(resultStatus, bootstrapResult),
                BlockedReason = string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
                    ? "Stage7 timeline formation requires a ready Stage6 bootstrap result with tracked person context."
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
                        blocked_reason = "Stage7 timeline formation requires tracked person bootstrap context."
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
        var operatorPerson = bootstrapResult.OperatorPerson;
        var evidenceRefs = BuildEvidenceRefs(bootstrapResult);
        var ambiguityCount = bootstrapResult.AmbiguityOutputs.Count;
        var contradictionCount = bootstrapResult.ContradictionOutputs.Count;
        var sliceCount = bootstrapResult.SliceOutputs.Count;
        var linkedPersonCount = bootstrapResult.DiscoveryOutputs.Count(x => string.Equals(x.DiscoveryType, Stage6BootstrapDiscoveryTypes.LinkedPerson, StringComparison.Ordinal));
        var baseConfidence = NormalizeConfidence(bootstrapResult.EvidenceCount, ambiguityCount, contradictionCount);
        var eventClosure = ResolveEventClosureState(ambiguityCount, contradictionCount);
        var episodeClosure = ResolveTimelineEpisodeClosureState(sliceCount, contradictionCount);
        var storyArcClosure = ResolveStoryArcClosureState(ambiguityCount, contradictionCount);

        return JsonSerializer.Serialize(new
        {
            result_status = ModelPassResultStatuses.ResultReady,
            facts = new object[]
            {
                new
                {
                    category = "event",
                    key = "anchor_event_type",
                    value = Stage7EventTypes.BootstrapAnchorEvent,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = baseConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "event",
                    key = "event_closure_state",
                    value = eventClosure,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.45f, baseConfidence - 0.05f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "timeline_episode",
                    key = "episode_type",
                    value = Stage7TimelineEpisodeTypes.BootstrapEpisode,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = baseConfidence,
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "timeline_episode",
                    key = "episode_closure_state",
                    value = episodeClosure,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.42f, baseConfidence - 0.06f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "story_arc",
                    key = "arc_type",
                    value = Stage7StoryArcTypes.OperatorTrackedArc,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.44f, baseConfidence - 0.04f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    category = "story_arc",
                    key = "arc_closure_state",
                    value = storyArcClosure,
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.40f, baseConfidence - 0.08f),
                    evidence_refs = evidenceRefs
                }
            },
            inferences = new object[]
            {
                new
                {
                    inference_type = "event_summary",
                    subject_type = "event",
                    subject_ref = $"person:{trackedPerson.PersonId:D}:event",
                    summary = $"Bootstrap evidence count {bootstrapResult.EvidenceCount} supports an operator-attached event between '{operatorPerson?.DisplayName ?? "unresolved"}' and '{trackedPerson.DisplayName}'.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.46f, baseConfidence - 0.04f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    inference_type = "timeline_episode_summary",
                    subject_type = "timeline_episode",
                    subject_ref = $"person:{trackedPerson.PersonId:D}:timeline_episode",
                    summary = $"Bootstrap retained {sliceCount} bootstrap slices with linked_people={linkedPersonCount}, ambiguity={ambiguityCount}, contradiction={contradictionCount}.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.44f, baseConfidence - 0.05f),
                    evidence_refs = evidenceRefs
                },
                new
                {
                    inference_type = "story_arc_summary",
                    subject_type = "story_arc",
                    subject_ref = operatorPerson == null
                        ? $"person:{trackedPerson.PersonId:D}:story_arc"
                        : $"pair:{operatorPerson.PersonId:D}:{trackedPerson.PersonId:D}:story_arc",
                    summary = $"Story arc closure is '{storyArcClosure}' under ambiguity={ambiguityCount}, contradiction={contradictionCount}, slice_count={sliceCount}.",
                    truth_layer = ModelNormalizationTruthLayers.DerivedButDurable,
                    confidence = Math.Max(0.42f, baseConfidence - 0.08f),
                    evidence_refs = evidenceRefs
                }
            },
            hypotheses = ambiguityCount == 0
                ? Array.Empty<object>()
                : new object[]
                {
                    new
                    {
                        hypothesis_type = "timeline_boundary_review",
                        subject_type = "timeline_episode",
                        subject_ref = $"person:{trackedPerson.PersonId:D}:timeline_episode",
                        statement = $"Timeline boundary may still shift because bootstrap ambiguity count is {ambiguityCount}.",
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
                    summary = $"Timeline/story-arc contradiction output '{x.OutputKey}' remains unresolved.",
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
            return "Stage7 timeline formation could not resolve tracked person context.";
        }

        return $"Durable timeline formation summarized tracked person '{bootstrapResult.TrackedPerson.DisplayName}' from bootstrap evidence count {bootstrapResult.EvidenceCount}.";
    }

    private static string BuildOutputSummary(string resultStatus, Stage6BootstrapGraphResult bootstrapResult)
    {
        return resultStatus switch
        {
            ModelPassResultStatuses.ResultReady => "Stage7 timeline formation produced durable event, timeline episode, and story arc outputs.",
            ModelPassResultStatuses.NeedMoreData => bootstrapResult.AuditRecord.Envelope.OutputSummary.Summary,
            ModelPassResultStatuses.NeedOperatorClarification => bootstrapResult.AuditRecord.Envelope.OutputSummary.Summary,
            _ => "Stage7 timeline formation was blocked by incomplete bootstrap context."
        };
    }

    private static string ResolveResultStatus(Stage6BootstrapGraphResult bootstrapResult)
    {
        if (!string.Equals(bootstrapResult.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            return bootstrapResult.AuditRecord.Envelope.ResultStatus;
        }

        if (bootstrapResult.TrackedPerson == null || string.IsNullOrWhiteSpace(bootstrapResult.ScopeKey))
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

    private static string ResolveEventClosureState(int ambiguityCount, int contradictionCount)
    {
        if (contradictionCount == 0 && ambiguityCount == 0)
        {
            return Stage7ClosureStates.Closed;
        }

        if (contradictionCount == 0)
        {
            return Stage7ClosureStates.SemiClosed;
        }

        return Stage7ClosureStates.Open;
    }

    private static string ResolveTimelineEpisodeClosureState(int sliceCount, int contradictionCount)
    {
        if (sliceCount == 0)
        {
            return Stage7ClosureStates.Open;
        }

        if (contradictionCount == 0)
        {
            return Stage7ClosureStates.Closed;
        }

        return Stage7ClosureStates.SemiClosed;
    }

    private static string ResolveStoryArcClosureState(int ambiguityCount, int contradictionCount)
    {
        if (contradictionCount > 0)
        {
            return Stage7ClosureStates.Open;
        }

        if (ambiguityCount > 0)
        {
            return Stage7ClosureStates.SemiClosed;
        }

        return Stage7ClosureStates.Closed;
    }

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
                UnknownType = "timeline_context_gap",
                Summary = "Stage7 timeline formation requires additional Stage6 timeline context before durable outputs can be formed.",
                RequiredAction = string.Equals(resultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)
                    ? "operator_clarification"
                    : "collect_more_bootstrap_evidence"
            });
        }

        return unknowns;
    }
}

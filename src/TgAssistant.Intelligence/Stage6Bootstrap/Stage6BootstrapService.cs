using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6Bootstrap;

public class Stage6BootstrapService : IStage6BootstrapService
{
    private readonly IStage6BootstrapRepository _bootstrapRepository;
    private readonly IModelPassAuditService _auditService;
    private readonly ILogger<Stage6BootstrapService> _logger;

    public Stage6BootstrapService(
        IStage6BootstrapRepository bootstrapRepository,
        IModelPassAuditService auditService,
        ILogger<Stage6BootstrapService> logger)
    {
        _bootstrapRepository = bootstrapRepository;
        _auditService = auditService;
        _logger = logger;
    }

    public async Task<Stage6BootstrapGraphResult> RunGraphInitializationAsync(Stage6BootstrapRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resolution = await _bootstrapRepository.ResolveScopeAsync(request, ct);
        var envelope = BuildEnvelope(request, resolution);
        var rawModelOutput = BuildRawModelOutput(resolution);
        var auditRecord = await _auditService.NormalizeAndPersistAsync(new ModelNormalizationRequest
        {
            Envelope = envelope,
            RawModelOutput = rawModelOutput
        }, ct);

        if (!string.Equals(auditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Stage6 bootstrap graph initialization did not materialize graph output: scope_key={ScopeKey}, status={Status}, reason={Reason}",
                auditRecord.Envelope.ScopeKey,
                auditRecord.Envelope.ResultStatus,
                resolution.Reason ?? auditRecord.Envelope.OutputSummary.BlockedReason ?? "not_ready");

            return new Stage6BootstrapGraphResult
            {
                AuditRecord = auditRecord,
                GraphInitialized = false
            };
        }

        var result = await _bootstrapRepository.UpsertGraphInitializationAsync(auditRecord, resolution, ct);
        result.DiscoveryOutputs = await _bootstrapRepository.UpsertDiscoveryOutputsAsync(auditRecord, resolution, ct);
        _logger.LogInformation(
            "Stage6 bootstrap graph initialized: scope_key={ScopeKey}, tracked_person_id={TrackedPersonId}, node_count={NodeCount}, edge_count={EdgeCount}, discovery_count={DiscoveryCount}",
            result.AuditRecord.Envelope.ScopeKey,
            resolution.TrackedPerson?.PersonId,
            result.Nodes.Count,
            result.Edges.Count,
            result.DiscoveryOutputs.Count);

        return result;
    }

    private static ModelPassEnvelope BuildEnvelope(Stage6BootstrapRequest request, Stage6BootstrapScopeResolution resolution)
    {
        var scopeKey = resolution.ScopeKey;
        var resultStatus = ResolveResultStatus(resolution.ResolutionStatus);
        var sourceRefs = resolution.SourceRefs
            .Select(x => new ModelPassSourceRef
            {
                SourceType = x.SourceType,
                SourceRef = x.SourceRef,
                SourceObjectId = x.SourceObjectId,
                EvidenceItemId = x.EvidenceItemId
            })
            .ToList();
        var outputSummary = new ModelPassOutputSummary
        {
            Summary = BuildOutputSummary(resultStatus, resolution),
            BlockedReason = string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
                ? resolution.Reason ?? "Stage 6 bootstrap request was invalid."
                : null
        };

        return new ModelPassEnvelope
        {
            Stage = "stage6_bootstrap",
            PassFamily = "graph_init",
            RunKind = string.IsNullOrWhiteSpace(request.RunKind) ? "manual" : request.RunKind.Trim(),
            ScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "stage6_bootstrap:unresolved" : scopeKey,
            Scope = new ModelPassScope
            {
                ScopeType = request.PersonId != null ? "person_scope" : "scope_key",
                ScopeRef = request.PersonId != null
                    ? resolution.TrackedPerson?.PersonRef ?? $"person:{request.PersonId:D}"
                    : (request.ScopeKey?.Trim() ?? resolution.ScopeKey),
                AdditionalRefs = resolution.OperatorPerson == null
                    ? []
                    : [resolution.OperatorPerson.PersonRef]
            },
            Target = new ModelPassTarget
            {
                TargetType = "person",
                TargetRef = resolution.TrackedPerson?.PersonRef
                    ?? (request.PersonId != null ? $"person:{request.PersonId:D}" : (request.ScopeKey?.Trim() ?? "person:unresolved"))
            },
            PersonId = resolution.TrackedPerson?.PersonId ?? request.PersonId,
            SourceObjectId = resolution.SourceRefs.FirstOrDefault()?.SourceObjectId,
            EvidenceItemId = resolution.SourceRefs.FirstOrDefault()?.EvidenceItemId,
            RequestedModel = string.IsNullOrWhiteSpace(request.RequestedModel) ? null : request.RequestedModel.Trim(),
            TriggerKind = string.IsNullOrWhiteSpace(request.TriggerKind) ? "manual" : request.TriggerKind.Trim(),
            TriggerRef = string.IsNullOrWhiteSpace(request.TriggerRef) ? null : request.TriggerRef.Trim(),
            SourceRefs = sourceRefs,
            TruthSummary = new ModelPassTruthSummary
            {
                TruthLayer = ModelNormalizationTruthLayers.CanonicalTruth,
                Summary = BuildTruthSummary(resolution),
                CanonicalRefs = sourceRefs
                    .Where(x => x.EvidenceItemId != null)
                    .Select(x => $"evidence:{x.EvidenceItemId:D}")
                    .Distinct(StringComparer.Ordinal)
                    .ToList()
            },
            Conflicts = [.. resolution.Conflicts],
            Unknowns = [.. resolution.Unknowns],
            ResultStatus = resultStatus,
            OutputSummary = outputSummary,
            StartedAtUtc = DateTime.UtcNow,
            FinishedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildRawModelOutput(Stage6BootstrapScopeResolution resolution)
    {
        var requestedStatus = ResolveResultStatus(resolution.ResolutionStatus);
        if (string.Equals(requestedStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                result_status = ModelPassResultStatuses.ResultReady,
                inferences = new[]
                {
                    new
                    {
                        inference_type = Stage6BootstrapEdgeTypes.TrackedPersonAttachment,
                        subject_type = "person",
                        subject_ref = resolution.TrackedPerson!.PersonRef,
                        summary = $"Tracked person '{resolution.TrackedPerson.DisplayName}' is attached to operator root '{resolution.OperatorPerson!.DisplayName}'.",
                        truth_layer = ModelNormalizationTruthLayers.ProposalLayer,
                        confidence = NormalizeConfidence(resolution.EvidenceCount),
                        evidence_refs = resolution.SourceRefs
                            .Where(x => x.EvidenceItemId != null)
                            .Select(x => $"evidence:{x.EvidenceItemId:D}")
                            .Distinct(StringComparer.Ordinal)
                            .ToArray()
                    }
                }
            });
        }

        if (string.Equals(requestedStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new
            {
                result_status = ModelPassResultStatuses.BlockedInvalidInput,
                output_summary = new
                {
                    blocked_reason = resolution.Reason ?? "Stage 6 bootstrap request was invalid."
                }
            });
        }

        return JsonSerializer.Serialize(new
        {
            result_status = requestedStatus,
            facts = Array.Empty<object>(),
            inferences = Array.Empty<object>(),
            hypotheses = Array.Empty<object>(),
            conflicts = Array.Empty<object>()
        });
    }

    private static string BuildTruthSummary(Stage6BootstrapScopeResolution resolution)
    {
        if (string.Equals(resolution.ResolutionStatus, Stage6BootstrapStatuses.Ready, StringComparison.Ordinal)
            && resolution.TrackedPerson != null
            && resolution.OperatorPerson != null)
        {
            return $"Bootstrap graph init resolved tracked person '{resolution.TrackedPerson.DisplayName}' with operator root '{resolution.OperatorPerson.DisplayName}' from {resolution.EvidenceCount} evidence rows.";
        }

        return resolution.Reason ?? "Bootstrap graph init could not resolve a ready scope.";
    }

    private static string BuildOutputSummary(string resultStatus, Stage6BootstrapScopeResolution resolution)
    {
        return resultStatus switch
        {
            ModelPassResultStatuses.ResultReady => "Stage 6 bootstrap graph initialization produced a tracked-person attachment seed.",
            ModelPassResultStatuses.NeedMoreData => resolution.Reason ?? "Stage 6 bootstrap needs more substrate coverage.",
            ModelPassResultStatuses.NeedOperatorClarification => resolution.Reason ?? "Stage 6 bootstrap needs operator clarification before graph initialization.",
            _ => resolution.Reason ?? "Stage 6 bootstrap request was blocked."
        };
    }

    private static string ResolveResultStatus(string resolutionStatus)
    {
        return resolutionStatus switch
        {
            Stage6BootstrapStatuses.Ready => ModelPassResultStatuses.ResultReady,
            Stage6BootstrapStatuses.NeedMoreData => ModelPassResultStatuses.NeedMoreData,
            Stage6BootstrapStatuses.NeedOperatorClarification => ModelPassResultStatuses.NeedOperatorClarification,
            _ => ModelPassResultStatuses.BlockedInvalidInput
        };
    }

    private static float NormalizeConfidence(int evidenceCount)
        => evidenceCount switch
        {
            <= 1 => 0.60f,
            2 => 0.75f,
            3 => 0.85f,
            _ => 0.95f
        };
}

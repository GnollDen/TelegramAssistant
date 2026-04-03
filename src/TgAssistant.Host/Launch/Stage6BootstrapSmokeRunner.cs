using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage6Bootstrap;

namespace TgAssistant.Host.Launch;

public static class Stage6BootstrapSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var repository = new InMemoryStage6BootstrapRepository();
        var auditStore = new InMemoryModelPassAuditStore();
        var auditService = new ModelPassAuditService(new ModelOutputNormalizer(), auditStore);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new Stage6BootstrapService(
            repository,
            auditService,
            loggerFactory.CreateLogger<Stage6BootstrapService>());

        var successRequest = new Stage6BootstrapRequest
        {
            PersonId = InMemoryStage6BootstrapRepository.SuccessTrackedPersonId,
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-005-a"
        };

        var firstSuccess = await service.RunGraphInitializationAsync(successRequest, ct);
        AssertReady(firstSuccess, "first success");
        var secondSuccess = await service.RunGraphInitializationAsync(successRequest, ct);
        AssertReady(secondSuccess, "second success");
        if (firstSuccess.Nodes.Count != secondSuccess.Nodes.Count
            || firstSuccess.Edges.Count != secondSuccess.Edges.Count
            || firstSuccess.Nodes[0].Id != secondSuccess.Nodes[0].Id
            || firstSuccess.Nodes[1].Id != secondSuccess.Nodes[1].Id
            || firstSuccess.Edges[0].Id != secondSuccess.Edges[0].Id)
        {
            throw new InvalidOperationException("Stage6 bootstrap smoke failed: rerun changed non-durable graph seed identities.");
        }

        var needMoreData = await service.RunGraphInitializationAsync(new Stage6BootstrapRequest
        {
            ScopeKey = InMemoryStage6BootstrapRepository.MissingOperatorScopeKey,
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-005-a-missing-operator"
        }, ct);
        if (!string.Equals(needMoreData.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
            || needMoreData.GraphInitialized
            || needMoreData.Nodes.Count != 0
            || needMoreData.Edges.Count != 0)
        {
            throw new InvalidOperationException("Stage6 bootstrap smoke failed: missing operator attachment was not surfaced as need_more_data.");
        }
    }

    private static void AssertReady(Stage6BootstrapGraphResult result, string label)
    {
        if (!result.GraphInitialized
            || !string.Equals(result.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            || result.Nodes.Count != 2
            || result.Edges.Count != 1)
        {
            throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} did not produce the expected graph seed.");
        }
    }

    private sealed class InMemoryStage6BootstrapRepository : IStage6BootstrapRepository
    {
        internal static readonly Guid SuccessTrackedPersonId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        internal const string SuccessScopeKey = "chat:bootstrap-smoke-success";
        internal const string MissingOperatorScopeKey = "chat:bootstrap-smoke-missing-operator";

        private readonly Dictionary<string, Stage6BootstrapGraphNode> _nodes = [];
        private readonly Dictionary<string, Stage6BootstrapGraphEdge> _edges = [];

        public Task<Stage6BootstrapScopeResolution> ResolveScopeAsync(Stage6BootstrapRequest request, CancellationToken ct = default)
        {
            if (request.PersonId == SuccessTrackedPersonId)
            {
                return Task.FromResult(new Stage6BootstrapScopeResolution
                {
                    ResolutionStatus = Stage6BootstrapStatuses.Ready,
                    ScopeKey = SuccessScopeKey,
                    TrackedPerson = new Stage6BootstrapPersonRef
                    {
                        PersonId = SuccessTrackedPersonId,
                        ScopeKey = SuccessScopeKey,
                        PersonType = "tracked_person",
                        DisplayName = "Tracked Smoke Person",
                        CanonicalName = "tracked smoke person"
                    },
                    OperatorPerson = new Stage6BootstrapPersonRef
                    {
                        PersonId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                        ScopeKey = SuccessScopeKey,
                        PersonType = "operator_root",
                        DisplayName = "Operator Smoke Root",
                        CanonicalName = "operator smoke root"
                    },
                    EvidenceCount = 3,
                    LatestEvidenceAtUtc = DateTime.UtcNow,
                    SourceRefs =
                    [
                        new Stage6BootstrapSourceRef
                        {
                            SourceType = "telegram_realtime_message",
                            SourceRef = "smoke:source-1",
                            SourceObjectId = Guid.Parse("21000000-0000-0000-0000-000000000001"),
                            EvidenceItemId = Guid.Parse("22000000-0000-0000-0000-000000000001"),
                            ObservedAtUtc = DateTime.UtcNow
                        }
                    ]
                });
            }

            if (string.Equals(request.ScopeKey, MissingOperatorScopeKey, StringComparison.Ordinal))
            {
                return Task.FromResult(new Stage6BootstrapScopeResolution
                {
                    ResolutionStatus = Stage6BootstrapStatuses.NeedMoreData,
                    ScopeKey = MissingOperatorScopeKey,
                    TrackedPerson = new Stage6BootstrapPersonRef
                    {
                        PersonId = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                        ScopeKey = MissingOperatorScopeKey,
                        PersonType = "tracked_person",
                        DisplayName = "Detached Smoke Person",
                        CanonicalName = "detached smoke person"
                    },
                    Reason = "Tracked person is not attached to an operator root yet.",
                    Unknowns =
                    [
                        new ModelPassUnknown
                        {
                            UnknownType = "operator_attachment_missing",
                            Summary = "Tracked person has no operator root attachment.",
                            RequiredAction = "attach_operator_root"
                        }
                    ]
                });
            }

            return Task.FromResult(new Stage6BootstrapScopeResolution
            {
                ResolutionStatus = Stage6BootstrapStatuses.BlockedInvalidInput,
                ScopeKey = request.ScopeKey ?? "stage6_bootstrap:smoke",
                Reason = "Smoke repository received an unsupported bootstrap request."
            });
        }

        public Task<Stage6BootstrapGraphResult> UpsertGraphInitializationAsync(
            ModelPassAuditRecord auditRecord,
            Stage6BootstrapScopeResolution resolution,
            CancellationToken ct = default)
        {
            var operatorNode = UpsertNode(
                resolution.ScopeKey,
                resolution.OperatorPerson!,
                Stage6BootstrapNodeTypes.OperatorRoot,
                auditRecord.ModelPassRunId);
            var trackedNode = UpsertNode(
                resolution.ScopeKey,
                resolution.TrackedPerson!,
                Stage6BootstrapNodeTypes.TrackedPersonSeed,
                auditRecord.ModelPassRunId);

            var edgeKey = $"{resolution.ScopeKey}|{operatorNode.NodeRef}|{trackedNode.NodeRef}|{Stage6BootstrapEdgeTypes.TrackedPersonAttachment}";
            if (!_edges.TryGetValue(edgeKey, out var edge))
            {
                edge = new Stage6BootstrapGraphEdge
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = resolution.ScopeKey,
                    FromNodeRef = operatorNode.NodeRef,
                    ToNodeRef = trackedNode.NodeRef,
                    EdgeType = Stage6BootstrapEdgeTypes.TrackedPersonAttachment,
                    Status = "active"
                };
                _edges[edgeKey] = edge;
            }

            edge.LastModelPassRunId = auditRecord.ModelPassRunId;
            edge.PayloadJson = $"{{\"tracked_person_ref\":\"{trackedNode.NodeRef}\"}}";

            return Task.FromResult(new Stage6BootstrapGraphResult
            {
                AuditRecord = auditRecord,
                GraphInitialized = true,
                Nodes = [operatorNode, trackedNode],
                Edges = [edge]
            });
        }

        private Stage6BootstrapGraphNode UpsertNode(
            string scopeKey,
            Stage6BootstrapPersonRef person,
            string nodeType,
            Guid modelPassRunId)
        {
            var key = $"{scopeKey}|{nodeType}|{person.PersonRef}";
            if (!_nodes.TryGetValue(key, out var node))
            {
                node = new Stage6BootstrapGraphNode
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    PersonId = person.PersonId,
                    NodeType = nodeType,
                    NodeRef = person.PersonRef,
                    Status = "active"
                };
                _nodes[key] = node;
            }

            node.LastModelPassRunId = modelPassRunId;
            node.PayloadJson = $"{{\"person_ref\":\"{person.PersonRef}\"}}";
            return node;
        }
    }

    private sealed class InMemoryModelPassAuditStore : IModelPassAuditStore
    {
        private readonly Dictionary<Guid, ModelPassAuditRecord> _records = [];

        public Task<ModelPassAuditRecord> UpsertAsync(
            ModelPassEnvelope envelope,
            ModelNormalizationResult normalizationResult,
            CancellationToken ct = default)
        {
            var record = new ModelPassAuditRecord
            {
                ModelPassRunId = envelope.RunId,
                NormalizationRunId = _records.TryGetValue(envelope.RunId, out var existing)
                    ? existing.NormalizationRunId
                    : Guid.NewGuid(),
                Envelope = envelope,
                Normalization = normalizationResult
            };
            _records[envelope.RunId] = record;
            return Task.FromResult(record);
        }

        public Task<ModelPassAuditRecord?> GetByModelPassRunIdAsync(Guid runId, CancellationToken ct = default)
        {
            _records.TryGetValue(runId, out var record);
            return Task.FromResult(record);
        }
    }
}

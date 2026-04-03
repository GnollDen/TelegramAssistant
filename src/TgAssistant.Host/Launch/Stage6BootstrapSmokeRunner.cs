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
            TriggerRef = "implement-005-b"
        };

        var firstSuccess = await service.RunGraphInitializationAsync(successRequest, ct);
        AssertReady(firstSuccess, "first success");
        AssertDiscoveryOutputs(firstSuccess, "first success");
        AssertPoolOutputs(firstSuccess, "first success");
        var secondSuccess = await service.RunGraphInitializationAsync(successRequest, ct);
        AssertReady(secondSuccess, "second success");
        AssertDiscoveryOutputs(secondSuccess, "second success");
        AssertPoolOutputs(secondSuccess, "second success");
        if (firstSuccess.Nodes.Count != secondSuccess.Nodes.Count
            || firstSuccess.Edges.Count != secondSuccess.Edges.Count
            || firstSuccess.Nodes[0].Id != secondSuccess.Nodes[0].Id
            || firstSuccess.Nodes[1].Id != secondSuccess.Nodes[1].Id
            || firstSuccess.Edges[0].Id != secondSuccess.Edges[0].Id)
        {
            throw new InvalidOperationException("Stage6 bootstrap smoke failed: rerun changed non-durable graph seed identities.");
        }
        AssertDiscoveryIdempotency(firstSuccess, secondSuccess);
        AssertPoolOutputIdempotency(firstSuccess, secondSuccess);

        var needMoreData = await service.RunGraphInitializationAsync(new Stage6BootstrapRequest
        {
            ScopeKey = InMemoryStage6BootstrapRepository.MissingOperatorScopeKey,
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-005-b-missing-operator"
        }, ct);
        if (!string.Equals(needMoreData.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
            || needMoreData.GraphInitialized
            || needMoreData.Nodes.Count != 0
            || needMoreData.Edges.Count != 0
            || needMoreData.DiscoveryOutputs.Count != 0
            || needMoreData.AmbiguityOutputs.Count != 0
            || needMoreData.ContradictionOutputs.Count != 0
            || needMoreData.SliceOutputs.Count != 0)
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

    private static void AssertDiscoveryOutputs(Stage6BootstrapGraphResult result, string label)
    {
        if (result.DiscoveryOutputs.Count != 3)
        {
            throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} did not produce three differentiated discovery outputs.");
        }

        var discoveryTypes = result.DiscoveryOutputs
            .Select(x => x.DiscoveryType)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var expectedTypes = new[]
        {
            Stage6BootstrapDiscoveryTypes.CandidateIdentity,
            Stage6BootstrapDiscoveryTypes.LinkedPerson,
            Stage6BootstrapDiscoveryTypes.Mention
        };
        if (!discoveryTypes.SequenceEqual(expectedTypes, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} did not differentiate linked_person, candidate_identity, and mention outputs.");
        }
    }

    private static void AssertPoolOutputs(Stage6BootstrapGraphResult result, string label)
    {
        if (result.AmbiguityOutputs.Count != 1
            || result.ContradictionOutputs.Count != 1
            || result.SliceOutputs.Count != 1)
        {
            throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} did not produce the expected ambiguity, contradiction, and slice outputs.");
        }

        if (!string.Equals(result.AmbiguityOutputs[0].OutputType, Stage6BootstrapPoolOutputTypes.AmbiguityPool, StringComparison.Ordinal)
            || !string.Equals(result.ContradictionOutputs[0].OutputType, Stage6BootstrapPoolOutputTypes.ContradictionPool, StringComparison.Ordinal)
            || !string.Equals(result.SliceOutputs[0].OutputType, Stage6BootstrapPoolOutputTypes.BootstrapSlice, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} returned the wrong pool output types.");
        }
    }

    private static void AssertDiscoveryIdempotency(Stage6BootstrapGraphResult first, Stage6BootstrapGraphResult second)
    {
        var firstMap = first.DiscoveryOutputs.ToDictionary(x => $"{x.DiscoveryType}|{x.DiscoveryKey}", x => x.Id);
        var secondMap = second.DiscoveryOutputs.ToDictionary(x => $"{x.DiscoveryType}|{x.DiscoveryKey}", x => x.Id);
        if (firstMap.Count != secondMap.Count)
        {
            throw new InvalidOperationException("Stage6 bootstrap smoke failed: discovery output count changed on rerun.");
        }

        foreach (var (key, id) in firstMap)
        {
            if (!secondMap.TryGetValue(key, out var rerunId) || rerunId != id)
            {
                throw new InvalidOperationException("Stage6 bootstrap smoke failed: discovery output IDs changed on rerun.");
            }
        }
    }

    private static void AssertPoolOutputIdempotency(Stage6BootstrapGraphResult first, Stage6BootstrapGraphResult second)
    {
        AssertPoolIdempotency("ambiguity", first.AmbiguityOutputs, second.AmbiguityOutputs);
        AssertPoolIdempotency("contradiction", first.ContradictionOutputs, second.ContradictionOutputs);
        AssertPoolIdempotency("slice", first.SliceOutputs, second.SliceOutputs);
    }

    private static void AssertPoolIdempotency(
        string label,
        IReadOnlyCollection<Stage6BootstrapPoolOutput> first,
        IReadOnlyCollection<Stage6BootstrapPoolOutput> second)
    {
        var firstMap = first.ToDictionary(x => $"{x.OutputType}|{x.OutputKey}", x => x.Id);
        var secondMap = second.ToDictionary(x => $"{x.OutputType}|{x.OutputKey}", x => x.Id);
        if (firstMap.Count != secondMap.Count)
        {
            throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} output count changed on rerun.");
        }

        foreach (var (key, id) in firstMap)
        {
            if (!secondMap.TryGetValue(key, out var rerunId) || rerunId != id)
            {
                throw new InvalidOperationException($"Stage6 bootstrap smoke failed: {label} output IDs changed on rerun.");
            }
        }
    }

    private sealed class InMemoryStage6BootstrapRepository : IStage6BootstrapRepository
    {
        internal static readonly Guid SuccessTrackedPersonId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        internal const string SuccessScopeKey = "chat:bootstrap-smoke-success";
        internal const string MissingOperatorScopeKey = "chat:bootstrap-smoke-missing-operator";

        private readonly Dictionary<string, Stage6BootstrapGraphNode> _nodes = [];
        private readonly Dictionary<string, Stage6BootstrapGraphEdge> _edges = [];
        private readonly Dictionary<string, Stage6BootstrapDiscoveryOutput> _discoveryOutputs = [];
        private readonly Dictionary<string, Stage6BootstrapPoolOutput> _poolOutputs = [];

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
                            SourceMessageId = 11_001,
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
                ScopeKey = resolution.ScopeKey,
                TrackedPerson = resolution.TrackedPerson,
                OperatorPerson = resolution.OperatorPerson,
                EvidenceCount = resolution.EvidenceCount,
                LatestEvidenceAtUtc = resolution.LatestEvidenceAtUtc,
                Nodes = [operatorNode, trackedNode],
                Edges = [edge]
            });
        }

        public Task<List<Stage6BootstrapDiscoveryOutput>> UpsertDiscoveryOutputsAsync(
            ModelPassAuditRecord auditRecord,
            Stage6BootstrapScopeResolution resolution,
            CancellationToken ct = default)
        {
            if (resolution.TrackedPerson == null)
            {
                return Task.FromResult(new List<Stage6BootstrapDiscoveryOutput>());
            }

            var linkedPerson = UpsertDiscoveryOutput(
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapDiscoveryTypes.LinkedPerson,
                "person:20000000-0000-0000-0000-000000000010",
                personId: Guid.Parse("20000000-0000-0000-0000-000000000010"),
                payloadJson: "{\"discovery_type\":\"linked_person\"}");
            var candidateIdentity = UpsertDiscoveryOutput(
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapDiscoveryTypes.CandidateIdentity,
                "candidate_identity:23000000-0000-0000-0000-000000000001",
                candidateIdentityStateId: Guid.Parse("23000000-0000-0000-0000-000000000001"),
                sourceMessageId: 11_001,
                payloadJson: "{\"discovery_type\":\"candidate_identity\"}");
            var mention = UpsertDiscoveryOutput(
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapDiscoveryTypes.Mention,
                "mention:23000000-0000-0000-0000-000000000002",
                candidateIdentityStateId: Guid.Parse("23000000-0000-0000-0000-000000000002"),
                sourceMessageId: 11_001,
                payloadJson: "{\"discovery_type\":\"mention\"}");

            return Task.FromResult(new List<Stage6BootstrapDiscoveryOutput>
            {
                candidateIdentity,
                linkedPerson,
                mention
            });
        }

        public Task<Stage6BootstrapPoolOutputSet> UpsertPoolOutputsAsync(
            ModelPassAuditRecord auditRecord,
            Stage6BootstrapScopeResolution resolution,
            CancellationToken ct = default)
        {
            if (resolution.TrackedPerson == null)
            {
                return Task.FromResult(new Stage6BootstrapPoolOutputSet());
            }

            var ambiguity = UpsertPoolOutput(
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapPoolOutputTypes.AmbiguityPool,
                "source_message:11001",
                sourceMessageId: 11_001,
                payloadJson: "{\"output_type\":\"ambiguity_pool\"}");
            var contradiction = UpsertPoolOutput(
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapPoolOutputTypes.ContradictionPool,
                "source_message:11001:anchor_type:conversation_partner",
                sourceMessageId: 11_001,
                payloadJson: "{\"output_type\":\"contradiction_pool\"}");
            var slice = UpsertPoolOutput(
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapPoolOutputTypes.BootstrapSlice,
                "day:20260403",
                sourceMessageId: 11_001,
                payloadJson: "{\"output_type\":\"bootstrap_slice\"}");

            return Task.FromResult(new Stage6BootstrapPoolOutputSet
            {
                AmbiguityOutputs = [ambiguity],
                ContradictionOutputs = [contradiction],
                SliceOutputs = [slice]
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

        private Stage6BootstrapDiscoveryOutput UpsertDiscoveryOutput(
            string scopeKey,
            Guid trackedPersonId,
            Guid modelPassRunId,
            string discoveryType,
            string discoveryKey,
            string payloadJson,
            Guid? personId = null,
            Guid? candidateIdentityStateId = null,
            long? sourceMessageId = null)
        {
            var key = $"{scopeKey}|{trackedPersonId:D}|{discoveryType}|{discoveryKey}";
            if (!_discoveryOutputs.TryGetValue(key, out var output))
            {
                output = new Stage6BootstrapDiscoveryOutput
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    TrackedPersonId = trackedPersonId,
                    DiscoveryType = discoveryType,
                    DiscoveryKey = discoveryKey,
                    Status = "active"
                };
                _discoveryOutputs[key] = output;
            }

            output.LastModelPassRunId = modelPassRunId;
            output.PersonId = personId;
            output.CandidateIdentityStateId = candidateIdentityStateId;
            output.SourceMessageId = sourceMessageId;
            output.PayloadJson = payloadJson;
            return output;
        }

        private Stage6BootstrapPoolOutput UpsertPoolOutput(
            string scopeKey,
            Guid trackedPersonId,
            Guid modelPassRunId,
            string outputType,
            string outputKey,
            string payloadJson,
            long? sourceMessageId = null)
        {
            var key = $"{scopeKey}|{trackedPersonId:D}|{outputType}|{outputKey}";
            if (!_poolOutputs.TryGetValue(key, out var output))
            {
                output = new Stage6BootstrapPoolOutput
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = scopeKey,
                    TrackedPersonId = trackedPersonId,
                    OutputType = outputType,
                    OutputKey = outputKey,
                    Status = "active"
                };
                _poolOutputs[key] = output;
            }

            output.LastModelPassRunId = modelPassRunId;
            output.SourceMessageId = sourceMessageId;
            output.PayloadJson = payloadJson;
            return output;
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

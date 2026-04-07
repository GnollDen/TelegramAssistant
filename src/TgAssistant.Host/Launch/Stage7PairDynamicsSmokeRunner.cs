using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage7Formation;

namespace TgAssistant.Host.Launch;

public static class Stage7PairDynamicsSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var repository = new InMemoryStage7PairDynamicsRepository();
        var auditStore = new InMemoryModelPassAuditStore();
        var auditService = new ModelPassAuditService(new ModelOutputNormalizer(), auditStore);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new Stage7PairDynamicsFormationService(
            repository,
            auditService,
            loggerFactory.CreateLogger<Stage7PairDynamicsFormationService>());

        var successRequest = new Stage7PairDynamicsFormationRequest
        {
            BootstrapResult = BuildReadyBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-b"
        };

        var firstResult = await service.FormAsync(successRequest, ct);
        AssertReady(firstResult, expectedRevisionNumber: 1, "first success");

        var secondResult = await service.FormAsync(successRequest, ct);
        AssertReady(secondResult, expectedRevisionNumber: 1, "second success");
        if (firstResult.PairDynamics!.Id != secondResult.PairDynamics!.Id
            || firstResult.CurrentRevision!.Id != secondResult.CurrentRevision!.Id)
        {
            throw new InvalidOperationException("Stage7 pair-dynamics smoke failed: rerun changed pair-dynamics or revision identity for identical input.");
        }

        var changedResult = await service.FormAsync(new Stage7PairDynamicsFormationRequest
        {
            BootstrapResult = BuildChangedBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-b-changed"
        }, ct);
        AssertReady(changedResult, expectedRevisionNumber: 2, "changed success");
        if (changedResult.PairDynamics!.Id != firstResult.PairDynamics!.Id
            || changedResult.CurrentRevision!.Id == firstResult.CurrentRevision!.Id)
        {
            throw new InvalidOperationException("Stage7 pair-dynamics smoke failed: changed pair-dynamics input did not create a new revision on the same durable object.");
        }

        var needMoreData = await service.FormAsync(new Stage7PairDynamicsFormationRequest
        {
            BootstrapResult = BuildNeedMoreDataBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-b-missing-bootstrap"
        }, ct);
        if (needMoreData.Formed
            || needMoreData.PairDynamics != null
            || needMoreData.CurrentRevision != null
            || !string.Equals(needMoreData.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage7 pair-dynamics smoke failed: need_more_data bootstrap should not materialize durable pair-dynamics outputs.");
        }
    }

    private static void AssertReady(Stage7PairDynamicsFormationResult result, int expectedRevisionNumber, string label)
    {
        if (!result.Formed
            || result.PairDynamics == null
            || result.CurrentRevision == null
            || result.EvidenceItemIds.Count == 0
            || !string.Equals(result.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Stage7 pair-dynamics smoke failed: {label} did not materialize the expected durable output.");
        }

        if (result.CurrentRevision.RevisionNumber != expectedRevisionNumber
            || result.PairDynamics.CurrentRevisionNumber != expectedRevisionNumber)
        {
            throw new InvalidOperationException($"Stage7 pair-dynamics smoke failed: {label} did not preserve the expected revision number.");
        }
    }

    private static Stage6BootstrapGraphResult BuildReadyBootstrapResult()
    {
        var result = Stage7DossierProfileSmokeRunnerAccessor.BuildReadyBootstrapResult();
        result.ScopeKey = "chat:stage7-pair-dynamics-smoke";
        result.TrackedPerson!.ScopeKey = result.ScopeKey;
        result.OperatorPerson!.ScopeKey = result.ScopeKey;
        return result;
    }

    private static Stage6BootstrapGraphResult BuildChangedBootstrapResult()
    {
        var result = BuildReadyBootstrapResult();
        result.ContradictionOutputs =
        [
            .. result.ContradictionOutputs,
            new Stage6BootstrapPoolOutput
            {
                Id = Guid.Parse("47000000-0000-0000-0000-000000000004"),
                ScopeKey = result.ScopeKey,
                TrackedPersonId = result.TrackedPerson!.PersonId,
                OutputType = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                OutputKey = "contradiction:2",
                RelationshipEdgeAnchorId = Guid.Parse("48000000-0000-0000-0000-000000000002"),
                Status = "active"
            }
        ];
        result.AuditRecord.Envelope.OutputSummary.Summary = "Changed contradiction pressure for pair smoke.";
        return result;
    }

    private static Stage6BootstrapGraphResult BuildNeedMoreDataBootstrapResult()
    {
        return Stage7DossierProfileSmokeRunnerAccessor.BuildNeedMoreDataBootstrapResult();
    }

    private sealed class InMemoryStage7PairDynamicsRepository : IStage7PairDynamicsRepository
    {
        private readonly Dictionary<string, Guid> _metadataIds = [];
        private readonly Dictionary<string, Stage7DurablePairDynamics> _pairs = [];
        private readonly Dictionary<Guid, List<Stage7DurablePairDynamicsRevision>> _revisions = [];

        public Task<Stage7PairDynamicsFormationResult> UpsertAsync(
            ModelPassAuditRecord auditRecord,
            Stage6BootstrapGraphResult bootstrapResult,
            CancellationToken ct = default)
        {
            var trackedPerson = bootstrapResult.TrackedPerson!;
            var operatorPerson = bootstrapResult.OperatorPerson!;
            var pairKey = $"{bootstrapResult.ScopeKey}|{operatorPerson.PersonId:D}|{trackedPerson.PersonId:D}|{Stage7PairDynamicsTypes.OperatorTrackedPair}";
            var metadataKey = $"{Stage7DurableObjectFamilies.PairDynamics}|{operatorPerson.PersonId:D}|{trackedPerson.PersonId:D}";
            if (!_metadataIds.TryGetValue(metadataKey, out var metadataId))
            {
                metadataId = Guid.NewGuid();
                _metadataIds[metadataKey] = metadataId;
            }

            if (!_pairs.TryGetValue(pairKey, out var pair))
            {
                pair = new Stage7DurablePairDynamics
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = bootstrapResult.ScopeKey,
                    LeftPersonId = operatorPerson.PersonId,
                    RightPersonId = trackedPerson.PersonId,
                    PairDynamicsType = Stage7PairDynamicsTypes.OperatorTrackedPair
                };
                _pairs[pairKey] = pair;
            }

            pair.DurableObjectMetadataId = metadataId;
            pair.LastModelPassRunId = auditRecord.ModelPassRunId;
            pair.Status = "active";

            if (!_revisions.TryGetValue(pair.Id, out var revisions))
            {
                revisions = [];
                _revisions[pair.Id] = revisions;
            }

            var revisionHash = $"{bootstrapResult.ContradictionOutputs.Count}:{bootstrapResult.AmbiguityOutputs.Count}:{bootstrapResult.EvidenceCount}";
            var revision = revisions.FirstOrDefault(x => string.Equals(x.RevisionHash, revisionHash, StringComparison.Ordinal));
            if (revision == null)
            {
                revision = new Stage7DurablePairDynamicsRevision
                {
                    Id = Guid.NewGuid(),
                    DurablePairDynamicsId = pair.Id,
                    RevisionNumber = revisions.Count + 1,
                    RevisionHash = revisionHash,
                    ModelPassRunId = auditRecord.ModelPassRunId,
                    Confidence = 0.70f,
                    Freshness = 1.0f,
                    Stability = bootstrapResult.ContradictionOutputs.Count == 0 ? 1.0f : 0.70f,
                    ContradictionMarkersJson = "{}",
                    SummaryJson = "{\"family\":\"pair_dynamics\"}",
                    PayloadJson = "{\"dimensions\":[\"initiative_balance\",\"response_rhythm\"]}",
                    CreatedAt = DateTime.UtcNow
                };
                revisions.Add(revision);
            }

            pair.CurrentRevisionNumber = revision.RevisionNumber;
            pair.CurrentRevisionHash = revision.RevisionHash;
            pair.SummaryJson = revision.SummaryJson;
            pair.PayloadJson = revision.PayloadJson;

            return Task.FromResult(new Stage7PairDynamicsFormationResult
            {
                AuditRecord = auditRecord,
                Formed = true,
                TrackedPerson = trackedPerson,
                OperatorPerson = operatorPerson,
                PairDynamics = pair,
                CurrentRevision = revision,
                EvidenceItemIds =
                [
                    bootstrapResult.AuditRecord.Envelope.EvidenceItemId!.Value
                ]
            });
        }

        public Task<CurrentWorldPairDynamicsReadSurface?> GetCurrentWorldReadSurfaceAsync(
            string scopeKey,
            Guid trackedPersonId,
            CancellationToken ct = default)
        {
            return Task.FromResult<CurrentWorldPairDynamicsReadSurface?>(null);
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
                NormalizationRunId = Guid.NewGuid(),
                Envelope = envelope,
                Normalization = normalizationResult
            };
            _records[record.ModelPassRunId] = record;
            return Task.FromResult(record);
        }

        public Task<ModelPassAuditRecord?> GetByModelPassRunIdAsync(Guid runId, CancellationToken ct = default)
        {
            _records.TryGetValue(runId, out var record);
            return Task.FromResult(record);
        }

        public Task<int> GetConsecutiveNeedMoreDataCountAsync(
            string scopeKey,
            string stage,
            string passFamily,
            CancellationToken ct = default)
        {
            var count = _records.Values
                .Where(x => string.Equals(x.Envelope.ScopeKey, scopeKey, StringComparison.Ordinal)
                    && string.Equals(x.Envelope.Stage, stage, StringComparison.Ordinal)
                    && string.Equals(x.Envelope.PassFamily, passFamily, StringComparison.Ordinal))
                .OrderByDescending(x => x.Envelope.StartedAtUtc)
                .ThenByDescending(x => x.Envelope.RunId)
                .Take(32)
                .TakeWhile(x => string.Equals(x.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
                .Count();
            return Task.FromResult(count);
        }
    }

    // Reuse the same synthetic bootstrap shape used by the dossier/profile smoke without introducing a shared production dependency.
    private static class Stage7DossierProfileSmokeRunnerAccessor
    {
        public static Stage6BootstrapGraphResult BuildReadyBootstrapResult()
        {
            var trackedPerson = new Stage6BootstrapPersonRef
            {
                PersonId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
                ScopeKey = "chat:stage7-pair-dynamics-smoke",
                PersonType = "tracked_person",
                DisplayName = "Durable Smoke Person",
                CanonicalName = "durable smoke person"
            };
            var operatorPerson = new Stage6BootstrapPersonRef
            {
                PersonId = Guid.Parse("30000000-0000-0000-0000-000000000002"),
                ScopeKey = "chat:stage7-pair-dynamics-smoke",
                PersonType = "operator_root",
                DisplayName = "Operator Smoke Root",
                CanonicalName = "operator smoke root"
            };
            var evidenceId = Guid.Parse("32000000-0000-0000-0000-000000000001");
            var sourceObjectId = Guid.Parse("31000000-0000-0000-0000-000000000001");
            var now = DateTime.UtcNow;

            return new Stage6BootstrapGraphResult
            {
                AuditRecord = new ModelPassAuditRecord
                {
                    ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000101"),
                    NormalizationRunId = Guid.Parse("34000000-0000-0000-0000-000000000101"),
                    Envelope = new ModelPassEnvelope
                    {
                        RunId = Guid.Parse("33000000-0000-0000-0000-000000000101"),
                        Stage = "stage6_bootstrap",
                        PassFamily = "graph_init",
                        RunKind = "smoke",
                        ScopeKey = trackedPerson.ScopeKey,
                        Scope = new ModelPassScope
                        {
                            ScopeType = "person_scope",
                            ScopeRef = trackedPerson.PersonRef,
                            AdditionalRefs = [operatorPerson.PersonRef]
                        },
                        Target = new ModelPassTarget
                        {
                            TargetType = "person",
                            TargetRef = trackedPerson.PersonRef
                        },
                        PersonId = trackedPerson.PersonId,
                        SourceObjectId = sourceObjectId,
                        EvidenceItemId = evidenceId,
                        SourceRefs =
                        [
                            new ModelPassSourceRef
                            {
                                SourceType = "telegram_realtime_message",
                                SourceRef = "smoke:source-1",
                                SourceObjectId = sourceObjectId,
                                EvidenceItemId = evidenceId
                            }
                        ],
                        TruthSummary = new ModelPassTruthSummary
                        {
                            TruthLayer = ModelNormalizationTruthLayers.CanonicalTruth,
                            Summary = "Smoke bootstrap summary.",
                            CanonicalRefs = [$"evidence:{evidenceId:D}"]
                        },
                        Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                            ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_init")),
                        ResultStatus = ModelPassResultStatuses.ResultReady,
                        OutputSummary = new ModelPassOutputSummary
                        {
                            Summary = "Smoke Stage6 bootstrap completed."
                        },
                        StartedAtUtc = now,
                        FinishedAtUtc = now
                    },
                    Normalization = new ModelNormalizationResult
                    {
                        ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000101"),
                        ScopeKey = trackedPerson.ScopeKey,
                        TargetType = "person",
                        TargetRef = trackedPerson.PersonRef,
                        TruthLayer = ModelNormalizationTruthLayers.ProposalLayer,
                        PersonId = trackedPerson.PersonId,
                        SourceObjectId = sourceObjectId,
                        EvidenceItemId = evidenceId,
                        Status = ModelPassResultStatuses.ResultReady
                    }
                },
                GraphInitialized = true,
                ScopeKey = trackedPerson.ScopeKey,
                TrackedPerson = trackedPerson,
                OperatorPerson = operatorPerson,
                EvidenceCount = 3,
                LatestEvidenceAtUtc = now,
                DiscoveryOutputs =
                [
                    new Stage6BootstrapDiscoveryOutput
                    {
                        Id = Guid.Parse("35000000-0000-0000-0000-000000000011"),
                        ScopeKey = trackedPerson.ScopeKey,
                        TrackedPersonId = trackedPerson.PersonId,
                        DiscoveryType = Stage6BootstrapDiscoveryTypes.LinkedPerson,
                        DiscoveryKey = "linked:1",
                        PersonId = Guid.Parse("30000000-0000-0000-0000-000000000010"),
                        Status = "active"
                    }
                ],
                AmbiguityOutputs =
                [
                    new Stage6BootstrapPoolOutput
                    {
                        Id = Guid.Parse("37000000-0000-0000-0000-000000000011"),
                        ScopeKey = trackedPerson.ScopeKey,
                        TrackedPersonId = trackedPerson.PersonId,
                        OutputType = Stage6BootstrapPoolOutputTypes.AmbiguityPool,
                        OutputKey = "ambiguity:1",
                        Status = "active"
                    }
                ],
                ContradictionOutputs =
                [
                    new Stage6BootstrapPoolOutput
                    {
                        Id = Guid.Parse("37000000-0000-0000-0000-000000000012"),
                        ScopeKey = trackedPerson.ScopeKey,
                        TrackedPersonId = trackedPerson.PersonId,
                        OutputType = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                        OutputKey = "contradiction:1",
                        RelationshipEdgeAnchorId = Guid.Parse("38000000-0000-0000-0000-000000000001"),
                        Status = "active"
                    }
                ],
                SliceOutputs =
                [
                    new Stage6BootstrapPoolOutput
                    {
                        Id = Guid.Parse("37000000-0000-0000-0000-000000000013"),
                        ScopeKey = trackedPerson.ScopeKey,
                        TrackedPersonId = trackedPerson.PersonId,
                        OutputType = Stage6BootstrapPoolOutputTypes.BootstrapSlice,
                        OutputKey = "slice:1",
                        Status = "active"
                    }
                ]
            };
        }

        public static Stage6BootstrapGraphResult BuildNeedMoreDataBootstrapResult()
        {
            var trackedPerson = new Stage6BootstrapPersonRef
            {
                PersonId = Guid.Parse("30000000-0000-0000-0000-000000000111"),
                ScopeKey = "chat:stage7-pair-dynamics-missing",
                PersonType = "tracked_person",
                DisplayName = "Detached Durable Smoke Person",
                CanonicalName = "detached durable smoke person"
            };

            return new Stage6BootstrapGraphResult
            {
                AuditRecord = new ModelPassAuditRecord
                {
                    ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000102"),
                    NormalizationRunId = Guid.Parse("34000000-0000-0000-0000-000000000102"),
                    Envelope = new ModelPassEnvelope
                    {
                        RunId = Guid.Parse("33000000-0000-0000-0000-000000000102"),
                        Stage = "stage6_bootstrap",
                        PassFamily = "graph_init",
                        RunKind = "smoke",
                        ScopeKey = trackedPerson.ScopeKey,
                        Scope = new ModelPassScope
                        {
                            ScopeType = "person_scope",
                            ScopeRef = trackedPerson.PersonRef
                        },
                        Target = new ModelPassTarget
                        {
                            TargetType = "person",
                            TargetRef = trackedPerson.PersonRef
                        },
                        PersonId = trackedPerson.PersonId,
                        Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                            ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_init")),
                        ResultStatus = ModelPassResultStatuses.NeedMoreData,
                        OutputSummary = new ModelPassOutputSummary
                        {
                            Summary = "Bootstrap missing operator attachment."
                        },
                        StartedAtUtc = DateTime.UtcNow,
                        FinishedAtUtc = DateTime.UtcNow
                    },
                    Normalization = new ModelNormalizationResult
                    {
                        ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000102"),
                        ScopeKey = trackedPerson.ScopeKey,
                        TargetType = "person",
                        TargetRef = trackedPerson.PersonRef,
                        TruthLayer = ModelNormalizationTruthLayers.ProposalLayer,
                        PersonId = trackedPerson.PersonId,
                        Status = ModelPassResultStatuses.NeedMoreData
                    }
                },
                ScopeKey = trackedPerson.ScopeKey,
                TrackedPerson = trackedPerson,
                EvidenceCount = 0
            };
        }
    }
}

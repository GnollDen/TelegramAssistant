using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage7Formation;

namespace TgAssistant.Host.Launch;

public static class Stage7DossierProfileSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var repository = new InMemoryStage7DossierProfileRepository();
        var auditStore = new InMemoryModelPassAuditStore();
        var auditService = new ModelPassAuditService(new ModelOutputNormalizer(), auditStore);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new Stage7DossierProfileFormationService(
            repository,
            auditService,
            loggerFactory.CreateLogger<Stage7DossierProfileFormationService>());

        var successRequest = new Stage7DossierProfileFormationRequest
        {
            BootstrapResult = BuildReadyBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-a"
        };

        var firstResult = await service.FormAsync(successRequest, ct);
        AssertReady(firstResult, expectedDossierRevisionNumber: 1, expectedProfileRevisionNumber: 1, "first success");
        var secondResult = await service.FormAsync(successRequest, ct);
        AssertReady(secondResult, expectedDossierRevisionNumber: 1, expectedProfileRevisionNumber: 1, "second success");
        if (firstResult.Dossier!.Id != secondResult.Dossier!.Id
            || firstResult.Profile!.Id != secondResult.Profile!.Id
            || firstResult.Dossier.DurableObjectMetadataId != secondResult.Dossier.DurableObjectMetadataId
            || firstResult.Profile.DurableObjectMetadataId != secondResult.Profile.DurableObjectMetadataId
            || firstResult.CurrentDossierRevision!.Id != secondResult.CurrentDossierRevision!.Id
            || firstResult.CurrentProfileRevision!.Id != secondResult.CurrentProfileRevision!.Id)
        {
            throw new InvalidOperationException("Stage7 dossier/profile smoke failed: rerun changed durable dossier/profile identities.");
        }

        var changedResult = await service.FormAsync(new Stage7DossierProfileFormationRequest
        {
            BootstrapResult = BuildChangedBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-d-dossier-profile-changed"
        }, ct);
        AssertReady(changedResult, expectedDossierRevisionNumber: 2, expectedProfileRevisionNumber: 2, "changed success");
        if (changedResult.Dossier!.Id != firstResult.Dossier.Id
            || changedResult.Profile!.Id != firstResult.Profile!.Id
            || changedResult.CurrentDossierRevision!.Id == firstResult.CurrentDossierRevision!.Id
            || changedResult.CurrentProfileRevision!.Id == firstResult.CurrentProfileRevision!.Id)
        {
            throw new InvalidOperationException("Stage7 dossier/profile smoke failed: changed input did not create new revisions on stable durable objects.");
        }

        var needMoreData = await service.FormAsync(new Stage7DossierProfileFormationRequest
        {
            BootstrapResult = BuildNeedMoreDataBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-a-missing-bootstrap"
        }, ct);
        if (needMoreData.Formed
            || needMoreData.Dossier != null
            || needMoreData.Profile != null
            || !string.Equals(needMoreData.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage7 dossier/profile smoke failed: need_more_data bootstrap should not materialize durable dossier/profile outputs.");
        }
    }

    private static void AssertReady(Stage7DossierProfileFormationResult result, int expectedDossierRevisionNumber, int expectedProfileRevisionNumber, string label)
    {
        if (!result.Formed
            || result.Dossier == null
            || result.Profile == null
            || result.CurrentDossierRevision == null
            || result.CurrentProfileRevision == null
            || result.EvidenceItemIds.Count == 0
            || !string.Equals(result.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Stage7 dossier/profile smoke failed: {label} did not materialize the expected durable outputs.");
        }

        if (result.Dossier.DurableObjectMetadataId == result.Profile.DurableObjectMetadataId)
        {
            throw new InvalidOperationException($"Stage7 dossier/profile smoke failed: {label} did not keep dossier/profile metadata explicit.");
        }

        if (result.Dossier.CurrentRevisionNumber != expectedDossierRevisionNumber
            || result.Profile.CurrentRevisionNumber != expectedProfileRevisionNumber
            || result.CurrentDossierRevision.RevisionNumber != expectedDossierRevisionNumber
            || result.CurrentProfileRevision.RevisionNumber != expectedProfileRevisionNumber)
        {
            throw new InvalidOperationException($"Stage7 dossier/profile smoke failed: {label} did not preserve expected dossier/profile revision numbers.");
        }
    }

    private static Stage6BootstrapGraphResult BuildReadyBootstrapResult()
    {
        var trackedPerson = new Stage6BootstrapPersonRef
        {
            PersonId = Guid.Parse("30000000-0000-0000-0000-000000000001"),
            ScopeKey = "chat:stage7-smoke-success",
            PersonType = "tracked_person",
            DisplayName = "Durable Smoke Person",
            CanonicalName = "durable smoke person"
        };
        var operatorPerson = new Stage6BootstrapPersonRef
        {
            PersonId = Guid.Parse("30000000-0000-0000-0000-000000000002"),
            ScopeKey = "chat:stage7-smoke-success",
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
                ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000001"),
                NormalizationRunId = Guid.Parse("34000000-0000-0000-0000-000000000001"),
                Envelope = new ModelPassEnvelope
                {
                    RunId = Guid.Parse("33000000-0000-0000-0000-000000000001"),
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
                    ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000001"),
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
                    Id = Guid.Parse("35000000-0000-0000-0000-000000000001"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    DiscoveryType = Stage6BootstrapDiscoveryTypes.LinkedPerson,
                    DiscoveryKey = "linked:1",
                    PersonId = Guid.Parse("30000000-0000-0000-0000-000000000010"),
                    Status = "active"
                },
                new Stage6BootstrapDiscoveryOutput
                {
                    Id = Guid.Parse("35000000-0000-0000-0000-000000000002"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    DiscoveryType = Stage6BootstrapDiscoveryTypes.CandidateIdentity,
                    DiscoveryKey = "candidate:1",
                    CandidateIdentityStateId = Guid.Parse("36000000-0000-0000-0000-000000000001"),
                    Status = "active"
                },
                new Stage6BootstrapDiscoveryOutput
                {
                    Id = Guid.Parse("35000000-0000-0000-0000-000000000003"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    DiscoveryType = Stage6BootstrapDiscoveryTypes.Mention,
                    DiscoveryKey = "mention:1",
                    CandidateIdentityStateId = Guid.Parse("36000000-0000-0000-0000-000000000002"),
                    Status = "active"
                }
            ],
            AmbiguityOutputs =
            [
                new Stage6BootstrapPoolOutput
                {
                    Id = Guid.Parse("37000000-0000-0000-0000-000000000001"),
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
                    Id = Guid.Parse("37000000-0000-0000-0000-000000000002"),
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
                    Id = Guid.Parse("37000000-0000-0000-0000-000000000003"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    OutputType = Stage6BootstrapPoolOutputTypes.BootstrapSlice,
                    OutputKey = "slice:1",
                    Status = "active"
                }
            ]
        };
    }

    private static Stage6BootstrapGraphResult BuildNeedMoreDataBootstrapResult()
    {
        var trackedPerson = new Stage6BootstrapPersonRef
        {
            PersonId = Guid.Parse("30000000-0000-0000-0000-000000000011"),
            ScopeKey = "chat:stage7-smoke-missing-data",
            PersonType = "tracked_person",
            DisplayName = "Detached Durable Smoke Person",
            CanonicalName = "detached durable smoke person"
        };

        return new Stage6BootstrapGraphResult
        {
            AuditRecord = new ModelPassAuditRecord
            {
                ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000002"),
                NormalizationRunId = Guid.Parse("34000000-0000-0000-0000-000000000002"),
                Envelope = new ModelPassEnvelope
                {
                    RunId = Guid.Parse("33000000-0000-0000-0000-000000000002"),
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
                    TruthSummary = new ModelPassTruthSummary
                    {
                        TruthLayer = ModelNormalizationTruthLayers.CanonicalTruth,
                        Summary = "Smoke bootstrap missing-data summary."
                    },
                    Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                        ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_init")),
                    ResultStatus = ModelPassResultStatuses.NeedMoreData,
                    OutputSummary = new ModelPassOutputSummary
                    {
                        Summary = "Bootstrap needs more evidence before durable formation."
                    },
                    Unknowns =
                    [
                        new ModelPassUnknown
                        {
                            UnknownType = "missing_context",
                            Summary = "No operator attachment.",
                            RequiredAction = "attach_operator_root"
                        }
                    ]
                },
                Normalization = new ModelNormalizationResult
                {
                    ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000002"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TargetType = "person",
                    TargetRef = trackedPerson.PersonRef,
                    TruthLayer = ModelNormalizationTruthLayers.CanonicalTruth,
                    PersonId = trackedPerson.PersonId,
                    Status = ModelPassResultStatuses.NeedMoreData
                }
            },
            GraphInitialized = false,
            ScopeKey = trackedPerson.ScopeKey,
            TrackedPerson = trackedPerson
        };
    }

    private static Stage6BootstrapGraphResult BuildChangedBootstrapResult()
    {
        var result = BuildReadyBootstrapResult();
        result.ContradictionOutputs =
        [
            .. result.ContradictionOutputs,
            new Stage6BootstrapPoolOutput
            {
                Id = Guid.Parse("37000000-0000-0000-0000-000000000004"),
                ScopeKey = result.ScopeKey,
                TrackedPersonId = result.TrackedPerson!.PersonId,
                OutputType = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                OutputKey = "contradiction:changed",
                RelationshipEdgeAnchorId = Guid.Parse("38000000-0000-0000-0000-000000000002"),
                Status = "active"
            }
        ];
        result.AuditRecord.Envelope.OutputSummary.Summary = "Changed bootstrap pressure for dossier/profile revision smoke.";
        return result;
    }

    private sealed class InMemoryStage7DossierProfileRepository : IStage7DossierProfileRepository
    {
        private readonly Dictionary<string, Guid> _metadataIds = [];
        private readonly Dictionary<string, Stage7DurableDossier> _dossiers = [];
        private readonly Dictionary<string, Stage7DurableProfile> _profiles = [];
        private readonly Dictionary<Guid, List<Stage7DurableDossierRevision>> _dossierRevisions = [];
        private readonly Dictionary<Guid, List<Stage7DurableProfileRevision>> _profileRevisions = [];

        public Task<Stage7DossierProfileFormationResult> UpsertAsync(
            ModelPassAuditRecord auditRecord,
            Stage6BootstrapGraphResult bootstrapResult,
            CancellationToken ct = default)
        {
            var trackedPerson = bootstrapResult.TrackedPerson!;
            var dossierKey = $"{bootstrapResult.ScopeKey}|{trackedPerson.PersonId:D}|{Stage7DossierTypes.PersonDossier}";
            var profileKey = $"{bootstrapResult.ScopeKey}|{trackedPerson.PersonId:D}|{Stage7ProfileScopes.Global}";
            var dossierMetadataKey = $"{Stage7DurableObjectFamilies.Dossier}|{trackedPerson.PersonId:D}";
            var profileMetadataKey = $"{Stage7DurableObjectFamilies.Profile}|{trackedPerson.PersonId:D}";

            if (!_metadataIds.TryGetValue(dossierMetadataKey, out var dossierMetadataId))
            {
                dossierMetadataId = Guid.NewGuid();
                _metadataIds[dossierMetadataKey] = dossierMetadataId;
            }

            if (!_metadataIds.TryGetValue(profileMetadataKey, out var profileMetadataId))
            {
                profileMetadataId = Guid.NewGuid();
                _metadataIds[profileMetadataKey] = profileMetadataId;
            }

            if (!_dossiers.TryGetValue(dossierKey, out var dossier))
            {
                dossier = new Stage7DurableDossier
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = bootstrapResult.ScopeKey,
                    PersonId = trackedPerson.PersonId,
                    DossierType = Stage7DossierTypes.PersonDossier
                };
                _dossiers[dossierKey] = dossier;
            }

            dossier.DurableObjectMetadataId = dossierMetadataId;
            dossier.LastModelPassRunId = auditRecord.ModelPassRunId;
            dossier.Status = "active";
            if (!_dossierRevisions.TryGetValue(dossier.Id, out var dossierRevisions))
            {
                dossierRevisions = [];
                _dossierRevisions[dossier.Id] = dossierRevisions;
            }

            var dossierRevisionHash = $"{bootstrapResult.DiscoveryOutputs.Count}:{bootstrapResult.ContradictionOutputs.Count}:{bootstrapResult.AmbiguityOutputs.Count}";
            var dossierRevision = dossierRevisions.FirstOrDefault(x => string.Equals(x.RevisionHash, dossierRevisionHash, StringComparison.Ordinal));
            if (dossierRevision == null)
            {
                dossierRevision = new Stage7DurableDossierRevision
                {
                    Id = Guid.NewGuid(),
                    DurableDossierId = dossier.Id,
                    RevisionNumber = dossierRevisions.Count + 1,
                    RevisionHash = dossierRevisionHash,
                    ModelPassRunId = auditRecord.ModelPassRunId,
                    Confidence = 0.72f,
                    Coverage = 0.85f,
                    Freshness = 1.0f,
                    Stability = bootstrapResult.ContradictionOutputs.Count == 0 ? 1.0f : 0.70f,
                    ContradictionMarkersJson = "{}",
                    SummaryJson = $"{{\"family\":\"dossier\",\"contradictions\":{bootstrapResult.ContradictionOutputs.Count}}}",
                    PayloadJson = $"{{\"fields\":[\"tracked_person_name\",\"operator_attachment\"],\"linked_people\":{bootstrapResult.DiscoveryOutputs.Count}}}",
                    CreatedAt = DateTime.UtcNow
                };
                dossierRevisions.Add(dossierRevision);
            }

            dossier.CurrentRevisionNumber = dossierRevision.RevisionNumber;
            dossier.CurrentRevisionHash = dossierRevision.RevisionHash;
            dossier.SummaryJson = dossierRevision.SummaryJson;
            dossier.PayloadJson = dossierRevision.PayloadJson;

            if (!_profiles.TryGetValue(profileKey, out var profile))
            {
                profile = new Stage7DurableProfile
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = bootstrapResult.ScopeKey,
                    PersonId = trackedPerson.PersonId,
                    ProfileScope = Stage7ProfileScopes.Global
                };
                _profiles[profileKey] = profile;
            }

            profile.DurableObjectMetadataId = profileMetadataId;
            profile.LastModelPassRunId = auditRecord.ModelPassRunId;
            profile.Status = "active";
            if (!_profileRevisions.TryGetValue(profile.Id, out var profileRevisions))
            {
                profileRevisions = [];
                _profileRevisions[profile.Id] = profileRevisions;
            }

            var profileRevisionHash = $"{bootstrapResult.AmbiguityOutputs.Count}:{bootstrapResult.ContradictionOutputs.Count}:{bootstrapResult.SliceOutputs.Count}";
            var profileRevision = profileRevisions.FirstOrDefault(x => string.Equals(x.RevisionHash, profileRevisionHash, StringComparison.Ordinal));
            if (profileRevision == null)
            {
                profileRevision = new Stage7DurableProfileRevision
                {
                    Id = Guid.NewGuid(),
                    DurableProfileId = profile.Id,
                    RevisionNumber = profileRevisions.Count + 1,
                    RevisionHash = profileRevisionHash,
                    ModelPassRunId = auditRecord.ModelPassRunId,
                    Confidence = 0.64f,
                    Coverage = 0.80f,
                    Freshness = 1.0f,
                    Stability = bootstrapResult.ContradictionOutputs.Count == 0 ? 1.0f : 0.68f,
                    ContradictionMarkersJson = "{}",
                    SummaryJson = $"{{\"family\":\"profile\",\"ambiguity\":{bootstrapResult.AmbiguityOutputs.Count}}}",
                    PayloadJson = $"{{\"traits\":[\"profile_context\",\"bootstrap_pressure\"],\"slices\":{bootstrapResult.SliceOutputs.Count}}}",
                    CreatedAt = DateTime.UtcNow
                };
                profileRevisions.Add(profileRevision);
            }

            profile.CurrentRevisionNumber = profileRevision.RevisionNumber;
            profile.CurrentRevisionHash = profileRevision.RevisionHash;
            profile.SummaryJson = profileRevision.SummaryJson;
            profile.PayloadJson = profileRevision.PayloadJson;

            return Task.FromResult(new Stage7DossierProfileFormationResult
            {
                AuditRecord = auditRecord,
                Formed = true,
                TrackedPerson = trackedPerson,
                Dossier = dossier,
                Profile = profile,
                CurrentDossierRevision = dossierRevision,
                CurrentProfileRevision = profileRevision,
                EvidenceItemIds =
                [
                    bootstrapResult.AuditRecord.Envelope.EvidenceItemId!.Value
                ]
            });
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
}

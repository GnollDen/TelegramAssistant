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
        AssertReady(firstResult, "first success");
        var secondResult = await service.FormAsync(successRequest, ct);
        AssertReady(secondResult, "second success");
        if (firstResult.Dossier!.Id != secondResult.Dossier!.Id
            || firstResult.Profile!.Id != secondResult.Profile!.Id
            || firstResult.Dossier.DurableObjectMetadataId != secondResult.Dossier.DurableObjectMetadataId
            || firstResult.Profile.DurableObjectMetadataId != secondResult.Profile.DurableObjectMetadataId)
        {
            throw new InvalidOperationException("Stage7 dossier/profile smoke failed: rerun changed durable dossier/profile identities.");
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

    private static void AssertReady(Stage7DossierProfileFormationResult result, string label)
    {
        if (!result.Formed
            || result.Dossier == null
            || result.Profile == null
            || result.EvidenceItemIds.Count == 0
            || !string.Equals(result.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Stage7 dossier/profile smoke failed: {label} did not materialize the expected durable outputs.");
        }

        if (result.Dossier.DurableObjectMetadataId == result.Profile.DurableObjectMetadataId)
        {
            throw new InvalidOperationException($"Stage7 dossier/profile smoke failed: {label} did not keep dossier/profile metadata explicit.");
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

    private sealed class InMemoryStage7DossierProfileRepository : IStage7DossierProfileRepository
    {
        private readonly Dictionary<string, Guid> _metadataIds = [];
        private readonly Dictionary<string, Stage7DurableDossier> _dossiers = [];
        private readonly Dictionary<string, Stage7DurableProfile> _profiles = [];

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
            dossier.SummaryJson = "{\"family\":\"dossier\"}";
            dossier.PayloadJson = "{\"fields\":[\"tracked_person_name\",\"operator_attachment\"]}";

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
            profile.SummaryJson = "{\"family\":\"profile\"}";
            profile.PayloadJson = "{\"traits\":[\"profile_context\",\"bootstrap_pressure\"]}";

            return Task.FromResult(new Stage7DossierProfileFormationResult
            {
                AuditRecord = auditRecord,
                Formed = true,
                TrackedPerson = trackedPerson,
                Dossier = dossier,
                Profile = profile,
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
    }
}

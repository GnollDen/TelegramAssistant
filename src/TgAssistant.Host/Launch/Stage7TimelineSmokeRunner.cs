using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage7Formation;

namespace TgAssistant.Host.Launch;

public static class Stage7TimelineSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var repository = new InMemoryStage7TimelineRepository();
        var auditStore = new InMemoryModelPassAuditStore();
        var auditService = new ModelPassAuditService(new ModelOutputNormalizer(), auditStore);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new Stage7TimelineFormationService(
            repository,
            auditService,
            loggerFactory.CreateLogger<Stage7TimelineFormationService>());

        var successRequest = new Stage7TimelineFormationRequest
        {
            BootstrapResult = BuildReadyBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-c"
        };

        var firstResult = await service.FormAsync(successRequest, ct);
        AssertReady(firstResult, expectedRevisionNumber: 1, "first success");

        var secondResult = await service.FormAsync(successRequest, ct);
        AssertReady(secondResult, expectedRevisionNumber: 1, "second success");
        if (firstResult.Event!.Id != secondResult.Event!.Id
            || firstResult.TimelineEpisode!.Id != secondResult.TimelineEpisode!.Id
            || firstResult.StoryArc!.Id != secondResult.StoryArc!.Id
            || firstResult.CurrentEventRevision!.Id != secondResult.CurrentEventRevision!.Id
            || firstResult.CurrentTimelineEpisodeRevision!.Id != secondResult.CurrentTimelineEpisodeRevision!.Id
            || firstResult.CurrentStoryArcRevision!.Id != secondResult.CurrentStoryArcRevision!.Id)
        {
            throw new InvalidOperationException("Stage7 timeline smoke failed: rerun changed durable timeline object identities.");
        }

        var changedResult = await service.FormAsync(new Stage7TimelineFormationRequest
        {
            BootstrapResult = BuildChangedBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-d-timeline-changed"
        }, ct);
        AssertReady(changedResult, expectedRevisionNumber: 2, "changed success");
        if (changedResult.Event!.Id != firstResult.Event.Id
            || changedResult.TimelineEpisode!.Id != firstResult.TimelineEpisode!.Id
            || changedResult.StoryArc!.Id != firstResult.StoryArc!.Id
            || changedResult.CurrentEventRevision!.Id == firstResult.CurrentEventRevision!.Id
            || changedResult.CurrentTimelineEpisodeRevision!.Id == firstResult.CurrentTimelineEpisodeRevision!.Id
            || changedResult.CurrentStoryArcRevision!.Id == firstResult.CurrentStoryArcRevision!.Id)
        {
            throw new InvalidOperationException("Stage7 timeline smoke failed: changed input did not create new revisions on stable durable timeline objects.");
        }

        var needMoreData = await service.FormAsync(new Stage7TimelineFormationRequest
        {
            BootstrapResult = BuildNeedMoreDataBootstrapResult(),
            RunKind = "smoke",
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-006-c-missing-bootstrap"
        }, ct);
        if (needMoreData.Formed
            || needMoreData.Event != null
            || needMoreData.TimelineEpisode != null
            || needMoreData.StoryArc != null
            || !string.Equals(needMoreData.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage7 timeline smoke failed: need_more_data bootstrap should not materialize durable timeline outputs.");
        }
    }

    private static void AssertReady(Stage7TimelineFormationResult result, int expectedRevisionNumber, string label)
    {
        if (!result.Formed
            || result.Event == null
            || result.TimelineEpisode == null
            || result.StoryArc == null
            || result.CurrentEventRevision == null
            || result.CurrentTimelineEpisodeRevision == null
            || result.CurrentStoryArcRevision == null
            || result.EvidenceItemIds.Count == 0
            || !string.Equals(result.AuditRecord.Envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Stage7 timeline smoke failed: {label} did not materialize the expected durable outputs.");
        }

        if (result.Event.BoundaryConfidence <= 0
            || string.IsNullOrWhiteSpace(result.Event.ClosureState)
            || result.TimelineEpisode.BoundaryConfidence <= 0
            || string.IsNullOrWhiteSpace(result.TimelineEpisode.ClosureState)
            || result.StoryArc.BoundaryConfidence <= 0
            || string.IsNullOrWhiteSpace(result.StoryArc.ClosureState))
        {
            throw new InvalidOperationException($"Stage7 timeline smoke failed: {label} did not preserve explicit boundary confidence and closure state.");
        }

        if (result.Event.CurrentRevisionNumber != expectedRevisionNumber
            || result.TimelineEpisode.CurrentRevisionNumber != expectedRevisionNumber
            || result.StoryArc.CurrentRevisionNumber != expectedRevisionNumber
            || result.CurrentEventRevision.RevisionNumber != expectedRevisionNumber
            || result.CurrentTimelineEpisodeRevision.RevisionNumber != expectedRevisionNumber
            || result.CurrentStoryArcRevision.RevisionNumber != expectedRevisionNumber)
        {
            throw new InvalidOperationException($"Stage7 timeline smoke failed: {label} did not preserve expected revision numbers.");
        }
    }

    private static Stage6BootstrapGraphResult BuildReadyBootstrapResult()
    {
        var trackedPerson = new Stage6BootstrapPersonRef
        {
            PersonId = Guid.Parse("30000000-0000-0000-0000-000000000101"),
            ScopeKey = "chat:stage7-timeline-smoke-success",
            PersonType = "tracked_person",
            DisplayName = "Timeline Smoke Person",
            CanonicalName = "timeline smoke person"
        };
        var operatorPerson = new Stage6BootstrapPersonRef
        {
            PersonId = Guid.Parse("30000000-0000-0000-0000-000000000102"),
            ScopeKey = trackedPerson.ScopeKey,
            PersonType = "operator_root",
            DisplayName = "Timeline Smoke Operator",
            CanonicalName = "timeline smoke operator"
        };
        var evidenceId = Guid.Parse("32000000-0000-0000-0000-000000000101");
        var sourceObjectId = Guid.Parse("31000000-0000-0000-0000-000000000101");
        var now = DateTime.UtcNow;

        return new Stage6BootstrapGraphResult
        {
            AuditRecord = new ModelPassAuditRecord
            {
                ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000201"),
                NormalizationRunId = Guid.Parse("34000000-0000-0000-0000-000000000201"),
                Envelope = new ModelPassEnvelope
                {
                    RunId = Guid.Parse("33000000-0000-0000-0000-000000000201"),
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
                            SourceRef = "smoke:timeline-source-1",
                            SourceObjectId = sourceObjectId,
                            EvidenceItemId = evidenceId
                        }
                    ],
                    TruthSummary = new ModelPassTruthSummary
                    {
                        TruthLayer = ModelNormalizationTruthLayers.CanonicalTruth,
                        Summary = "Timeline smoke bootstrap summary.",
                        CanonicalRefs = [$"evidence:{evidenceId:D}"]
                    },
                    ResultStatus = ModelPassResultStatuses.ResultReady,
                    OutputSummary = new ModelPassOutputSummary
                    {
                        Summary = "Timeline smoke Stage6 bootstrap completed."
                    },
                    StartedAtUtc = now,
                    FinishedAtUtc = now
                },
                Normalization = new ModelNormalizationResult
                {
                    ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000201"),
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
            EvidenceCount = 4,
            LatestEvidenceAtUtc = now,
            DiscoveryOutputs =
            [
                new Stage6BootstrapDiscoveryOutput
                {
                    Id = Guid.Parse("35000000-0000-0000-0000-000000000101"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    DiscoveryType = Stage6BootstrapDiscoveryTypes.LinkedPerson,
                    DiscoveryKey = "linked:timeline-1",
                    PersonId = Guid.Parse("30000000-0000-0000-0000-000000000110"),
                    Status = "active"
                }
            ],
            AmbiguityOutputs =
            [
                new Stage6BootstrapPoolOutput
                {
                    Id = Guid.Parse("37000000-0000-0000-0000-000000000101"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    OutputType = Stage6BootstrapPoolOutputTypes.AmbiguityPool,
                    OutputKey = "ambiguity:timeline-1",
                    Status = "active"
                }
            ],
            ContradictionOutputs = [],
            SliceOutputs =
            [
                new Stage6BootstrapPoolOutput
                {
                    Id = Guid.Parse("37000000-0000-0000-0000-000000000102"),
                    ScopeKey = trackedPerson.ScopeKey,
                    TrackedPersonId = trackedPerson.PersonId,
                    OutputType = Stage6BootstrapPoolOutputTypes.BootstrapSlice,
                    OutputKey = "slice:timeline-1",
                    Status = "active"
                }
            ]
        };
    }

    private static Stage6BootstrapGraphResult BuildNeedMoreDataBootstrapResult()
    {
        var trackedPerson = new Stage6BootstrapPersonRef
        {
            PersonId = Guid.Parse("30000000-0000-0000-0000-000000000111"),
            ScopeKey = "chat:stage7-timeline-smoke-missing-data",
            PersonType = "tracked_person",
            DisplayName = "Detached Timeline Smoke Person",
            CanonicalName = "detached timeline smoke person"
        };

        return new Stage6BootstrapGraphResult
        {
            AuditRecord = new ModelPassAuditRecord
            {
                ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000202"),
                NormalizationRunId = Guid.Parse("34000000-0000-0000-0000-000000000202"),
                Envelope = new ModelPassEnvelope
                {
                    RunId = Guid.Parse("33000000-0000-0000-0000-000000000202"),
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
                        Summary = "Timeline smoke missing-data summary."
                    },
                    ResultStatus = ModelPassResultStatuses.NeedMoreData,
                    OutputSummary = new ModelPassOutputSummary
                    {
                        Summary = "Bootstrap needs more evidence before durable timeline formation."
                    },
                    Unknowns =
                    [
                        new ModelPassUnknown
                        {
                            UnknownType = "missing_context",
                            Summary = "No bootstrap timeline context.",
                            RequiredAction = "collect_more_bootstrap_evidence"
                        }
                    ]
                },
                Normalization = new ModelNormalizationResult
                {
                    ModelPassRunId = Guid.Parse("33000000-0000-0000-0000-000000000202"),
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
            new Stage6BootstrapPoolOutput
            {
                Id = Guid.Parse("37000000-0000-0000-0000-000000000103"),
                ScopeKey = result.ScopeKey,
                TrackedPersonId = result.TrackedPerson!.PersonId,
                OutputType = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                OutputKey = "contradiction:timeline-1",
                RelationshipEdgeAnchorId = Guid.Parse("38000000-0000-0000-0000-000000000103"),
                Status = "active"
            }
        ];
        result.AuditRecord.Envelope.OutputSummary.Summary = "Changed timeline pressure for revision smoke.";
        return result;
    }

    private sealed class InMemoryStage7TimelineRepository : IStage7TimelineRepository
    {
        private readonly Dictionary<string, Guid> _metadataIds = [];
        private readonly Dictionary<string, Stage7DurableEvent> _events = [];
        private readonly Dictionary<string, Stage7DurableTimelineEpisode> _episodes = [];
        private readonly Dictionary<string, Stage7DurableStoryArc> _storyArcs = [];
        private readonly Dictionary<Guid, List<Stage7DurableEventRevision>> _eventRevisions = [];
        private readonly Dictionary<Guid, List<Stage7DurableTimelineEpisodeRevision>> _episodeRevisions = [];
        private readonly Dictionary<Guid, List<Stage7DurableStoryArcRevision>> _storyArcRevisions = [];

        public Task<Stage7TimelineFormationResult> UpsertAsync(
            ModelPassAuditRecord auditRecord,
            Stage6BootstrapGraphResult bootstrapResult,
            CancellationToken ct = default)
        {
            var trackedPerson = bootstrapResult.TrackedPerson!;
            var operatorPerson = bootstrapResult.OperatorPerson;
            var eventKey = $"{bootstrapResult.ScopeKey}|{trackedPerson.PersonId:D}|{Stage7EventTypes.BootstrapAnchorEvent}";
            var episodeKey = $"{bootstrapResult.ScopeKey}|{trackedPerson.PersonId:D}|{Stage7TimelineEpisodeTypes.BootstrapEpisode}";
            var storyArcKey = $"{bootstrapResult.ScopeKey}|{trackedPerson.PersonId:D}|{Stage7StoryArcTypes.OperatorTrackedArc}";

            var eventMetadataId = GetOrCreateMetadataId($"{Stage7DurableObjectFamilies.Event}|{trackedPerson.PersonId:D}");
            var episodeMetadataId = GetOrCreateMetadataId($"{Stage7DurableObjectFamilies.TimelineEpisode}|{trackedPerson.PersonId:D}");
            var storyArcMetadataId = GetOrCreateMetadataId($"{Stage7DurableObjectFamilies.StoryArc}|{trackedPerson.PersonId:D}");

            if (!_events.TryGetValue(eventKey, out var eventRow))
            {
                eventRow = new Stage7DurableEvent
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = bootstrapResult.ScopeKey,
                    PersonId = trackedPerson.PersonId,
                    EventType = Stage7EventTypes.BootstrapAnchorEvent
                };
                _events[eventKey] = eventRow;
            }

            if (!_episodes.TryGetValue(episodeKey, out var episodeRow))
            {
                episodeRow = new Stage7DurableTimelineEpisode
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = bootstrapResult.ScopeKey,
                    PersonId = trackedPerson.PersonId,
                    EpisodeType = Stage7TimelineEpisodeTypes.BootstrapEpisode
                };
                _episodes[episodeKey] = episodeRow;
            }

            if (!_storyArcs.TryGetValue(storyArcKey, out var storyArcRow))
            {
                storyArcRow = new Stage7DurableStoryArc
                {
                    Id = Guid.NewGuid(),
                    ScopeKey = bootstrapResult.ScopeKey,
                    PersonId = trackedPerson.PersonId,
                    ArcType = Stage7StoryArcTypes.OperatorTrackedArc
                };
                _storyArcs[storyArcKey] = storyArcRow;
            }

            eventRow.RelatedPersonId = operatorPerson?.PersonId;
            eventRow.DurableObjectMetadataId = eventMetadataId;
            eventRow.LastModelPassRunId = auditRecord.ModelPassRunId;
            eventRow.Status = "active";
            if (!_eventRevisions.TryGetValue(eventRow.Id, out var eventRevisions))
            {
                eventRevisions = [];
                _eventRevisions[eventRow.Id] = eventRevisions;
            }

            var eventRevisionHash = $"{bootstrapResult.ContradictionOutputs.Count}:{bootstrapResult.AmbiguityOutputs.Count}:{bootstrapResult.EvidenceCount}";
            var eventRevision = eventRevisions.FirstOrDefault(x => string.Equals(x.RevisionHash, eventRevisionHash, StringComparison.Ordinal));
            if (eventRevision == null)
            {
                eventRevision = new Stage7DurableEventRevision
                {
                    Id = Guid.NewGuid(),
                    DurableEventId = eventRow.Id,
                    RevisionNumber = eventRevisions.Count + 1,
                    RevisionHash = eventRevisionHash,
                    ModelPassRunId = auditRecord.ModelPassRunId,
                    Confidence = 0.70f,
                    Freshness = 1.0f,
                    Stability = bootstrapResult.ContradictionOutputs.Count == 0 ? 1.0f : 0.68f,
                    BoundaryConfidence = bootstrapResult.ContradictionOutputs.Count == 0 ? 0.80f : 0.61f,
                    EventConfidence = bootstrapResult.ContradictionOutputs.Count == 0 ? 0.70f : 0.58f,
                    ClosureState = bootstrapResult.ContradictionOutputs.Count == 0 ? Stage7ClosureStates.SemiClosed : Stage7ClosureStates.Open,
                    ContradictionMarkersJson = "{}",
                    SummaryJson = $"{{\"family\":\"event\",\"contradictions\":{bootstrapResult.ContradictionOutputs.Count}}}",
                    PayloadJson = $"{{\"event_type\":\"bootstrap_anchor_event\",\"ambiguity\":{bootstrapResult.AmbiguityOutputs.Count}}}",
                    CreatedAt = DateTime.UtcNow
                };
                eventRevisions.Add(eventRevision);
            }

            eventRow.CurrentRevisionNumber = eventRevision.RevisionNumber;
            eventRow.CurrentRevisionHash = eventRevision.RevisionHash;
            eventRow.BoundaryConfidence = eventRevision.BoundaryConfidence;
            eventRow.EventConfidence = eventRevision.EventConfidence;
            eventRow.ClosureState = eventRevision.ClosureState;
            eventRow.SummaryJson = eventRevision.SummaryJson;
            eventRow.PayloadJson = eventRevision.PayloadJson;

            episodeRow.RelatedPersonId = operatorPerson?.PersonId;
            episodeRow.DurableObjectMetadataId = episodeMetadataId;
            episodeRow.LastModelPassRunId = auditRecord.ModelPassRunId;
            episodeRow.Status = "active";
            if (!_episodeRevisions.TryGetValue(episodeRow.Id, out var episodeRevisions))
            {
                episodeRevisions = [];
                _episodeRevisions[episodeRow.Id] = episodeRevisions;
            }

            var episodeRevisionHash = $"{bootstrapResult.SliceOutputs.Count}:{bootstrapResult.ContradictionOutputs.Count}:{bootstrapResult.AmbiguityOutputs.Count}";
            var episodeRevision = episodeRevisions.FirstOrDefault(x => string.Equals(x.RevisionHash, episodeRevisionHash, StringComparison.Ordinal));
            if (episodeRevision == null)
            {
                episodeRevision = new Stage7DurableTimelineEpisodeRevision
                {
                    Id = Guid.NewGuid(),
                    DurableTimelineEpisodeId = episodeRow.Id,
                    RevisionNumber = episodeRevisions.Count + 1,
                    RevisionHash = episodeRevisionHash,
                    ModelPassRunId = auditRecord.ModelPassRunId,
                    Confidence = 0.66f,
                    Freshness = 1.0f,
                    Stability = bootstrapResult.ContradictionOutputs.Count == 0 ? 1.0f : 0.69f,
                    BoundaryConfidence = bootstrapResult.ContradictionOutputs.Count == 0 ? 0.76f : 0.60f,
                    ClosureState = bootstrapResult.ContradictionOutputs.Count == 0 ? Stage7ClosureStates.Closed : Stage7ClosureStates.SemiClosed,
                    ContradictionMarkersJson = "{}",
                    SummaryJson = $"{{\"family\":\"timeline_episode\",\"contradictions\":{bootstrapResult.ContradictionOutputs.Count}}}",
                    PayloadJson = $"{{\"episode_type\":\"bootstrap_episode\",\"slice_count\":{bootstrapResult.SliceOutputs.Count}}}",
                    CreatedAt = DateTime.UtcNow
                };
                episodeRevisions.Add(episodeRevision);
            }

            episodeRow.CurrentRevisionNumber = episodeRevision.RevisionNumber;
            episodeRow.CurrentRevisionHash = episodeRevision.RevisionHash;
            episodeRow.BoundaryConfidence = episodeRevision.BoundaryConfidence;
            episodeRow.ClosureState = episodeRevision.ClosureState;
            episodeRow.SummaryJson = episodeRevision.SummaryJson;
            episodeRow.PayloadJson = episodeRevision.PayloadJson;

            storyArcRow.RelatedPersonId = operatorPerson?.PersonId;
            storyArcRow.DurableObjectMetadataId = storyArcMetadataId;
            storyArcRow.LastModelPassRunId = auditRecord.ModelPassRunId;
            storyArcRow.Status = "active";
            if (!_storyArcRevisions.TryGetValue(storyArcRow.Id, out var storyArcRevisions))
            {
                storyArcRevisions = [];
                _storyArcRevisions[storyArcRow.Id] = storyArcRevisions;
            }

            var storyArcRevisionHash = $"{bootstrapResult.ContradictionOutputs.Count}:{bootstrapResult.AmbiguityOutputs.Count}:{bootstrapResult.DiscoveryOutputs.Count}";
            var storyArcRevision = storyArcRevisions.FirstOrDefault(x => string.Equals(x.RevisionHash, storyArcRevisionHash, StringComparison.Ordinal));
            if (storyArcRevision == null)
            {
                storyArcRevision = new Stage7DurableStoryArcRevision
                {
                    Id = Guid.NewGuid(),
                    DurableStoryArcId = storyArcRow.Id,
                    RevisionNumber = storyArcRevisions.Count + 1,
                    RevisionHash = storyArcRevisionHash,
                    ModelPassRunId = auditRecord.ModelPassRunId,
                    Confidence = 0.63f,
                    Freshness = 1.0f,
                    Stability = bootstrapResult.ContradictionOutputs.Count == 0 ? 1.0f : 0.67f,
                    BoundaryConfidence = bootstrapResult.ContradictionOutputs.Count == 0 ? 0.72f : 0.57f,
                    ClosureState = bootstrapResult.ContradictionOutputs.Count == 0 ? Stage7ClosureStates.SemiClosed : Stage7ClosureStates.Open,
                    ContradictionMarkersJson = "{}",
                    SummaryJson = $"{{\"family\":\"story_arc\",\"contradictions\":{bootstrapResult.ContradictionOutputs.Count}}}",
                    PayloadJson = $"{{\"arc_type\":\"operator_tracked_arc\",\"linked_people\":{bootstrapResult.DiscoveryOutputs.Count}}}",
                    CreatedAt = DateTime.UtcNow
                };
                storyArcRevisions.Add(storyArcRevision);
            }

            storyArcRow.CurrentRevisionNumber = storyArcRevision.RevisionNumber;
            storyArcRow.CurrentRevisionHash = storyArcRevision.RevisionHash;
            storyArcRow.BoundaryConfidence = storyArcRevision.BoundaryConfidence;
            storyArcRow.ClosureState = storyArcRevision.ClosureState;
            storyArcRow.SummaryJson = storyArcRevision.SummaryJson;
            storyArcRow.PayloadJson = storyArcRevision.PayloadJson;

            return Task.FromResult(new Stage7TimelineFormationResult
            {
                AuditRecord = auditRecord,
                Formed = true,
                TrackedPerson = trackedPerson,
                OperatorPerson = operatorPerson,
                Event = eventRow,
                TimelineEpisode = episodeRow,
                StoryArc = storyArcRow,
                CurrentEventRevision = eventRevision,
                CurrentTimelineEpisodeRevision = episodeRevision,
                CurrentStoryArcRevision = storyArcRevision,
                EvidenceItemIds =
                [
                    bootstrapResult.AuditRecord.Envelope.EvidenceItemId!.Value
                ]
            });
        }

        private Guid GetOrCreateMetadataId(string key)
        {
            if (_metadataIds.TryGetValue(key, out var id))
            {
                return id;
            }

            id = Guid.NewGuid();
            _metadataIds[key] = id;
            return id;
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

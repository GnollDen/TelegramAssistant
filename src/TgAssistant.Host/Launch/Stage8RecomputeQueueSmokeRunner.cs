using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage8Recompute;

namespace TgAssistant.Host.Launch;

public static class Stage8RecomputeQueueSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var repository = new InMemoryStage8RecomputeQueueRepository();
        var bootstrapService = new FakeStage6BootstrapService();
        var dossierProfileService = new FakeStage7DossierProfileService();
        var pairDynamicsService = new FakeStage7PairDynamicsService();
        var timelineService = new FakeStage7TimelineService();
        var gateRepository = new InMemoryStage8OutcomeGateRepository();
        var defectRepository = new InMemoryRuntimeDefectRepository();
        var controlStateService = new InMemoryRuntimeControlStateService();
        var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new Stage8RecomputeQueueService(
            repository,
            bootstrapService,
            dossierProfileService,
            pairDynamicsService,
            timelineService,
            controlStateService,
            gateRepository,
            defectRepository,
            loggerFactory.CreateLogger<Stage8RecomputeQueueService>(),
            baseRetryDelay: TimeSpan.FromMilliseconds(1),
            maxRetryDelay: TimeSpan.FromMilliseconds(5));

        var noWork = await service.ExecuteNextAsync(ct);
        if (noWork.Executed || !string.Equals(noWork.ExecutionStatus, Stage8RecomputeExecutionStatuses.NoWorkAvailable, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: empty queue did not return no_work_available.");
        }

        var dedupedRequest = new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-recompute-smoke",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000001"),
            TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-007-a-dedupe"
        };
        var firstItem = await service.EnqueueAsync(dedupedRequest, ct);
        var secondItem = await service.EnqueueAsync(dedupedRequest, ct);
        if (firstItem.Id != secondItem.Id || repository.ActiveCount != 1)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: duplicate enqueue did not collapse to one active queue item.");
        }

        var dossierExecution = await service.ExecuteNextAsync(ct);
        if (!dossierExecution.Executed
            || !string.Equals(dossierExecution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal)
            || !string.Equals(dossierExecution.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            || dossierExecution.ModelPassRunId != FakeStage7DossierProfileService.ReadyRunId)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: dossier/profile scoped execution did not complete successfully.");
        }

        if (bootstrapService.CallCount != 1
            || dossierProfileService.CallCount != 1
            || pairDynamicsService.CallCount != 0
            || timelineService.CallCount != 0
            || gateRepository.CallCount != 1)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: scoped execution was broader than the requested dossier/profile family.");
        }

        if (bootstrapService.LastRequest?.PersonId != dedupedRequest.PersonId
            || !string.Equals(bootstrapService.LastRequest?.ScopeKey, dedupedRequest.ScopeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: bootstrap execution did not preserve smallest-sufficient scope.");
        }

        pairDynamicsService.FailuresRemaining = 1;
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-recompute-smoke-retry",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000002"),
            TargetFamily = Stage8RecomputeTargetFamilies.PairDynamics,
            TriggerKind = "manual_smoke",
            TriggerRef = "implement-007-a-retry"
        }, ct);

        var retryExecution = await service.ExecuteNextAsync(ct);
        if (!retryExecution.Executed
            || !string.Equals(retryExecution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal)
            || retryExecution.QueueItem == null
            || !string.Equals(retryExecution.QueueItem.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal)
            || retryExecution.QueueItem.AttemptCount != 1
            || string.IsNullOrWhiteSpace(retryExecution.QueueItem.LastError))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: transient failure did not reschedule the leased queue item.");
        }

        if (defectRepository.CallCount == 0
            || !string.Equals(defectRepository.LastRequest?.DefectClass, RuntimeDefectClasses.ControlPlane, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: execution failure did not persist a control-plane defect.");
        }

        var retrySuccess = await service.ExecuteNextAsync(ct);
        if (!retrySuccess.Executed
            || !string.Equals(retrySuccess.ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal)
            || retrySuccess.ModelPassRunId != FakeStage7PairDynamicsService.ReadyRunId
            || pairDynamicsService.CallCount != 2)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: retried pair-dynamics work did not complete on the second attempt.");
        }

        timelineService.NextResultStatus = ModelPassResultStatuses.NeedOperatorClarification;
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-recompute-smoke-clarification",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000003"),
            TargetFamily = Stage8RecomputeTargetFamilies.TimelineObjects,
            TriggerKind = "clarification_smoke",
            TriggerRef = "implement-007-c-clarification"
        }, ct);

        var clarificationExecution = await service.ExecuteNextAsync(ct);
        if (!clarificationExecution.Executed
            || !string.Equals(clarificationExecution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal)
            || !string.Equals(clarificationExecution.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: clarification outcome was not persisted as a completed gated execution.");
        }

        if (!string.Equals(gateRepository.LastRequest?.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)
            || !string.Equals(gateRepository.LastRequest?.TargetFamily, Stage8RecomputeTargetFamilies.TimelineObjects, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: clarification gate did not receive crystallization result context.");
        }

        if (!string.Equals(defectRepository.LastRequest?.DefectClass, RuntimeDefectClasses.Normalization, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: clarification-gated outcome did not persist a normalization defect.");
        }

        controlStateService.NextDecision = new RuntimeControlEnforcementDecision
        {
            State = RuntimeControlStates.SafeMode,
            Reason = "safe-mode-smoke",
            PauseAllExecution = true
        };
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-recompute-smoke-safe-mode",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000004"),
            TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
            TriggerKind = "safe_mode_smoke",
            TriggerRef = "implement-009-c-safe-mode"
        }, ct);

        var safeModeExecution = await service.ExecuteNextAsync(ct);
        if (!safeModeExecution.Executed
            || !string.Equals(safeModeExecution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal)
            || !string.Equals(controlStateService.LastDecision?.State, RuntimeControlStates.SafeMode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: safe-mode runtime control state did not defer execution.");
        }
    }

    private sealed class InMemoryStage8RecomputeQueueRepository : IStage8RecomputeQueueRepository
    {
        private readonly List<Stage8RecomputeQueueItem> _items = [];

        public int ActiveCount => _items.Count(x => !string.IsNullOrWhiteSpace(x.ActiveDedupeKey));

        public Task<Stage8RecomputeQueueItem> EnqueueAsync(Stage8RecomputeQueueRequest request, CancellationToken ct = default)
        {
            var scopeKey = request.ScopeKey.Trim();
            var targetRef = request.PersonId != null
                ? $"person:{request.PersonId:D}"
                : $"scope:{scopeKey}";
            var dedupeKey = $"{scopeKey}|{request.TargetFamily}|{targetRef}";
            var existing = _items.FirstOrDefault(x => string.Equals(x.ActiveDedupeKey, dedupeKey, StringComparison.Ordinal));
            if (existing != null)
            {
                if (string.Equals(existing.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal))
                {
                    existing.Priority = Math.Min(existing.Priority, request.Priority);
                    existing.AvailableAtUtc = existing.AvailableAtUtc <= (request.AvailableAtUtc ?? DateTime.UtcNow)
                        ? existing.AvailableAtUtc
                        : request.AvailableAtUtc ?? DateTime.UtcNow;
                    existing.UpdatedAtUtc = DateTime.UtcNow;
                }

                return Task.FromResult(Clone(existing));
            }

            var item = new Stage8RecomputeQueueItem
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                PersonId = request.PersonId,
                TargetFamily = request.TargetFamily,
                TargetRef = targetRef,
                DedupeKey = dedupeKey,
                ActiveDedupeKey = dedupeKey,
                TriggerKind = string.IsNullOrWhiteSpace(request.TriggerKind) ? "manual" : request.TriggerKind.Trim(),
                TriggerRef = request.TriggerRef,
                Status = Stage8RecomputeQueueStatuses.Pending,
                Priority = request.Priority,
                AttemptCount = 0,
                MaxAttempts = request.MaxAttempts,
                AvailableAtUtc = request.AvailableAtUtc ?? DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _items.Add(item);
            return Task.FromResult(Clone(item));
        }

        public Task<Stage8RecomputeQueueItem?> LeaseNextAsync(DateTime nowUtc, TimeSpan leaseDuration, CancellationToken ct = default)
        {
            var item = _items
                .Where(x => string.Equals(x.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal)
                            && x.AvailableAtUtc <= nowUtc)
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.AvailableAtUtc)
                .ThenBy(x => x.CreatedAtUtc)
                .FirstOrDefault();
            if (item == null)
            {
                return Task.FromResult<Stage8RecomputeQueueItem?>(null);
            }

            item.Status = Stage8RecomputeQueueStatuses.Leased;
            item.AttemptCount += 1;
            item.LeaseToken = Guid.NewGuid();
            item.LeasedUntilUtc = nowUtc.Add(leaseDuration);
            item.UpdatedAtUtc = nowUtc;
            return Task.FromResult<Stage8RecomputeQueueItem?>(Clone(item));
        }

        public Task CompleteAsync(Guid queueItemId, Guid leaseToken, string resultStatus, Guid? modelPassRunId, CancellationToken ct = default)
        {
            var item = GetLeased(queueItemId, leaseToken);
            item.Status = Stage8RecomputeQueueStatuses.Completed;
            item.ActiveDedupeKey = null;
            item.LeaseToken = null;
            item.LeasedUntilUtc = null;
            item.LastError = null;
            item.LastResultStatus = resultStatus;
            item.LastModelPassRunId = modelPassRunId;
            item.CompletedAtUtc = DateTime.UtcNow;
            item.UpdatedAtUtc = item.CompletedAtUtc.Value;
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(Guid queueItemId, Guid leaseToken, string error, DateTime nextAvailableAtUtc, bool terminalFailure, CancellationToken ct = default)
        {
            var item = GetLeased(queueItemId, leaseToken);
            item.Status = terminalFailure ? Stage8RecomputeQueueStatuses.Failed : Stage8RecomputeQueueStatuses.Pending;
            item.ActiveDedupeKey = terminalFailure ? null : item.DedupeKey;
            item.LeaseToken = null;
            item.LeasedUntilUtc = null;
            item.LastError = error;
            item.LastResultStatus = terminalFailure
                ? Stage8RecomputeExecutionStatuses.FailedTerminally
                : Stage8RecomputeExecutionStatuses.Rescheduled;
            item.AvailableAtUtc = nextAvailableAtUtc;
            item.CompletedAtUtc = terminalFailure ? DateTime.UtcNow : null;
            item.UpdatedAtUtc = DateTime.UtcNow;
            return Task.CompletedTask;
        }

        private Stage8RecomputeQueueItem GetLeased(Guid queueItemId, Guid leaseToken)
        {
            var item = _items.FirstOrDefault(x => x.Id == queueItemId);
            if (item == null
                || !string.Equals(item.Status, Stage8RecomputeQueueStatuses.Leased, StringComparison.Ordinal)
                || item.LeaseToken != leaseToken)
            {
                throw new InvalidOperationException("Queue item is not currently leased by the caller.");
            }

            return item;
        }

        private static Stage8RecomputeQueueItem Clone(Stage8RecomputeQueueItem item)
        {
            return new Stage8RecomputeQueueItem
            {
                Id = item.Id,
                ScopeKey = item.ScopeKey,
                PersonId = item.PersonId,
                TargetFamily = item.TargetFamily,
                TargetRef = item.TargetRef,
                DedupeKey = item.DedupeKey,
                ActiveDedupeKey = item.ActiveDedupeKey,
                TriggerKind = item.TriggerKind,
                TriggerRef = item.TriggerRef,
                Status = item.Status,
                Priority = item.Priority,
                AttemptCount = item.AttemptCount,
                MaxAttempts = item.MaxAttempts,
                AvailableAtUtc = item.AvailableAtUtc,
                LeasedUntilUtc = item.LeasedUntilUtc,
                LeaseToken = item.LeaseToken,
                LastError = item.LastError,
                LastResultStatus = item.LastResultStatus,
                LastModelPassRunId = item.LastModelPassRunId,
                CreatedAtUtc = item.CreatedAtUtc,
                UpdatedAtUtc = item.UpdatedAtUtc,
                CompletedAtUtc = item.CompletedAtUtc
            };
        }
    }

    private sealed class FakeStage6BootstrapService : IStage6BootstrapService
    {
        public int CallCount { get; private set; }
        public Stage6BootstrapRequest? LastRequest { get; private set; }

        public Task<Stage6BootstrapGraphResult> RunGraphInitializationAsync(Stage6BootstrapRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            LastRequest = request;
            var now = DateTime.UtcNow;
            return Task.FromResult(new Stage6BootstrapGraphResult
            {
                AuditRecord = new ModelPassAuditRecord
                {
                    ModelPassRunId = Guid.Parse("82000000-0000-0000-0000-000000000001"),
                    NormalizationRunId = Guid.Parse("82000000-0000-0000-0000-000000000002"),
                    Envelope = new ModelPassEnvelope
                    {
                        RunId = Guid.Parse("82000000-0000-0000-0000-000000000001"),
                        Stage = "stage6_bootstrap",
                        PassFamily = "graph_init",
                        RunKind = request.RunKind,
                        ScopeKey = request.ScopeKey ?? "chat:stage8-recompute-smoke",
                        Scope = new ModelPassScope
                        {
                            ScopeType = "person_scope",
                            ScopeRef = request.PersonId != null ? $"person:{request.PersonId:D}" : "person:unresolved"
                        },
                        Target = new ModelPassTarget
                        {
                            TargetType = "person",
                            TargetRef = request.PersonId != null ? $"person:{request.PersonId:D}" : "person:unresolved"
                        },
                        PersonId = request.PersonId,
                        ResultStatus = ModelPassResultStatuses.ResultReady,
                        OutputSummary = new ModelPassOutputSummary
                        {
                            Summary = "Stage8 recompute bootstrap smoke result."
                        },
                        StartedAtUtc = now,
                        FinishedAtUtc = now
                    },
                    Normalization = new ModelNormalizationResult
                    {
                        ModelPassRunId = Guid.Parse("82000000-0000-0000-0000-000000000001"),
                        ScopeKey = request.ScopeKey ?? "chat:stage8-recompute-smoke",
                        TargetType = "person",
                        TargetRef = request.PersonId != null ? $"person:{request.PersonId:D}" : "person:unresolved",
                        TruthLayer = ModelNormalizationTruthLayers.ProposalLayer,
                        PersonId = request.PersonId,
                        Status = ModelPassResultStatuses.ResultReady
                    }
                },
                GraphInitialized = true,
                ScopeKey = request.ScopeKey ?? "chat:stage8-recompute-smoke",
                TrackedPerson = request.PersonId == null
                    ? null
                    : new Stage6BootstrapPersonRef
                    {
                        PersonId = request.PersonId.Value,
                        ScopeKey = request.ScopeKey ?? "chat:stage8-recompute-smoke",
                        PersonType = "tracked_person",
                        DisplayName = "Stage8 Smoke Person",
                        CanonicalName = "stage8 smoke person"
                    },
                OperatorPerson = new Stage6BootstrapPersonRef
                {
                    PersonId = Guid.Parse("82000000-0000-0000-0000-000000000099"),
                    ScopeKey = request.ScopeKey ?? "chat:stage8-recompute-smoke",
                    PersonType = "operator_root",
                    DisplayName = "Stage8 Smoke Operator",
                    CanonicalName = "stage8 smoke operator"
                },
                EvidenceCount = 3,
                LatestEvidenceAtUtc = now
            });
        }
    }

    private sealed class FakeStage7DossierProfileService : IStage7DossierProfileService
    {
        public static readonly Guid ReadyRunId = Guid.Parse("83000000-0000-0000-0000-000000000001");
        public int CallCount { get; private set; }

        public Task<Stage7DossierProfileFormationResult> FormAsync(Stage7DossierProfileFormationRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            return Task.FromResult(new Stage7DossierProfileFormationResult
            {
                AuditRecord = BuildAuditRecord(ReadyRunId, request.BootstrapResult.ScopeKey, request.BootstrapResult.TrackedPerson?.PersonRef ?? "person:unresolved", "dossier_profile"),
                Formed = true
            });
        }
    }

    private sealed class FakeStage7PairDynamicsService : IStage7PairDynamicsService
    {
        public static readonly Guid ReadyRunId = Guid.Parse("84000000-0000-0000-0000-000000000001");
        public int CallCount { get; private set; }
        public int FailuresRemaining { get; set; }

        public Task<Stage7PairDynamicsFormationResult> FormAsync(Stage7PairDynamicsFormationRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            if (FailuresRemaining > 0)
            {
                FailuresRemaining -= 1;
                throw new InvalidOperationException("Synthetic transient pair-dynamics failure.");
            }

            return Task.FromResult(new Stage7PairDynamicsFormationResult
            {
                AuditRecord = BuildAuditRecord(ReadyRunId, request.BootstrapResult.ScopeKey, request.BootstrapResult.TrackedPerson?.PersonRef ?? "person:unresolved", "pair_dynamics"),
                Formed = true
            });
        }
    }

    private sealed class FakeStage7TimelineService : IStage7TimelineService
    {
        public int CallCount { get; private set; }
        public string NextResultStatus { get; set; } = ModelPassResultStatuses.ResultReady;

        public Task<Stage7TimelineFormationResult> FormAsync(Stage7TimelineFormationRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            return Task.FromResult(new Stage7TimelineFormationResult
            {
                AuditRecord = BuildAuditRecord(
                    Guid.Parse("85000000-0000-0000-0000-000000000001"),
                    request.BootstrapResult.ScopeKey,
                    request.BootstrapResult.TrackedPerson?.PersonRef ?? "person:unresolved",
                    "timeline_objects",
                    NextResultStatus),
                Formed = true
            });
        }
    }

    private static ModelPassAuditRecord BuildAuditRecord(
        Guid runId,
        string scopeKey,
        string targetRef,
        string passFamily,
        string resultStatus = ModelPassResultStatuses.ResultReady)
    {
        var now = DateTime.UtcNow;
        return new ModelPassAuditRecord
        {
            ModelPassRunId = runId,
            NormalizationRunId = Guid.NewGuid(),
            Envelope = new ModelPassEnvelope
            {
                RunId = runId,
                Stage = "stage8_recompute",
                PassFamily = passFamily,
                RunKind = "stage8_recompute_queue",
                ScopeKey = scopeKey,
                Scope = new ModelPassScope
                {
                    ScopeType = "person_scope",
                    ScopeRef = targetRef
                },
                Target = new ModelPassTarget
                {
                    TargetType = "person",
                    TargetRef = targetRef
                },
                ResultStatus = resultStatus,
                OutputSummary = new ModelPassOutputSummary
                {
                    Summary = "Stage8 recompute smoke completed."
                },
                StartedAtUtc = now,
                FinishedAtUtc = now
            },
            Normalization = new ModelNormalizationResult
            {
                ModelPassRunId = runId,
                ScopeKey = scopeKey,
                TargetType = "person",
                TargetRef = targetRef,
                TruthLayer = ModelNormalizationTruthLayers.DerivedButDurable,
                Status = resultStatus
            }
        };
    }

    private sealed class InMemoryStage8OutcomeGateRepository : IStage8OutcomeGateRepository
    {
        public int CallCount { get; private set; }
        public Stage8OutcomeGateRequest? LastRequest { get; private set; }

        public Task<Stage8OutcomeGateResult> ApplyOutcomeGateAsync(Stage8OutcomeGateRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            LastRequest = request;
            var result = new Stage8OutcomeGateResult
            {
                ScopeKey = request.ScopeKey,
                TargetFamily = request.TargetFamily,
                ResultStatus = request.ResultStatus,
                AffectedCount = 1,
                PromotedCount = string.Equals(request.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal) ? 1 : 0,
                PromotionBlockedCount = string.Equals(request.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal) ? 0 : 1,
                ClarificationBlockedCount = string.Equals(request.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal) ? 1 : 0
            };
            return Task.FromResult(result);
        }
    }

    private sealed class InMemoryRuntimeDefectRepository : IRuntimeDefectRepository
    {
        private readonly List<RuntimeDefectRecord> _records = [];

        public int CallCount { get; private set; }
        public RuntimeDefectUpsertRequest? LastRequest { get; private set; }

        public Task<RuntimeDefectRecord> UpsertAsync(RuntimeDefectUpsertRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            LastRequest = request;
            var now = DateTime.UtcNow;
            var record = new RuntimeDefectRecord
            {
                Id = Guid.NewGuid(),
                DefectClass = request.DefectClass,
                Severity = request.Severity,
                ScopeKey = request.ScopeKey,
                DedupeKey = request.DedupeKey,
                RunId = request.RunId,
                ObjectType = request.ObjectType,
                ObjectRef = request.ObjectRef,
                Summary = request.Summary,
                DetailsJson = request.DetailsJson,
                OccurrenceCount = 1,
                EscalationAction = RuntimeDefectEscalationActions.Ticket,
                EscalationReason = "smoke",
                FirstSeenAtUtc = now,
                LastSeenAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _records.Add(record);
            return Task.FromResult(record);
        }

        public Task<List<RuntimeDefectRecord>> GetOpenAsync(int limit = 200, CancellationToken ct = default)
            => Task.FromResult(_records.Take(Math.Max(1, limit)).ToList());
    }

    private sealed class InMemoryRuntimeControlStateService : IRuntimeControlStateService
    {
        public RuntimeControlEnforcementDecision NextDecision { get; set; } = new();
        public RuntimeControlEnforcementDecision? LastDecision { get; private set; }

        public Task<RuntimeControlEnforcementDecision> EvaluateAndApplyFromDefectsAsync(CancellationToken ct = default)
        {
            LastDecision = NextDecision;
            NextDecision = new RuntimeControlEnforcementDecision
            {
                State = RuntimeControlStates.Normal,
                Reason = "normal-smoke"
            };
            return Task.FromResult(LastDecision);
        }
    }
}

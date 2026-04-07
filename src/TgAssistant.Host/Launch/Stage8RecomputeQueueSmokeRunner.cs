using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;
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
        var relatedConflictRepository = new InMemoryStage8RelatedConflictRepository();
        var defectRepository = new InMemoryRuntimeDefectRepository();
        var controlStateService = new InMemoryRuntimeControlStateService();
        var clarificationBranchStateRepository = new InMemoryClarificationBranchStateRepository();
        var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new Stage8RecomputeQueueService(
            repository,
            bootstrapService,
            dossierProfileService,
            pairDynamicsService,
            timelineService,
            controlStateService,
            gateRepository,
            relatedConflictRepository,
            defectRepository,
            clarificationBranchStateRepository,
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

        clarificationBranchStateRepository.AddOpenBranch(
            scopeKey: "chat:stage8-recompute-smoke-branch-block",
            branchFamily: Stage8RecomputeTargetFamilies.TimelineObjects,
            targetType: "timeline_bundle",
            targetRef: "person:81000000-0000-0000-0000-000000000005",
            personId: Guid.Parse("81000000-0000-0000-0000-000000000005"),
            blockReason: "Timeline clarification remains unresolved.");
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-recompute-smoke-branch-block",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000005"),
            TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
            TriggerKind = "branch_scope_smoke",
            TriggerRef = "implement-012-b-dossier"
        }, ct);
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-recompute-smoke-branch-block",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000005"),
            TargetFamily = Stage8RecomputeTargetFamilies.TimelineObjects,
            TriggerKind = "branch_scope_smoke",
            TriggerRef = "implement-012-b-timeline"
        }, ct);

        var unaffectedExecution = await service.ExecuteNextAsync(ct);
        if (!unaffectedExecution.Executed
            || !string.Equals(unaffectedExecution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal)
            || !string.Equals(unaffectedExecution.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal)
            || dossierProfileService.CallCount != 2)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: unaffected dossier/profile branch did not continue under a sibling clarification block.");
        }

        var blockedBranchExecution = await service.ExecuteNextAsync(ct);
        if (!blockedBranchExecution.Executed
            || !string.Equals(blockedBranchExecution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal)
            || !string.Equals(blockedBranchExecution.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal)
            || timelineService.CallCount != 1)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: blocked timeline branch did not remain clarification-blocked without re-execution.");
        }

        if (!string.Equals(gateRepository.LastRequest?.TargetFamily, Stage8RecomputeTargetFamilies.TimelineObjects, StringComparison.Ordinal)
            || !string.Equals(gateRepository.LastRequest?.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: blocked timeline branch did not keep clarification gate state explicit.");
        }

        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-backfill-smoke-a",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000010"),
            TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
            TriggerKind = "backfill_smoke",
            TriggerRef = "implement-016-a-scope-a-1"
        }, ct);
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-backfill-smoke-a",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000010"),
            TargetFamily = Stage8RecomputeTargetFamilies.PairDynamics,
            TriggerKind = "backfill_smoke",
            TriggerRef = "implement-016-a-scope-a-2"
        }, ct);
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-backfill-smoke-b",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000011"),
            TargetFamily = Stage8RecomputeTargetFamilies.TimelineObjects,
            TriggerKind = "backfill_smoke",
            TriggerRef = "implement-016-a-scope-b-1"
        }, ct);

        var firstBackfillLease = await repository.LeaseNextBackfillAsync(
            DateTime.UtcNow,
            TimeSpan.FromMinutes(5),
            maxConcurrentScopes: 2,
            workerId: "backfill-smoke-worker-a",
            ct);
        var secondBackfillLease = await repository.LeaseNextBackfillAsync(
            DateTime.UtcNow,
            TimeSpan.FromMinutes(5),
            maxConcurrentScopes: 2,
            workerId: "backfill-smoke-worker-b",
            ct);
        var thirdBackfillLease = await repository.LeaseNextBackfillAsync(
            DateTime.UtcNow,
            TimeSpan.FromMinutes(5),
            maxConcurrentScopes: 2,
            workerId: "backfill-smoke-worker-c",
            ct);
        if (firstBackfillLease == null
            || secondBackfillLease == null
            || thirdBackfillLease != null
            || string.Equals(firstBackfillLease.ScopeKey, secondBackfillLease.ScopeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: bounded backfill leasing did not respect scope locks and concurrency ceiling.");
        }

        await repository.RescheduleAsync(
            firstBackfillLease.Id,
            firstBackfillLease.LeaseToken!.Value,
            "synthetic backfill retry",
            DateTime.UtcNow,
            terminalFailure: false,
            recoveryTelemetry: null,
            ct);
        var resumedBackfillLease = await repository.LeaseNextBackfillAsync(
            DateTime.UtcNow,
            TimeSpan.FromMinutes(5),
            maxConcurrentScopes: 2,
            workerId: "backfill-smoke-worker-a",
            ct);
        if (resumedBackfillLease == null
            || !string.Equals(resumedBackfillLease.ScopeKey, firstBackfillLease.ScopeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: released backfill scope did not resume from checkpoint state.");
        }

        var readyCheckpoint = await repository.GetBackfillCheckpointAsync(resumedBackfillLease.ScopeKey, ct);
        if (readyCheckpoint == null
            || !string.Equals(readyCheckpoint.Status, Stage8BackfillCheckpointStatuses.InProgress, StringComparison.Ordinal)
            || !string.Equals(readyCheckpoint.ActiveLeaseOwner, "backfill-smoke-worker-a", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: backfill checkpoint was not updated on resumed scope lease.");
        }

        await repository.CompleteAsync(
            resumedBackfillLease.Id,
            resumedBackfillLease.LeaseToken!.Value,
            ModelPassResultStatuses.ResultReady,
            FakeStage7DossierProfileService.ReadyRunId,
            ct);
        await repository.CompleteAsync(
            secondBackfillLease.Id,
            secondBackfillLease.LeaseToken!.Value,
            ModelPassResultStatuses.ResultReady,
            Guid.Parse("86000000-0000-0000-0000-000000000001"),
            ct);

        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-backfill-recovery",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000012"),
            TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
            TriggerKind = "backfill_smoke",
            TriggerRef = "implement-016-a-recovery"
        }, ct);
        var staleLease = await repository.LeaseNextBackfillAsync(
            DateTime.UtcNow,
            TimeSpan.FromMilliseconds(1),
            maxConcurrentScopes: 1,
            workerId: "backfill-smoke-recovery-a",
            ct);
        if (staleLease == null)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: recovery scope could not be leased for stale-lease test.");
        }

        var recoveredLease = await repository.LeaseNextBackfillAsync(
            DateTime.UtcNow.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            maxConcurrentScopes: 1,
            workerId: "backfill-smoke-recovery-b",
            ct);
        if (recoveredLease == null
            || recoveredLease.Id != staleLease.Id
            || recoveredLease.AttemptCount < 2)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: stale backfill lease did not resume the previously checkpointed queue item.");
        }

        var recoveredCheckpoint = await repository.GetBackfillCheckpointAsync(recoveredLease.ScopeKey, ct);
        if (recoveredCheckpoint == null
            || recoveredCheckpoint.ResumeCount < 1
            || !string.Equals(recoveredCheckpoint.Status, Stage8BackfillCheckpointStatuses.InProgress, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: stale lease recovery did not leave an auditable checkpoint resume trail.");
        }

        await repository.CompleteAsync(
            recoveredLease.Id,
            recoveredLease.LeaseToken!.Value,
            ModelPassResultStatuses.ResultReady,
            Guid.Parse("86000000-0000-0000-0000-000000000002"),
            ct);

        // Drain any leftover backfill work from the prior bounded-concurrency checks so
        // the deadlock scenario below executes the intended synthetic scope deterministically.
        await service.ExecuteBackfillBatchAsync(new Stage8BackfillExecutionRequest
        {
            MaxConcurrentScopes = 1,
            MaxItems = 4,
            WorkerId = "backfill-smoke-drain",
            LeaseDuration = TimeSpan.FromMilliseconds(20)
        }, ct);

        pairDynamicsService.ConflictFailuresRemaining = 1;
        await service.EnqueueAsync(new Stage8RecomputeQueueRequest
        {
            ScopeKey = "chat:stage8-backfill-deadlock",
            PersonId = Guid.Parse("81000000-0000-0000-0000-000000000013"),
            TargetFamily = Stage8RecomputeTargetFamilies.PairDynamics,
            TriggerKind = "backfill_smoke",
            TriggerRef = "implement-016-b-deadlock"
        }, ct);
        var deadlockBatch = await service.ExecuteBackfillBatchAsync(new Stage8BackfillExecutionRequest
        {
            MaxConcurrentScopes = 1,
            MaxItems = 1,
            WorkerId = "backfill-smoke-deadlock",
            LeaseDuration = TimeSpan.FromMilliseconds(20)
        }, ct);
        if (deadlockBatch.ExecutedCount != 1
            || deadlockBatch.RescheduledCount != 1
            || deadlockBatch.Items.Count != 1
            || deadlockBatch.Items[0].QueueItem == null
            || !string.Equals(deadlockBatch.Items[0].ExecutionStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Stage8 recompute queue smoke failed: synthetic deadlock did not reschedule backfill work. executed={deadlockBatch.ExecutedCount}, rescheduled={deadlockBatch.RescheduledCount}, items={deadlockBatch.Items.Count}, first_status={deadlockBatch.Items.FirstOrDefault()?.ExecutionStatus ?? "<none>"}");
        }

        var deadlockCheckpoint = await repository.GetBackfillCheckpointAsync("chat:stage8-backfill-deadlock", ct);
        if (deadlockCheckpoint == null
            || deadlockCheckpoint.RetryCount < 1
            || deadlockCheckpoint.DeadlockRetryCount < 1
            || !string.Equals(deadlockCheckpoint.LastRecoveryKind, Stage8BackfillRecoveryKinds.DeadlockRetry, StringComparison.Ordinal)
            || deadlockCheckpoint.LastBackoffUntilUtc == null)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: deadlock retry telemetry was not persisted on the backfill checkpoint.");
        }

        await Task.Delay(10, ct);
        var deadlockRecoveryBatch = await service.ExecuteBackfillBatchAsync(new Stage8BackfillExecutionRequest
        {
            MaxConcurrentScopes = 1,
            MaxItems = 1,
            WorkerId = "backfill-smoke-deadlock",
            LeaseDuration = TimeSpan.FromMilliseconds(20)
        }, ct);
        if (deadlockRecoveryBatch.CompletedCount != 1
            || deadlockRecoveryBatch.Items.Count != 1
            || !string.Equals(deadlockRecoveryBatch.Items[0].ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: rescheduled deadlock work did not recover on the next bounded backfill attempt.");
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

        await AssertRepositoryBackedReintegrationLinkageAsync(ct);
    }

    private static async Task AssertRepositoryBackedReintegrationLinkageAsync(CancellationToken ct)
    {
        var resolvedOutputPath = ResolveReintegrationEvidenceOutputPath();
        var report = new Stage8ReintegrationLinkageSmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var connectionString = ResolveDatabaseConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException("Stage8 recompute queue smoke failed: Database:ConnectionString is required for repository-backed reintegration linkage evidence.");
            }

            var scopeKey = $"chat:stage8-recompute-smoke-ledger:{Guid.NewGuid():N}";
            var trackedPersonId = Guid.NewGuid();

            var dbInit = new DatabaseInitializer(
                Options.Create(new DatabaseSettings
                {
                    ConnectionString = connectionString
                }),
                NullLogger<DatabaseInitializer>.Instance);
            await dbInit.InitializeAsync(ct);

            var options = new DbContextOptionsBuilder<TgAssistantDbContext>()
                .UseNpgsql(connectionString)
                .Options;
            await using var dbFactory = new SmokeDbContextFactory(options);
            await SeedTrackedPersonAsync(dbFactory, scopeKey, trackedPersonId, ct);

            var queueRepository = new Stage8RecomputeQueueRepository(dbFactory, NullLogger<Stage8RecomputeQueueRepository>.Instance);
            var queueItem = await queueRepository.EnqueueAsync(
                new Stage8RecomputeQueueRequest
                {
                    ScopeKey = scopeKey,
                    PersonId = trackedPersonId,
                    TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                    TriggerKind = "stage8_recompute_smoke",
                    TriggerRef = "phb-008b"
                },
                ct);
            if (queueItem.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Stage8 recompute queue smoke failed: repository-backed queue item did not persist.");
            }

            var reintegrationRepository = new ResolutionCaseReintegrationLedgerRepository(dbFactory);
            var reintegrationService = new ResolutionCaseReintegrationService(reintegrationRepository);

            var unresolved = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = scopeKey,
                    ScopeItemKey = "scope-item:stage8-recompute-smoke",
                    TrackedPersonId = trackedPersonId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.Open,
                    UnresolvedResidueJson = """{"reason":"stage8_queue_smoke_open"}"""
                },
                ct);

            var resolving = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = unresolved.ScopeKey,
                    ScopeItemKey = unresolved.ScopeItemKey,
                    TrackedPersonId = unresolved.TrackedPersonId,
                    CarryForwardCaseId = unresolved.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.Stage8RecomputeRequest,
                    NextStatus = IterativeCaseStatuses.ResolvingAi,
                    ExpectedPreviousLedgerEntryId = unresolved.Id
                },
                ct);

            var resolved = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = unresolved.ScopeKey,
                    ScopeItemKey = unresolved.ScopeItemKey,
                    TrackedPersonId = unresolved.TrackedPersonId,
                    CarryForwardCaseId = unresolved.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.Stage8RecomputeRequest,
                    NextStatus = IterativeCaseStatuses.ResolvedByAi,
                    RecomputeQueueItemId = queueItem.Id,
                    RecomputeTargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                    RecomputeTargetRef = $"person:{unresolved.TrackedPersonId:D}",
                    ExpectedPreviousLedgerEntryId = resolving.Id
                },
                ct);

            var roundTripPassed = resolved.RecomputeQueueItemId == queueItem.Id
                                  && string.Equals(resolved.RecomputeTargetFamily, Stage8RecomputeTargetFamilies.DossierProfile, StringComparison.Ordinal)
                                  && string.Equals(resolved.RecomputeTargetRef, $"person:{trackedPersonId:D}", StringComparison.Ordinal);
            report.Rows.Add(new Stage8ReintegrationLinkageSmokeRow
            {
                CaseId = "repository_round_trip",
                ExpectedDecision = "allow",
                ActualDecision = roundTripPassed ? "allow" : "reject",
                Reason = roundTripPassed ? "persisted_linkage_tuple" : "linkage_tuple_mismatch",
                Passed = roundTripPassed
            });

            var persistedRows = await reintegrationService.QueryAsync(
                new ResolutionCaseReintegrationQuery
                {
                    ScopeKey = scopeKey,
                    CarryForwardCaseId = unresolved.CarryForwardCaseId,
                    Limit = 10
                },
                ct);
            var readbackPassed = persistedRows.Count >= 2
                                 && persistedRows.Any(x => x.Id == unresolved.Id)
                                 && persistedRows.Any(x => x.Id == resolved.Id);
            report.Rows.Add(new Stage8ReintegrationLinkageSmokeRow
            {
                CaseId = "repository_readback",
                ExpectedDecision = "allow",
                ActualDecision = readbackPassed ? "allow" : "reject",
                Reason = readbackPassed ? "service_query_returned_persisted_rows" : "missing_persisted_rows",
                Passed = readbackPassed
            });

            var crossScopeRejected = await ExpectRejectedReasonAsync(
                reintegrationService,
                ReintegrationLedgerFailureReasons.CrossScopeLinkageRejected,
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = scopeKey,
                    ScopeItemKey = "scope-item:stage8-recompute-smoke-other",
                    TrackedPersonId = trackedPersonId,
                    CarryForwardCaseId = unresolved.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.Open
                },
                ct);
            report.Rows.Add(new Stage8ReintegrationLinkageSmokeRow
            {
                CaseId = "cross_scope_linkage_rejected",
                ExpectedDecision = "reject",
                ActualDecision = crossScopeRejected ? "reject" : "allow",
                Reason = crossScopeRejected
                    ? ReintegrationLedgerFailureReasons.CrossScopeLinkageRejected
                    : "unexpectedly_accepted",
                Passed = crossScopeRejected
            });

            var staleRejected = await ExpectRejectedReasonAsync(
                reintegrationService,
                ReintegrationLedgerFailureReasons.StaleRecomputeLinkageRejected,
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = unresolved.ScopeKey,
                    ScopeItemKey = unresolved.ScopeItemKey,
                    TrackedPersonId = unresolved.TrackedPersonId,
                    CarryForwardCaseId = unresolved.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.Superseded,
                    ExpectedPreviousLedgerEntryId = unresolved.Id
                },
                ct);
            report.Rows.Add(new Stage8ReintegrationLinkageSmokeRow
            {
                CaseId = "stale_recompute_linkage_rejected",
                ExpectedDecision = "reject",
                ActualDecision = staleRejected ? "reject" : "allow",
                Reason = staleRejected
                    ? ReintegrationLedgerFailureReasons.StaleRecomputeLinkageRejected
                    : "unexpectedly_accepted",
                Passed = staleRejected
            });

            report.Passed = report.Rows.All(x => x.Passed);
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.Passed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.Passed)
        {
            throw new InvalidOperationException("Stage8 recompute queue smoke failed: repository-backed reintegration linkage evidence is incomplete.", fatal);
        }
    }

    private static async Task<bool> ExpectRejectedReasonAsync(
        IResolutionCaseReintegrationService service,
        string expectedReason,
        ResolutionCaseReintegrationRecordRequest request,
        CancellationToken ct)
    {
        try
        {
            await service.RecordAsync(request, ct);
            return false;
        }
        catch (InvalidOperationException ex) when (string.Equals(ex.Message, expectedReason, StringComparison.Ordinal))
        {
            return true;
        }
    }

    private static async Task SeedTrackedPersonAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string scopeKey,
        Guid personId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var exists = await db.Persons.AnyAsync(x => x.Id == personId, ct);
        if (exists)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        db.Persons.Add(new DbPerson
        {
            Id = personId,
            ScopeKey = scopeKey,
            PersonType = "tracked_person",
            DisplayName = "Stage8 Recompute Smoke Person",
            CanonicalName = "stage8_recompute_smoke_person",
            Status = "active",
            MetadataJson = "{}",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? ResolveDatabaseConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("Database__ConnectionString");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv.Trim();
        }

        var baseDir = AppContext.BaseDirectory;
        var appSettingsPath = Path.Combine(baseDir, "appsettings.json");
        if (!File.Exists(appSettingsPath))
        {
            appSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "TgAssistant.Host", "appsettings.json");
        }

        if (!File.Exists(appSettingsPath))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        if (doc.RootElement.TryGetProperty("Database", out var databaseElement)
            && databaseElement.TryGetProperty("ConnectionString", out var connectionElement))
        {
            var fromFile = connectionElement.GetString();
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                return fromFile.Trim();
            }
        }

        return null;
    }

    private static string ResolveReintegrationEvidenceOutputPath()
    {
        var cwd = Directory.GetCurrentDirectory();
        var hostArtifactsRoot = string.Equals(Path.GetFileName(cwd), "TgAssistant.Host", StringComparison.Ordinal)
            ? Path.Combine(cwd, "artifacts")
            : Path.Combine(cwd, "src", "TgAssistant.Host", "artifacts");
        return Path.GetFullPath(Path.Combine(
            hostArtifactsRoot,
            "phase-b",
            "stage8-recompute-reintegration-linkage-smoke.json"));
    }

    private sealed class SmokeDbContextFactory : IDbContextFactory<TgAssistantDbContext>, IAsyncDisposable
    {
        private readonly DbContextOptions<TgAssistantDbContext> _options;

        public SmokeDbContextFactory(DbContextOptions<TgAssistantDbContext> options)
        {
            _options = options;
        }

        public TgAssistantDbContext CreateDbContext()
            => new(_options);

        public Task<TgAssistantDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InMemoryStage8RecomputeQueueRepository : IStage8RecomputeQueueRepository
    {
        private readonly List<Stage8RecomputeQueueItem> _items = [];
        private readonly Dictionary<string, Stage8BackfillCheckpoint> _checkpoints = new(StringComparer.Ordinal);

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

        public Task<Stage8RecomputeQueueItem?> LeaseNextBackfillAsync(
            DateTime nowUtc,
            TimeSpan leaseDuration,
            int maxConcurrentScopes,
            string workerId,
            CancellationToken ct = default)
        {
            foreach (var expiredItem in _items.Where(x =>
                         string.Equals(x.Status, Stage8RecomputeQueueStatuses.Leased, StringComparison.Ordinal)
                         && x.LeasedUntilUtc != null
                         && x.LeasedUntilUtc <= nowUtc))
            {
                expiredItem.Status = Stage8RecomputeQueueStatuses.Pending;
                expiredItem.LeaseToken = null;
                expiredItem.LeasedUntilUtc = null;
                expiredItem.LastError ??= "lease_expired_resume";
                expiredItem.LastResultStatus = Stage8RecomputeExecutionStatuses.Rescheduled;
                expiredItem.UpdatedAtUtc = nowUtc;
            }

            foreach (var expiredCheckpoint in _checkpoints.Values.Where(x =>
                         string.Equals(x.Status, Stage8BackfillCheckpointStatuses.InProgress, StringComparison.Ordinal)
                         && x.LeaseExpiresAtUtc != null
                         && x.LeaseExpiresAtUtc <= nowUtc))
            {
                expiredCheckpoint.Status = Stage8BackfillCheckpointStatuses.Ready;
                expiredCheckpoint.ActiveQueueItemId = null;
                expiredCheckpoint.ActiveTargetFamily = null;
                expiredCheckpoint.ActiveLeaseToken = null;
                expiredCheckpoint.ActiveLeaseOwner = null;
                expiredCheckpoint.LeaseExpiresAtUtc = null;
                expiredCheckpoint.LastError ??= "lease_expired_resume";
                expiredCheckpoint.ResumeCount += 1;
                expiredCheckpoint.LastCheckpointAtUtc = nowUtc;
                expiredCheckpoint.UpdatedAtUtc = nowUtc;
            }

            var activeScopeCount = _checkpoints.Values.Count(x =>
                string.Equals(x.Status, Stage8BackfillCheckpointStatuses.InProgress, StringComparison.Ordinal)
                && x.LeaseExpiresAtUtc != null
                && x.LeaseExpiresAtUtc > nowUtc);
            if (activeScopeCount >= maxConcurrentScopes)
            {
                return Task.FromResult<Stage8RecomputeQueueItem?>(null);
            }

            var activeScopes = _checkpoints.Values
                .Where(x => string.Equals(x.Status, Stage8BackfillCheckpointStatuses.InProgress, StringComparison.Ordinal)
                            && x.LeaseExpiresAtUtc != null
                            && x.LeaseExpiresAtUtc > nowUtc)
                .Select(x => x.ScopeKey)
                .ToHashSet(StringComparer.Ordinal);

            var item = _items
                .Where(x => string.Equals(x.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal)
                            && x.AvailableAtUtc <= nowUtc
                            && !activeScopes.Contains(x.ScopeKey))
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

            if (!_checkpoints.TryGetValue(item.ScopeKey, out var checkpoint))
            {
                checkpoint = new Stage8BackfillCheckpoint
                {
                    ScopeKey = item.ScopeKey,
                    FirstStartedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };
                _checkpoints[item.ScopeKey] = checkpoint;
            }

            checkpoint.Status = Stage8BackfillCheckpointStatuses.InProgress;
            checkpoint.ActiveQueueItemId = item.Id;
            checkpoint.ActiveTargetFamily = item.TargetFamily;
            checkpoint.ActiveLeaseToken = item.LeaseToken;
            checkpoint.ActiveLeaseOwner = workerId;
            checkpoint.LeaseExpiresAtUtc = item.LeasedUntilUtc;
            checkpoint.LastQueueItemId = item.Id;
            checkpoint.LastTargetFamily = item.TargetFamily;
            checkpoint.LastCheckpointAtUtc = nowUtc;
            checkpoint.UpdatedAtUtc = nowUtc;
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
            ReleaseCheckpoint(item, resultStatus, modelPassRunId, terminalFailure: false, error: null, recoveryTelemetry: null, item.CompletedAtUtc.Value);
            return Task.CompletedTask;
        }

        public Task RescheduleAsync(Guid queueItemId, Guid leaseToken, string error, DateTime nextAvailableAtUtc, bool terminalFailure, Stage8BackfillRecoveryTelemetry? recoveryTelemetry = null, CancellationToken ct = default)
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
            ReleaseCheckpoint(item, item.LastResultStatus, modelPassId: null, terminalFailure, error, recoveryTelemetry, item.UpdatedAtUtc);
            return Task.CompletedTask;
        }

        public Task<Stage8BackfillCheckpoint?> GetBackfillCheckpointAsync(string scopeKey, CancellationToken ct = default)
        {
            _checkpoints.TryGetValue(scopeKey, out var checkpoint);
            return Task.FromResult(checkpoint == null ? null : Clone(checkpoint));
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

        private void ReleaseCheckpoint(
            Stage8RecomputeQueueItem item,
            string resultStatus,
            Guid? modelPassId,
            bool terminalFailure,
            string? error,
            Stage8BackfillRecoveryTelemetry? recoveryTelemetry,
            DateTime nowUtc)
        {
            if (!_checkpoints.TryGetValue(item.ScopeKey, out var checkpoint))
            {
                return;
            }

            var hasRemaining = _items.Any(x =>
                string.Equals(x.ScopeKey, item.ScopeKey, StringComparison.Ordinal)
                && (string.Equals(x.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal)
                    || string.Equals(x.Status, Stage8RecomputeQueueStatuses.Leased, StringComparison.Ordinal)));
            checkpoint.Status = hasRemaining
                ? Stage8BackfillCheckpointStatuses.Ready
                : terminalFailure
                    ? Stage8BackfillCheckpointStatuses.Failed
                    : Stage8BackfillCheckpointStatuses.Completed;
            checkpoint.ActiveQueueItemId = null;
            checkpoint.ActiveTargetFamily = null;
            checkpoint.ActiveLeaseToken = null;
            checkpoint.ActiveLeaseOwner = null;
            checkpoint.LeaseExpiresAtUtc = null;
            checkpoint.LastQueueItemId = item.Id;
            checkpoint.LastTargetFamily = item.TargetFamily;
            checkpoint.LastResultStatus = resultStatus;
            checkpoint.LastModelPassRunId = modelPassIdOrExisting(modelPassId, checkpoint.LastModelPassRunId);
            checkpoint.LastError = error;
            checkpoint.LastRecoveryKind = recoveryTelemetry?.RecoveryKind
                ?? (string.Equals(resultStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal)
                    ? Stage8BackfillRecoveryKinds.GeneralRetry
                    : Stage8BackfillRecoveryKinds.None);
            checkpoint.LastRecoveryAtUtc = recoveryTelemetry?.OccurredAtUtc;
            checkpoint.LastBackoffUntilUtc = recoveryTelemetry?.NextAttemptAtUtc;
            checkpoint.LastCheckpointAtUtc = nowUtc;
            checkpoint.UpdatedAtUtc = nowUtc;
            checkpoint.LastCompletedAtUtc = string.Equals(resultStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal)
                ? null
                : nowUtc;
            if (terminalFailure)
            {
                checkpoint.FailedItemCount += 1;
            }
            else if (string.Equals(resultStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal))
            {
                checkpoint.RetryCount += 1;
                if (recoveryTelemetry?.IsDeadlock == true)
                {
                    checkpoint.DeadlockRetryCount += 1;
                }

                if (recoveryTelemetry?.IsTransientConflict == true)
                {
                    checkpoint.TransientRetryCount += 1;
                }
            }
            else
            {
                checkpoint.CompletedItemCount += 1;
                checkpoint.LastRecoveryKind = Stage8BackfillRecoveryKinds.None;
                checkpoint.LastRecoveryAtUtc = null;
                checkpoint.LastBackoffUntilUtc = null;
            }
        }

        private static Guid? modelPassIdOrExisting(Guid? modelPassRunId, Guid? existing)
            => modelPassRunId ?? existing;

        private static Stage8BackfillCheckpoint Clone(Stage8BackfillCheckpoint checkpoint)
        {
            return new Stage8BackfillCheckpoint
            {
                ScopeKey = checkpoint.ScopeKey,
                Status = checkpoint.Status,
                ActiveQueueItemId = checkpoint.ActiveQueueItemId,
                ActiveTargetFamily = checkpoint.ActiveTargetFamily,
                ActiveLeaseToken = checkpoint.ActiveLeaseToken,
                ActiveLeaseOwner = checkpoint.ActiveLeaseOwner,
                LeaseExpiresAtUtc = checkpoint.LeaseExpiresAtUtc,
                LastQueueItemId = checkpoint.LastQueueItemId,
                LastTargetFamily = checkpoint.LastTargetFamily,
                LastResultStatus = checkpoint.LastResultStatus,
                LastModelPassRunId = checkpoint.LastModelPassRunId,
                LastError = checkpoint.LastError,
                CompletedItemCount = checkpoint.CompletedItemCount,
                FailedItemCount = checkpoint.FailedItemCount,
                ResumeCount = checkpoint.ResumeCount,
                RetryCount = checkpoint.RetryCount,
                DeadlockRetryCount = checkpoint.DeadlockRetryCount,
                TransientRetryCount = checkpoint.TransientRetryCount,
                LastRecoveryKind = checkpoint.LastRecoveryKind,
                LastRecoveryAtUtc = checkpoint.LastRecoveryAtUtc,
                LastBackoffUntilUtc = checkpoint.LastBackoffUntilUtc,
                FirstStartedAtUtc = checkpoint.FirstStartedAtUtc,
                LastCheckpointAtUtc = checkpoint.LastCheckpointAtUtc,
                LastCompletedAtUtc = checkpoint.LastCompletedAtUtc,
                UpdatedAtUtc = checkpoint.UpdatedAtUtc
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
                        Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                            ModelPassBudgetCatalog.Create("stage6_bootstrap", "graph_init")),
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
        public int ConflictFailuresRemaining { get; set; }

        public Task<Stage7PairDynamicsFormationResult> FormAsync(Stage7PairDynamicsFormationRequest request, CancellationToken ct = default)
        {
            CallCount += 1;
            if (ConflictFailuresRemaining > 0)
            {
                ConflictFailuresRemaining -= 1;
                throw new InvalidOperationException("deadlock detected while updating synthetic pair-dynamics rows");
            }

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
                Budget = ModelPassBudgetCatalog.ConsumeOneIteration(
                    ModelPassBudgetCatalog.Create("stage8_recompute", passFamily)),
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

    private sealed class InMemoryStage8RelatedConflictRepository : IStage8RelatedConflictRepository
    {
        public Task<Stage8RelatedConflictReevaluationResult> ReevaluateAsync(
            Stage8RelatedConflictReevaluationRequest request,
            CancellationToken ct = default)
        {
            return Task.FromResult(new Stage8RelatedConflictReevaluationResult
            {
                Applied = true
            });
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

        public Task<int> ResolveOpenByDedupeKeyAsync(string dedupeKey, Guid? runId = null, CancellationToken ct = default)
        {
            var affected = 0;
            foreach (var row in _records.Where(x => string.Equals(x.DedupeKey, dedupeKey, StringComparison.Ordinal)
                                                    && string.Equals(x.Status, RuntimeDefectStatuses.Open, StringComparison.Ordinal)))
            {
                row.Status = RuntimeDefectStatuses.Resolved;
                row.ResolvedAtUtc = DateTime.UtcNow;
                row.RunId = runId ?? row.RunId;
                row.UpdatedAtUtc = DateTime.UtcNow;
                affected++;
            }

            return Task.FromResult(affected);
        }
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

    private sealed class InMemoryClarificationBranchStateRepository : IClarificationBranchStateRepository
    {
        private readonly List<ClarificationBranchStateRecord> _branches = [];

        public void AddOpenBranch(
            string scopeKey,
            string branchFamily,
            string targetType,
            string targetRef,
            Guid? personId,
            string blockReason)
        {
            _branches.Add(new ClarificationBranchStateRecord
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                BranchFamily = branchFamily,
                BranchKey = $"{scopeKey}|{branchFamily}|{targetType}|{targetRef}",
                Stage = branchFamily == Stage8RecomputeTargetFamilies.Stage6Bootstrap
                    ? "stage6_bootstrap"
                    : "stage7_durable_formation",
                PassFamily = branchFamily switch
                {
                    Stage8RecomputeTargetFamilies.DossierProfile => "dossier_profile",
                    Stage8RecomputeTargetFamilies.PairDynamics => "pair_dynamics",
                    Stage8RecomputeTargetFamilies.TimelineObjects => "timeline_objects",
                    _ => "graph_init"
                },
                TargetType = targetType,
                TargetRef = targetRef,
                PersonId = personId,
                LastModelPassRunId = Guid.Parse("86000000-0000-0000-0000-000000000001"),
                Status = ClarificationBranchStatuses.Open,
                BlockReason = blockReason,
                RequiredAction = "operator_clarification",
                FirstBlockedAtUtc = DateTime.UtcNow,
                LastBlockedAtUtc = DateTime.UtcNow
            });
        }

        public Task<ClarificationBranchStateRecord?> ApplyOutcomeAsync(
            ModelPassAuditRecord record,
            CancellationToken ct = default)
            => Task.FromResult<ClarificationBranchStateRecord?>(null);

        public Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAsync(
            string scopeKey,
            CancellationToken ct = default)
            => Task.FromResult(_branches
                .Where(x => string.Equals(x.ScopeKey, scopeKey, StringComparison.Ordinal)
                    && string.Equals(x.Status, ClarificationBranchStatuses.Open, StringComparison.Ordinal))
                .ToList());

        public Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAndFamilyAsync(
            string scopeKey,
            string branchFamily,
            CancellationToken ct = default)
            => Task.FromResult(_branches
                .Where(x => string.Equals(x.ScopeKey, scopeKey, StringComparison.Ordinal)
                    && string.Equals(x.BranchFamily, branchFamily, StringComparison.Ordinal)
                    && string.Equals(x.Status, ClarificationBranchStatuses.Open, StringComparison.Ordinal))
                .ToList());
    }
}

public sealed class Stage8ReintegrationLinkageSmokeReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("fatal_error")]
    public string? FatalError { get; set; }

    [JsonPropertyName("rows")]
    public List<Stage8ReintegrationLinkageSmokeRow> Rows { get; set; } = [];
}

public sealed class Stage8ReintegrationLinkageSmokeRow
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("expected_decision")]
    public string ExpectedDecision { get; set; } = string.Empty;

    [JsonPropertyName("actual_decision")]
    public string ActualDecision { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}

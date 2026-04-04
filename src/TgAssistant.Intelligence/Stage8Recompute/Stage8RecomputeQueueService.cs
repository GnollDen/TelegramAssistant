using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage8Recompute;

public class Stage8RecomputeQueueService : IStage8RecomputeQueueService
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultBackfillLeaseDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromMinutes(15);

    private readonly IStage8RecomputeQueueRepository _repository;
    private readonly IStage6BootstrapService _stage6BootstrapService;
    private readonly IStage7DossierProfileService _stage7DossierProfileService;
    private readonly IStage7PairDynamicsService _stage7PairDynamicsService;
    private readonly IStage7TimelineService _stage7TimelineService;
    private readonly IRuntimeControlStateService _runtimeControlStateService;
    private readonly IStage8OutcomeGateRepository _outcomeGateRepository;
    private readonly IStage8RelatedConflictRepository _relatedConflictRepository;
    private readonly IRuntimeDefectRepository _runtimeDefectRepository;
    private readonly IClarificationBranchStateRepository _clarificationBranchStateRepository;
    private readonly ILogger<Stage8RecomputeQueueService> _logger;
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _maxRetryDelay;

    public Stage8RecomputeQueueService(
        IStage8RecomputeQueueRepository repository,
        IStage6BootstrapService stage6BootstrapService,
        IStage7DossierProfileService stage7DossierProfileService,
        IStage7PairDynamicsService stage7PairDynamicsService,
        IStage7TimelineService stage7TimelineService,
        IRuntimeControlStateService runtimeControlStateService,
        IStage8OutcomeGateRepository outcomeGateRepository,
        IStage8RelatedConflictRepository relatedConflictRepository,
        IRuntimeDefectRepository runtimeDefectRepository,
        IClarificationBranchStateRepository clarificationBranchStateRepository,
        ILogger<Stage8RecomputeQueueService> logger,
        TimeSpan? baseRetryDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        _repository = repository;
        _stage6BootstrapService = stage6BootstrapService;
        _stage7DossierProfileService = stage7DossierProfileService;
        _stage7PairDynamicsService = stage7PairDynamicsService;
        _stage7TimelineService = stage7TimelineService;
        _runtimeControlStateService = runtimeControlStateService;
        _outcomeGateRepository = outcomeGateRepository;
        _relatedConflictRepository = relatedConflictRepository;
        _runtimeDefectRepository = runtimeDefectRepository;
        _clarificationBranchStateRepository = clarificationBranchStateRepository;
        _logger = logger;
        _baseRetryDelay = baseRetryDelay ?? DefaultBaseRetryDelay;
        _maxRetryDelay = maxRetryDelay ?? DefaultMaxRetryDelay;
    }

    public Task<Stage8RecomputeQueueItem> EnqueueAsync(
        Stage8RecomputeQueueRequest request,
        CancellationToken ct = default)
        => _repository.EnqueueAsync(request, ct);

    public async Task<Stage8RecomputeExecutionResult> ExecuteNextAsync(CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var leasedItem = await _repository.LeaseNextAsync(nowUtc, DefaultLeaseDuration, ct);
        if (leasedItem == null)
        {
            return new Stage8RecomputeExecutionResult
            {
                Executed = false,
                ExecutionStatus = Stage8RecomputeExecutionStatuses.NoWorkAvailable
            };
        }

        return await ExecuteLeasedItemAsync(leasedItem, nowUtc, ct);
    }

    public async Task<Stage8BackfillExecutionResult> ExecuteBackfillBatchAsync(
        Stage8BackfillExecutionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var maxConcurrentScopes = Math.Clamp(request.MaxConcurrentScopes, 1, 64);
        var maxItems = Math.Clamp(request.MaxItems, 1, 256);
        var workerId = string.IsNullOrWhiteSpace(request.WorkerId)
            ? $"stage8_backfill:{Environment.ProcessId}"
            : request.WorkerId.Trim();
        var leaseDuration = request.LeaseDuration ?? DefaultBackfillLeaseDuration;

        var result = new Stage8BackfillExecutionResult
        {
            MaxConcurrentScopes = maxConcurrentScopes,
            MaxItems = maxItems,
            WorkerId = workerId
        };

        for (var index = 0; index < maxItems && !ct.IsCancellationRequested; index += 1)
        {
            var nowUtc = DateTime.UtcNow;
            var leasedItem = await _repository.LeaseNextBackfillAsync(
                nowUtc,
                leaseDuration,
                maxConcurrentScopes,
                workerId,
                ct);
            if (leasedItem == null)
            {
                break;
            }

            var execution = await ExecuteLeasedItemAsync(leasedItem, nowUtc, ct);
            result.ExecutedCount += execution.Executed ? 1 : 0;
            if (string.Equals(execution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Completed, StringComparison.Ordinal))
            {
                result.CompletedCount += 1;
            }
            else if (string.Equals(execution.ExecutionStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal))
            {
                result.RescheduledCount += 1;
            }
            else if (string.Equals(execution.ExecutionStatus, Stage8RecomputeExecutionStatuses.FailedTerminally, StringComparison.Ordinal))
            {
                result.FailedCount += 1;
            }

            result.Items.Add(execution);
            var checkpoint = await _repository.GetBackfillCheckpointAsync(leasedItem.ScopeKey, ct);
            if (checkpoint != null)
            {
                var existing = result.Checkpoints.FindIndex(x => string.Equals(x.ScopeKey, checkpoint.ScopeKey, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    result.Checkpoints[existing] = checkpoint;
                }
                else
                {
                    result.Checkpoints.Add(checkpoint);
                }
            }
        }

        return result;
    }

    private async Task<Stage8RecomputeExecutionResult> ExecuteLeasedItemAsync(
        Stage8RecomputeQueueItem leasedItem,
        DateTime nowUtc,
        CancellationToken ct)
    {
        try
        {
            var runtimeControl = await _runtimeControlStateService.EvaluateAndApplyFromDefectsAsync(ct);
            if (ShouldDeferExecution(runtimeControl, leasedItem))
            {
                var deferReason = $"Runtime control state '{runtimeControl.State}' deferred Stage8 execution: {runtimeControl.Reason}";
                var nextAvailableAtUtc = nowUtc.Add(ComputeRetryDelay(leasedItem.AttemptCount));
                await _repository.RescheduleAsync(
                    leasedItem.Id,
                    leasedItem.LeaseToken!.Value,
                    deferReason,
                    nextAvailableAtUtc,
                    terminalFailure: false,
                    recoveryTelemetry: null,
                    ct);

                leasedItem.Status = Stage8RecomputeQueueStatuses.Pending;
                leasedItem.LastError = deferReason;
                leasedItem.LastResultStatus = Stage8RecomputeExecutionStatuses.Rescheduled;
                leasedItem.LeaseToken = null;
                leasedItem.LeasedUntilUtc = null;
                leasedItem.AvailableAtUtc = nextAvailableAtUtc;
                leasedItem.CompletedAtUtc = null;

                _logger.LogInformation(
                    "Stage8 recompute deferred by runtime control state: queue_item_id={QueueItemId}, target_family={TargetFamily}, state={ControlState}, reason={ControlReason}",
                    leasedItem.Id,
                    leasedItem.TargetFamily,
                    runtimeControl.State,
                    runtimeControl.Reason);

                return new Stage8RecomputeExecutionResult
                {
                    Executed = true,
                    QueueItem = leasedItem,
                    ExecutionStatus = Stage8RecomputeExecutionStatuses.Rescheduled,
                    Error = deferReason
                };
            }

            var blockedBranchResult = await TryCompleteBlockedBranchAsync(leasedItem, runtimeControl.State, ct);
            if (blockedBranchResult != null)
            {
                return blockedBranchResult;
            }

            var (resultStatus, modelPassRunId) = await ExecuteScopedRecomputeAsync(leasedItem, ct);
            var gateResult = await _outcomeGateRepository.ApplyOutcomeGateAsync(new Stage8OutcomeGateRequest
            {
                ScopeKey = leasedItem.ScopeKey,
                PersonId = leasedItem.PersonId,
                TargetFamily = leasedItem.TargetFamily,
                TargetRef = leasedItem.TargetRef,
                ResultStatus = resultStatus,
                ModelPassRunId = modelPassRunId,
                TriggerKind = leasedItem.TriggerKind,
                TriggerRef = leasedItem.TriggerRef,
                ForcePromotionBlocked = runtimeControl.ForcePromotionBlocked,
                RuntimeControlState = runtimeControl.State
            }, ct);
            await RecordOutcomeDefectsAsync(leasedItem, resultStatus, gateResult, modelPassRunId, runtimeControl.State, ct);
            var relatedConflictResult = await _relatedConflictRepository.ReevaluateAsync(new Stage8RelatedConflictReevaluationRequest
            {
                QueueItemId = leasedItem.Id,
                ScopeKey = leasedItem.ScopeKey,
                PersonId = leasedItem.PersonId,
                TargetFamily = leasedItem.TargetFamily,
                TargetRef = leasedItem.TargetRef,
                ResultStatus = resultStatus,
                TriggerKind = leasedItem.TriggerKind,
                TriggerRef = leasedItem.TriggerRef,
                ModelPassRunId = modelPassRunId
            }, ct);
            await _repository.CompleteAsync(leasedItem.Id, leasedItem.LeaseToken!.Value, resultStatus, modelPassRunId, ct);
            await _runtimeDefectRepository.ResolveOpenByDedupeKeyAsync(
                $"{leasedItem.ScopeKey}|{leasedItem.TargetFamily}|execution_failure",
                modelPassRunId,
                ct);
            leasedItem.Status = Stage8RecomputeQueueStatuses.Completed;
            leasedItem.ActiveDedupeKey = null;
            leasedItem.LastError = null;
            leasedItem.LastResultStatus = resultStatus;
            leasedItem.LastModelPassRunId = modelPassRunId;
            leasedItem.CompletedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Stage8 recompute completed: queue_item_id={QueueItemId}, target_family={TargetFamily}, target_ref={TargetRef}, result_status={ResultStatus}, gate_affected={GateAffected}, gate_promoted={GatePromoted}, gate_promotion_blocked={GatePromotionBlocked}, gate_clarification_blocked={GateClarificationBlocked}, related_conflicts_applied={RelatedConflictsApplied}, related_conflicts_created={RelatedConflictCreated}, related_conflicts_refreshed={RelatedConflictRefreshed}, related_conflicts_resolved={RelatedConflictResolved}, related_conflicts_unchanged={RelatedConflictUnchanged}, related_conflicts_skip_reason={RelatedConflictSkipReason}",
                leasedItem.Id,
                leasedItem.TargetFamily,
                leasedItem.TargetRef,
                resultStatus,
                gateResult.AffectedCount,
                gateResult.PromotedCount,
                gateResult.PromotionBlockedCount,
                gateResult.ClarificationBlockedCount,
                relatedConflictResult.Applied,
                relatedConflictResult.CreatedCount,
                relatedConflictResult.RefreshedCount,
                relatedConflictResult.ResolvedCount,
                relatedConflictResult.UnchangedCount,
                relatedConflictResult.SkipReason);

            return new Stage8RecomputeExecutionResult
            {
                Executed = true,
                QueueItem = leasedItem,
                ExecutionStatus = Stage8RecomputeExecutionStatuses.Completed,
                ResultStatus = resultStatus,
                ModelPassRunId = modelPassRunId
            };
        }
        catch (Exception ex)
        {
            var terminalFailure = leasedItem.AttemptCount >= leasedItem.MaxAttempts;
            var recoveryTelemetry = ClassifyRecoveryTelemetry(ex, leasedItem.AttemptCount, nowUtc);
            var nextAvailableAtUtc = recoveryTelemetry.NextAttemptAtUtc ?? nowUtc.Add(ComputeRetryDelay(leasedItem.AttemptCount));
            await RecordExecutionFailureDefectAsync(leasedItem, ex, terminalFailure, recoveryTelemetry, ct);
            await _repository.RescheduleAsync(
                leasedItem.Id,
                leasedItem.LeaseToken!.Value,
                ex.Message,
                nextAvailableAtUtc,
                terminalFailure,
                recoveryTelemetry,
                ct);

            leasedItem.Status = terminalFailure
                ? Stage8RecomputeQueueStatuses.Failed
                : Stage8RecomputeQueueStatuses.Pending;
            leasedItem.LastError = ex.Message;
            leasedItem.LastResultStatus = terminalFailure
                ? Stage8RecomputeExecutionStatuses.FailedTerminally
                : Stage8RecomputeExecutionStatuses.Rescheduled;
            leasedItem.LeaseToken = null;
            leasedItem.LeasedUntilUtc = null;
            leasedItem.AvailableAtUtc = nextAvailableAtUtc;
            leasedItem.CompletedAtUtc = terminalFailure ? DateTime.UtcNow : null;

            _logger.LogWarning(
                ex,
                "Stage8 recompute execution failed: queue_item_id={QueueItemId}, target_family={TargetFamily}, target_ref={TargetRef}, terminal={TerminalFailure}, attempt={AttemptCount}, max_attempts={MaxAttempts}, recovery_kind={RecoveryKind}, retryable_conflict={RetryableConflict}",
                leasedItem.Id,
                leasedItem.TargetFamily,
                leasedItem.TargetRef,
                terminalFailure,
                leasedItem.AttemptCount,
                leasedItem.MaxAttempts,
                recoveryTelemetry.RecoveryKind,
                recoveryTelemetry.IsDeadlock || recoveryTelemetry.IsTransientConflict);

            return new Stage8RecomputeExecutionResult
            {
                Executed = true,
                QueueItem = leasedItem,
                ExecutionStatus = terminalFailure
                    ? Stage8RecomputeExecutionStatuses.FailedTerminally
                    : Stage8RecomputeExecutionStatuses.Rescheduled,
                Error = ex.Message
            };
        }
    }

    private async Task<Stage8RecomputeExecutionResult?> TryCompleteBlockedBranchAsync(
        Stage8RecomputeQueueItem queueItem,
        string runtimeControlState,
        CancellationToken ct)
    {
        var openBranches = await _clarificationBranchStateRepository.GetOpenByScopeAndFamilyAsync(
            queueItem.ScopeKey,
            queueItem.TargetFamily,
            ct);
        if (openBranches.Count == 0)
        {
            return null;
        }

        var branch = openBranches
            .FirstOrDefault(x => BranchMatches(queueItem, x))
            ?? openBranches[0];
        var resultStatus = ModelPassResultStatuses.NeedOperatorClarification;
        var gateResult = await _outcomeGateRepository.ApplyOutcomeGateAsync(new Stage8OutcomeGateRequest
        {
            ScopeKey = queueItem.ScopeKey,
            PersonId = queueItem.PersonId,
            TargetFamily = queueItem.TargetFamily,
            TargetRef = queueItem.TargetRef,
            ResultStatus = resultStatus,
            ModelPassRunId = branch.LastModelPassRunId,
            TriggerKind = queueItem.TriggerKind,
            TriggerRef = queueItem.TriggerRef,
            RuntimeControlState = runtimeControlState
        }, ct);

        await _repository.CompleteAsync(queueItem.Id, queueItem.LeaseToken!.Value, resultStatus, branch.LastModelPassRunId, ct);
        queueItem.Status = Stage8RecomputeQueueStatuses.Completed;
        queueItem.ActiveDedupeKey = null;
        queueItem.LastError = null;
        queueItem.LastResultStatus = resultStatus;
        queueItem.LastModelPassRunId = branch.LastModelPassRunId;
        queueItem.LeaseToken = null;
        queueItem.LeasedUntilUtc = null;
        queueItem.CompletedAtUtc = DateTime.UtcNow;

        _logger.LogInformation(
            "Stage8 recompute held on open clarification branch while other families remain eligible: queue_item_id={QueueItemId}, scope_key={ScopeKey}, target_family={TargetFamily}, branch_key={BranchKey}, clarification_blocked={ClarificationBlocked}",
            queueItem.Id,
            queueItem.ScopeKey,
            queueItem.TargetFamily,
            branch.BranchKey,
            gateResult.ClarificationBlockedCount);

        return new Stage8RecomputeExecutionResult
        {
            Executed = true,
            QueueItem = queueItem,
            ExecutionStatus = Stage8RecomputeExecutionStatuses.Completed,
            ResultStatus = resultStatus,
            ModelPassRunId = branch.LastModelPassRunId,
            Error = branch.BlockReason
        };
    }

    private async Task<(string ResultStatus, Guid? ModelPassRunId)> ExecuteScopedRecomputeAsync(
        Stage8RecomputeQueueItem queueItem,
        CancellationToken ct)
    {
        var bootstrapRequest = new Stage6BootstrapRequest
        {
            PersonId = queueItem.PersonId,
            ScopeKey = queueItem.ScopeKey,
            RunKind = "stage8_recompute_queue",
            TriggerKind = queueItem.TriggerKind,
            TriggerRef = queueItem.TriggerRef
        };

        if (string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal))
        {
            var bootstrapResult = await _stage6BootstrapService.RunGraphInitializationAsync(bootstrapRequest, ct);
            return (bootstrapResult.AuditRecord.Envelope.ResultStatus, bootstrapResult.AuditRecord.ModelPassRunId);
        }

        var upstreamBootstrap = await _stage6BootstrapService.RunGraphInitializationAsync(bootstrapRequest, ct);

        if (string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.DossierProfile, StringComparison.Ordinal))
        {
            var dossierResult = await _stage7DossierProfileService.FormAsync(new Stage7DossierProfileFormationRequest
            {
                BootstrapResult = upstreamBootstrap,
                RunKind = "stage8_recompute_queue",
                TriggerKind = queueItem.TriggerKind,
                TriggerRef = queueItem.TriggerRef
            }, ct);
            return (dossierResult.AuditRecord.Envelope.ResultStatus, dossierResult.AuditRecord.ModelPassRunId);
        }

        if (string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.PairDynamics, StringComparison.Ordinal))
        {
            var pairResult = await _stage7PairDynamicsService.FormAsync(new Stage7PairDynamicsFormationRequest
            {
                BootstrapResult = upstreamBootstrap,
                RunKind = "stage8_recompute_queue",
                TriggerKind = queueItem.TriggerKind,
                TriggerRef = queueItem.TriggerRef
            }, ct);
            return (pairResult.AuditRecord.Envelope.ResultStatus, pairResult.AuditRecord.ModelPassRunId);
        }

        if (string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.TimelineObjects, StringComparison.Ordinal))
        {
            var timelineResult = await _stage7TimelineService.FormAsync(new Stage7TimelineFormationRequest
            {
                BootstrapResult = upstreamBootstrap,
                RunKind = "stage8_recompute_queue",
                TriggerKind = queueItem.TriggerKind,
                TriggerRef = queueItem.TriggerRef
            }, ct);
            return (timelineResult.AuditRecord.Envelope.ResultStatus, timelineResult.AuditRecord.ModelPassRunId);
        }

        return (ModelPassResultStatuses.BlockedInvalidInput, null);
    }

    private TimeSpan ComputeRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(Math.Max(attemptCount - 1, 0), 0, 10);
        var retryDelay = TimeSpan.FromTicks(_baseRetryDelay.Ticks * (1L << exponent));
        return retryDelay <= _maxRetryDelay ? retryDelay : _maxRetryDelay;
    }

    private Stage8BackfillRecoveryTelemetry ClassifyRecoveryTelemetry(
        Exception ex,
        int attemptCount,
        DateTime nowUtc)
    {
        var recoveryKind = Stage8BackfillRecoveryKinds.GeneralRetry;
        var isDeadlock = IsDeadlock(ex);
        var isTransientConflict = isDeadlock || IsTransientConflict(ex);
        if (isDeadlock)
        {
            recoveryKind = Stage8BackfillRecoveryKinds.DeadlockRetry;
        }
        else if (isTransientConflict)
        {
            recoveryKind = Stage8BackfillRecoveryKinds.TransientConflictRetry;
        }

        return new Stage8BackfillRecoveryTelemetry
        {
            RecoveryKind = recoveryKind,
            IsDeadlock = isDeadlock,
            IsTransientConflict = isTransientConflict,
            OccurredAtUtc = nowUtc,
            NextAttemptAtUtc = nowUtc.Add(ComputeRetryDelay(attemptCount))
        };
    }

    private async Task RecordOutcomeDefectsAsync(
        Stage8RecomputeQueueItem queueItem,
        string resultStatus,
        Stage8OutcomeGateResult gateResult,
        Guid? modelPassRunId,
        string runtimeControlState,
        CancellationToken ct)
    {
        if (string.Equals(resultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
        {
            await _runtimeDefectRepository.UpsertAsync(new RuntimeDefectUpsertRequest
            {
                DefectClass = RuntimeDefectClasses.Normalization,
                Severity = RuntimeDefectSeverities.Medium,
                ScopeKey = queueItem.ScopeKey,
                DedupeKey = $"{queueItem.ScopeKey}|{queueItem.TargetFamily}|clarification_gate",
                RunId = modelPassRunId,
                ObjectType = queueItem.TargetFamily,
                ObjectRef = queueItem.TargetRef,
                Summary = "Crystallization result requires operator clarification before promotion.",
                DetailsJson = $$"""
                    {"result_status":"{{resultStatus}}","promotion_blocked":{{gateResult.PromotionBlockedCount}},"clarification_blocked":{{gateResult.ClarificationBlockedCount}},"runtime_control_state":"{{runtimeControlState}}"}
                    """
            }, ct);
            return;
        }

        if (string.Equals(resultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal))
        {
            await _runtimeDefectRepository.UpsertAsync(new RuntimeDefectUpsertRequest
            {
                DefectClass = RuntimeDefectClasses.Data,
                Severity = RuntimeDefectSeverities.High,
                ScopeKey = queueItem.ScopeKey,
                DedupeKey = $"{queueItem.ScopeKey}|{queueItem.TargetFamily}|invalid_input",
                RunId = modelPassRunId,
                ObjectType = queueItem.TargetFamily,
                ObjectRef = queueItem.TargetRef,
                Summary = "Crystallization execution was blocked by invalid scope input.",
                DetailsJson = $$"""{"result_status":"{{resultStatus}}","runtime_control_state":"{{runtimeControlState}}"}"""
            }, ct);
            return;
        }

        if (gateResult.PromotionBlockedCount > 0 && string.Equals(resultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            await _runtimeDefectRepository.UpsertAsync(new RuntimeDefectUpsertRequest
            {
                DefectClass = RuntimeDefectClasses.SemanticDrift,
                Severity = RuntimeDefectSeverities.High,
                ScopeKey = queueItem.ScopeKey,
                DedupeKey = $"{queueItem.ScopeKey}|{queueItem.TargetFamily}|promotion_blocked",
                RunId = modelPassRunId,
                ObjectType = queueItem.TargetFamily,
                ObjectRef = queueItem.TargetRef,
                Summary = "Crystallization produced result_ready output but promotion was blocked by truth-layer gate.",
                DetailsJson = $$"""{"result_status":"{{resultStatus}}","promotion_blocked":{{gateResult.PromotionBlockedCount}},"runtime_control_state":"{{runtimeControlState}}"}"""
            }, ct);
            return;
        }

        if (string.Equals(resultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal))
        {
            await _runtimeDefectRepository.UpsertAsync(new RuntimeDefectUpsertRequest
            {
                DefectClass = RuntimeDefectClasses.Ingestion,
                Severity = RuntimeDefectSeverities.Medium,
                ScopeKey = queueItem.ScopeKey,
                DedupeKey = $"{queueItem.ScopeKey}|{queueItem.TargetFamily}|need_more_data",
                RunId = modelPassRunId,
                ObjectType = queueItem.TargetFamily,
                ObjectRef = queueItem.TargetRef,
                Summary = "Crystallization requires additional evidence before promotion.",
                DetailsJson = $$"""{"result_status":"{{resultStatus}}","runtime_control_state":"{{runtimeControlState}}"}"""
            }, ct);
        }
    }

    private Task RecordExecutionFailureDefectAsync(
        Stage8RecomputeQueueItem queueItem,
        Exception ex,
        bool terminalFailure,
        Stage8BackfillRecoveryTelemetry recoveryTelemetry,
        CancellationToken ct)
    {
        return _runtimeDefectRepository.UpsertAsync(new RuntimeDefectUpsertRequest
        {
            DefectClass = RuntimeDefectClasses.ControlPlane,
            Severity = terminalFailure ? RuntimeDefectSeverities.Critical : RuntimeDefectSeverities.High,
            ScopeKey = queueItem.ScopeKey,
            DedupeKey = $"{queueItem.ScopeKey}|{queueItem.TargetFamily}|execution_failure",
            RunId = queueItem.LastModelPassRunId,
            ObjectType = queueItem.TargetFamily,
            ObjectRef = queueItem.TargetRef,
            Summary = "Stage8 crystallization queue execution failed.",
            DetailsJson = $$"""{"error":"{{EscapeJson(ex.Message)}}","terminal":{{(terminalFailure ? "true" : "false")}},"recovery_kind":"{{recoveryTelemetry.RecoveryKind}}","is_deadlock":{{(recoveryTelemetry.IsDeadlock ? "true" : "false")}},"is_transient_conflict":{{(recoveryTelemetry.IsTransientConflict ? "true" : "false")}},"next_attempt_at_utc":"{{recoveryTelemetry.NextAttemptAtUtc?.ToString("O")}}"}"""
        }, ct);
    }

    private static bool IsDeadlock(Exception ex)
    {
        return FindPostgresException(ex) is { SqlState: PostgresErrorCodes.DeadlockDetected }
               || ex.Message.Contains("deadlock detected", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTransientConflict(Exception ex)
    {
        var postgres = FindPostgresException(ex);
        if (postgres != null)
        {
            return string.Equals(postgres.SqlState, PostgresErrorCodes.SerializationFailure, StringComparison.Ordinal)
                   || string.Equals(postgres.SqlState, PostgresErrorCodes.LockNotAvailable, StringComparison.Ordinal);
        }

        return ex.Message.Contains("could not serialize access", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("could not obtain lock", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("transient conflict", StringComparison.OrdinalIgnoreCase);
    }

    private static PostgresException? FindPostgresException(Exception ex)
    {
        return ex switch
        {
            PostgresException postgres => postgres,
            DbUpdateException { InnerException: PostgresException postgres } => postgres,
            _ when ex.InnerException != null => FindPostgresException(ex.InnerException),
            _ => null
        };
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool BranchMatches(Stage8RecomputeQueueItem queueItem, ClarificationBranchStateRecord branch)
    {
        if (!string.Equals(queueItem.ScopeKey, branch.ScopeKey, StringComparison.Ordinal)
            || !string.Equals(queueItem.TargetFamily, branch.BranchFamily, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(queueItem.TargetRef)
            && string.Equals(queueItem.TargetRef, branch.TargetRef, StringComparison.Ordinal))
        {
            return true;
        }

        return queueItem.PersonId != null
            && branch.PersonId != null
            && queueItem.PersonId == branch.PersonId;
    }

    private static bool ShouldDeferExecution(RuntimeControlEnforcementDecision decision, Stage8RecomputeQueueItem queueItem)
    {
        if (decision.PauseAllExecution)
        {
            return true;
        }

        if (decision.RestrictToBootstrapOnly
            && !string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.Stage6Bootstrap, StringComparison.Ordinal))
        {
            return true;
        }

        if (decision.DeferTimelineTargets
            && string.Equals(queueItem.TargetFamily, Stage8RecomputeTargetFamilies.TimelineObjects, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }
}

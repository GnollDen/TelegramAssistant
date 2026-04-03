using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage8Recompute;

public class Stage8RecomputeQueueService : IStage8RecomputeQueueService
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultBaseRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultMaxRetryDelay = TimeSpan.FromMinutes(15);

    private readonly IStage8RecomputeQueueRepository _repository;
    private readonly IStage6BootstrapService _stage6BootstrapService;
    private readonly IStage7DossierProfileService _stage7DossierProfileService;
    private readonly IStage7PairDynamicsService _stage7PairDynamicsService;
    private readonly IStage7TimelineService _stage7TimelineService;
    private readonly IStage8OutcomeGateRepository _outcomeGateRepository;
    private readonly IRuntimeDefectRepository _runtimeDefectRepository;
    private readonly ILogger<Stage8RecomputeQueueService> _logger;
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _maxRetryDelay;

    public Stage8RecomputeQueueService(
        IStage8RecomputeQueueRepository repository,
        IStage6BootstrapService stage6BootstrapService,
        IStage7DossierProfileService stage7DossierProfileService,
        IStage7PairDynamicsService stage7PairDynamicsService,
        IStage7TimelineService stage7TimelineService,
        IStage8OutcomeGateRepository outcomeGateRepository,
        IRuntimeDefectRepository runtimeDefectRepository,
        ILogger<Stage8RecomputeQueueService> logger,
        TimeSpan? baseRetryDelay = null,
        TimeSpan? maxRetryDelay = null)
    {
        _repository = repository;
        _stage6BootstrapService = stage6BootstrapService;
        _stage7DossierProfileService = stage7DossierProfileService;
        _stage7PairDynamicsService = stage7PairDynamicsService;
        _stage7TimelineService = stage7TimelineService;
        _outcomeGateRepository = outcomeGateRepository;
        _runtimeDefectRepository = runtimeDefectRepository;
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

        try
        {
            var (resultStatus, modelPassRunId) = await ExecuteScopedRecomputeAsync(leasedItem, ct);
            var gateResult = await _outcomeGateRepository.ApplyOutcomeGateAsync(new Stage8OutcomeGateRequest
            {
                ScopeKey = leasedItem.ScopeKey,
                TargetFamily = leasedItem.TargetFamily,
                ResultStatus = resultStatus,
                ModelPassRunId = modelPassRunId,
                TriggerKind = leasedItem.TriggerKind,
                TriggerRef = leasedItem.TriggerRef
            }, ct);
            await RecordOutcomeDefectsAsync(leasedItem, resultStatus, gateResult, modelPassRunId, ct);
            await _repository.CompleteAsync(leasedItem.Id, leasedItem.LeaseToken!.Value, resultStatus, modelPassRunId, ct);
            leasedItem.Status = Stage8RecomputeQueueStatuses.Completed;
            leasedItem.ActiveDedupeKey = null;
            leasedItem.LastError = null;
            leasedItem.LastResultStatus = resultStatus;
            leasedItem.LastModelPassRunId = modelPassRunId;
            leasedItem.CompletedAtUtc = DateTime.UtcNow;

            _logger.LogInformation(
                "Stage8 recompute completed: queue_item_id={QueueItemId}, target_family={TargetFamily}, target_ref={TargetRef}, result_status={ResultStatus}, gate_affected={GateAffected}, gate_promoted={GatePromoted}, gate_promotion_blocked={GatePromotionBlocked}, gate_clarification_blocked={GateClarificationBlocked}",
                leasedItem.Id,
                leasedItem.TargetFamily,
                leasedItem.TargetRef,
                resultStatus,
                gateResult.AffectedCount,
                gateResult.PromotedCount,
                gateResult.PromotionBlockedCount,
                gateResult.ClarificationBlockedCount);

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
            var nextAvailableAtUtc = nowUtc.Add(ComputeRetryDelay(leasedItem.AttemptCount));
            await RecordExecutionFailureDefectAsync(leasedItem, ex, terminalFailure, ct);
            await _repository.RescheduleAsync(
                leasedItem.Id,
                leasedItem.LeaseToken!.Value,
                ex.Message,
                nextAvailableAtUtc,
                terminalFailure,
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
                "Stage8 recompute execution failed: queue_item_id={QueueItemId}, target_family={TargetFamily}, target_ref={TargetRef}, terminal={TerminalFailure}, attempt={AttemptCount}, max_attempts={MaxAttempts}",
                leasedItem.Id,
                leasedItem.TargetFamily,
                leasedItem.TargetRef,
                terminalFailure,
                leasedItem.AttemptCount,
                leasedItem.MaxAttempts);

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

    private async Task RecordOutcomeDefectsAsync(
        Stage8RecomputeQueueItem queueItem,
        string resultStatus,
        Stage8OutcomeGateResult gateResult,
        Guid? modelPassRunId,
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
                    {"result_status":"{{resultStatus}}","promotion_blocked":{{gateResult.PromotionBlockedCount}},"clarification_blocked":{{gateResult.ClarificationBlockedCount}}}
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
                DetailsJson = $$"""{"result_status":"{{resultStatus}}"}"""
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
                DetailsJson = $$"""{"result_status":"{{resultStatus}}","promotion_blocked":{{gateResult.PromotionBlockedCount}}}"""
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
                DetailsJson = $$"""{"result_status":"{{resultStatus}}"}"""
            }, ct);
        }
    }

    private Task RecordExecutionFailureDefectAsync(
        Stage8RecomputeQueueItem queueItem,
        Exception ex,
        bool terminalFailure,
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
            DetailsJson = $$"""{"error":"{{EscapeJson(ex.Message)}}","terminal":{{(terminalFailure ? "true" : "false")}}}"""
        }, ct);
    }

    private static string EscapeJson(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

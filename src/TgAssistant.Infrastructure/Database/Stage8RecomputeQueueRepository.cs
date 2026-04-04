using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage8RecomputeQueueRepository : IStage8RecomputeQueueRepository
{
    private const long BackfillCoordinationLockId = 8_016_001;

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<Stage8RecomputeQueueRepository> _logger;

    public Stage8RecomputeQueueRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<Stage8RecomputeQueueRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<Stage8RecomputeQueueItem> EnqueueAsync(
        Stage8RecomputeQueueRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var enqueueResult = await Stage8RecomputeQueueStorage.EnqueueAsync(db, request, ct);

        _logger.LogInformation(
            enqueueResult.Created
                ? "Stage8 recompute queued: queue_item_id={QueueItemId}, scope_key={ScopeKey}, target_family={TargetFamily}, target_ref={TargetRef}"
                : "Stage8 recompute deduped: queue_item_id={QueueItemId}, scope_key={ScopeKey}, target_family={TargetFamily}, target_ref={TargetRef}",
            enqueueResult.Item.Id,
            enqueueResult.Item.ScopeKey,
            enqueueResult.Item.TargetFamily,
            enqueueResult.Item.TargetRef);
        return enqueueResult.Item;
    }

    public async Task<Stage8RecomputeQueueItem?> LeaseNextAsync(
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var item = await LeaseNextPendingItemAsync(db, nowUtc, leaseDuration, ct);
        await tx.CommitAsync(ct);
        return item;
    }

    public async Task<Stage8RecomputeQueueItem?> LeaseNextBackfillAsync(
        DateTime nowUtc,
        TimeSpan leaseDuration,
        int maxConcurrentScopes,
        string workerId,
        CancellationToken ct = default)
    {
        if (maxConcurrentScopes <= 0)
        {
            throw new InvalidOperationException("Stage8 backfill leasing requires maxConcurrentScopes > 0.");
        }

        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new InvalidOperationException("Stage8 backfill leasing requires a non-empty workerId.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        await AcquireBackfillCoordinationLockAsync(db, ct);
        await ReleaseExpiredBackfillLeasesAsync(db, nowUtc, ct);

        var activeScopeCount = await db.Stage8BackfillScopeCheckpoints.CountAsync(
            x => x.Status == Stage8BackfillCheckpointStatuses.InProgress
                && x.LeaseExpiresAtUtc != null
                && x.LeaseExpiresAtUtc > nowUtc,
            ct);
        if (activeScopeCount >= maxConcurrentScopes)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        var leasedItem = await LeaseNextPendingItemAsync(
            db,
            nowUtc,
            leaseDuration,
            ct,
            excludeScopesWithActiveBackfillLease: true);
        if (leasedItem == null)
        {
            await tx.CommitAsync(ct);
            return null;
        }

        await UpsertBackfillCheckpointAsync(
            db,
            leasedItem,
            workerId.Trim(),
            nowUtc,
            leasedItem.LeasedUntilUtc ?? nowUtc.Add(leaseDuration),
            ct);

        await tx.CommitAsync(ct);
        return leasedItem;
    }

    public async Task CompleteAsync(
        Guid queueItemId,
        Guid leaseToken,
        string resultStatus,
        Guid? modelPassRunId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var nowUtc = DateTime.UtcNow;
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update stage8_recompute_queue_items
            set status = {Stage8RecomputeQueueStatuses.Completed},
                active_dedupe_key = null,
                leased_until_utc = null,
                lease_token = null,
                last_error = null,
                last_result_status = {resultStatus},
                last_model_pass_run_id = {modelPassRunId},
                completed_at_utc = {nowUtc},
                updated_at_utc = {nowUtc}
            where id = {queueItemId}
              and status = {Stage8RecomputeQueueStatuses.Leased}
              and lease_token = {leaseToken}
            """, ct);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Failed to complete Stage8 recompute queue item '{queueItemId:D}' for the current lease.");
        }

        await UpdateBackfillCheckpointAfterLeaseReleaseAsync(
            db,
            queueItemId,
            resultStatus,
            modelPassRunId,
            lastError: null,
            terminalFailure: false,
            recoveryTelemetry: null,
            nowUtc,
            ct);
        await tx.CommitAsync(ct);
    }

    public async Task RescheduleAsync(
        Guid queueItemId,
        Guid leaseToken,
        string error,
        DateTime nextAvailableAtUtc,
        bool terminalFailure,
        Stage8BackfillRecoveryTelemetry? recoveryTelemetry = null,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var nowUtc = DateTime.UtcNow;
        var resultStatus = terminalFailure
            ? Stage8RecomputeExecutionStatuses.FailedTerminally
            : Stage8RecomputeExecutionStatuses.Rescheduled;
        var status = terminalFailure
            ? Stage8RecomputeQueueStatuses.Failed
            : Stage8RecomputeQueueStatuses.Pending;
        var activeDedupeKey = terminalFailure
            ? null
            : await db.Stage8RecomputeQueueItems
                .Where(x => x.Id == queueItemId)
                .Select(x => x.DedupeKey)
                .FirstOrDefaultAsync(ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            update stage8_recompute_queue_items
            set status = {status},
                active_dedupe_key = {activeDedupeKey},
                leased_until_utc = null,
                lease_token = null,
                last_error = {error},
                last_result_status = {resultStatus},
                available_at_utc = {nextAvailableAtUtc},
                updated_at_utc = {nowUtc},
                completed_at_utc = {(terminalFailure ? nowUtc : (DateTime?)null)}
            where id = {queueItemId}
              and status = {Stage8RecomputeQueueStatuses.Leased}
              and lease_token = {leaseToken}
            """, ct);
        if (affected != 1)
        {
            throw new InvalidOperationException($"Failed to reschedule Stage8 recompute queue item '{queueItemId:D}' for the current lease.");
        }

        await UpdateBackfillCheckpointAfterLeaseReleaseAsync(
            db,
            queueItemId,
            resultStatus,
            modelPassRunId: null,
            lastError: error,
            terminalFailure,
            recoveryTelemetry,
            nowUtc,
            ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Stage8BackfillCheckpoint?> GetBackfillCheckpointAsync(
        string scopeKey,
        CancellationToken ct = default)
    {
        var normalizedScopeKey = scopeKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedScopeKey))
        {
            throw new InvalidOperationException("Stage8 backfill checkpoint lookup requires a non-empty scope key.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.Stage8BackfillScopeCheckpoints.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScopeKey == normalizedScopeKey, ct);
        return row == null ? null : MapCheckpoint(row);
    }

    private async Task<Stage8RecomputeQueueItem?> LeaseNextPendingItemAsync(
        TgAssistantDbContext db,
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct,
        bool excludeScopesWithActiveBackfillLease = false)
    {
        var leasedUntilUtc = nowUtc.Add(leaseDuration);
        var leaseToken = Guid.NewGuid();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = excludeScopesWithActiveBackfillLease
            ? """
                with next_item as (
                    select queue_item.id
                    from stage8_recompute_queue_items queue_item
                    where queue_item.status = @pending_status
                      and queue_item.available_at_utc <= @now_utc
                      and not exists (
                          select 1
                          from stage8_backfill_scope_checkpoints checkpoint
                          where checkpoint.scope_key = queue_item.scope_key
                            and checkpoint.status = @checkpoint_in_progress_status
                            and checkpoint.lease_expires_at_utc is not null
                            and checkpoint.lease_expires_at_utc > @now_utc
                      )
                    order by queue_item.priority asc, queue_item.available_at_utc asc, queue_item.created_at_utc asc
                    for update skip locked
                    limit 1
                )
                update stage8_recompute_queue_items queue_item
                set status = @leased_status,
                    attempt_count = queue_item.attempt_count + 1,
                    leased_until_utc = @leased_until_utc,
                    lease_token = @lease_token,
                    updated_at_utc = @now_utc
                from next_item
                where queue_item.id = next_item.id
                returning
                    queue_item.id,
                    queue_item.scope_key,
                    queue_item.person_id,
                    queue_item.target_family,
                    queue_item.target_ref,
                    queue_item.dedupe_key,
                    queue_item.active_dedupe_key,
                    queue_item.trigger_kind,
                    queue_item.trigger_ref,
                    queue_item.status,
                    queue_item.priority,
                    queue_item.attempt_count,
                    queue_item.max_attempts,
                    queue_item.available_at_utc,
                    queue_item.leased_until_utc,
                    queue_item.lease_token,
                    queue_item.last_error,
                    queue_item.last_result_status,
                    queue_item.last_model_pass_run_id,
                    queue_item.created_at_utc,
                    queue_item.updated_at_utc,
                    queue_item.completed_at_utc;
                """
            : """
                with next_item as (
                    select id
                    from stage8_recompute_queue_items
                    where status = @pending_status
                      and available_at_utc <= @now_utc
                    order by priority asc, available_at_utc asc, created_at_utc asc
                    for update skip locked
                    limit 1
                )
                update stage8_recompute_queue_items queue_item
                set status = @leased_status,
                    attempt_count = queue_item.attempt_count + 1,
                    leased_until_utc = @leased_until_utc,
                    lease_token = @lease_token,
                    updated_at_utc = @now_utc
                from next_item
                where queue_item.id = next_item.id
                returning
                    queue_item.id,
                    queue_item.scope_key,
                    queue_item.person_id,
                    queue_item.target_family,
                    queue_item.target_ref,
                    queue_item.dedupe_key,
                    queue_item.active_dedupe_key,
                    queue_item.trigger_kind,
                    queue_item.trigger_ref,
                    queue_item.status,
                    queue_item.priority,
                    queue_item.attempt_count,
                    queue_item.max_attempts,
                    queue_item.available_at_utc,
                    queue_item.leased_until_utc,
                    queue_item.lease_token,
                    queue_item.last_error,
                    queue_item.last_result_status,
                    queue_item.last_model_pass_run_id,
                    queue_item.created_at_utc,
                    queue_item.updated_at_utc,
                    queue_item.completed_at_utc;
                """;
        Stage8RecomputeQueueStorage.AddParameter(command, "pending_status", Stage8RecomputeQueueStatuses.Pending);
        Stage8RecomputeQueueStorage.AddParameter(command, "leased_status", Stage8RecomputeQueueStatuses.Leased);
        Stage8RecomputeQueueStorage.AddParameter(command, "checkpoint_in_progress_status", Stage8BackfillCheckpointStatuses.InProgress);
        Stage8RecomputeQueueStorage.AddParameter(command, "now_utc", nowUtc);
        Stage8RecomputeQueueStorage.AddParameter(command, "leased_until_utc", leasedUntilUtc);
        Stage8RecomputeQueueStorage.AddParameter(command, "lease_token", leaseToken);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Stage8RecomputeQueueStorage.MapQueueItem(reader);
    }

    private async Task AcquireBackfillCoordinationLockAsync(
        TgAssistantDbContext db,
        CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "select pg_advisory_xact_lock(@lock_id);";
        Stage8RecomputeQueueStorage.AddParameter(command, "lock_id", BackfillCoordinationLockId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReleaseExpiredBackfillLeasesAsync(
        TgAssistantDbContext db,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            update stage8_recompute_queue_items
            set status = {Stage8RecomputeQueueStatuses.Pending},
                leased_until_utc = null,
                lease_token = null,
                last_error = coalesce(nullif(last_error, ''), 'lease_expired_resume'),
                last_result_status = {Stage8RecomputeExecutionStatuses.Rescheduled},
                updated_at_utc = {nowUtc},
                completed_at_utc = null
            where status = {Stage8RecomputeQueueStatuses.Leased}
              and leased_until_utc is not null
              and leased_until_utc <= {nowUtc}
            """, ct);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            update stage8_backfill_scope_checkpoints
            set status = {Stage8BackfillCheckpointStatuses.Ready},
                active_queue_item_id = null,
                active_target_family = null,
                active_lease_token = null,
                active_lease_owner = null,
                lease_expires_at_utc = null,
                last_error = coalesce(nullif(last_error, ''), 'lease_expired_resume'),
                last_recovery_kind = {Stage8BackfillRecoveryKinds.LeaseExpiredResume},
                last_recovery_at_utc = {nowUtc},
                last_backoff_until_utc = null,
                resume_count = resume_count + 1,
                last_checkpoint_at_utc = {nowUtc},
                updated_at_utc = {nowUtc}
            where status = {Stage8BackfillCheckpointStatuses.InProgress}
              and lease_expires_at_utc is not null
              and lease_expires_at_utc <= {nowUtc}
            """, ct);
    }

    private static async Task UpsertBackfillCheckpointAsync(
        TgAssistantDbContext db,
        Stage8RecomputeQueueItem item,
        string workerId,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        CancellationToken ct)
    {
        var checkpoint = await db.Stage8BackfillScopeCheckpoints
            .FirstOrDefaultAsync(x => x.ScopeKey == item.ScopeKey, ct);
        if (checkpoint == null)
        {
            checkpoint = new DbStage8BackfillScopeCheckpoint
            {
                ScopeKey = item.ScopeKey,
                Status = Stage8BackfillCheckpointStatuses.InProgress,
                ActiveQueueItemId = item.Id,
                ActiveTargetFamily = item.TargetFamily,
                ActiveLeaseToken = item.LeaseToken,
                ActiveLeaseOwner = workerId,
                LeaseExpiresAtUtc = leaseExpiresAtUtc,
                LastQueueItemId = item.Id,
                LastTargetFamily = item.TargetFamily,
                FirstStartedAtUtc = nowUtc,
                LastCheckpointAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc
            };
            db.Stage8BackfillScopeCheckpoints.Add(checkpoint);
        }
        else
        {
            checkpoint.Status = Stage8BackfillCheckpointStatuses.InProgress;
            checkpoint.ActiveQueueItemId = item.Id;
            checkpoint.ActiveTargetFamily = item.TargetFamily;
            checkpoint.ActiveLeaseToken = item.LeaseToken;
            checkpoint.ActiveLeaseOwner = workerId;
            checkpoint.LeaseExpiresAtUtc = leaseExpiresAtUtc;
            checkpoint.LastQueueItemId = item.Id;
            checkpoint.LastTargetFamily = item.TargetFamily;
            checkpoint.LastCheckpointAtUtc = nowUtc;
            checkpoint.UpdatedAtUtc = nowUtc;
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task UpdateBackfillCheckpointAfterLeaseReleaseAsync(
        TgAssistantDbContext db,
        Guid queueItemId,
        string resultStatus,
        Guid? modelPassRunId,
        string? lastError,
        bool terminalFailure,
        Stage8BackfillRecoveryTelemetry? recoveryTelemetry,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var releasedItem = await db.Stage8RecomputeQueueItems.AsNoTracking()
            .Where(x => x.Id == queueItemId)
            .Select(x => new { x.ScopeKey, x.TargetFamily })
            .FirstOrDefaultAsync(ct);
        if (releasedItem == null)
        {
            return;
        }

        var checkpoint = await db.Stage8BackfillScopeCheckpoints
            .FirstOrDefaultAsync(x => x.ScopeKey == releasedItem.ScopeKey, ct);
        if (checkpoint == null)
        {
            return;
        }

        var hasRemaining = await db.Stage8RecomputeQueueItems.AsNoTracking().AnyAsync(
            x => x.ScopeKey == releasedItem.ScopeKey
                && (x.Status == Stage8RecomputeQueueStatuses.Pending
                    || x.Status == Stage8RecomputeQueueStatuses.Leased),
            ct);

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
        checkpoint.LastQueueItemId = queueItemId;
        checkpoint.LastTargetFamily = releasedItem.TargetFamily;
        checkpoint.LastResultStatus = resultStatus;
        checkpoint.LastModelPassRunId = modelPassRunId;
        checkpoint.LastError = lastError;
        checkpoint.LastRecoveryKind = recoveryTelemetry?.RecoveryKind
            ?? (string.Equals(resultStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal)
                ? Stage8BackfillRecoveryKinds.GeneralRetry
                : Stage8BackfillRecoveryKinds.None);
        checkpoint.LastRecoveryAtUtc = recoveryTelemetry?.OccurredAtUtc;
        checkpoint.LastBackoffUntilUtc = recoveryTelemetry?.NextAttemptAtUtc;
        checkpoint.LastCheckpointAtUtc = nowUtc;
        checkpoint.LastCompletedAtUtc = nowUtc;
        checkpoint.UpdatedAtUtc = nowUtc;
        if (terminalFailure)
        {
            checkpoint.FailedItemCount += 1;
        }
        else if (string.Equals(resultStatus, Stage8RecomputeExecutionStatuses.Rescheduled, StringComparison.Ordinal))
        {
            checkpoint.LastCompletedAtUtc = null;
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
            checkpoint.LastRecoveryAtUtc = null;
            checkpoint.LastBackoffUntilUtc = null;
            checkpoint.LastRecoveryKind = Stage8BackfillRecoveryKinds.None;
        }

        await db.SaveChangesAsync(ct);
    }

    private static Stage8RecomputeQueueItem MapQueueItem(DbStage8RecomputeQueueItem row)
    {
        return new Stage8RecomputeQueueItem
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            TargetFamily = row.TargetFamily,
            TargetRef = row.TargetRef,
            DedupeKey = row.DedupeKey,
            ActiveDedupeKey = row.ActiveDedupeKey,
            TriggerKind = row.TriggerKind,
            TriggerRef = row.TriggerRef,
            Status = row.Status,
            Priority = row.Priority,
            AttemptCount = row.AttemptCount,
            MaxAttempts = row.MaxAttempts,
            AvailableAtUtc = row.AvailableAtUtc,
            LeasedUntilUtc = row.LeasedUntilUtc,
            LeaseToken = row.LeaseToken,
            LastError = row.LastError,
            LastResultStatus = row.LastResultStatus,
            LastModelPassRunId = row.LastModelPassRunId,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            CompletedAtUtc = row.CompletedAtUtc
        };
    }

    private static Stage8BackfillCheckpoint MapCheckpoint(DbStage8BackfillScopeCheckpoint row)
    {
        return new Stage8BackfillCheckpoint
        {
            ScopeKey = row.ScopeKey,
            Status = row.Status,
            ActiveQueueItemId = row.ActiveQueueItemId,
            ActiveTargetFamily = row.ActiveTargetFamily,
            ActiveLeaseToken = row.ActiveLeaseToken,
            ActiveLeaseOwner = row.ActiveLeaseOwner,
            LeaseExpiresAtUtc = row.LeaseExpiresAtUtc,
            LastQueueItemId = row.LastQueueItemId,
            LastTargetFamily = row.LastTargetFamily,
            LastResultStatus = row.LastResultStatus,
            LastModelPassRunId = row.LastModelPassRunId,
            LastError = row.LastError,
            CompletedItemCount = row.CompletedItemCount,
            FailedItemCount = row.FailedItemCount,
            ResumeCount = row.ResumeCount,
            RetryCount = row.RetryCount,
            DeadlockRetryCount = row.DeadlockRetryCount,
            TransientRetryCount = row.TransientRetryCount,
            LastRecoveryKind = row.LastRecoveryKind,
            LastRecoveryAtUtc = row.LastRecoveryAtUtc,
            LastBackoffUntilUtc = row.LastBackoffUntilUtc,
            FirstStartedAtUtc = row.FirstStartedAtUtc,
            LastCheckpointAtUtc = row.LastCheckpointAtUtc,
            LastCompletedAtUtc = row.LastCompletedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        };
    }

}

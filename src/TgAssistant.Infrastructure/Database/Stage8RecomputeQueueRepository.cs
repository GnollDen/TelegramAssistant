using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage8RecomputeQueueRepository : IStage8RecomputeQueueRepository
{
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

        var normalized = NormalizeRequest(request);
        var nowUtc = DateTime.UtcNow;
        var dedupeKey = BuildDedupeKey(normalized.ScopeKey, normalized.TargetFamily, normalized.TargetRef);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Stage8RecomputeQueueItems.FirstOrDefaultAsync(
            x => x.ActiveDedupeKey == dedupeKey,
            ct);
        if (existing != null)
        {
            if (string.Equals(existing.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal))
            {
                existing.Priority = Math.Min(existing.Priority, normalized.Priority);
                existing.AvailableAtUtc = existing.AvailableAtUtc <= normalized.AvailableAtUtc
                    ? existing.AvailableAtUtc
                    : normalized.AvailableAtUtc;
                existing.UpdatedAtUtc = nowUtc;
                await db.SaveChangesAsync(ct);
            }

            return MapQueueItem(existing);
        }

        var row = new DbStage8RecomputeQueueItem
        {
            Id = Guid.NewGuid(),
            ScopeKey = normalized.ScopeKey,
            PersonId = normalized.PersonId,
            TargetFamily = normalized.TargetFamily,
            TargetRef = normalized.TargetRef,
            DedupeKey = dedupeKey,
            ActiveDedupeKey = dedupeKey,
            TriggerKind = normalized.TriggerKind,
            TriggerRef = normalized.TriggerRef,
            Status = Stage8RecomputeQueueStatuses.Pending,
            Priority = normalized.Priority,
            AttemptCount = 0,
            MaxAttempts = normalized.MaxAttempts,
            AvailableAtUtc = normalized.AvailableAtUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.Stage8RecomputeQueueItems.Add(row);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            var concurrent = await db.Stage8RecomputeQueueItems.AsNoTracking().FirstOrDefaultAsync(
                x => x.ActiveDedupeKey == dedupeKey,
                ct);
            if (concurrent != null)
            {
                _logger.LogInformation(
                    "Stage8 recompute queue dedupe hit after concurrent enqueue: scope_key={ScopeKey}, target_family={TargetFamily}, target_ref={TargetRef}",
                    normalized.ScopeKey,
                    normalized.TargetFamily,
                    normalized.TargetRef);
                return MapQueueItem(concurrent);
            }

            throw;
        }

        _logger.LogInformation(
            "Stage8 recompute queued: queue_item_id={QueueItemId}, scope_key={ScopeKey}, target_family={TargetFamily}, target_ref={TargetRef}",
            row.Id,
            row.ScopeKey,
            row.TargetFamily,
            row.TargetRef);
        return MapQueueItem(row);
    }

    public async Task<Stage8RecomputeQueueItem?> LeaseNextAsync(
        DateTime nowUtc,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

        var leasedUntilUtc = nowUtc.Add(leaseDuration);
        var leaseToken = Guid.NewGuid();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
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
        AddParameter(command, "pending_status", Stage8RecomputeQueueStatuses.Pending);
        AddParameter(command, "leased_status", Stage8RecomputeQueueStatuses.Leased);
        AddParameter(command, "now_utc", nowUtc);
        AddParameter(command, "leased_until_utc", leasedUntilUtc);
        AddParameter(command, "lease_token", leaseToken);

        await using var reader = await command.ExecuteReaderAsync(ct);
        Stage8RecomputeQueueItem? item = null;
        if (await reader.ReadAsync(ct))
        {
            item = MapQueueItem(reader);
        }

        await tx.CommitAsync(ct);
        return item;
    }

    public async Task CompleteAsync(
        Guid queueItemId,
        Guid leaseToken,
        string resultStatus,
        Guid? modelPassRunId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
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
    }

    public async Task RescheduleAsync(
        Guid queueItemId,
        Guid leaseToken,
        string error,
        DateTime nextAvailableAtUtc,
        bool terminalFailure,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;
        var resultStatus = terminalFailure
            ? Stage8RecomputeExecutionStatuses.FailedTerminally
            : Stage8RecomputeExecutionStatuses.Rescheduled;
        var status = terminalFailure
            ? Stage8RecomputeQueueStatuses.Failed
            : Stage8RecomputeQueueStatuses.Pending;
        var activeDedupeKey = terminalFailure ? null : await db.Stage8RecomputeQueueItems
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
    }

    private static (string ScopeKey, Guid? PersonId, string TargetFamily, string TargetRef, string TriggerKind, string? TriggerRef, int Priority, int MaxAttempts, DateTime AvailableAtUtc) NormalizeRequest(
        Stage8RecomputeQueueRequest request)
    {
        var scopeKey = request.ScopeKey?.Trim();
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            throw new InvalidOperationException("Stage8 recompute queue requires a non-empty scope_key.");
        }

        var targetFamily = request.TargetFamily?.Trim();
        if (string.IsNullOrWhiteSpace(targetFamily) || !Stage8RecomputeTargetFamilies.All.Contains(targetFamily, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported Stage8 recompute target family '{request.TargetFamily}'.");
        }

        var targetRef = request.PersonId != null
            ? $"person:{request.PersonId:D}"
            : $"scope:{scopeKey}";
        var triggerKind = string.IsNullOrWhiteSpace(request.TriggerKind) ? "manual" : request.TriggerKind.Trim();
        var triggerRef = string.IsNullOrWhiteSpace(request.TriggerRef) ? null : request.TriggerRef.Trim();
        var priority = Math.Clamp(request.Priority, 0, 10_000);
        var maxAttempts = Math.Clamp(request.MaxAttempts, 1, 25);
        var availableAtUtc = request.AvailableAtUtc ?? DateTime.UtcNow;
        return (scopeKey, request.PersonId, targetFamily, targetRef, triggerKind, triggerRef, priority, maxAttempts, availableAtUtc);
    }

    private static string BuildDedupeKey(string scopeKey, string targetFamily, string targetRef)
        => $"{scopeKey}|{targetFamily}|{targetRef}";

    private static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException postgres && string.Equals(postgres.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal);

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

    private static Stage8RecomputeQueueItem MapQueueItem(DbDataReader reader)
    {
        return new Stage8RecomputeQueueItem
        {
            Id = reader.GetFieldValue<Guid>(reader.GetOrdinal("id")),
            ScopeKey = reader.GetString(reader.GetOrdinal("scope_key")),
            PersonId = reader.IsDBNull(reader.GetOrdinal("person_id")) ? null : reader.GetFieldValue<Guid>(reader.GetOrdinal("person_id")),
            TargetFamily = reader.GetString(reader.GetOrdinal("target_family")),
            TargetRef = reader.GetString(reader.GetOrdinal("target_ref")),
            DedupeKey = reader.GetString(reader.GetOrdinal("dedupe_key")),
            ActiveDedupeKey = reader.IsDBNull(reader.GetOrdinal("active_dedupe_key")) ? null : reader.GetString(reader.GetOrdinal("active_dedupe_key")),
            TriggerKind = reader.GetString(reader.GetOrdinal("trigger_kind")),
            TriggerRef = reader.IsDBNull(reader.GetOrdinal("trigger_ref")) ? null : reader.GetString(reader.GetOrdinal("trigger_ref")),
            Status = reader.GetString(reader.GetOrdinal("status")),
            Priority = reader.GetInt32(reader.GetOrdinal("priority")),
            AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
            MaxAttempts = reader.GetInt32(reader.GetOrdinal("max_attempts")),
            AvailableAtUtc = reader.GetFieldValue<DateTime>(reader.GetOrdinal("available_at_utc")),
            LeasedUntilUtc = reader.IsDBNull(reader.GetOrdinal("leased_until_utc")) ? null : reader.GetFieldValue<DateTime>(reader.GetOrdinal("leased_until_utc")),
            LeaseToken = reader.IsDBNull(reader.GetOrdinal("lease_token")) ? null : reader.GetFieldValue<Guid>(reader.GetOrdinal("lease_token")),
            LastError = reader.IsDBNull(reader.GetOrdinal("last_error")) ? null : reader.GetString(reader.GetOrdinal("last_error")),
            LastResultStatus = reader.IsDBNull(reader.GetOrdinal("last_result_status")) ? null : reader.GetString(reader.GetOrdinal("last_result_status")),
            LastModelPassRunId = reader.IsDBNull(reader.GetOrdinal("last_model_pass_run_id")) ? null : reader.GetFieldValue<Guid>(reader.GetOrdinal("last_model_pass_run_id")),
            CreatedAtUtc = reader.GetFieldValue<DateTime>(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc = reader.GetFieldValue<DateTime>(reader.GetOrdinal("updated_at_utc")),
            CompletedAtUtc = reader.IsDBNull(reader.GetOrdinal("completed_at_utc")) ? null : reader.GetFieldValue<DateTime>(reader.GetOrdinal("completed_at_utc"))
        };
    }
}

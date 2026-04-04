using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

internal static class Stage8RecomputeQueueStorage
{
    internal static async Task<Stage8RecomputeQueueEnqueueResult> EnqueueAsync(
        TgAssistantDbContext db,
        Stage8RecomputeQueueRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);

        var normalized = NormalizeRequest(request);
        var nowUtc = DateTime.UtcNow;
        var insertedId = Guid.NewGuid();
        var dedupeKey = BuildDedupeKey(normalized.ScopeKey, normalized.TargetFamily, normalized.TargetRef);

        await db.Database.OpenConnectionAsync(ct);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText =
            """
            insert into stage8_recompute_queue_items (
                id,
                scope_key,
                person_id,
                target_family,
                target_ref,
                dedupe_key,
                active_dedupe_key,
                trigger_kind,
                trigger_ref,
                status,
                priority,
                attempt_count,
                max_attempts,
                available_at_utc,
                created_at_utc,
                updated_at_utc
            )
            values (
                @id,
                @scope_key,
                @person_id,
                @target_family,
                @target_ref,
                @dedupe_key,
                @active_dedupe_key,
                @trigger_kind,
                @trigger_ref,
                @pending_status,
                @priority,
                0,
                @max_attempts,
                @available_at_utc,
                @now_utc,
                @now_utc
            )
            on conflict (active_dedupe_key) where active_dedupe_key is not null
            do update set
                priority = case
                    when stage8_recompute_queue_items.status = @pending_status
                        then least(stage8_recompute_queue_items.priority, excluded.priority)
                    else stage8_recompute_queue_items.priority
                end,
                available_at_utc = case
                    when stage8_recompute_queue_items.status = @pending_status
                        then least(stage8_recompute_queue_items.available_at_utc, excluded.available_at_utc)
                    else stage8_recompute_queue_items.available_at_utc
                end,
                updated_at_utc = case
                    when stage8_recompute_queue_items.status = @pending_status
                        then @now_utc
                    else stage8_recompute_queue_items.updated_at_utc
                end
            returning
                id,
                scope_key,
                person_id,
                target_family,
                target_ref,
                dedupe_key,
                active_dedupe_key,
                trigger_kind,
                trigger_ref,
                status,
                priority,
                attempt_count,
                max_attempts,
                available_at_utc,
                leased_until_utc,
                lease_token,
                last_error,
                last_result_status,
                last_model_pass_run_id,
                created_at_utc,
                updated_at_utc,
                completed_at_utc;
            """;
        AddParameter(command, "id", insertedId);
        AddParameter(command, "scope_key", normalized.ScopeKey);
        AddParameter(command, "person_id", normalized.PersonId);
        AddParameter(command, "target_family", normalized.TargetFamily);
        AddParameter(command, "target_ref", normalized.TargetRef);
        AddParameter(command, "dedupe_key", dedupeKey);
        AddParameter(command, "active_dedupe_key", dedupeKey);
        AddParameter(command, "trigger_kind", normalized.TriggerKind);
        AddParameter(command, "trigger_ref", normalized.TriggerRef);
        AddParameter(command, "pending_status", Stage8RecomputeQueueStatuses.Pending);
        AddParameter(command, "priority", normalized.Priority);
        AddParameter(command, "max_attempts", normalized.MaxAttempts);
        AddParameter(command, "available_at_utc", normalized.AvailableAtUtc);
        AddParameter(command, "now_utc", nowUtc);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            throw new InvalidOperationException("Stage8 recompute enqueue did not return a queue row.");
        }

        var item = MapQueueItem(reader);
        return new Stage8RecomputeQueueEnqueueResult
        {
            Item = item,
            Created = item.Id == insertedId
        };
    }

    internal static (string ScopeKey, Guid? PersonId, string TargetFamily, string TargetRef, string TriggerKind, string? TriggerRef, int Priority, int MaxAttempts, DateTime AvailableAtUtc) NormalizeRequest(
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

    internal static string BuildDedupeKey(string scopeKey, string targetFamily, string targetRef)
        => $"{scopeKey}|{targetFamily}|{targetRef}";

    internal static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    internal static Stage8RecomputeQueueItem MapQueueItem(DbDataReader reader)
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

internal sealed class Stage8RecomputeQueueEnqueueResult
{
    public Stage8RecomputeQueueItem Item { get; init; } = new();
    public bool Created { get; init; }
}

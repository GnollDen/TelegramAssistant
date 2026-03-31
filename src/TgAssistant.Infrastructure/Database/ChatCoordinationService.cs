using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ChatCoordinationService : IChatCoordinationService
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ChatCoordinationSettings _settings;
    private readonly ILogger<ChatCoordinationService> _logger;

    public ChatCoordinationService(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IOptions<ChatCoordinationSettings> settings,
        ILogger<ChatCoordinationService> logger)
    {
        _dbFactory = dbFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Dictionary<long, ChatCoordinationState>> EnsureStatesAsync(
        IReadOnlyCollection<long> monitoredChatIds,
        IReadOnlyCollection<long> backfillChatIds,
        bool backfillEnabled,
        int handoverPendingExtractionThreshold,
        CancellationToken ct = default)
    {
        var chatIds = monitoredChatIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (chatIds.Count == 0)
        {
            return new Dictionary<long, ChatCoordinationState>();
        }

        var backfillSet = backfillChatIds
            .Where(id => id > 0)
            .ToHashSet();
        var now = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existingRows = await db.ChatCoordinationStates
            .AsNoTracking()
            .Where(x => chatIds.Contains(x.ChatId))
            .ToListAsync(ct);
        var existingByChatId = existingRows
            .Select(x => x.ChatId)
            .ToHashSet();
        var missingChatIds = chatIds
            .Where(chatId => !existingByChatId.Contains(chatId))
            .ToList();

        if (missingChatIds.Count > 0)
        {
            foreach (var chatId in missingChatIds)
            {
                var reason = backfillSet.Contains(chatId)
                    ? "historical_catchup_required"
                    : "onboarding_requires_historical_catchup";

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    insert into ops_chat_coordination_states (chat_id, state, reason, updated_at)
                    values ({chatId}, {ChatCoordinationStates.HistoricalRequired}, {reason}, {now})
                    on conflict (chat_id) do nothing
                    """,
                    ct);
            }
        }

        var rows = await db.ChatCoordinationStates
            .Where(x => chatIds.Contains(x.ChatId))
            .ToListAsync(ct);
        var byChat = rows.ToDictionary(x => x.ChatId);

        foreach (var chatId in chatIds)
        {
            if (!byChat.TryGetValue(chatId, out var row))
            {
                continue;
            }

            if (_settings.AutoRecoveryCatchupEnabled
                && !backfillEnabled
                && string.Equals(row.State, ChatCoordinationStates.RealtimeActive, StringComparison.Ordinal)
                && IsDowntimeCatchupRequired(row, now))
            {
                row.State = ChatCoordinationStates.HistoricalRequired;
                row.Reason = "downtime_catchup_required";
                row.UpdatedAt = now;
            }
        }

        foreach (var row in byChat.Values)
        {
            if (!string.Equals(row.State, ChatCoordinationStates.HandoverPending, StringComparison.Ordinal))
            {
                continue;
            }

            var pending = await CountPendingExtractionsAsync(db, row.ChatId, ct);
            if (pending <= Math.Max(0, handoverPendingExtractionThreshold))
            {
                row.State = ChatCoordinationStates.RealtimeActive;
                row.Reason = "handover_completed";
                row.RealtimeActivatedAt = now;
                row.UpdatedAt = now;
            }
            else
            {
                row.Reason = $"handover_waiting_stage5_pending_extractions={pending}";
                row.UpdatedAt = now;
            }
        }

        await db.SaveChangesAsync(ct);

        return byChat.ToDictionary(x => x.Key, x => Map(x.Value));
    }

    public async Task<HashSet<long>> ResolveRealtimeEligibleChatIdsAsync(
        IReadOnlyCollection<long> monitoredChatIds,
        IReadOnlyCollection<long> backfillChatIds,
        bool backfillEnabled,
        int handoverPendingExtractionThreshold,
        CancellationToken ct = default)
    {
        var states = await EnsureStatesAsync(
            monitoredChatIds,
            backfillChatIds,
            backfillEnabled,
            handoverPendingExtractionThreshold,
            ct);

        if (ShouldEnforceGlobalBackfillExclusivity(backfillEnabled, states))
        {
            _logger.LogWarning(
                "Global backfill exclusivity is active: listener eligibility is paused while at least one chat is in backfill_active state.");
            return new HashSet<long>();
        }

        return states.Values
            .Where(x => string.Equals(x.State, ChatCoordinationStates.RealtimeActive, StringComparison.Ordinal))
            .Select(x => x.ChatId)
            .ToHashSet();
    }

    public async Task MarkBackfillStartedAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = await db.ChatCoordinationStates.FirstOrDefaultAsync(x => x.ChatId == chatId, ct)
            ?? new DbChatCoordinationState
            {
                ChatId = chatId
            };

        row.State = ChatCoordinationStates.BackfillActive;
        row.Reason = "historical_backfill_running";
        row.LastBackfillStartedAt = now;
        row.UpdatedAt = now;

        if (db.Entry(row).State == EntityState.Detached)
        {
            db.ChatCoordinationStates.Add(row);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkBackfillCompletedAsync(
        long chatId,
        int handoverPendingExtractionThreshold,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = await db.ChatCoordinationStates.FirstOrDefaultAsync(x => x.ChatId == chatId, ct)
            ?? new DbChatCoordinationState
            {
                ChatId = chatId
            };

        row.State = ChatCoordinationStates.HandoverPending;
        row.Reason = "backfill_completed_pending_handover";
        row.LastBackfillCompletedAt = now;
        row.HandoverReadyAt = now;
        row.UpdatedAt = now;

        if (db.Entry(row).State == EntityState.Detached)
        {
            db.ChatCoordinationStates.Add(row);
        }

        var pending = await CountPendingExtractionsAsync(db, chatId, ct);
        if (pending <= Math.Max(0, handoverPendingExtractionThreshold))
        {
            row.State = ChatCoordinationStates.RealtimeActive;
            row.Reason = "handover_completed";
            row.RealtimeActivatedAt = now;
            row.UpdatedAt = now;
        }
        else
        {
            row.Reason = $"handover_waiting_stage5_pending_extractions={pending}";
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkBackfillDegradedAsync(long chatId, string reason, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = await db.ChatCoordinationStates.FirstOrDefaultAsync(x => x.ChatId == chatId, ct)
            ?? new DbChatCoordinationState
            {
                ChatId = chatId
            };

        row.State = ChatCoordinationStates.DegradedBackfill;
        row.Reason = string.IsNullOrWhiteSpace(reason) ? "backfill_failed" : reason.Trim();
        row.UpdatedAt = now;

        if (db.Entry(row).State == EntityState.Detached)
        {
            db.ChatCoordinationStates.Add(row);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task TouchRealtimeHeartbeatAsync(IReadOnlyCollection<long> realtimeChatIds, CancellationToken ct = default)
    {
        var chatIds = realtimeChatIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        if (chatIds.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ChatCoordinationStates
            .Where(x => chatIds.Contains(x.ChatId))
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.LastListenerSeenAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<ChatPhaseGuardDecision> TryAcquirePhaseAsync(
        long chatId,
        string requestedPhase,
        string ownerId,
        string? reason = null,
        DateTime? reopenWindowFromUtc = null,
        DateTime? reopenWindowToUtc = null,
        string? reopenOperator = null,
        string? reopenAuditId = null,
        CancellationToken ct = default)
    {
        if (!_settings.PhaseGuardsEnabled || chatId <= 0)
        {
            return new ChatPhaseGuardDecision
            {
                ChatId = chatId,
                Allowed = true,
                RequestedPhase = requestedPhase,
                ObservedAtUtc = DateTime.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(requestedPhase) || string.IsNullOrWhiteSpace(ownerId))
        {
            return new ChatPhaseGuardDecision
            {
                ChatId = chatId,
                Allowed = false,
                RequestedPhase = requestedPhase ?? string.Empty,
                DenyCode = "invalid_phase_request",
                DenyReason = "requested_phase and owner_id are required",
                ObservedAtUtc = DateTime.UtcNow
            };
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into ops_chat_phase_guards (chat_id, updated_at)
            values ({chatId}, now())
            on conflict (chat_id) do nothing
            """,
            ct);

        // Lock the guard row before evaluating and mutating phase state to avoid read-then-write races.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            select 1
            from ops_chat_phase_guards
            where chat_id = {chatId}
            for update
            """,
            ct);

        var now = DateTime.UtcNow;
        var row = await db.ChatPhaseGuards.SingleAsync(x => x.ChatId == chatId, ct);
        var currentPhase = row.ActivePhase ?? string.Empty;
        var currentOwner = row.OwnerId ?? string.Empty;
        var normalizedPhase = requestedPhase.Trim();
        var normalizedOwner = ownerId.Trim();

        var evaluation = EvaluatePhaseAcquire(
            currentPhase,
            currentOwner,
            normalizedPhase,
            normalizedOwner,
            now,
            row.LeaseExpiresAt,
            row.TailReopenWindowFromUtc,
            row.TailReopenWindowToUtc,
            reopenWindowFromUtc,
            reopenWindowToUtc,
            reopenOperator,
            reopenAuditId);
        var denial = evaluation.Denial;
        var recovery = evaluation.Recovery;
        var leaseTtl = TimeSpan.FromMinutes(Math.Max(1, _settings.PhaseGuardLeaseTtlMinutes));
        var leaseExpiresAt = now.Add(leaseTtl);
        var currentLeaseIsFresh = IsLeaseFresh(now, row.LeaseExpiresAt);

        row.LastRequestedPhase = normalizedPhase;
        row.LastObservedPhase = string.IsNullOrWhiteSpace(currentPhase) ? null : currentPhase;

        if (denial is not null)
        {
            denial.ChatId = chatId;
            denial.CurrentLeaseExpiresAtUtc = row.LeaseExpiresAt;
            denial.CurrentLeaseIsFresh = currentLeaseIsFresh;
            row.LastDenyCode = denial.DenyCode;
            row.LastDenyReason = denial.DenyReason;
            row.LastDeniedAt = denial.ObservedAtUtc;
            row.UpdatedAt = now;

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            _logger.LogWarning(
                "Phase guard blocked transition: chat_id={ChatId}, requested_phase={RequestedPhase}, current_phase={CurrentPhase}, deny_code={DenyCode}, deny_reason={DenyReason}, lease_expires_at_utc={LeaseExpiresAtUtc}, lease_is_fresh={LeaseIsFresh}",
                denial.ChatId,
                denial.RequestedPhase,
                denial.CurrentPhase,
                denial.DenyCode,
                denial.DenyReason,
                denial.CurrentLeaseExpiresAtUtc,
                denial.CurrentLeaseIsFresh);
            return denial;
        }

        row.ActivePhase = normalizedPhase;
        row.OwnerId = normalizedOwner;
        row.PhaseReason = string.IsNullOrWhiteSpace(reason)
            ? recovery?.Reason
            : reason.Trim();
        row.ActiveSince = string.Equals(currentPhase, normalizedPhase, StringComparison.Ordinal)
            ? row.ActiveSince ?? now
            : now;
        row.LeaseExpiresAt = leaseExpiresAt;
        row.UpdatedAt = now;
        row.LastDenyCode = null;
        row.LastDenyReason = null;
        row.LastDeniedAt = null;
        if (recovery is not null)
        {
            row.LastRecoveryAt = now;
            row.LastRecoveryFromOwnerId = recovery.FromOwnerId;
            row.LastRecoveryCode = recovery.Code;
            row.LastRecoveryReason = recovery.Reason;
        }
        if (string.Equals(normalizedPhase, ChatRuntimePhases.TailReopen, StringComparison.Ordinal))
        {
            row.TailReopenWindowFromUtc = reopenWindowFromUtc;
            row.TailReopenWindowToUtc = reopenWindowToUtc;
            row.TailReopenOperator = reopenOperator;
            row.TailReopenAuditId = reopenAuditId;
        }
        else
        {
            row.TailReopenWindowFromUtc = null;
            row.TailReopenWindowToUtc = null;
            row.TailReopenOperator = null;
            row.TailReopenAuditId = null;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (recovery is not null)
        {
            _logger.LogWarning(
                "Phase guard recovery takeover applied: chat_id={ChatId}, requested_phase={RequestedPhase}, previous_phase={PreviousPhase}, previous_owner={PreviousOwner}, new_owner={NewOwner}, recovery_code={RecoveryCode}, recovery_reason={RecoveryReason}, new_lease_expires_at_utc={LeaseExpiresAtUtc}",
                chatId,
                normalizedPhase,
                currentPhase,
                recovery.FromOwnerId,
                normalizedOwner,
                recovery.Code,
                recovery.Reason,
                leaseExpiresAt);
        }

        return new ChatPhaseGuardDecision
        {
            ChatId = chatId,
            Allowed = true,
            RequestedPhase = normalizedPhase,
            CurrentPhase = currentPhase,
            CurrentLeaseExpiresAtUtc = leaseExpiresAt,
            CurrentLeaseIsFresh = true,
            RecoveryApplied = recovery is not null,
            RecoveryCode = recovery?.Code ?? string.Empty,
            RecoveryReason = recovery?.Reason ?? string.Empty,
            ObservedAtUtc = now
        };
    }

    public async Task<ChatPhaseLeaseRenewDecision> TryRenewPhaseLeaseAsync(
        long chatId,
        string phase,
        string ownerId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var observedAt = DateTime.UtcNow;
        if (!_settings.PhaseGuardsEnabled || chatId <= 0 || string.IsNullOrWhiteSpace(phase) || string.IsNullOrWhiteSpace(ownerId))
        {
            return new ChatPhaseLeaseRenewDecision
            {
                ChatId = chatId,
                Phase = phase ?? string.Empty,
                OwnerId = ownerId ?? string.Empty,
                Renewed = false,
                DenyCode = "invalid_renew_request",
                DenyReason = "phase guards disabled or invalid chat_id/phase/owner_id",
                ObservedAtUtc = observedAt
            };
        }

        var now = DateTime.UtcNow;
        var normalizedPhase = phase.Trim();
        var normalizedOwner = ownerId.Trim();
        var leaseTtl = TimeSpan.FromMinutes(Math.Max(1, _settings.PhaseGuardLeaseTtlMinutes));
        var leaseExpiresAt = now.Add(leaseTtl);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "lease_renewed" : reason.Trim();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            update ops_chat_phase_guards
            set lease_expires_at = {leaseExpiresAt},
                phase_reason = {normalizedReason},
                updated_at = now()
            where chat_id = {chatId}
              and active_phase = {normalizedPhase}
              and owner_id = {normalizedOwner}
            """,
            ct);

        if (affected > 0)
        {
            return new ChatPhaseLeaseRenewDecision
            {
                ChatId = chatId,
                Phase = normalizedPhase,
                OwnerId = normalizedOwner,
                Renewed = true,
                CurrentLeaseExpiresAtUtc = leaseExpiresAt,
                ObservedAtUtc = now
            };
        }

        var snapshot = await db.ChatPhaseGuards
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ChatId == chatId, ct);
        var denyCode = snapshot is null
            ? "phase_guard_missing"
            : "ownership_mismatch";
        var denyReason = snapshot is null
            ? "phase guard row is missing"
            : $"renew denied because active_phase='{snapshot.ActivePhase ?? "none"}', owner_id='{snapshot.OwnerId ?? "none"}'";

        _logger.LogWarning(
            "Phase lease renew mismatch: chat_id={ChatId}, requested_phase={RequestedPhase}, requested_owner={RequestedOwner}, current_phase={CurrentPhase}, current_owner={CurrentOwner}, current_lease_expires_at_utc={CurrentLeaseExpiresAtUtc}, deny_code={DenyCode}, deny_reason={DenyReason}",
            chatId,
            normalizedPhase,
            normalizedOwner,
            snapshot?.ActivePhase,
            snapshot?.OwnerId,
            snapshot?.LeaseExpiresAt,
            denyCode,
            denyReason);

        return new ChatPhaseLeaseRenewDecision
        {
            ChatId = chatId,
            Phase = normalizedPhase,
            OwnerId = normalizedOwner,
            Renewed = false,
            DenyCode = denyCode,
            DenyReason = denyReason,
            CurrentLeaseExpiresAtUtc = snapshot?.LeaseExpiresAt,
            ObservedAtUtc = now
        };
    }

    public async Task<ChatPhaseReleaseResult> ReleasePhaseAsync(
        long chatId,
        string phase,
        string ownerId,
        string? reason = null,
        CancellationToken ct = default)
    {
        var observedAt = DateTime.UtcNow;
        if (!_settings.PhaseGuardsEnabled || chatId <= 0 || string.IsNullOrWhiteSpace(phase) || string.IsNullOrWhiteSpace(ownerId))
        {
            return new ChatPhaseReleaseResult
            {
                ChatId = chatId,
                Phase = phase ?? string.Empty,
                OwnerId = ownerId ?? string.Empty,
                Released = false,
                OwnershipMismatch = false,
                ObservedAtUtc = observedAt
            };
        }

        var now = DateTime.UtcNow;
        var normalizedPhase = phase.Trim();
        var normalizedOwner = ownerId.Trim();
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "released" : reason.Trim();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            update ops_chat_phase_guards
            set active_phase = null,
                owner_id = null,
                active_since = null,
                lease_expires_at = null,
                phase_reason = {normalizedReason},
                updated_at = now(),
                tail_reopen_window_from_utc = null,
                tail_reopen_window_to_utc = null,
                tail_reopen_operator = null,
                tail_reopen_audit_id = null
            where chat_id = {chatId}
              and active_phase = {normalizedPhase}
              and owner_id = {normalizedOwner}
            """,
            ct);

        if (affected > 0)
        {
            return new ChatPhaseReleaseResult
            {
                ChatId = chatId,
                Phase = normalizedPhase,
                OwnerId = normalizedOwner,
                Released = true,
                OwnershipMismatch = false,
                ObservedAtUtc = now
            };
        }

        var snapshot = await db.ChatPhaseGuards
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ChatId == chatId, ct);
        _logger.LogWarning(
            "Phase release mismatch: chat_id={ChatId}, requested_phase={RequestedPhase}, requested_owner={RequestedOwner}, current_phase={CurrentPhase}, current_owner={CurrentOwner}, current_lease_expires_at_utc={CurrentLeaseExpiresAtUtc}, reason={Reason}",
            chatId,
            normalizedPhase,
            normalizedOwner,
            snapshot?.ActivePhase,
            snapshot?.OwnerId,
            snapshot?.LeaseExpiresAt,
            normalizedReason);

        return new ChatPhaseReleaseResult
        {
            ChatId = chatId,
            Phase = normalizedPhase,
            OwnerId = normalizedOwner,
            Released = false,
            OwnershipMismatch = true,
            CurrentPhase = snapshot?.ActivePhase ?? string.Empty,
            CurrentOwnerId = snapshot?.OwnerId ?? string.Empty,
            CurrentLeaseExpiresAtUtc = snapshot?.LeaseExpiresAt,
            ObservedAtUtc = now
        };
    }

    public async Task RecordBackupEvidenceAsync(
        BackupMetadataEvidence evidence,
        string recordedBy,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var row = await db.BackupEvidenceRecords.FirstOrDefaultAsync(x => x.BackupId == evidence.BackupId, ct)
            ?? new DbBackupEvidenceRecord
            {
                BackupId = evidence.BackupId
            };

        row.CreatedAtUtc = evidence.CreatedAtUtc;
        row.Scope = evidence.Scope;
        row.ArtifactUri = evidence.ArtifactUri;
        row.Checksum = evidence.Checksum;
        row.RecordedAtUtc = now;
        row.RecordedBy = string.IsNullOrWhiteSpace(recordedBy) ? "unknown" : recordedBy.Trim();
        row.MetadataJson = JsonSerializer.Serialize(new
        {
            evidence.BackupId,
            evidence.CreatedAtUtc,
            evidence.Scope,
            evidence.ArtifactUri,
            evidence.Checksum,
            recorded_by = row.RecordedBy,
            recorded_at_utc = now
        });

        if (db.Entry(row).State == EntityState.Detached)
        {
            db.BackupEvidenceRecords.Add(row);
        }

        await db.SaveChangesAsync(ct);
    }

    private PhaseAcquireEvaluation EvaluatePhaseAcquire(
        string currentPhase,
        string currentOwner,
        string requestedPhase,
        string requestedOwner,
        DateTime now,
        DateTime? currentLeaseExpiresAtUtc,
        DateTime? currentTailReopenWindowFromUtc,
        DateTime? currentTailReopenWindowToUtc,
        DateTime? reopenWindowFromUtc,
        DateTime? reopenWindowToUtc,
        string? reopenOperator,
        string? reopenAuditId)
    {
        var current = currentPhase.Trim();
        var requested = requestedPhase.Trim();
        var ownerChanged = !string.Equals(currentOwner, requestedOwner, StringComparison.Ordinal);
        var leaseIsFresh = IsLeaseFresh(now, currentLeaseExpiresAtUtc);
        var hasCurrent = !string.IsNullOrWhiteSpace(current);

        if (!IsKnownPhase(requested))
        {
            return new PhaseAcquireEvaluation
            {
                Denial = BuildDeniedDecision(requested, current, "unknown_requested_phase", $"unknown requested phase '{requested}'", now)
            };
        }

        if (hasCurrent && !IsKnownPhase(current))
        {
            return new PhaseAcquireEvaluation
            {
                Denial = BuildDeniedDecision(requested, current, "unknown_current_phase", $"unknown current phase '{current}'", now)
            };
        }

        if (hasCurrent && ownerChanged && leaseIsFresh)
        {
            var lockOwner = string.IsNullOrWhiteSpace(currentOwner) ? "unknown" : currentOwner;
            return new PhaseAcquireEvaluation
            {
                Denial = BuildDeniedDecision(
                    requested,
                    current,
                    "phase_owned_by_other",
                    $"{current} is still owned by '{lockOwner}' with an active lease",
                    now)
            };
        }

        if (hasCurrent && !string.Equals(current, requested, StringComparison.Ordinal) && !IsExplicitTransitionAllowed(current, requested))
        {
            return new PhaseAcquireEvaluation
            {
                Denial = BuildDeniedDecision(
                    requested,
                    current,
                    "transition_not_allowed",
                    $"transition '{current}->{requested}' is not allowed by phase matrix",
                    now)
            };
        }

        if (hasCurrent
            && string.Equals(current, ChatRuntimePhases.TailReopen, StringComparison.Ordinal)
            && string.Equals(requested, ChatRuntimePhases.Stage5Process, StringComparison.Ordinal)
            && !IsTailReopenWindowStillValid(now, currentTailReopenWindowFromUtc, currentTailReopenWindowToUtc))
        {
            return new PhaseAcquireEvaluation
            {
                Denial = BuildDeniedDecision(
                    requested,
                    current,
                    "tail_reopen_window_expired",
                    "tail_reopen -> stage5_process requires an active bounded reopen window",
                    now)
            };
        }

        PhaseRecoveryContext? recovery = null;
        if (hasCurrent && ownerChanged && !leaseIsFresh)
        {
            var samePhase = string.Equals(current, requested, StringComparison.Ordinal);
            var recoveryCode = samePhase
                ? "expired_lease_takeover_same_phase"
                : "expired_lease_takeover_transition";
            var recoveryReason = samePhase
                ? $"expired lease takeover for phase '{requested}'"
                : $"expired lease takeover forced transition '{current}->{requested}'";

            recovery = new PhaseRecoveryContext
            {
                FromOwnerId = currentOwner,
                Code = recoveryCode,
                Reason = recoveryReason
            };
        }

        if (string.Equals(requested, ChatRuntimePhases.TailReopen, StringComparison.Ordinal))
        {
            if (reopenWindowFromUtc is null || reopenWindowToUtc is null)
            {
                return new PhaseAcquireEvaluation
                {
                    Denial = BuildDeniedDecision(requested, current, "tail_reopen_window_required", "tail_reopen requires explicit bounded window", now)
                };
            }

            if (reopenWindowToUtc < reopenWindowFromUtc)
            {
                return new PhaseAcquireEvaluation
                {
                    Denial = BuildDeniedDecision(requested, current, "tail_reopen_window_invalid", "tail_reopen window end must be >= start", now)
                };
            }

            if (now < reopenWindowFromUtc.Value || now > reopenWindowToUtc.Value)
            {
                return new PhaseAcquireEvaluation
                {
                    Denial = BuildDeniedDecision(
                        requested,
                        current,
                        "tail_reopen_window_not_active",
                        "tail_reopen requires active window (from <= now <= to)",
                        now)
                };
            }

            var maxWindow = TimeSpan.FromHours(Math.Max(1, _settings.TailReopenMaxWindowHours));
            if (reopenWindowToUtc.Value - reopenWindowFromUtc.Value > maxWindow)
            {
                return new PhaseAcquireEvaluation
                {
                    Denial = BuildDeniedDecision(requested, current, "tail_reopen_window_exceeded", $"tail_reopen window exceeds {maxWindow.TotalHours:0}h limit", now)
                };
            }

            if (string.IsNullOrWhiteSpace(reopenOperator) || string.IsNullOrWhiteSpace(reopenAuditId))
            {
                return new PhaseAcquireEvaluation
                {
                    Denial = BuildDeniedDecision(requested, current, "tail_reopen_audit_required", "tail_reopen requires operator and audit id", now)
                };
            }
        }

        return new PhaseAcquireEvaluation
        {
            Recovery = recovery
        };
    }

    private bool ShouldEnforceGlobalBackfillExclusivity(
        bool backfillEnabled,
        IReadOnlyDictionary<long, ChatCoordinationState> states)
    {
        if (!_settings.EnforceGlobalBackfillExclusivity || !backfillEnabled)
        {
            return false;
        }

        return states.Values.Any(x => string.Equals(x.State, ChatCoordinationStates.BackfillActive, StringComparison.Ordinal));
    }

    private static bool IsExplicitTransitionAllowed(string fromPhase, string toPhase)
    {
        return (fromPhase, toPhase) switch
        {
            (ChatRuntimePhases.BackfillIngest, ChatRuntimePhases.SliceBuild) => true,
            (ChatRuntimePhases.SliceBuild, ChatRuntimePhases.Stage5Process) => true,
            (ChatRuntimePhases.Stage5Process, ChatRuntimePhases.TailReopen) => true,
            (ChatRuntimePhases.TailReopen, ChatRuntimePhases.Stage5Process) => true,
            _ => false
        };
    }

    private static bool IsTailReopenWindowStillValid(
        DateTime now,
        DateTime? windowFromUtc,
        DateTime? windowToUtc)
    {
        if (!windowFromUtc.HasValue || !windowToUtc.HasValue)
        {
            return false;
        }

        return now >= windowFromUtc.Value && now <= windowToUtc.Value;
    }

    private static bool IsLeaseFresh(DateTime now, DateTime? leaseExpiresAtUtc)
    {
        return leaseExpiresAtUtc.HasValue && leaseExpiresAtUtc.Value > now;
    }

    private static bool IsKnownPhase(string phase)
    {
        return phase is ChatRuntimePhases.BackfillIngest
            or ChatRuntimePhases.SliceBuild
            or ChatRuntimePhases.Stage5Process
            or ChatRuntimePhases.TailReopen;
    }

    private static ChatPhaseGuardDecision BuildDeniedDecision(
        string requestedPhase,
        string currentPhase,
        string denyCode,
        string denyReason,
        DateTime observedAtUtc)
    {
        return new ChatPhaseGuardDecision
        {
            Allowed = false,
            RequestedPhase = requestedPhase,
            CurrentPhase = currentPhase,
            DenyCode = denyCode,
            DenyReason = denyReason,
            ObservedAtUtc = observedAtUtc
        };
    }

    private sealed class PhaseAcquireEvaluation
    {
        public ChatPhaseGuardDecision? Denial { get; init; }
        public PhaseRecoveryContext? Recovery { get; init; }
    }

    private sealed class PhaseRecoveryContext
    {
        public string FromOwnerId { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
    }

    private bool IsDowntimeCatchupRequired(DbChatCoordinationState row, DateTime now)
    {
        var downtimeThreshold = TimeSpan.FromMinutes(Math.Max(1, _settings.DowntimeCatchupThresholdMinutes));
        var heartbeat = row.LastListenerSeenAt.HasValue && row.RealtimeActivatedAt.HasValue
            ? (row.LastListenerSeenAt.Value >= row.RealtimeActivatedAt.Value
                ? row.LastListenerSeenAt
                : row.RealtimeActivatedAt)
            : row.LastListenerSeenAt ?? row.RealtimeActivatedAt;
        if (!heartbeat.HasValue)
        {
            return false;
        }

        return now - heartbeat.Value >= downtimeThreshold;
    }

    private static async Task<int> CountPendingExtractionsAsync(TgAssistantDbContext db, long chatId, CancellationToken ct)
    {
        return await (
                from m in db.Messages.AsNoTracking()
                where m.ChatId == chatId
                   && m.Source == (short)MessageSource.Archive
                   && m.ProcessingStatus == (short)ProcessingStatus.Processed
                join me in db.MessageExtractions.AsNoTracking().Where(x => !x.IsQuarantined) on m.Id equals me.MessageId into ext
                from me in ext.DefaultIfEmpty()
                where me == null
                select m.Id)
            .CountAsync(ct);
    }

    private static ChatCoordinationState Map(DbChatCoordinationState row)
    {
        return new ChatCoordinationState
        {
            ChatId = row.ChatId,
            State = row.State,
            Reason = row.Reason,
            LastBackfillStartedAt = row.LastBackfillStartedAt,
            LastBackfillCompletedAt = row.LastBackfillCompletedAt,
            HandoverReadyAt = row.HandoverReadyAt,
            RealtimeActivatedAt = row.RealtimeActivatedAt,
            LastListenerSeenAt = row.LastListenerSeenAt,
            UpdatedAt = row.UpdatedAt
        };
    }
}

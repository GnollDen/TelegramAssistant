using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Stage5Repair;

public sealed class Stage5ScopedRepairCommand
{
    private const long MainChatId = 885574984;
    private static readonly TimeSpan BackupFutureClockSkewTolerance = TimeSpan.FromMinutes(5);
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IChatCoordinationService _chatCoordinationService;
    private readonly RiskyOperationSafetySettings _safetySettings;
    private readonly ChatCoordinationSettings _coordinationSettings;
    private readonly ILogger<Stage5ScopedRepairCommand> _logger;
    private const string RepairPhaseOwnerId = "stage5_scoped_repair_command";

    public Stage5ScopedRepairCommand(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IChatCoordinationService chatCoordinationService,
        IOptions<RiskyOperationSafetySettings> safetySettings,
        IOptions<ChatCoordinationSettings> coordinationSettings,
        ILogger<Stage5ScopedRepairCommand> logger)
    {
        _dbFactory = dbFactory;
        _chatCoordinationService = chatCoordinationService;
        _safetySettings = safetySettings.Value;
        _coordinationSettings = coordinationSettings.Value;
        _logger = logger;
    }

    public async Task<Stage5ScopedRepairResult> RunAsync(
        long chatId,
        bool apply,
        string? auditDirectory,
        Stage5ScopedRepairExecutionOptions? options,
        CancellationToken ct)
    {
        if (chatId != MainChatId)
        {
            throw new InvalidOperationException($"Scoped repair is hard-limited to chat_id={MainChatId}. Requested chat_id={chatId} is blocked.");
        }

        options ??= new Stage5ScopedRepairExecutionOptions();
        var before = await GetSnapshotAsync(chatId, ct);
        var plan = await BuildPlanAsync(chatId, ct);
        var predicted = BuildPredictedSnapshot(before, plan);
        var backupGuardrail = EvaluateBackupGuardrail(chatId, options);
        var integrityPreflight = await BuildIntegrityPreflightSummaryAsync(chatId, before, plan, predicted, ct);

        var summary = new Stage5ScopedRepairSummary
        {
            ChatId = chatId,
            DryRunOnly = !apply,
            GeneratedAtUtc = DateTime.UtcNow,
            Before = before,
            PredictedAfter = predicted,
            Plan = plan,
            BackupGuardrail = backupGuardrail,
            IntegrityPreflight = integrityPreflight,
            Applied = null
        };

        var auditPath = await WriteAuditAsync(summary, auditDirectory, ct);

        _logger.LogInformation(
            "Stage5 scoped repair dry-run: chat_id={ChatId}, pending_before={PendingBefore}, trusted_restore={TrustedRestore}, dual_migrations={Migrations}, orphan_placeholders={Orphans}, pending_after_predicted={PendingAfter}, frontier_after_predicted={FrontierAfter}, audit={AuditPath}",
            chatId,
            before.PendingSessions,
            plan.TrustedSessionIndexes.Count,
            plan.DualSourceMigrations.Count,
            plan.OrphanPlaceholderMessageIds.Count,
            predicted.PendingSessions,
            predicted.MinPendingSessionIndex,
            auditPath);

        if (!apply)
        {
            return new Stage5ScopedRepairResult(summary, auditPath);
        }

        EnsureApplyAllowed(summary, backupGuardrail, integrityPreflight, options);
        if (backupGuardrail.UsingEvidence && options.BackupEvidence is not null)
        {
            await _chatCoordinationService.RecordBackupEvidenceAsync(
                options.BackupEvidence,
                options.EffectiveOperatorIdentity,
                ct);
        }

        var tailReopenDecision = await TryAcquireTailReopenAsync(chatId, options, ct);
        if (tailReopenDecision is not null)
        {
            summary.TailReopenPhaseDecision = tailReopenDecision;
            if (!tailReopenDecision.Allowed)
            {
                throw new InvalidOperationException(
                    $"Tail-reopen policy blocked apply: {tailReopenDecision.DenyCode} ({tailReopenDecision.DenyReason}).");
            }
        }

        try
        {
            var tailLeaseHeartbeat = StartPhaseLeaseHeartbeat(
                chatId,
                ChatRuntimePhases.TailReopen,
                "stage5_scoped_repair_tail_reopen_lease_heartbeat",
                ct);
            using var tailLeaseScope = CreateLeaseLinkedTokenSource(tailLeaseHeartbeat, ct);
            var applyCt = tailLeaseScope.Token;

            Stage5RepairApplyResult applied;
            Stage5RepairSnapshot after;
            try
            {
                applied = await ApplyAsync(chatId, plan, applyCt);
                after = await GetSnapshotAsync(chatId, applyCt);
            }
            catch (OperationCanceledException) when (tailLeaseHeartbeat.LeaseLost && !ct.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "Tail-reopen lease was lost during scoped repair apply; operation aborted fail-closed.");
            }
            finally
            {
                await StopPhaseLeaseHeartbeatAsync(tailLeaseHeartbeat);
            }

            summary.DryRunOnly = false;
            summary.Applied = applied;
            summary.After = after;

            auditPath = await WriteAuditAsync(summary, auditDirectory, applyCt);

            _logger.LogInformation(
                "Stage5 scoped repair applied: chat_id={ChatId}, trusted_marked={TrustedMarked}, archive_inserted={ArchiveInserted}, realtime_quarantined={RealtimeQuarantined}, orphan_inserted={OrphanInserted}, pending_after={PendingAfter}, frontier_after={FrontierAfter}, audit={AuditPath}",
                chatId,
                applied.SessionsMarkedAnalyzed,
                applied.ArchiveExtractionsInserted,
                applied.RealtimeExtractionsQuarantined,
                applied.OrphanPlaceholdersInserted,
                after.PendingSessions,
                after.MinPendingSessionIndex,
                auditPath);

            return new Stage5ScopedRepairResult(summary, auditPath);
        }
        finally
        {
            if (tailReopenDecision is not null && tailReopenDecision.Allowed)
            {
                var releaseResult = await _chatCoordinationService.ReleasePhaseAsync(
                    chatId,
                    ChatRuntimePhases.TailReopen,
                    RepairPhaseOwnerId,
                    reason: "stage5_scoped_repair_apply_completed",
                    ct: CancellationToken.None);
                if (!releaseResult.Released)
                {
                    _logger.LogError(
                        "Tail-reopen phase release escalation: chat_id={ChatId}, requested_phase={RequestedPhase}, owner_id={OwnerId}, ownership_mismatch={OwnershipMismatch}, current_phase={CurrentPhase}, current_owner={CurrentOwner}, current_lease_expires_at_utc={LeaseExpiresAtUtc}",
                        chatId,
                        ChatRuntimePhases.TailReopen,
                        RepairPhaseOwnerId,
                        releaseResult.OwnershipMismatch,
                        releaseResult.CurrentPhase,
                        releaseResult.CurrentOwnerId,
                        releaseResult.CurrentLeaseExpiresAtUtc);
                    throw new InvalidOperationException(
                        $"Tail-reopen release denied after scoped repair apply: current_phase='{releaseResult.CurrentPhase}', current_owner='{releaseResult.CurrentOwnerId}'.");
                }
            }
        }
    }

    private Stage5BackupGuardrailEvaluation EvaluateBackupGuardrail(long chatId, Stage5ScopedRepairExecutionOptions options)
    {
        var evaluation = new Stage5BackupGuardrailEvaluation
        {
            Required = _safetySettings.Enabled && _safetySettings.RequireBackupEvidenceForRepairApply,
            OverrideUsed = options.HasValidOverride,
            OverrideAuditId = options.Override?.AuditId ?? string.Empty
        };

        if (!evaluation.Required)
        {
            evaluation.Passed = true;
            evaluation.Reason = "backup_guardrail_disabled_by_config";
            return evaluation;
        }

        if (options.BackupEvidence is null)
        {
            if (options.HasValidOverride)
            {
                evaluation.Passed = true;
                evaluation.OverrideUsed = true;
                evaluation.Reason = "backup_missing_but_override_approved";
                return evaluation;
            }

            evaluation.Passed = false;
            evaluation.Reason = "backup evidence is required for apply path";
            return evaluation;
        }

        var evidence = options.BackupEvidence;
        if (string.IsNullOrWhiteSpace(evidence.BackupId)
            || string.IsNullOrWhiteSpace(evidence.Scope)
            || string.IsNullOrWhiteSpace(evidence.ArtifactUri)
            || string.IsNullOrWhiteSpace(evidence.Checksum))
        {
            if (options.HasValidOverride)
            {
                evaluation.Passed = true;
                evaluation.OverrideUsed = true;
                evaluation.Reason = "backup metadata incomplete but override approved";
                return evaluation;
            }

            evaluation.Passed = false;
            evaluation.Reason = "backup metadata fields are incomplete";
            return evaluation;
        }

        var now = DateTime.UtcNow;
        var maxAge = TimeSpan.FromHours(Math.Max(1, _safetySettings.BackupFreshnessHours));
        var age = now - evidence.CreatedAtUtc;
        var futureOffset = evidence.CreatedAtUtc - now;
        evaluation.BackupId = evidence.BackupId;
        evaluation.BackupAgeHours = Math.Round(Math.Max(0d, age.TotalHours), 3);
        evaluation.BackupScope = evidence.Scope;
        evaluation.UsingEvidence = true;

        if (futureOffset > BackupFutureClockSkewTolerance)
        {
            if (options.HasValidOverride)
            {
                evaluation.Passed = true;
                evaluation.OverrideUsed = true;
                evaluation.Reason =
                    $"backup created_at_utc is in the future by {futureOffset.TotalMinutes:0.00}m (> {BackupFutureClockSkewTolerance.TotalMinutes:0}m) but override approved";
                return evaluation;
            }

            evaluation.Passed = false;
            evaluation.Reason =
                $"backup created_at_utc is in the future by {futureOffset.TotalMinutes:0.00}m (> {BackupFutureClockSkewTolerance.TotalMinutes:0}m)";
            return evaluation;
        }

        if (age > maxAge)
        {
            if (options.HasValidOverride)
            {
                evaluation.Passed = true;
                evaluation.OverrideUsed = true;
                evaluation.Reason = $"backup is stale ({age.TotalHours:0.00}h > {_safetySettings.BackupFreshnessHours}h) but override approved";
                return evaluation;
            }

            evaluation.Passed = false;
            evaluation.Reason = $"backup is stale ({age.TotalHours:0.00}h > {_safetySettings.BackupFreshnessHours}h)";
            return evaluation;
        }

        var expectedScope = BuildExpectedBackupScope(chatId);
        if (!BackupScopeMatches(evidence.Scope, chatId))
        {
            if (options.HasValidOverride)
            {
                evaluation.Passed = true;
                evaluation.OverrideUsed = true;
                evaluation.Reason = $"backup scope '{evidence.Scope}' does not match expected target '{expectedScope}', but override approved";
                return evaluation;
            }

            evaluation.Passed = false;
            evaluation.Reason = $"backup scope '{evidence.Scope}' does not match expected target '{expectedScope}'";
            return evaluation;
        }

        evaluation.Passed = true;
        evaluation.Reason = "fresh backup evidence verified";
        return evaluation;
    }

    private async Task<IntegrityPreflightSummary> BuildIntegrityPreflightSummaryAsync(
        long chatId,
        Stage5RepairSnapshot before,
        Stage5ScopedRepairPlan plan,
        Stage5RepairSnapshot predicted,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var duplicateCmd = conn.CreateCommand();
        duplicateCmd.CommandText = """
            with active as (
                select
                    m.source,
                    m.telegram_message_id,
                    count(*)::int as duplicate_count
                from messages m
                join message_extractions me
                  on me.message_id = m.id
                 and coalesce(me.is_quarantined, false) = false
                where m.chat_id = @chat_id
                  and m.telegram_message_id > 0
                group by m.source, m.telegram_message_id
                having count(*) > 1
            )
            select count(*)::int
            from active;
            """;
        duplicateCmd.Parameters.AddWithValue("chat_id", chatId);
        var duplicateBusinessKeyCount = 0;
        var duplicateScalar = await duplicateCmd.ExecuteScalarAsync(ct);
        if (duplicateScalar is not null && duplicateScalar is not DBNull)
        {
            duplicateBusinessKeyCount = Convert.ToInt32(duplicateScalar);
        }

        var sessionHoles = await QuerySingleIntAsync(conn, """
            with bounds as (
                select
                    min(session_index) as min_idx,
                    max(session_index) as max_idx,
                    count(distinct session_index) as present_count
                from chat_sessions
                where chat_id = @chat_id
            )
            select coalesce((max_idx - min_idx + 1) - present_count, 0)::int
            from bounds;
            """, chatId, ct);

        var mixedSourceConflicts = plan.DualSourceMigrations.Count;
        var plannedWriteVolume = plan.DualSourceMigrations.Count
                                 + plan.OrphanPlaceholderMessageIds.Count
                                 + plan.TrustedSessionIndexes.Count;
        var tailLag = 0;
        if (before.MaxAnalyzedSessionIndex.HasValue && predicted.MinPendingSessionIndex.HasValue)
        {
            tailLag = before.MaxAnalyzedSessionIndex.Value - predicted.MinPendingSessionIndex.Value;
        }

        var checks = new List<IntegrityPreflightCheck>
        {
            new()
            {
                Code = "duplicate_overlap",
                Status = duplicateBusinessKeyCount > 0 ? "warning" : "ok",
                Details = $"active_duplicate_business_keys={duplicateBusinessKeyCount};business_key=source+telegram_message_id"
            },
            new()
            {
                Code = "session_index_holes",
                Status = sessionHoles > 0 ? "warning" : "ok",
                Details = $"missing_session_indexes={sessionHoles}"
            },
            new()
            {
                Code = "mixed_source_conflicts",
                Status = mixedSourceConflicts > 0 ? "warning" : "ok",
                Details = $"dual_source_migrations_planned={mixedSourceConflicts}"
            },
            new()
            {
                Code = "planned_write_volume",
                Status = plannedWriteVolume >= _safetySettings.IntegrityWriteVolumeUnsafeThreshold
                    ? "unsafe"
                    : plannedWriteVolume >= _safetySettings.IntegrityWriteVolumeWarningThreshold
                        ? "warning"
                        : "ok",
                Details = $"planned_writes={plannedWriteVolume};warning={_safetySettings.IntegrityWriteVolumeWarningThreshold};unsafe={_safetySettings.IntegrityWriteVolumeUnsafeThreshold}"
            },
            new()
            {
                Code = "tail_reopen_window",
                Status = tailLag > _coordinationSettings.TailReopenMaxSessionLag ? "unsafe" : "ok",
                Details = $"session_lag={tailLag};max_allowed={_coordinationSettings.TailReopenMaxSessionLag}"
            }
        };

        var blockingReasons = checks
            .Where(x => string.Equals(x.Status, "unsafe", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Code}: {x.Details}")
            .ToList();
        var hasWarning = checks.Any(x => string.Equals(x.Status, "warning", StringComparison.OrdinalIgnoreCase));
        var result = blockingReasons.Count > 0
            ? IntegrityPreflightStates.Unsafe
            : hasWarning
                ? IntegrityPreflightStates.Warning
                : IntegrityPreflightStates.Clean;

        return new IntegrityPreflightSummary
        {
            Result = result,
            Scope = $"stage5_scoped_repair:chat_id={chatId}",
            Checks = checks,
            BlockingReasons = blockingReasons,
            GeneratedAtUtc = DateTime.UtcNow
        };
    }

    private void EnsureApplyAllowed(
        Stage5ScopedRepairSummary summary,
        Stage5BackupGuardrailEvaluation backupGuardrail,
        IntegrityPreflightSummary integrityPreflight,
        Stage5ScopedRepairExecutionOptions options)
    {
        _logger.LogInformation(
            "Stage5 scoped repair preflight: chat_id={ChatId}, backup_passed={BackupPassed}, backup_reason={BackupReason}, preflight_result={PreflightResult}, blocking_count={BlockingCount}, override_used={OverrideUsed}",
            summary.ChatId,
            backupGuardrail.Passed,
            backupGuardrail.Reason,
            integrityPreflight.Result,
            integrityPreflight.BlockingReasons.Count,
            backupGuardrail.OverrideUsed || options.HasValidOverride);

        if (!backupGuardrail.Passed)
        {
            throw new InvalidOperationException($"Backup guardrail blocked apply: {backupGuardrail.Reason}");
        }

        if (string.Equals(integrityPreflight.Result, IntegrityPreflightStates.Unsafe, StringComparison.OrdinalIgnoreCase))
        {
            var details = integrityPreflight.BlockingReasons.Count == 0
                ? "unsafe scope detected"
                : string.Join("; ", integrityPreflight.BlockingReasons);
            throw new InvalidOperationException($"Integrity preflight blocked apply: {details}");
        }

        if (string.Equals(integrityPreflight.Result, IntegrityPreflightStates.Warning, StringComparison.OrdinalIgnoreCase)
            && !_safetySettings.AllowWarningApplyWithoutOverride
            && !options.HasValidOverride)
        {
            throw new InvalidOperationException(
                "Integrity preflight returned warning; explicit approved override is required before apply.");
        }
    }

    private async Task<ChatPhaseGuardDecision?> TryAcquireTailReopenAsync(
        long chatId,
        Stage5ScopedRepairExecutionOptions options,
        CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var maxWindow = TimeSpan.FromHours(Math.Max(1, _coordinationSettings.TailReopenMaxWindowHours));
        var auditId = options.Override?.AuditId ?? options.AuditId;
        return await _chatCoordinationService.TryAcquirePhaseAsync(
            chatId,
            ChatRuntimePhases.TailReopen,
            RepairPhaseOwnerId,
            reason: string.IsNullOrWhiteSpace(options.OperatorReason) ? "stage5_scoped_repair_apply" : options.OperatorReason,
            reopenWindowFromUtc: now - maxWindow,
            reopenWindowToUtc: now,
            reopenOperator: options.EffectiveOperatorIdentity,
            reopenAuditId: auditId,
            ct: ct);
    }

    private PhaseLeaseHeartbeatHandle StartPhaseLeaseHeartbeat(
        long chatId,
        string phase,
        string reason,
        CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return PhaseLeaseHeartbeatHandle.None;
        }

        var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaseLostCts = new CancellationTokenSource();
        var interval = ResolvePhaseLeaseRenewInterval();
        var task = Task.Run(
            () => RunPhaseLeaseHeartbeatLoopAsync(chatId, phase, reason, interval, heartbeatCts.Token, leaseLostCts),
            CancellationToken.None);
        return new PhaseLeaseHeartbeatHandle(heartbeatCts, leaseLostCts, task);
    }

    private async Task StopPhaseLeaseHeartbeatAsync(PhaseLeaseHeartbeatHandle heartbeat)
    {
        if (!heartbeat.IsActive)
        {
            return;
        }

        heartbeat.HeartbeatTokenSource!.Cancel();
        try
        {
            await heartbeat.RunTask!;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            heartbeat.HeartbeatTokenSource.Dispose();
            heartbeat.LeaseLostTokenSource?.Dispose();
        }
    }

    private static CancellationTokenSource CreateLeaseLinkedTokenSource(PhaseLeaseHeartbeatHandle heartbeat, CancellationToken ct)
    {
        return heartbeat.LeaseLostTokenSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, heartbeat.LeaseLostTokenSource.Token);
    }

    private async Task RunPhaseLeaseHeartbeatLoopAsync(
        long chatId,
        string phase,
        string reason,
        TimeSpan interval,
        CancellationToken ct,
        CancellationTokenSource leaseLostCts)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var renewDecision = await _chatCoordinationService.TryRenewPhaseLeaseAsync(
                    chatId,
                    phase,
                    RepairPhaseOwnerId,
                    reason,
                    ct);
                if (renewDecision.Renewed)
                {
                    _logger.LogDebug(
                        "Scoped repair lease renewed: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}, lease_expires_at_utc={LeaseExpiresAtUtc}",
                        chatId,
                        phase,
                        RepairPhaseOwnerId,
                        renewDecision.CurrentLeaseExpiresAtUtc);
                    continue;
                }

                _logger.LogWarning(
                    "Scoped repair lease renewal stopped: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}, deny_code={DenyCode}, deny_reason={DenyReason}",
                    chatId,
                    phase,
                    RepairPhaseOwnerId,
                    renewDecision.DenyCode,
                    renewDecision.DenyReason);
                if (!leaseLostCts.IsCancellationRequested)
                {
                    leaseLostCts.Cancel();
                }

                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Scoped repair lease heartbeat iteration failed: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}",
                    chatId,
                    phase,
                    RepairPhaseOwnerId);
            }
        }
    }

    private TimeSpan ResolvePhaseLeaseRenewInterval()
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _coordinationSettings.PhaseGuardLeaseTtlMinutes));
        var seconds = Math.Max(5, ttl.TotalSeconds / 3d);
        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task<int> QuerySingleIntAsync(NpgsqlConnection conn, string sql, long chatId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("chat_id", chatId);
        var scalar = await cmd.ExecuteScalarAsync(ct);
        if (scalar is null || scalar is DBNull)
        {
            return 0;
        }

        return Convert.ToInt32(scalar);
    }

    private static string BuildExpectedBackupScope(long chatId) => $"stage5_scoped_repair:chat_id={chatId}";

    private static bool BackupScopeMatches(string scope, long chatId)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return false;
        }

        var normalized = scope.Trim().ToLowerInvariant();
        return normalized.Contains("stage5_scoped_repair", StringComparison.Ordinal)
               && normalized.Contains($"chat_id={chatId}", StringComparison.Ordinal);
    }

    private async Task<Stage5RepairSnapshot> GetSnapshotAsync(long chatId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string sql = """
            with session_stats as (
                select
                    count(*)::int as total_sessions,
                    count(*) filter (where is_analyzed)::int as analyzed_sessions,
                    count(*) filter (where not is_analyzed and not is_finalized)::int as pending_sessions,
                    min(session_index) filter (where not is_analyzed and not is_finalized) as min_pending_session_index,
                    max(session_index) filter (where is_analyzed) as max_analyzed_session_index
                from chat_sessions
                where chat_id = @chat_id
            ),
            extraction_stats as (
                select
                    count(*)::int as extraction_count,
                    count(*) filter (where m.source = 1)::int as archive_extractions,
                    count(*) filter (where m.source = 0)::int as realtime_extractions
                from message_extractions me
                join messages m on m.id = me.message_id
                where m.chat_id = @chat_id
                  and coalesce(me.is_quarantined, false) = false
            ),
            overlap_stats as (
                select count(*)::int as dual_tg_ids
                from (
                    select telegram_message_id
                    from messages
                    where chat_id = @chat_id
                    group by telegram_message_id
                    having count(distinct source) > 1
                ) d
            )
            select
                ss.total_sessions,
                ss.analyzed_sessions,
                ss.pending_sessions,
                ss.min_pending_session_index,
                ss.max_analyzed_session_index,
                es.extraction_count,
                es.archive_extractions,
                es.realtime_extractions,
                os.dual_tg_ids
            from session_stats ss
            cross join extraction_stats es
            cross join overlap_stats os;
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("chat_id", chatId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return new Stage5RepairSnapshot();
        }

        return new Stage5RepairSnapshot
        {
            TotalSessions = reader.GetInt32(0),
            AnalyzedSessions = reader.GetInt32(1),
            PendingSessions = reader.GetInt32(2),
            MinPendingSessionIndex = reader.IsDBNull(3) ? null : reader.GetInt32(3),
            MaxAnalyzedSessionIndex = reader.IsDBNull(4) ? null : reader.GetInt32(4),
            ActiveExtractions = reader.GetInt32(5),
            ArchiveExtractions = reader.GetInt32(6),
            RealtimeExtractions = reader.GetInt32(7),
            DualSourceTelegramIds = reader.GetInt32(8)
        };
    }

    private static Stage5RepairSnapshot BuildPredictedSnapshot(Stage5RepairSnapshot before, Stage5ScopedRepairPlan plan)
    {
        var pendingAfter = Math.Max(0, before.PendingSessions - plan.TrustedSessionIndexes.Count);
        var analyzedAfter = before.AnalyzedSessions + plan.TrustedSessionIndexes.Count;
        var realtimeAfter = Math.Max(0, before.RealtimeExtractions - plan.DualSourceMigrations.Count);
        var archiveAfter = before.ArchiveExtractions + plan.DualSourceMigrations.Count + plan.OrphanPlaceholderMessageIds.Count;

        return new Stage5RepairSnapshot
        {
            TotalSessions = before.TotalSessions,
            AnalyzedSessions = analyzedAfter,
            PendingSessions = pendingAfter,
            MinPendingSessionIndex = plan.ProjectedMinPendingSessionIndex,
            MaxAnalyzedSessionIndex = plan.ProjectedMaxAnalyzedSessionIndex,
            ActiveExtractions = before.ActiveExtractions + plan.OrphanPlaceholderMessageIds.Count,
            ArchiveExtractions = archiveAfter,
            RealtimeExtractions = realtimeAfter,
            DualSourceTelegramIds = before.DualSourceTelegramIds
        };
    }

    private async Task<Stage5ScopedRepairPlan> BuildPlanAsync(long chatId, CancellationToken ct)
    {
        var dualSourceMigrations = await LoadDualSourceMigrationsAsync(chatId, ct);
        var orphanPlaceholders = await LoadOrphanPlaceholderIdsAsync(chatId, ct);
        var trustedSessions = await LoadTrustedSessionIndexesAsync(chatId, ct);
        var problematicSessions = await LoadProblematicSessionIndexesAsync(chatId, ct);

        var projectedPending = problematicSessions.Except(trustedSessions).ToList();
        projectedPending.Sort();

        var projectedMinPending = projectedPending.Count == 0 ? (int?)null : projectedPending[0];
        var projectedMaxAnalyzed = await LoadProjectedMaxAnalyzedIndexAsync(chatId, trustedSessions, ct);

        return new Stage5ScopedRepairPlan
        {
            TrustedSessionIndexes = trustedSessions,
            DualSourceMigrations = dualSourceMigrations,
            OrphanPlaceholderMessageIds = orphanPlaceholders,
            ProblematicSessionIndexes = problematicSessions,
            ProjectedMinPendingSessionIndex = projectedMinPending,
            ProjectedMaxAnalyzedSessionIndex = projectedMaxAnalyzed
        };
    }

    private async Task<List<DualSourceMigrationItem>> LoadDualSourceMigrationsAsync(long chatId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string sql = """
            with pending as (
                select session_index, start_date, end_date
                from chat_sessions
                where chat_id = @chat_id
                  and not is_analyzed
                  and not is_finalized
            )
            select
                p.session_index,
                ma.id as archive_message_id,
                mr.id as realtime_message_id,
                ma.telegram_message_id
            from pending p
            join messages ma
              on ma.chat_id = @chat_id
             and ma.source = 1
             and ma.timestamp >= p.start_date
             and ma.timestamp <= p.end_date
            left join message_extractions mea
              on mea.message_id = ma.id
            join messages mr
              on mr.chat_id = @chat_id
             and mr.source = 0
             and mr.telegram_message_id = ma.telegram_message_id
            join message_extractions mer
              on mer.message_id = mr.id
             and coalesce(mer.is_quarantined, false) = false
            where mea.id is null
            order by p.session_index, ma.id;
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("chat_id", chatId);

        var result = new List<DualSourceMigrationItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new DualSourceMigrationItem
            {
                SessionIndex = reader.GetInt32(0),
                ArchiveMessageId = reader.GetInt64(1),
                RealtimeMessageId = reader.GetInt64(2),
                TelegramMessageId = reader.GetInt64(3)
            });
        }

        return result;
    }

    private async Task<List<long>> LoadOrphanPlaceholderIdsAsync(long chatId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string sql = """
            with pending as (
                select session_index, start_date, end_date
                from chat_sessions
                where chat_id = @chat_id
                  and not is_analyzed
                  and not is_finalized
            ),
            missing as (
                select
                    ma.id as archive_message_id,
                    ma.telegram_message_id,
                    ma.processing_status
                from pending p
                join messages ma
                  on ma.chat_id = @chat_id
                 and ma.source = 1
                 and ma.timestamp >= p.start_date
                 and ma.timestamp <= p.end_date
                left join message_extractions mea
                  on mea.message_id = ma.id
                where mea.id is null
            )
            select m.archive_message_id
            from missing m
            left join messages mr
              on mr.chat_id = @chat_id
             and mr.source = 0
             and mr.telegram_message_id = m.telegram_message_id
            left join message_extractions mer
              on mer.message_id = mr.id
             and coalesce(mer.is_quarantined, false) = false
            where m.processing_status = 1
              and mer.id is null
            order by m.archive_message_id;
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("chat_id", chatId);

        var result = new List<long>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetInt64(0));
        }

        return result;
    }

    private async Task<List<int>> LoadTrustedSessionIndexesAsync(long chatId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string sql = """
            with pending as (
                select session_index, start_date, end_date, coalesce(length(summary), 0) as summary_len
                from chat_sessions
                where chat_id = @chat_id
                  and not is_analyzed
                  and not is_finalized
            ),
            blocking as (
                select
                    p.session_index,
                    count(*)::int as blocking_missing
                from pending p
                join messages ma
                  on ma.chat_id = @chat_id
                 and ma.source = 1
                 and ma.timestamp >= p.start_date
                 and ma.timestamp <= p.end_date
                left join message_extractions mea
                  on mea.message_id = ma.id
                left join messages mr
                  on mr.chat_id = @chat_id
                 and mr.source = 0
                 and mr.telegram_message_id = ma.telegram_message_id
                left join message_extractions mer
                  on mer.message_id = mr.id
                 and coalesce(mer.is_quarantined, false) = false
                where mea.id is null
                  and ma.processing_status = 1
                  and mer.id is null
                group by p.session_index
            )
            select p.session_index
            from pending p
            left join blocking b on b.session_index = p.session_index
            where p.summary_len > 0
              and coalesce(b.blocking_missing, 0) = 0
            order by p.session_index;
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("chat_id", chatId);

        var result = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    private async Task<List<int>> LoadProblematicSessionIndexesAsync(long chatId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        const string sql = """
            with pending as (
                select session_index, start_date, end_date, coalesce(length(summary), 0) as summary_len
                from chat_sessions
                where chat_id = @chat_id
                  and not is_analyzed
                  and not is_finalized
            ),
            blocking as (
                select distinct p.session_index
                from pending p
                join messages ma
                  on ma.chat_id = @chat_id
                 and ma.source = 1
                 and ma.timestamp >= p.start_date
                 and ma.timestamp <= p.end_date
                left join message_extractions mea
                  on mea.message_id = ma.id
                left join messages mr
                  on mr.chat_id = @chat_id
                 and mr.source = 0
                 and mr.telegram_message_id = ma.telegram_message_id
                left join message_extractions mer
                  on mer.message_id = mr.id
                 and coalesce(mer.is_quarantined, false) = false
                where mea.id is null
                  and ma.processing_status = 1
                  and mer.id is null
            )
            select p.session_index
            from pending p
            where p.summary_len = 0
               or exists (select 1 from blocking b where b.session_index = p.session_index)
            order by p.session_index;
            """;

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("chat_id", chatId);

        var result = new List<int>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetInt32(0));
        }

        return result;
    }

    private async Task<int?> LoadProjectedMaxAnalyzedIndexAsync(long chatId, IReadOnlyCollection<int> trustedSessionIndexes, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existingMax = await db.ChatSessions
            .Where(x => x.ChatId == chatId && x.IsAnalyzed)
            .Select(x => (int?)x.SessionIndex)
            .MaxAsync(ct);

        if (trustedSessionIndexes.Count == 0)
        {
            return existingMax;
        }

        var trustedMax = trustedSessionIndexes.Max();
        if (!existingMax.HasValue)
        {
            return trustedMax;
        }

        return Math.Max(existingMax.Value, trustedMax);
    }

    private async Task<Stage5RepairApplyResult> ApplyAsync(long chatId, Stage5ScopedRepairPlan plan, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        await using var tx = await conn.BeginTransactionAsync(ct);

        var archiveInserted = 0;
        foreach (var item in plan.DualSourceMigrations)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                insert into message_extractions (
                    message_id,
                    cheap_json,
                    expensive_json,
                    needs_expensive,
                    is_quarantined,
                    quarantine_reason,
                    quarantined_at,
                    expensive_retry_count,
                    expensive_next_retry_at,
                    expensive_last_error,
                    created_at,
                    updated_at
                )
                select
                    @archive_message_id,
                    me.cheap_json,
                    me.expensive_json,
                    false,
                    false,
                    null,
                    null,
                    0,
                    null,
                    null,
                    now(),
                    now()
                from message_extractions me
                where me.message_id = @realtime_message_id
                  and coalesce(me.is_quarantined, false) = false
                on conflict (message_id) do nothing;
                """;
            cmd.Parameters.AddWithValue("archive_message_id", item.ArchiveMessageId);
            cmd.Parameters.AddWithValue("realtime_message_id", item.RealtimeMessageId);
            archiveInserted += await cmd.ExecuteNonQueryAsync(ct);
        }

        var quarantined = 0;
        if (plan.DualSourceMigrations.Count > 0)
        {
            var realtimeIds = plan.DualSourceMigrations
                .Select(x => x.RealtimeMessageId)
                .Distinct()
                .ToArray();

            await using var quarantineCmd = conn.CreateCommand();
            quarantineCmd.Transaction = tx;
            quarantineCmd.CommandText = """
                update message_extractions
                set is_quarantined = true,
                    quarantine_reason = 'stage5_scoped_repair_dual_source',
                    quarantined_at = now(),
                    needs_expensive = false,
                    expensive_next_retry_at = null,
                    updated_at = now()
                where message_id = any(@message_ids)
                  and coalesce(is_quarantined, false) = false;
                """;
            quarantineCmd.Parameters.Add(new NpgsqlParameter("message_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
            {
                Value = realtimeIds
            });
            quarantined = await quarantineCmd.ExecuteNonQueryAsync(ct);
        }

        var orphansInserted = 0;
        if (plan.OrphanPlaceholderMessageIds.Count > 0)
        {
            var orphanIds = plan.OrphanPlaceholderMessageIds
                .Distinct()
                .ToArray();

            await using var orphanCmd = conn.CreateCommand();
            orphanCmd.Transaction = tx;
            orphanCmd.CommandText = """
                insert into message_extractions (
                    message_id,
                    cheap_json,
                    expensive_json,
                    needs_expensive,
                    is_quarantined,
                    quarantine_reason,
                    quarantined_at,
                    expensive_retry_count,
                    expensive_next_retry_at,
                    expensive_last_error,
                    created_at,
                    updated_at
                )
                select
                    src.message_id,
                    '{"repair":{"type":"stage5_orphan_placeholder","scope":"scoped_stage5_repair"},"facts":[],"claims":[],"events":[]}'::jsonb,
                    null,
                    false,
                    false,
                    null,
                    null,
                    0,
                    null,
                    null,
                    now(),
                    now()
                from unnest(@message_ids::bigint[]) as src(message_id)
                on conflict (message_id) do nothing;
                """;
            orphanCmd.Parameters.Add(new NpgsqlParameter("message_ids", NpgsqlDbType.Array | NpgsqlDbType.Bigint)
            {
                Value = orphanIds
            });
            orphansInserted = await orphanCmd.ExecuteNonQueryAsync(ct);
        }

        var sessionsMarked = 0;
        if (plan.TrustedSessionIndexes.Count > 0)
        {
            var sessionIndexes = plan.TrustedSessionIndexes
                .Distinct()
                .ToArray();

            await using var sessionCmd = conn.CreateCommand();
            sessionCmd.Transaction = tx;
            sessionCmd.CommandText = """
                update chat_sessions
                set is_analyzed = true,
                    updated_at = now()
                where chat_id = @chat_id
                  and session_index = any(@session_indexes)
                  and not is_analyzed;
                """;
            sessionCmd.Parameters.AddWithValue("chat_id", chatId);
            sessionCmd.Parameters.Add(new NpgsqlParameter("session_indexes", NpgsqlDbType.Array | NpgsqlDbType.Integer)
            {
                Value = sessionIndexes
            });
            sessionsMarked = await sessionCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        return new Stage5RepairApplyResult
        {
            ArchiveExtractionsInserted = archiveInserted,
            RealtimeExtractionsQuarantined = quarantined,
            OrphanPlaceholdersInserted = orphansInserted,
            SessionsMarkedAnalyzed = sessionsMarked
        };
    }

    private static async Task<string> WriteAuditAsync(Stage5ScopedRepairSummary summary, string? auditDirectory, CancellationToken ct)
    {
        var baseDir = string.IsNullOrWhiteSpace(auditDirectory)
            ? Path.Combine(AppContext.BaseDirectory, "artifacts", "stage5-repair")
            : auditDirectory.Trim();

        Directory.CreateDirectory(baseDir);

        var mode = summary.DryRunOnly ? "dryrun" : "apply";
        var fileName = $"stage5-scoped-repair-{mode}-{summary.ChatId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
        var path = Path.Combine(baseDir, fileName);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, summary, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }, ct);

        return path;
    }

    private sealed class PhaseLeaseHeartbeatHandle
    {
        public static PhaseLeaseHeartbeatHandle None { get; } = new();

        public PhaseLeaseHeartbeatHandle()
        {
        }

        public PhaseLeaseHeartbeatHandle(
            CancellationTokenSource heartbeatTokenSource,
            CancellationTokenSource leaseLostTokenSource,
            Task runTask)
        {
            HeartbeatTokenSource = heartbeatTokenSource;
            LeaseLostTokenSource = leaseLostTokenSource;
            RunTask = runTask;
        }

        public CancellationTokenSource? HeartbeatTokenSource { get; }
        public CancellationTokenSource? LeaseLostTokenSource { get; }
        public Task? RunTask { get; }
        public bool IsActive => HeartbeatTokenSource is not null && RunTask is not null;
        public bool LeaseLost => LeaseLostTokenSource?.IsCancellationRequested == true;
    }
}

public sealed record Stage5ScopedRepairResult(Stage5ScopedRepairSummary Summary, string AuditPath);

public sealed class Stage5ScopedRepairSummary
{
    public long ChatId { get; set; }
    public bool DryRunOnly { get; set; }
    public DateTime GeneratedAtUtc { get; set; }
    public Stage5RepairSnapshot Before { get; set; } = new();
    public Stage5RepairSnapshot PredictedAfter { get; set; } = new();
    public Stage5RepairSnapshot? After { get; set; }
    public Stage5ScopedRepairPlan Plan { get; set; } = new();
    public Stage5BackupGuardrailEvaluation BackupGuardrail { get; set; } = new();
    public IntegrityPreflightSummary IntegrityPreflight { get; set; } = new();
    public ChatPhaseGuardDecision? TailReopenPhaseDecision { get; set; }
    public Stage5RepairApplyResult? Applied { get; set; }
}

public sealed class Stage5ScopedRepairExecutionOptions
{
    public BackupMetadataEvidence? BackupEvidence { get; set; }
    public RiskOperationOverride? Override { get; set; }
    public string OperatorIdentity { get; set; } = string.Empty;
    public string OperatorReason { get; set; } = "stage5_scoped_repair_apply";
    public string AuditId { get; set; } = string.Empty;

    public bool HasValidOverride =>
        !string.IsNullOrWhiteSpace(Override?.OperatorIdentity)
        && !string.IsNullOrWhiteSpace(Override?.Reason)
        && !string.IsNullOrWhiteSpace(Override?.ApprovalToken)
        && !string.IsNullOrWhiteSpace(Override?.AuditId);

    public string EffectiveOperatorIdentity =>
        !string.IsNullOrWhiteSpace(OperatorIdentity)
            ? OperatorIdentity
            : Override?.OperatorIdentity ?? "unknown_operator";
}

public sealed class Stage5BackupGuardrailEvaluation
{
    public bool Required { get; set; }
    public bool Passed { get; set; }
    public bool OverrideUsed { get; set; }
    public bool UsingEvidence { get; set; }
    public string BackupId { get; set; } = string.Empty;
    public string BackupScope { get; set; } = string.Empty;
    public double BackupAgeHours { get; set; }
    public string OverrideAuditId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class Stage5RepairSnapshot
{
    public int TotalSessions { get; set; }
    public int AnalyzedSessions { get; set; }
    public int PendingSessions { get; set; }
    public int? MinPendingSessionIndex { get; set; }
    public int? MaxAnalyzedSessionIndex { get; set; }
    public int ActiveExtractions { get; set; }
    public int ArchiveExtractions { get; set; }
    public int RealtimeExtractions { get; set; }
    public int DualSourceTelegramIds { get; set; }
}

public sealed class Stage5ScopedRepairPlan
{
    public List<int> TrustedSessionIndexes { get; set; } = [];
    public List<DualSourceMigrationItem> DualSourceMigrations { get; set; } = [];
    public List<long> OrphanPlaceholderMessageIds { get; set; } = [];
    public List<int> ProblematicSessionIndexes { get; set; } = [];
    public int? ProjectedMinPendingSessionIndex { get; set; }
    public int? ProjectedMaxAnalyzedSessionIndex { get; set; }
}

public sealed class DualSourceMigrationItem
{
    public int SessionIndex { get; set; }
    public long ArchiveMessageId { get; set; }
    public long RealtimeMessageId { get; set; }
    public long TelegramMessageId { get; set; }
}

public sealed class Stage5RepairApplyResult
{
    public int ArchiveExtractionsInserted { get; set; }
    public int RealtimeExtractionsQuarantined { get; set; }
    public int OrphanPlaceholdersInserted { get; set; }
    public int SessionsMarkedAnalyzed { get; set; }
}

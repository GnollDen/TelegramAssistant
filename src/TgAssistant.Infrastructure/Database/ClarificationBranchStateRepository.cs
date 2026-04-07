using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ClarificationBranchStateRepository : IClarificationBranchStateRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ClarificationBranchStateRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ClarificationBranchStateRecord?> ApplyOutcomeAsync(
        ModelPassAuditRecord record,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var envelope = record.Envelope;
        var scopeKey = NormalizeRequired(envelope.ScopeKey);
        var targetType = NormalizeRequired(envelope.Target.TargetType);
        var targetRef = NormalizeRequired(envelope.Target.TargetRef);
        if (string.IsNullOrWhiteSpace(scopeKey)
            || string.IsNullOrWhiteSpace(targetType)
            || string.IsNullOrWhiteSpace(targetRef))
        {
            return null;
        }

        var branchFamily = ResolveBranchFamily(envelope.Stage, envelope.PassFamily);
        var branchKey = $"{scopeKey}|{branchFamily}|{targetType}|{targetRef}";
        var now = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ClarificationBranchStates
            .FirstOrDefaultAsync(x => x.BranchKey == branchKey, ct);

        if (string.Equals(envelope.ResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
        {
            if (row == null)
            {
                row = new DbClarificationBranchState
                {
                    Id = Guid.NewGuid(),
                    BranchKey = branchKey,
                    FirstBlockedAtUtc = now
                };
                db.ClarificationBranchStates.Add(row);
            }

            row.ScopeKey = scopeKey;
            row.BranchFamily = branchFamily;
            row.Stage = NormalizeRequired(envelope.Stage);
            row.PassFamily = NormalizeRequired(envelope.PassFamily);
            row.TargetType = targetType;
            row.TargetRef = targetRef;
            row.PersonId = envelope.PersonId;
            row.LastModelPassRunId = record.ModelPassRunId;
            row.Status = ClarificationBranchStatuses.Open;
            row.BlockReason = ResolveBlockReason(record);
            row.RequiredAction = envelope.Unknowns
                .Select(x => NormalizeNullable(x.RequiredAction))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            row.DetailsJson = MergeDetailsJson(row.DetailsJson, record, resolvedAtUtc: null, now);
            row.LastBlockedAtUtc = now;
            row.ResolvedAtUtc = null;

            await db.SaveChangesAsync(ct);
            return Map(row);
        }

        if (row == null
            || !string.Equals(row.Status, ClarificationBranchStatuses.Open, StringComparison.Ordinal)
            || !string.Equals(envelope.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            return null;
        }

        row.LastModelPassRunId = record.ModelPassRunId;
        row.Status = ClarificationBranchStatuses.Resolved;
        row.DetailsJson = MergeDetailsJson(row.DetailsJson, record, now, now);
        row.ResolvedAtUtc = now;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAsync(
        string scopeKey,
        CancellationToken ct = default)
    {
        var normalizedScopeKey = NormalizeRequired(scopeKey);
        if (string.IsNullOrWhiteSpace(normalizedScopeKey))
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ClarificationBranchStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == normalizedScopeKey && x.Status == ClarificationBranchStatuses.Open)
            .OrderByDescending(x => x.LastBlockedAtUtc)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<List<ClarificationBranchStateRecord>> GetOpenByScopeAndFamilyAsync(
        string scopeKey,
        string branchFamily,
        CancellationToken ct = default)
    {
        var normalizedScopeKey = NormalizeRequired(scopeKey);
        var normalizedBranchFamily = NormalizeRequired(branchFamily);
        if (string.IsNullOrWhiteSpace(normalizedScopeKey)
            || string.IsNullOrWhiteSpace(normalizedBranchFamily))
        {
            return [];
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.ClarificationBranchStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == normalizedScopeKey
                && x.BranchFamily == normalizedBranchFamily
                && x.Status == ClarificationBranchStatuses.Open)
            .OrderByDescending(x => x.LastBlockedAtUtc)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    private static string ResolveBranchFamily(string stage, string passFamily)
    {
        var normalizedStage = NormalizeRequired(stage);
        var normalizedPassFamily = NormalizeRequired(passFamily);

        if (StageSemanticContract.TryMapRuntimeStageAndPassFamilyToSemanticOutputFamily(
                normalizedStage,
                normalizedPassFamily,
                out var semanticOutputFamily)
            && !string.IsNullOrWhiteSpace(semanticOutputFamily))
        {
            if (string.Equals(semanticOutputFamily, StageSemanticOwnedOutputFamilies.Stage6BootstrapGraph, StringComparison.Ordinal)
                || string.Equals(semanticOutputFamily, StageSemanticOwnedOutputFamilies.Stage6DiscoveryPool, StringComparison.Ordinal))
            {
                return Stage8RecomputeTargetFamilies.Stage6Bootstrap;
            }

            if (StageSemanticContract.TryMapSemanticFamilyToStage8RecomputeTargetFamily(semanticOutputFamily, out var targetFamily)
                && !string.IsNullOrWhiteSpace(targetFamily))
            {
                return targetFamily;
            }
        }

        return $"{normalizedStage}:{normalizedPassFamily}";
    }

    private static string ResolveBlockReason(ModelPassAuditRecord record)
    {
        var summary = NormalizeNullable(record.Envelope.OutputSummary.BlockedReason)
            ?? record.Envelope.Unknowns
                .Select(x => NormalizeNullable(x.Summary))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? record.Normalization.Issues
                .Select(x => NormalizeNullable(x.Summary))
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? NormalizeNullable(record.Envelope.OutputSummary.Summary);

        return summary ?? "Operator clarification is required before this graph branch can continue.";
    }

    private static string MergeDetailsJson(
        string? existingDetailsJson,
        ModelPassAuditRecord record,
        DateTime? resolvedAtUtc,
        DateTime changedAtUtc)
    {
        JsonObject root;
        if (string.IsNullOrWhiteSpace(existingDetailsJson))
        {
            root = [];
        }
        else
        {
            try
            {
                root = JsonNode.Parse(existingDetailsJson) as JsonObject ?? [];
            }
            catch (JsonException)
            {
                root = [];
            }
        }

        var envelope = record.Envelope;
        root["branch"] = new JsonObject
        {
            ["scope_ref"] = envelope.Scope.ScopeRef,
            ["scope_type"] = envelope.Scope.ScopeType,
            ["target_type"] = envelope.Target.TargetType,
            ["target_ref"] = envelope.Target.TargetRef,
            ["trigger_kind"] = NormalizeNullable(envelope.TriggerKind),
            ["trigger_ref"] = NormalizeNullable(envelope.TriggerRef),
            ["updated_at_utc"] = changedAtUtc.ToString("O")
        };
        root["unknowns"] = new JsonArray(
            envelope.Unknowns.Select(x => JsonSerializer.SerializeToNode(new
            {
                unknown_type = x.UnknownType,
                summary = x.Summary,
                required_action = x.RequiredAction
            })!).ToArray());
        root["normalization_issues"] = new JsonArray(
            record.Normalization.Issues.Select(x => JsonSerializer.SerializeToNode(new
            {
                severity = x.Severity,
                code = x.Code,
                summary = x.Summary,
                path = x.Path
            })!).ToArray());
        root["last_result"] = new JsonObject
        {
            ["model_pass_run_id"] = record.ModelPassRunId.ToString("D"),
            ["result_status"] = envelope.ResultStatus,
            ["output_summary"] = envelope.OutputSummary.Summary,
            ["resolved_at_utc"] = resolvedAtUtc?.ToString("O")
        };

        return root.ToJsonString();
    }

    private static ClarificationBranchStateRecord Map(DbClarificationBranchState row)
    {
        return new ClarificationBranchStateRecord
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            BranchFamily = row.BranchFamily,
            BranchKey = row.BranchKey,
            Stage = row.Stage,
            PassFamily = row.PassFamily,
            TargetType = row.TargetType,
            TargetRef = row.TargetRef,
            PersonId = row.PersonId,
            LastModelPassRunId = row.LastModelPassRunId,
            Status = row.Status,
            BlockReason = row.BlockReason,
            RequiredAction = row.RequiredAction,
            DetailsJson = row.DetailsJson,
            FirstBlockedAtUtc = row.FirstBlockedAtUtc,
            LastBlockedAtUtc = row.LastBlockedAtUtc,
            ResolvedAtUtc = row.ResolvedAtUtc
        };
    }

    private static string NormalizeRequired(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

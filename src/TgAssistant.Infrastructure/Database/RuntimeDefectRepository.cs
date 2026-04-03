using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class RuntimeDefectRepository : IRuntimeDefectRepository
{
    private static readonly IReadOnlySet<string> AllowedClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        RuntimeDefectClasses.Ingestion,
        RuntimeDefectClasses.Data,
        RuntimeDefectClasses.Identity,
        RuntimeDefectClasses.Model,
        RuntimeDefectClasses.Normalization,
        RuntimeDefectClasses.ControlPlane,
        RuntimeDefectClasses.Cost,
        RuntimeDefectClasses.SemanticDrift
    };

    private static readonly IReadOnlySet<string> AllowedSeverities = new HashSet<string>(StringComparer.Ordinal)
    {
        RuntimeDefectSeverities.Low,
        RuntimeDefectSeverities.Medium,
        RuntimeDefectSeverities.High,
        RuntimeDefectSeverities.Critical
    };

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public RuntimeDefectRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<RuntimeDefectRecord> UpsertAsync(
        RuntimeDefectUpsertRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var defectClass = NormalizeClass(request.DefectClass);
        var severity = NormalizeSeverity(request.Severity);
        var scopeKey = string.IsNullOrWhiteSpace(request.ScopeKey) ? "global" : request.ScopeKey.Trim();
        var dedupeKey = string.IsNullOrWhiteSpace(request.DedupeKey)
            ? $"{defectClass}|{scopeKey}|{request.ObjectType ?? "runtime"}|{request.ObjectRef ?? "unresolved"}"
            : request.DedupeKey.Trim();
        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? $"{defectClass} defect recorded."
            : request.Summary.Trim();
        var detailsJson = string.IsNullOrWhiteSpace(request.DetailsJson) ? "{}" : request.DetailsJson.Trim();
        var now = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.RuntimeDefects
            .FirstOrDefaultAsync(
                x => x.DedupeKey == dedupeKey && x.Status == RuntimeDefectStatuses.Open,
                ct);

        if (row == null)
        {
            var escalation = RuntimeDefectEscalationPolicy.Resolve(defectClass, severity, 1);
            row = new DbRuntimeDefect
            {
                Id = Guid.NewGuid(),
                DefectClass = defectClass,
                Severity = severity,
                Status = RuntimeDefectStatuses.Open,
                ScopeKey = scopeKey,
                DedupeKey = dedupeKey,
                RunId = request.RunId,
                ObjectType = NormalizeNullable(request.ObjectType),
                ObjectRef = NormalizeNullable(request.ObjectRef),
                Summary = summary,
                DetailsJson = detailsJson,
                OccurrenceCount = 1,
                EscalationAction = escalation.EscalationAction,
                EscalationReason = escalation.EscalationReason,
                FirstSeenAtUtc = now,
                LastSeenAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.RuntimeDefects.Add(row);
        }
        else
        {
            row.Severity = MaxSeverity(row.Severity, severity);
            row.RunId = request.RunId ?? row.RunId;
            row.ObjectType = NormalizeNullable(request.ObjectType) ?? row.ObjectType;
            row.ObjectRef = NormalizeNullable(request.ObjectRef) ?? row.ObjectRef;
            row.Summary = summary;
            row.DetailsJson = detailsJson;
            row.OccurrenceCount += 1;
            row.LastSeenAtUtc = now;
            row.UpdatedAtUtc = now;
            var escalation = RuntimeDefectEscalationPolicy.Resolve(row.DefectClass, row.Severity, row.OccurrenceCount);
            row.EscalationAction = escalation.EscalationAction;
            row.EscalationReason = escalation.EscalationReason;
        }

        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<List<RuntimeDefectRecord>> GetOpenAsync(
        int limit = 200,
        CancellationToken ct = default)
    {
        var boundedLimit = Math.Clamp(limit, 1, 2000);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.RuntimeDefects
            .AsNoTracking()
            .Where(x => x.Status == RuntimeDefectStatuses.Open)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Take(boundedLimit)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<int> ResolveOpenByDedupeKeyAsync(
        string dedupeKey,
        Guid? runId = null,
        CancellationToken ct = default)
    {
        var normalizedDedupe = dedupeKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedDedupe))
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var rows = await db.RuntimeDefects
            .Where(x => x.DedupeKey == normalizedDedupe && x.Status == RuntimeDefectStatuses.Open)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return 0;
        }

        foreach (var row in rows)
        {
            row.Status = RuntimeDefectStatuses.Resolved;
            row.ResolvedAtUtc = now;
            row.RunId = runId ?? row.RunId;
            row.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private static RuntimeDefectRecord Map(DbRuntimeDefect row)
    {
        return new RuntimeDefectRecord
        {
            Id = row.Id,
            DefectClass = row.DefectClass,
            Severity = row.Severity,
            Status = row.Status,
            ScopeKey = row.ScopeKey,
            DedupeKey = row.DedupeKey,
            RunId = row.RunId,
            ObjectType = row.ObjectType,
            ObjectRef = row.ObjectRef,
            Summary = row.Summary,
            DetailsJson = row.DetailsJson,
            OccurrenceCount = row.OccurrenceCount,
            EscalationAction = row.EscalationAction,
            EscalationReason = row.EscalationReason,
            FirstSeenAtUtc = row.FirstSeenAtUtc,
            LastSeenAtUtc = row.LastSeenAtUtc,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            ResolvedAtUtc = row.ResolvedAtUtc
        };
    }

    private static string NormalizeClass(string defectClass)
    {
        var value = defectClass?.Trim() ?? string.Empty;
        return AllowedClasses.Contains(value) ? value : RuntimeDefectClasses.ControlPlane;
    }

    private static string NormalizeSeverity(string severity)
    {
        var value = severity?.Trim() ?? string.Empty;
        return AllowedSeverities.Contains(value) ? value : RuntimeDefectSeverities.Medium;
    }

    private static string MaxSeverity(string current, string incoming)
    {
        return SeverityRank(incoming) > SeverityRank(current) ? incoming : current;
    }

    private static int SeverityRank(string severity)
        => severity switch
        {
            RuntimeDefectSeverities.Low => 1,
            RuntimeDefectSeverities.Medium => 2,
            RuntimeDefectSeverities.High => 3,
            RuntimeDefectSeverities.Critical => 4,
            _ => 0
        };

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

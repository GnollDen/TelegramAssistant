using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class TemporalPersonStateRepository : ITemporalPersonStateRepository
{
    private const string ActivePersonStatus = "active";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public TemporalPersonStateRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<TemporalPersonState> InsertAsync(
        TemporalPersonStateWriteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = NormalizeRequired(request.ScopeKey, nameof(request.ScopeKey));
        var subjectRef = NormalizeRequired(request.SubjectRef, nameof(request.SubjectRef));
        var factType = NormalizeRequired(request.FactType, nameof(request.FactType));
        var value = NormalizeRequired(request.Value, nameof(request.Value));
        var triggerKind = NormalizeRequired(request.TriggerKind, nameof(request.TriggerKind));
        var triggerRef = NormalizeOptional(request.TriggerRef);
        var trackedPersonId = request.TrackedPersonId;
        if (trackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(request));
        }

        var factCategory = NormalizeRequired(request.FactCategory, nameof(request.FactCategory));
        if (!TemporalPersonStateFactCategories.IsSupported(factCategory))
        {
            throw new InvalidOperationException($"Unsupported fact category: {factCategory}");
        }

        var stateStatus = NormalizeRequired(request.StateStatus, nameof(request.StateStatus));
        if (!TemporalPersonStateStatuses.IsSupported(stateStatus))
        {
            throw new InvalidOperationException($"Unsupported state status: {stateStatus}");
        }

        var validFromUtc = request.ValidFromUtc == default ? DateTime.UtcNow : request.ValidFromUtc;
        var validToUtc = request.ValidToUtc;
        if (validToUtc.HasValue && validToUtc.Value < validFromUtc)
        {
            throw new InvalidOperationException("valid_to_utc cannot be earlier than valid_from_utc.");
        }

        var evidenceRefs = request.EvidenceRefs?
            .Select(NormalizeOptional)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        var nowUtc = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPersonExists = await db.Persons
            .AsNoTracking()
            .AnyAsync(x => x.Id == trackedPersonId && x.ScopeKey == scopeKey && x.Status == ActivePersonStatus, ct);
        if (!trackedPersonExists)
        {
            throw new InvalidOperationException("Tracked person not found or inactive.");
        }

        if (request.SupersedesStateId.HasValue)
        {
            var referencedState = await db.TemporalPersonStates.AsNoTracking().FirstOrDefaultAsync(
                x => x.Id == request.SupersedesStateId.Value,
                ct);
            if (referencedState is null
                || !string.Equals(referencedState.ScopeKey, scopeKey, StringComparison.Ordinal)
                || referencedState.TrackedPersonId != trackedPersonId
                || !string.Equals(referencedState.SubjectRef, subjectRef, StringComparison.Ordinal)
                || !string.Equals(referencedState.FactType, factType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("supersedes_state_id must reference the same scope/person/subject/fact tuple.");
            }
        }

        if (TemporalSingleValuedFactFamilies.Contains(factType))
        {
            var existingOpen = await db.TemporalPersonStates
                .FirstOrDefaultAsync(x =>
                    x.ScopeKey == scopeKey
                    && x.SubjectRef == subjectRef
                    && x.FactType == factType
                    && x.StateStatus == TemporalPersonStateStatuses.Open
                    && x.ValidToUtc == null,
                    ct);
            if (existingOpen != null)
            {
                if (!request.SupersedesStateId.HasValue)
                {
                    throw new InvalidOperationException("duplicate_open_active_row_rejected");
                }

                if (request.SupersedesStateId.Value != existingOpen.Id)
                {
                    throw new InvalidOperationException("supersession_link_required_for_replacement");
                }

                existingOpen.ValidToUtc = validFromUtc;
                existingOpen.StateStatus = TemporalPersonStateStatuses.Superseded;
                existingOpen.UpdatedAtUtc = nowUtc;
            }
        }

        var row = new DbTemporalPersonState
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            SubjectRef = subjectRef,
            FactType = factType,
            FactCategory = factCategory,
            Value = value,
            ValidFromUtc = validFromUtc,
            ValidToUtc = validToUtc,
            Confidence = request.Confidence,
            EvidenceRefsJson = JsonSerializer.Serialize(evidenceRefs, JsonOptions),
            StateStatus = stateStatus,
            SupersedesStateId = request.SupersedesStateId,
            SupersededByStateId = null,
            TriggerKind = triggerKind,
            TriggerRef = triggerRef,
            TriggerModelPassRunId = request.TriggerModelPassRunId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.TemporalPersonStates.Add(row);
        await db.SaveChangesAsync(ct);

        if (request.SupersedesStateId.HasValue)
        {
            var supersededRow = await db.TemporalPersonStates.FirstOrDefaultAsync(
                x => x.Id == request.SupersedesStateId.Value
                    && x.ScopeKey == scopeKey
                    && x.TrackedPersonId == trackedPersonId,
                ct);
            if (supersededRow != null && supersededRow.SupersededByStateId != row.Id)
            {
                supersededRow.SupersededByStateId = row.Id;
                supersededRow.UpdatedAtUtc = nowUtc;
                await db.SaveChangesAsync(ct);
            }
        }

        return Map(row);
    }

    public async Task<List<TemporalPersonState>> QueryScopedAsync(
        TemporalPersonStateScopeQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scopeKey = NormalizeRequired(query.ScopeKey, nameof(query.ScopeKey));
        if (query.TrackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(query));
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 200 : query.Limit, 1, 500);
        var subjectRef = NormalizeOptional(query.SubjectRef);
        var factType = NormalizeOptional(query.FactType);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var baseQuery = db.TemporalPersonStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.TrackedPersonId == query.TrackedPersonId);

        if (!string.IsNullOrWhiteSpace(subjectRef))
        {
            baseQuery = baseQuery.Where(x => x.SubjectRef == subjectRef);
        }

        if (!string.IsNullOrWhiteSpace(factType))
        {
            baseQuery = baseQuery.Where(x => x.FactType == factType);
        }

        var rows = await baseQuery
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<List<TemporalPersonState>> QueryOpenScopedAsync(
        string scopeKey,
        Guid trackedPersonId,
        DateTime asOfUtc,
        CancellationToken ct = default)
    {
        var normalizedScope = NormalizeRequired(scopeKey, nameof(scopeKey));
        if (trackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(trackedPersonId));
        }

        var query = new TemporalPersonStateScopeQuery
        {
            ScopeKey = normalizedScope,
            TrackedPersonId = trackedPersonId,
            Limit = 500
        };

        var rows = await QueryScopedAsync(query, ct);
        return rows
            .Where(x => TemporalPersonStateContract.IsCurrentOpenState(x, asOfUtc))
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .ToList();
    }

    public async Task<TemporalPersonState?> GetOpenStateAsync(
        string scopeKey,
        string subjectRef,
        string factType,
        CancellationToken ct = default)
    {
        var normalizedScope = NormalizeRequired(scopeKey, nameof(scopeKey));
        var normalizedSubject = NormalizeRequired(subjectRef, nameof(subjectRef));
        var normalizedFactType = NormalizeRequired(factType, nameof(factType));

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.TemporalPersonStates
            .AsNoTracking()
            .Where(x =>
                x.ScopeKey == normalizedScope
                && x.SubjectRef == normalizedSubject
                && x.FactType == normalizedFactType
                && x.StateStatus == TemporalPersonStateStatuses.Open
                && x.ValidToUtc == null)
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row);
    }

    public async Task<TemporalPersonState?> UpdateSupersessionAsync(
        TemporalPersonStateSupersessionUpdateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = NormalizeRequired(request.ScopeKey, nameof(request.ScopeKey));
        if (request.TrackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(request));
        }

        if (request.PreviousStateId == Guid.Empty || request.SupersededByStateId == Guid.Empty)
        {
            throw new ArgumentException("PreviousStateId and SupersededByStateId are required.", nameof(request));
        }

        if (request.PreviousStateId == request.SupersededByStateId)
        {
            throw new InvalidOperationException("A state cannot supersede itself.");
        }

        var nextStatus = NormalizeRequired(request.NextStatus, nameof(request.NextStatus));
        if (!TemporalPersonStateStatuses.IsSupported(nextStatus))
        {
            throw new InvalidOperationException($"Unsupported status: {nextStatus}");
        }

        var supersededAtUtc = request.SupersededAtUtc == default ? DateTime.UtcNow : request.SupersededAtUtc;
        var nowUtc = DateTime.UtcNow;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.TemporalPersonStates.FirstOrDefaultAsync(x =>
            x.Id == request.PreviousStateId
            && x.ScopeKey == scopeKey
            && x.TrackedPersonId == request.TrackedPersonId,
            ct);
        if (row == null)
        {
            return null;
        }

        row.ValidToUtc = supersededAtUtc;
        row.StateStatus = nextStatus;
        row.SupersededByStateId = request.SupersededByStateId;
        row.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    private static TemporalPersonState Map(DbTemporalPersonState row)
    {
        return new TemporalPersonState
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            TrackedPersonId = row.TrackedPersonId,
            SubjectRef = row.SubjectRef,
            FactType = row.FactType,
            FactCategory = row.FactCategory,
            Value = row.Value,
            ValidFromUtc = row.ValidFromUtc,
            ValidToUtc = row.ValidToUtc,
            Confidence = row.Confidence,
            EvidenceRefs = ParseEvidenceRefs(row.EvidenceRefsJson),
            StateStatus = row.StateStatus,
            SupersedesStateId = row.SupersedesStateId,
            SupersededByStateId = row.SupersededByStateId,
            TriggerKind = row.TriggerKind,
            TriggerRef = row.TriggerRef,
            TriggerModelPassRunId = row.TriggerModelPassRunId,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        };
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{fieldName} is required.", fieldName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static List<string> ParseEvidenceRefs(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            if (parsed is null)
            {
                return [];
            }

            return parsed
                .Select(NormalizeOptional)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList()!;
        }
        catch
        {
            return [];
        }
    }
}

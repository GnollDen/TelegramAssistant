using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class ConditionalKnowledgeRepository : IConditionalKnowledgeRepository
{
    private const string ActivePersonStatus = "active";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ConditionalKnowledgeRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ConditionalKnowledgeState> InsertAsync(
        ConditionalKnowledgeWriteRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = NormalizeRequired(request.ScopeKey, nameof(request.ScopeKey));
        var subjectRef = NormalizeRequired(request.SubjectRef, nameof(request.SubjectRef));
        var factFamily = NormalizeRequired(request.FactFamily, nameof(request.FactFamily));
        var ruleKind = NormalizeRequired(request.RuleKind, nameof(request.RuleKind));
        var triggerKind = NormalizeRequired(request.TriggerKind, nameof(request.TriggerKind));
        var triggerRef = NormalizeOptional(request.TriggerRef);
        if (request.TrackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(request));
        }

        if (request.RuleId == Guid.Empty)
        {
            throw new ArgumentException("RuleId is required.", nameof(request));
        }

        if (!TemporalConditionalFactFamilies.Contains(factFamily))
        {
            throw new InvalidOperationException($"Unsupported conditional fact family: {factFamily}");
        }

        if (!ConditionalKnowledgeRuleKinds.IsSupported(ruleKind))
        {
            throw new InvalidOperationException($"Unsupported conditional rule kind: {ruleKind}");
        }

        var stateStatus = NormalizeRequired(request.StateStatus, nameof(request.StateStatus));
        if (!ConditionalKnowledgeStateStatuses.IsSupported(stateStatus))
        {
            throw new InvalidOperationException($"Unsupported conditional state status: {stateStatus}");
        }

        ValidateRulePayload(request, ruleKind);

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
        if (evidenceRefs.Length == 0)
        {
            throw new InvalidOperationException(ConditionalKnowledgeFailureReasons.EvidenceRefsRequired);
        }

        var conditionClauseIds = request.ConditionClauseIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray() ?? Array.Empty<Guid>();
        var sourceRefIds = request.SourceRefIds?
            .Select(NormalizeOptional)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
        var linkedTemporalStateIds = request.LinkedTemporalStateIds?
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray() ?? Array.Empty<Guid>();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPersonExists = await db.Persons
            .AsNoTracking()
            .AnyAsync(x => x.Id == request.TrackedPersonId && x.ScopeKey == scopeKey && x.Status == ActivePersonStatus, ct);
        if (!trackedPersonExists)
        {
            throw new InvalidOperationException("Tracked person not found or inactive.");
        }

        DbConditionalKnowledgeState? supersededCandidate = null;
        if (request.SupersedesStateId.HasValue)
        {
            supersededCandidate = await db.ConditionalKnowledgeStates
                .FirstOrDefaultAsync(x => x.Id == request.SupersedesStateId.Value, ct);
            if (supersededCandidate is null
                || !string.Equals(supersededCandidate.ScopeKey, scopeKey, StringComparison.Ordinal)
                || supersededCandidate.TrackedPersonId != request.TrackedPersonId
                || !string.Equals(supersededCandidate.FactFamily, factFamily, StringComparison.Ordinal)
                || !string.Equals(supersededCandidate.SubjectRef, subjectRef, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("supersedes_state_id must reference the same scope/person/family/subject tuple.");
            }
        }

        var nowUtc = DateTime.UtcNow;
        if (supersededCandidate != null)
        {
            supersededCandidate.ValidToUtc = validFromUtc;
            supersededCandidate.StateStatus = ConditionalKnowledgeStateStatuses.Superseded;
            supersededCandidate.UpdatedAtUtc = nowUtc;
        }

        var row = new DbConditionalKnowledgeState
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            TrackedPersonId = request.TrackedPersonId,
            FactFamily = factFamily,
            SubjectRef = subjectRef,
            RuleKind = ruleKind,
            RuleId = request.RuleId,
            ParentRuleId = request.ParentRuleId,
            BaselineValue = NormalizeOptional(request.BaselineValue),
            ExceptionValue = NormalizeOptional(request.ExceptionValue),
            StyleLabel = NormalizeOptional(request.StyleLabel),
            PhaseLabel = NormalizeOptional(request.PhaseLabel),
            PhaseReason = NormalizeOptional(request.PhaseReason),
            ConditionClauseIdsJson = JsonSerializer.Serialize(conditionClauseIds, JsonOptions),
            SourceRefIdsJson = JsonSerializer.Serialize(sourceRefIds, JsonOptions),
            LinkedTemporalStateIdsJson = JsonSerializer.Serialize(linkedTemporalStateIds, JsonOptions),
            EvidenceRefsJson = JsonSerializer.Serialize(evidenceRefs, JsonOptions),
            ValidFromUtc = validFromUtc,
            ValidToUtc = validToUtc,
            Confidence = request.Confidence,
            StateStatus = stateStatus,
            SupersedesStateId = request.SupersedesStateId,
            SupersededByStateId = null,
            TriggerKind = triggerKind,
            TriggerRef = triggerRef,
            TriggerModelPassRunId = request.TriggerModelPassRunId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.ConditionalKnowledgeStates.Add(row);
        await db.SaveChangesAsync(ct);

        if (supersededCandidate != null && supersededCandidate.SupersededByStateId != row.Id)
        {
            supersededCandidate.SupersededByStateId = row.Id;
            supersededCandidate.UpdatedAtUtc = nowUtc;
            await db.SaveChangesAsync(ct);
        }

        return Map(row);
    }

    public async Task<List<ConditionalKnowledgeState>> QueryScopedAsync(
        ConditionalKnowledgeScopeQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scopeKey = NormalizeRequired(query.ScopeKey, nameof(query.ScopeKey));
        if (query.TrackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(query));
        }

        var limit = Math.Clamp(query.Limit <= 0 ? 200 : query.Limit, 1, 500);
        var factFamily = NormalizeOptional(query.FactFamily);
        var subjectRef = NormalizeOptional(query.SubjectRef);
        var ruleKind = NormalizeOptional(query.RuleKind);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var baseQuery = db.ConditionalKnowledgeStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.TrackedPersonId == query.TrackedPersonId);

        if (!string.IsNullOrWhiteSpace(factFamily))
        {
            baseQuery = baseQuery.Where(x => x.FactFamily == factFamily);
        }

        if (!string.IsNullOrWhiteSpace(subjectRef))
        {
            baseQuery = baseQuery.Where(x => x.SubjectRef == subjectRef);
        }

        if (!string.IsNullOrWhiteSpace(ruleKind))
        {
            baseQuery = baseQuery.Where(x => x.RuleKind == ruleKind);
        }

        var rows = await baseQuery
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<List<ConditionalKnowledgeState>> QueryOpenScopedAsync(
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

        var rows = await QueryScopedAsync(new ConditionalKnowledgeScopeQuery
        {
            ScopeKey = normalizedScope,
            TrackedPersonId = trackedPersonId,
            Limit = 500
        }, ct);

        return rows
            .Where(x =>
                string.Equals(x.StateStatus, ConditionalKnowledgeStateStatuses.Open, StringComparison.Ordinal)
                && x.ValidFromUtc <= asOfUtc
                && (x.ValidToUtc is null || x.ValidToUtc > asOfUtc))
            .OrderByDescending(x => x.ValidFromUtc)
            .ThenByDescending(x => x.Id)
            .ToList();
    }

    public async Task<ConditionalKnowledgeState?> UpdateSupersessionAsync(
        ConditionalKnowledgeSupersessionUpdateRequest request,
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
            throw new ArgumentException("Both previous and superseding ids are required.", nameof(request));
        }

        var nextStatus = NormalizeRequired(request.NextStatus, nameof(request.NextStatus));
        if (!ConditionalKnowledgeStateStatuses.IsSupported(nextStatus))
        {
            throw new InvalidOperationException($"Unsupported conditional state status: {nextStatus}");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.ConditionalKnowledgeStates.FirstOrDefaultAsync(
            x => x.Id == request.PreviousStateId
                && x.ScopeKey == scopeKey
                && x.TrackedPersonId == request.TrackedPersonId,
            ct);
        if (row is null)
        {
            return null;
        }

        row.ValidToUtc = request.SupersededAtUtc;
        row.StateStatus = nextStatus;
        row.SupersededByStateId = request.SupersededByStateId;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    private static void ValidateRulePayload(ConditionalKnowledgeWriteRequest request, string ruleKind)
    {
        switch (ruleKind)
        {
            case ConditionalKnowledgeRuleKinds.BaselineRule:
                _ = NormalizeRequired(request.BaselineValue, nameof(request.BaselineValue));
                break;
            case ConditionalKnowledgeRuleKinds.ExceptionRule:
                _ = NormalizeRequired(request.ExceptionValue, nameof(request.ExceptionValue));
                if (!request.ParentRuleId.HasValue || request.ParentRuleId.Value == Guid.Empty)
                {
                    throw new InvalidOperationException("parent_rule_id is required for exception_rule.");
                }
                break;
            case ConditionalKnowledgeRuleKinds.StyleDrift:
                _ = NormalizeRequired(request.StyleLabel, nameof(request.StyleLabel));
                break;
            case ConditionalKnowledgeRuleKinds.PhaseMarker:
                _ = NormalizeRequired(request.PhaseLabel, nameof(request.PhaseLabel));
                _ = NormalizeRequired(request.PhaseReason, nameof(request.PhaseReason));
                break;
            default:
                throw new InvalidOperationException($"Unsupported conditional rule kind: {ruleKind}");
        }
    }

    private static ConditionalKnowledgeState Map(DbConditionalKnowledgeState row)
    {
        return new ConditionalKnowledgeState
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            TrackedPersonId = row.TrackedPersonId,
            FactFamily = row.FactFamily,
            SubjectRef = row.SubjectRef,
            RuleKind = row.RuleKind,
            RuleId = row.RuleId,
            ParentRuleId = row.ParentRuleId,
            BaselineValue = row.BaselineValue,
            ExceptionValue = row.ExceptionValue,
            StyleLabel = row.StyleLabel,
            PhaseLabel = row.PhaseLabel,
            PhaseReason = row.PhaseReason,
            ConditionClauseIds = DeserializeGuidArray(row.ConditionClauseIdsJson),
            SourceRefIds = DeserializeStringArray(row.SourceRefIdsJson),
            LinkedTemporalStateIds = DeserializeGuidArray(row.LinkedTemporalStateIdsJson),
            EvidenceRefs = DeserializeStringArray(row.EvidenceRefsJson),
            ValidFromUtc = row.ValidFromUtc,
            ValidToUtc = row.ValidToUtc,
            Confidence = row.Confidence,
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

    private static List<string> DeserializeStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions)?
                .Select(NormalizeOptional)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<Guid> DeserializeGuidArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions)?
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeRequired(string? value, string paramName)
    {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class OperatorOfflineEventRepository : IOperatorOfflineEventRepository
{
    private const string ActivePersonStatus = "active";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public OperatorOfflineEventRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<OperatorOfflineEventRecord> CreateAsync(
        OperatorOfflineEventCreateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var trackedPersonId = request.TrackedPersonId;
        if (trackedPersonId == Guid.Empty)
        {
            throw new ArgumentException("TrackedPersonId is required.", nameof(request));
        }

        var summary = NormalizeRequired(request.Summary, "Summary is required.", nameof(request));
        var scopeKey = NormalizeRequired(request.ScopeKey, "ScopeKey is required.", nameof(request));
        var nowUtc = DateTime.UtcNow;
        var capturedAtUtc = request.CapturedAtUtc == default ? nowUtc : request.CapturedAtUtc;
        var status = NormalizeStatus(request.Status);
        var operatorIdentity = request.OperatorIdentity ?? new OperatorIdentityContext();
        var session = request.Session ?? new OperatorSessionContext();
        var surface = OperatorSurfaceTypes.Normalize(session.Surface);
        if (!OperatorSurfaceTypes.IsSupported(surface))
        {
            throw new ArgumentException("Session.Surface must be a supported operator surface.", nameof(request));
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPersonExists = await db.Persons
            .AsNoTracking()
            .AnyAsync(x => x.Id == trackedPersonId && x.Status == ActivePersonStatus, ct);
        if (!trackedPersonExists)
        {
            throw new InvalidOperationException("Tracked person not found or inactive.");
        }

        var row = new DbOperatorOfflineEvent
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            SummaryText = summary,
            RecordingReference = NormalizeOptional(request.RecordingReference),
            Status = status,
            CapturePayloadJson = NormalizeJson(request.CapturePayloadJson),
            ClarificationStateJson = NormalizeJson(request.ClarificationStateJson),
            TimelineLinkageJson = NormalizeJson(request.TimelineLinkageJson),
            Confidence = request.Confidence,
            OperatorId = NormalizeRequired(operatorIdentity.OperatorId, "unknown"),
            OperatorDisplay = NormalizeRequired(operatorIdentity.OperatorDisplay, "unknown"),
            OperatorSessionId = NormalizeRequired(session.OperatorSessionId, "unknown"),
            Surface = surface,
            SurfaceSubject = NormalizeRequired(operatorIdentity.SurfaceSubject, "unknown"),
            AuthSource = NormalizeRequired(operatorIdentity.AuthSource, "unknown"),
            AuthTimeUtc = operatorIdentity.AuthTimeUtc == default
                ? nowUtc
                : operatorIdentity.AuthTimeUtc,
            SessionAuthenticatedAtUtc = session.AuthenticatedAtUtc == default
                ? nowUtc
                : session.AuthenticatedAtUtc,
            SessionLastSeenAtUtc = session.LastSeenAtUtc == default
                ? nowUtc
                : session.LastSeenAtUtc,
            SessionExpiresAtUtc = session.ExpiresAtUtc,
            ActiveMode = NormalizeRequired(OperatorModeTypes.Normalize(session.ActiveMode), "unknown"),
            UnfinishedStepKind = NormalizeOptional(session.UnfinishedStep?.StepKind),
            UnfinishedStepState = NormalizeOptional(session.UnfinishedStep?.StepState),
            UnfinishedStepStartedAtUtc = session.UnfinishedStep?.StartedAtUtc,
            CapturedAtUtc = capturedAtUtc,
            SavedAtUtc = request.SavedAtUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        db.OperatorOfflineEvents.Add(row);
        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<OperatorOfflineEventRecord?> GetByIdAsync(
        Guid offlineEventId,
        CancellationToken ct = default)
    {
        if (offlineEventId == Guid.Empty)
        {
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.OperatorOfflineEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offlineEventId, ct);
        return row == null ? null : Map(row);
    }

    public async Task<OperatorOfflineEventRecord?> GetByIdWithinScopeAsync(
        Guid offlineEventId,
        string scopeKey,
        Guid trackedPersonId,
        CancellationToken ct = default)
    {
        if (offlineEventId == Guid.Empty || trackedPersonId == Guid.Empty || string.IsNullOrWhiteSpace(scopeKey))
        {
            return null;
        }

        var normalizedScopeKey = scopeKey.Trim();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.OperatorOfflineEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == offlineEventId
                && x.ScopeKey == normalizedScopeKey
                && x.TrackedPersonId == trackedPersonId,
                ct);
        return row == null ? null : Map(row);
    }

    public async Task<OperatorOfflineEventQueryResult> QueryAsync(
        OperatorOfflineEventQueryRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TrackedPersonId == Guid.Empty)
        {
            return new OperatorOfflineEventQueryResult
            {
                ScopeBound = false,
                ScopeFailureReason = "tracked_person_id_required"
            };
        }

        if (string.IsNullOrWhiteSpace(request.ScopeKey))
        {
            return new OperatorOfflineEventQueryResult
            {
                ScopeBound = false,
                ScopeFailureReason = "scope_key_required"
            };
        }

        var scopeKey = request.ScopeKey.Trim();
        var statuses = (request.Statuses ?? [])
            .Select(NormalizeStatus)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sortBy = OperatorOfflineEventSortFields.Normalize(request.SortBy);
        var sortDirection = ResolutionSortDirections.Normalize(request.SortDirection);
        var limit = Math.Clamp(request.Limit, 1, 200);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var trackedPersonExists = await db.Persons
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == request.TrackedPersonId
                && x.ScopeKey == scopeKey
                && x.Status == ActivePersonStatus,
                ct);
        if (!trackedPersonExists)
        {
            return new OperatorOfflineEventQueryResult
            {
                ScopeBound = false,
                ScopeFailureReason = "tracked_person_not_found_or_inactive"
            };
        }

        var baseQuery = db.OperatorOfflineEvents
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.TrackedPersonId == request.TrackedPersonId);

        var totalCount = await baseQuery.CountAsync(ct);
        var filteredQuery = statuses.Length == 0
            ? baseQuery
            : baseQuery.Where(x => statuses.Contains(x.Status));
        var filteredCount = await filteredQuery.CountAsync(ct);

        var orderedQuery = ApplySort(filteredQuery, sortBy, sortDirection);
        var rows = await orderedQuery
            .Take(limit)
            .ToListAsync(ct);

        return new OperatorOfflineEventQueryResult
        {
            ScopeBound = true,
            TrackedPersonId = request.TrackedPersonId,
            ScopeKey = scopeKey,
            TotalCount = totalCount,
            FilteredCount = filteredCount,
            Items = rows.Select(MapReadSummary).ToList()
        };
    }

    private static OperatorOfflineEventRecord Map(DbOperatorOfflineEvent row)
    {
        return new OperatorOfflineEventRecord
        {
            OfflineEventId = row.Id,
            TrackedPersonId = row.TrackedPersonId,
            ScopeKey = row.ScopeKey,
            Summary = row.SummaryText,
            RecordingReference = row.RecordingReference,
            Status = row.Status,
            CapturePayloadJson = row.CapturePayloadJson,
            ClarificationStateJson = row.ClarificationStateJson,
            TimelineLinkageJson = row.TimelineLinkageJson,
            Confidence = row.Confidence,
            OperatorId = row.OperatorId,
            OperatorDisplay = row.OperatorDisplay,
            OperatorSessionId = row.OperatorSessionId,
            Surface = row.Surface,
            SurfaceSubject = row.SurfaceSubject,
            AuthSource = row.AuthSource,
            AuthTimeUtc = row.AuthTimeUtc,
            SessionAuthenticatedAtUtc = row.SessionAuthenticatedAtUtc,
            SessionLastSeenAtUtc = row.SessionLastSeenAtUtc,
            SessionExpiresAtUtc = row.SessionExpiresAtUtc,
            ActiveMode = row.ActiveMode,
            UnfinishedStepKind = row.UnfinishedStepKind,
            UnfinishedStepState = row.UnfinishedStepState,
            UnfinishedStepStartedAtUtc = row.UnfinishedStepStartedAtUtc,
            CapturedAtUtc = row.CapturedAtUtc,
            SavedAtUtc = row.SavedAtUtc,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        };
    }

    private static OperatorOfflineEventReadSummary MapReadSummary(DbOperatorOfflineEvent row)
    {
        return new OperatorOfflineEventReadSummary
        {
            OfflineEventId = row.Id,
            TrackedPersonId = row.TrackedPersonId,
            ScopeKey = row.ScopeKey,
            Summary = row.SummaryText,
            RecordingReference = row.RecordingReference,
            Status = NormalizeStatus(row.Status),
            Confidence = row.Confidence,
            CapturedAtUtc = row.CapturedAtUtc,
            SavedAtUtc = row.SavedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            TimelineLinkage = ParseTimelineLinkageMetadata(row.TimelineLinkageJson)
        };
    }

    private static IQueryable<DbOperatorOfflineEvent> ApplySort(
        IQueryable<DbOperatorOfflineEvent> query,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, ResolutionSortDirections.Desc, StringComparison.Ordinal);
        return sortBy switch
        {
            OperatorOfflineEventSortFields.CapturedAt => descending
                ? query.OrderByDescending(x => x.CapturedAtUtc).ThenByDescending(x => x.UpdatedAtUtc)
                : query.OrderBy(x => x.CapturedAtUtc).ThenBy(x => x.UpdatedAtUtc),
            OperatorOfflineEventSortFields.SavedAt => descending
                ? query.OrderByDescending(x => x.SavedAtUtc ?? DateTime.MinValue).ThenByDescending(x => x.UpdatedAtUtc)
                : query.OrderBy(x => x.SavedAtUtc ?? DateTime.MinValue).ThenBy(x => x.UpdatedAtUtc),
            OperatorOfflineEventSortFields.CreatedAt => descending
                ? query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.UpdatedAtUtc)
                : query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.UpdatedAtUtc),
            _ => descending
                ? query.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.CreatedAtUtc)
                : query.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.CreatedAtUtc)
        };
    }

    private static OperatorOfflineEventTimelineLinkageMetadata ParseTimelineLinkageMetadata(string? timelineLinkageJson)
    {
        var rawJson = NormalizeJson(timelineLinkageJson);
        var metadata = new OperatorOfflineEventTimelineLinkageMetadata
        {
            RawJson = rawJson
        };

        if (!TryParseJsonObject(rawJson, out var root))
        {
            return metadata;
        }

        metadata.HasLinkage = root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Any();
        metadata.LinkageStatus = NormalizeOptional(
            GetString(root, "linkage_status")
            ?? GetString(root, "status")
            ?? GetString(root, "timeline_status")
            ?? GetNestedString(root, "target", "linkage_status")
            ?? GetNestedString(root, "target", "status"))
            ?? (metadata.HasLinkage ? "linked" : "unlinked");
        metadata.TargetFamily = NormalizeOptional(
            GetString(root, "target_family")
            ?? GetString(root, "object_family")
            ?? GetString(root, "timeline_family")
            ?? GetNestedString(root, "target", "family")
            ?? GetNestedString(root, "target", "target_family"));
        metadata.TargetRef = NormalizeOptional(
            GetString(root, "target_ref")
            ?? GetString(root, "object_ref")
            ?? GetString(root, "timeline_ref")
            ?? GetNestedString(root, "target", "ref")
            ?? GetNestedString(root, "target", "target_ref"));
        metadata.LinkedAtUtc = GetDateTime(root, "linked_at_utc")
            ?? GetDateTime(root, "updated_at_utc")
            ?? GetDateTime(root, "resolved_at_utc")
            ?? GetNestedDateTime(root, "target", "linked_at_utc");

        return metadata;
    }

    private static string NormalizeStatus(string? status)
    {
        var normalized = OperatorOfflineEventStatuses.Normalize(status);
        return OperatorOfflineEventStatuses.IsSupported(normalized)
            ? normalized
            : OperatorOfflineEventStatuses.Draft;
    }

    private static string NormalizeJson(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
    }

    private static bool TryParseJsonObject(string json, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                root = document.RootElement.Clone();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        root = default;
        return false;
    }

    private static string NormalizeRequired(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string NormalizeRequired(string? value, string errorMessage, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(errorMessage, paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static string? GetNestedString(JsonElement element, string objectPropertyName, string nestedPropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var nestedObject)
            || nestedObject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetString(nestedObject, nestedPropertyName);
    }

    private static DateTime? GetDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String
            && DateTime.TryParse(property.GetString(), out var parsed))
        {
            return parsed.Kind == DateTimeKind.Utc
                ? parsed
                : parsed.ToUniversalTime();
        }

        return null;
    }

    private static DateTime? GetNestedDateTime(JsonElement element, string objectPropertyName, string nestedPropertyName)
    {
        if (!element.TryGetProperty(objectPropertyName, out var nestedObject)
            || nestedObject.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return GetDateTime(nestedObject, nestedPropertyName);
    }
}

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
}

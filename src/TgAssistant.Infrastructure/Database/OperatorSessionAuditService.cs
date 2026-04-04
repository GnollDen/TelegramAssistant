using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class OperatorSessionAuditService : IOperatorSessionAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public OperatorSessionAuditService(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Guid> RecordSessionEventAsync(
        OperatorSessionAuditRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var eventTimeUtc = request.EventTimeUtc == default ? DateTime.UtcNow : request.EventTimeUtc;
        var auditEventId = Guid.NewGuid();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.OperatorAuditEvents.Add(new DbOperatorAuditEvent
        {
            AuditEventId = auditEventId,
            RequestId = NormalizeRequired(request.RequestId, "session_event"),
            ScopeKey = NormalizeOptional(request.ScopeKey),
            TrackedPersonId = request.TrackedPersonId == Guid.Empty ? null : request.TrackedPersonId,
            ScopeItemKey = NormalizeOptional(request.ScopeItemKey),
            ItemType = NormalizeOptional(request.ItemType),
            OperatorId = NormalizeRequired(request.OperatorIdentity?.OperatorId, "unknown"),
            OperatorDisplay = NormalizeRequired(request.OperatorIdentity?.OperatorDisplay, "unknown"),
            OperatorSessionId = NormalizeRequired(request.Session?.OperatorSessionId, "unknown"),
            Surface = NormalizeRequired(OperatorSurfaceTypes.Normalize(request.Session?.Surface), "unknown"),
            SurfaceSubject = NormalizeRequired(request.OperatorIdentity?.SurfaceSubject, "unknown"),
            AuthSource = NormalizeRequired(request.OperatorIdentity?.AuthSource, "unknown"),
            ActiveMode = NormalizeRequired(NormalizeOptional(request.Session?.ActiveMode), "unknown"),
            UnfinishedStepKind = NormalizeOptional(request.Session?.UnfinishedStep?.StepKind),
            ActionType = null,
            SessionEventType = NormalizeRequired(request.SessionEventType, "session_event"),
            DecisionOutcome = NormalizeRequired(request.DecisionOutcome, OperatorAuditDecisionOutcomes.Accepted),
            FailureReason = NormalizeOptional(request.FailureReason),
            DetailsJson = JsonSerializer.Serialize(request.Details ?? new Dictionary<string, object?>(), JsonOptions),
            EventTimeUtc = eventTimeUtc
        });

        await db.SaveChangesAsync(ct);
        return auditEventId;
    }

    private static string NormalizeRequired(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

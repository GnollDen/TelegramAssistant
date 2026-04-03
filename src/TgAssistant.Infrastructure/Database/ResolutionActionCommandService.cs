using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class ResolutionActionCommandService : IResolutionActionService
{
    private const string ActiveStatus = "active";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IResolutionReadService _resolutionReadService;
    private readonly ILogger<ResolutionActionCommandService> _logger;

    public ResolutionActionCommandService(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IResolutionReadService resolutionReadService,
        ILogger<ResolutionActionCommandService> logger)
    {
        _dbFactory = dbFactory;
        _resolutionReadService = resolutionReadService;
        _logger = logger;
    }

    public async Task<ResolutionActionResult> SubmitAsync(
        ResolutionActionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nowUtc = DateTime.UtcNow;
        var normalizedAction = ResolutionActionTypes.Normalize(request.ActionType);
        var normalizedRequestId = NormalizeRequired(request.RequestId);
        var normalizedScopeItemKey = NormalizeRequired(request.ScopeItemKey);
        var normalizedExplanation = NormalizeOptional(request.Explanation);
        var normalizedSurface = OperatorSurfaceTypes.Normalize(request.Session.Surface);
        var normalizedMode = OperatorModeTypes.Normalize(request.Session.ActiveMode);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!string.IsNullOrWhiteSpace(normalizedRequestId))
        {
            var existing = await db.OperatorResolutionActions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequestId == normalizedRequestId, ct);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Resolution action idempotent replay: request_id={RequestId}, action_id={ActionId}, operator_id={OperatorId}, decision={Decision}",
                    existing.RequestId,
                    existing.Id,
                    existing.OperatorId,
                    existing.Decision);

                return MapAcceptedResult(existing, idempotentReplay: true, auditEventId: null);
            }
        }

        var trackedPerson = request.TrackedPersonId == Guid.Empty
            ? null
            : await LoadTrackedPersonScopeAsync(db, request.TrackedPersonId, ct);

        var envelopeFailure = ValidateEnvelope(
            request,
            normalizedRequestId,
            normalizedScopeItemKey,
            normalizedAction,
            normalizedExplanation,
            normalizedSurface,
            normalizedMode,
            nowUtc);
        if (envelopeFailure != null)
        {
            return await PersistDeniedAuditAsync(
                db,
                request,
                trackedPerson,
                normalizedRequestId,
                normalizedScopeItemKey,
                normalizedAction,
                normalizedExplanation,
                null,
                envelopeFailure,
                nowUtc,
                ct);
        }

        if (trackedPerson == null)
        {
            return await PersistDeniedAuditAsync(
                db,
                request,
                null,
                normalizedRequestId,
                normalizedScopeItemKey,
                normalizedAction,
                normalizedExplanation,
                null,
                "tracked_person_not_found_or_inactive",
                nowUtc,
                ct);
        }

        var detail = await _resolutionReadService.GetDetailAsync(
            new ResolutionDetailRequest
            {
                TrackedPersonId = trackedPerson.PersonId,
                ScopeItemKey = normalizedScopeItemKey,
                EvidenceLimit = 1
            },
            ct);

        if (!detail.ScopeBound || !detail.ItemFound || detail.Item == null)
        {
            var failureReason = detail.ScopeBound
                ? "scope_item_not_found"
                : detail.ScopeFailureReason ?? "scope_item_not_found";
            return await PersistDeniedAuditAsync(
                db,
                request,
                trackedPerson,
                normalizedRequestId,
                normalizedScopeItemKey,
                normalizedAction,
                normalizedExplanation,
                null,
                failureReason,
                nowUtc,
                ct);
        }

        var clarificationPayloadJson = request.ClarificationPayload == null
            ? null
            : JsonSerializer.Serialize(request.ClarificationPayload, JsonOptions);
        var auditEventId = Guid.NewGuid();
        var actionRow = BuildAcceptedActionRow(
            request,
            trackedPerson,
            detail.Item,
            normalizedRequestId,
            normalizedScopeItemKey,
            normalizedAction,
            normalizedExplanation,
            clarificationPayloadJson,
            nowUtc);
        var auditRow = BuildAuditRow(
            request,
            trackedPerson,
            detail.Item,
            normalizedRequestId,
            normalizedScopeItemKey,
            normalizedAction,
            OperatorAuditDecisionOutcomes.Accepted,
            failureReason: null,
            detail.Item.ItemType,
            nowUtc,
            auditEventId,
            BuildAuditDetailsJson(
                request,
                trackedPerson.ScopeKey,
                detail.Item,
                normalizedAction,
                normalizedExplanation,
                clarificationPayloadJson,
                failureReason: null,
                nowUtc));

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        db.OperatorResolutionActions.Add(actionRow);
        db.OperatorAuditEvents.Add(auditRow);

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await tx.RollbackAsync(ct);

            var existing = await db.OperatorResolutionActions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.RequestId == normalizedRequestId, ct);
            if (existing != null)
            {
                _logger.LogInformation(
                    "Resolution action deduplicated after concurrent write: request_id={RequestId}, action_id={ActionId}, operator_id={OperatorId}, decision={Decision}",
                    existing.RequestId,
                    existing.Id,
                    existing.OperatorId,
                    existing.Decision);

                return MapAcceptedResult(existing, idempotentReplay: true, auditEventId: null);
            }

            throw;
        }

        _logger.LogInformation(
            "Resolution action accepted: action_id={ActionId}, request_id={RequestId}, tracked_person_id={TrackedPersonId}, scope_item_key={ScopeItemKey}, item_type={ItemType}, decision={Decision}, operator_id={OperatorId}, surface={Surface}",
            actionRow.Id,
            actionRow.RequestId,
            actionRow.TrackedPersonId,
            actionRow.ScopeItemKey,
            actionRow.ItemType,
            actionRow.Decision,
            actionRow.OperatorId,
            actionRow.Surface);

        return MapAcceptedResult(actionRow, idempotentReplay: false, auditEventId);
    }

    private async Task<ResolutionActionResult> PersistDeniedAuditAsync(
        TgAssistantDbContext db,
        ResolutionActionRequest request,
        TrackedPersonScope? trackedPerson,
        string normalizedRequestId,
        string normalizedScopeItemKey,
        string normalizedAction,
        string? normalizedExplanation,
        string? itemType,
        string failureReason,
        DateTime eventTimeUtc,
        CancellationToken ct)
    {
        var auditEventId = Guid.NewGuid();
        var auditRow = BuildAuditRow(
            request,
            trackedPerson,
            item: null,
            normalizedRequestId,
            normalizedScopeItemKey,
            normalizedAction,
            OperatorAuditDecisionOutcomes.Denied,
            failureReason,
            itemType,
            eventTimeUtc,
            auditEventId,
            BuildAuditDetailsJson(
                request,
                trackedPerson?.ScopeKey,
                item: null,
                normalizedAction,
                normalizedExplanation,
                clarificationPayloadJson: request.ClarificationPayload == null
                    ? null
                    : JsonSerializer.Serialize(request.ClarificationPayload, JsonOptions),
                failureReason,
                eventTimeUtc));

        db.OperatorAuditEvents.Add(auditRow);
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Resolution action denied: request_id={RequestId}, tracked_person_id={TrackedPersonId}, scope_item_key={ScopeItemKey}, decision={Decision}, failure_reason={FailureReason}, operator_id={OperatorId}",
            auditRow.RequestId,
            trackedPerson?.PersonId ?? request.TrackedPersonId,
            normalizedScopeItemKey,
            normalizedAction,
            failureReason,
            auditRow.OperatorId);

        return new ResolutionActionResult
        {
            Accepted = false,
            FailureReason = failureReason,
            AuditEventId = auditEventId,
            TrackedPersonId = trackedPerson?.PersonId == Guid.Empty ? null : trackedPerson?.PersonId ?? request.TrackedPersonId,
            ScopeItemKey = normalizedScopeItemKey,
            ActionType = normalizedAction,
            ItemType = itemType,
            ProcessedAtUtc = eventTimeUtc
        };
    }

    private static string? ValidateEnvelope(
        ResolutionActionRequest request,
        string normalizedRequestId,
        string normalizedScopeItemKey,
        string normalizedAction,
        string? normalizedExplanation,
        string normalizedSurface,
        string normalizedMode,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(normalizedRequestId))
        {
            return "request_id_required";
        }

        if (request.TrackedPersonId == Guid.Empty)
        {
            return "tracked_person_id_required";
        }

        if (string.IsNullOrWhiteSpace(normalizedScopeItemKey))
        {
            return "scope_item_key_required";
        }

        if (string.IsNullOrWhiteSpace(normalizedAction))
        {
            return "action_type_required";
        }

        if (!ResolutionActionTypes.IsSupported(normalizedAction))
        {
            return "unsupported_action_type";
        }

        if (ResolutionActionTypes.RequiresExplanation(normalizedAction) && string.IsNullOrWhiteSpace(normalizedExplanation))
        {
            return "explanation_required";
        }

        if (request.OperatorIdentity == null)
        {
            return "operator_identity_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(request.OperatorIdentity.OperatorId)))
        {
            return "operator_id_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(request.OperatorIdentity.OperatorDisplay)))
        {
            return "operator_display_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(request.OperatorIdentity.SurfaceSubject)))
        {
            return "surface_subject_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(request.OperatorIdentity.AuthSource)))
        {
            return "auth_source_required";
        }

        if (request.OperatorIdentity.AuthTimeUtc == default)
        {
            return "auth_time_utc_required";
        }

        if (request.Session == null)
        {
            return "operator_session_required";
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(request.Session.OperatorSessionId)))
        {
            return "operator_session_id_required";
        }

        if (!OperatorSurfaceTypes.IsSupported(normalizedSurface))
        {
            return string.IsNullOrWhiteSpace(normalizedSurface)
                ? "surface_required"
                : "unsupported_surface";
        }

        if (request.Session.AuthenticatedAtUtc == default)
        {
            return "session_authenticated_at_utc_required";
        }

        if (request.Session.LastSeenAtUtc == default)
        {
            return "session_last_seen_at_utc_required";
        }

        if (request.Session.ExpiresAtUtc.HasValue && request.Session.ExpiresAtUtc.Value <= nowUtc)
        {
            return "session_expired";
        }

        if (request.Session.ActiveTrackedPersonId == Guid.Empty)
        {
            return "session_active_tracked_person_required";
        }

        if (request.Session.ActiveTrackedPersonId != request.TrackedPersonId)
        {
            return "session_active_tracked_person_mismatch";
        }

        var activeScopeItemKey = NormalizeRequired(request.Session.ActiveScopeItemKey);
        if (string.IsNullOrWhiteSpace(activeScopeItemKey))
        {
            return "session_active_scope_item_required";
        }

        if (!string.Equals(activeScopeItemKey, normalizedScopeItemKey, StringComparison.Ordinal))
        {
            return "session_scope_item_mismatch";
        }

        if (!OperatorModeTypes.IsSupported(normalizedMode))
        {
            return string.IsNullOrWhiteSpace(normalizedMode)
                ? "active_mode_required"
                : "invalid_active_mode";
        }

        if (!OperatorModeTypes.AllowsMutatingAction(normalizedMode))
        {
            return "action_not_allowed_from_mode";
        }

        var unfinishedStep = request.Session.UnfinishedStep;
        if (unfinishedStep == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(unfinishedStep.StepKind)))
        {
            return "unfinished_step_kind_required";
        }

        if (unfinishedStep.StartedAtUtc == default)
        {
            return "unfinished_step_started_at_utc_required";
        }

        if (unfinishedStep.BoundTrackedPersonId == Guid.Empty)
        {
            return "unfinished_step_tracked_person_required";
        }

        if (unfinishedStep.BoundTrackedPersonId != request.TrackedPersonId)
        {
            return "unfinished_step_tracked_person_mismatch";
        }

        if (string.IsNullOrWhiteSpace(NormalizeRequired(unfinishedStep.BoundScopeItemKey)))
        {
            return "unfinished_step_scope_item_required";
        }

        if (!string.Equals(
                unfinishedStep.BoundScopeItemKey.Trim(),
                normalizedScopeItemKey,
                StringComparison.Ordinal))
        {
            return "unfinished_step_scope_item_mismatch";
        }

        return null;
    }

    private static DbOperatorResolutionAction BuildAcceptedActionRow(
        ResolutionActionRequest request,
        TrackedPersonScope trackedPerson,
        ResolutionItemDetail item,
        string normalizedRequestId,
        string normalizedScopeItemKey,
        string normalizedAction,
        string? normalizedExplanation,
        string? clarificationPayloadJson,
        DateTime nowUtc)
    {
        return new DbOperatorResolutionAction
        {
            Id = Guid.NewGuid(),
            RequestId = normalizedRequestId,
            ScopeKey = trackedPerson.ScopeKey,
            TrackedPersonId = trackedPerson.PersonId,
            ScopeItemKey = normalizedScopeItemKey,
            ItemType = NormalizeRequired(item.ItemType),
            SourceKind = NormalizeRequired(item.SourceKind, fallback: "resolution_item"),
            SourceRef = NormalizeRequired(item.SourceRef, fallback: normalizedScopeItemKey),
            AffectedFamily = NormalizeRequired(item.AffectedFamily, fallback: "unknown"),
            AffectedObjectRef = NormalizeRequired(item.AffectedObjectRef, fallback: "unknown"),
            Decision = normalizedAction,
            Explanation = normalizedExplanation,
            ClarificationPayloadJson = clarificationPayloadJson,
            OperatorId = request.OperatorIdentity.OperatorId.Trim(),
            OperatorDisplay = request.OperatorIdentity.OperatorDisplay.Trim(),
            OperatorSessionId = request.Session.OperatorSessionId.Trim(),
            Surface = OperatorSurfaceTypes.Normalize(request.Session.Surface),
            SurfaceSubject = request.OperatorIdentity.SurfaceSubject.Trim(),
            AuthSource = request.OperatorIdentity.AuthSource.Trim(),
            AuthTimeUtc = request.OperatorIdentity.AuthTimeUtc,
            SessionAuthenticatedAtUtc = request.Session.AuthenticatedAtUtc,
            SessionLastSeenAtUtc = request.Session.LastSeenAtUtc,
            SessionExpiresAtUtc = request.Session.ExpiresAtUtc,
            ActiveMode = OperatorModeTypes.Normalize(request.Session.ActiveMode),
            UnfinishedStepKind = NormalizeOptional(request.Session.UnfinishedStep?.StepKind),
            UnfinishedStepState = NormalizeOptional(request.Session.UnfinishedStep?.StepState),
            UnfinishedStepStartedAtUtc = request.Session.UnfinishedStep?.StartedAtUtc,
            SubmittedAtUtc = request.SubmittedAtUtc == default ? nowUtc : request.SubmittedAtUtc,
            CreatedAtUtc = nowUtc
        };
    }

    private static DbOperatorAuditEvent BuildAuditRow(
        ResolutionActionRequest request,
        TrackedPersonScope? trackedPerson,
        ResolutionItemDetail? item,
        string normalizedRequestId,
        string normalizedScopeItemKey,
        string normalizedAction,
        string decisionOutcome,
        string? failureReason,
        string? itemType,
        DateTime eventTimeUtc,
        Guid auditEventId,
        string detailsJson)
    {
        return new DbOperatorAuditEvent
        {
            AuditEventId = auditEventId,
            RequestId = string.IsNullOrWhiteSpace(normalizedRequestId) ? "unknown" : normalizedRequestId,
            ScopeKey = trackedPerson?.ScopeKey,
            TrackedPersonId = trackedPerson?.PersonId == Guid.Empty ? null : trackedPerson?.PersonId ?? (request.TrackedPersonId == Guid.Empty ? null : request.TrackedPersonId),
            ScopeItemKey = string.IsNullOrWhiteSpace(normalizedScopeItemKey) ? null : normalizedScopeItemKey,
            ItemType = NormalizeOptional(itemType ?? item?.ItemType),
            OperatorId = NormalizeAuditValue(request.OperatorIdentity?.OperatorId),
            OperatorDisplay = NormalizeAuditValue(request.OperatorIdentity?.OperatorDisplay),
            OperatorSessionId = NormalizeAuditValue(request.Session?.OperatorSessionId),
            Surface = NormalizeAuditValue(OperatorSurfaceTypes.Normalize(request.Session?.Surface), fallback: "unknown"),
            SurfaceSubject = NormalizeAuditValue(request.OperatorIdentity?.SurfaceSubject),
            AuthSource = NormalizeAuditValue(request.OperatorIdentity?.AuthSource),
            ActiveMode = NormalizeAuditValue(OperatorModeTypes.Normalize(request.Session?.ActiveMode), fallback: "unknown"),
            UnfinishedStepKind = NormalizeOptional(request.Session?.UnfinishedStep?.StepKind),
            ActionType = string.IsNullOrWhiteSpace(normalizedAction) ? null : normalizedAction,
            SessionEventType = null,
            DecisionOutcome = decisionOutcome,
            FailureReason = NormalizeOptional(failureReason),
            DetailsJson = detailsJson,
            EventTimeUtc = eventTimeUtc
        };
    }

    private static string BuildAuditDetailsJson(
        ResolutionActionRequest request,
        string? scopeKey,
        ResolutionItemDetail? item,
        string normalizedAction,
        string? normalizedExplanation,
        string? clarificationPayloadJson,
        string? failureReason,
        DateTime eventTimeUtc)
    {
        return JsonSerializer.Serialize(
            new
            {
                request_id = NormalizeRequired(request.RequestId, fallback: "unknown"),
                scope_key = scopeKey,
                tracked_person_id = request.TrackedPersonId == Guid.Empty ? (Guid?)null : request.TrackedPersonId,
                scope_item_key = NormalizeOptional(request.ScopeItemKey),
                action_type = string.IsNullOrWhiteSpace(normalizedAction) ? null : normalizedAction,
                explanation = normalizedExplanation,
                explanation_required = ResolutionActionTypes.RequiresExplanation(normalizedAction),
                clarification_payload_present = clarificationPayloadJson != null,
                source_kind = item?.SourceKind,
                source_ref = item?.SourceRef,
                affected_family = item?.AffectedFamily,
                affected_object_ref = item?.AffectedObjectRef,
                submitted_at_utc = request.SubmittedAtUtc == default ? eventTimeUtc : request.SubmittedAtUtc,
                auth_time_utc = request.OperatorIdentity?.AuthTimeUtc == default ? null : request.OperatorIdentity?.AuthTimeUtc,
                session_authenticated_at_utc = request.Session?.AuthenticatedAtUtc == default ? null : request.Session?.AuthenticatedAtUtc,
                session_last_seen_at_utc = request.Session?.LastSeenAtUtc == default ? null : request.Session?.LastSeenAtUtc,
                session_expires_at_utc = request.Session?.ExpiresAtUtc,
                failure_reason = failureReason
            },
            JsonOptions);
    }

    private static ResolutionActionResult MapAcceptedResult(
        DbOperatorResolutionAction row,
        bool idempotentReplay,
        Guid? auditEventId)
    {
        return new ResolutionActionResult
        {
            Accepted = true,
            IdempotentReplay = idempotentReplay,
            ActionId = row.Id,
            AuditEventId = auditEventId,
            TrackedPersonId = row.TrackedPersonId,
            ScopeItemKey = row.ScopeItemKey,
            ActionType = row.Decision,
            ItemType = row.ItemType,
            ProcessedAtUtc = row.CreatedAtUtc
        };
    }

    private static async Task<TrackedPersonScope?> LoadTrackedPersonScopeAsync(
        TgAssistantDbContext db,
        Guid trackedPersonId,
        CancellationToken ct)
    {
        var row = await db.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == trackedPersonId
                && x.Status == ActiveStatus
                && x.PersonType == "tracked_person",
                ct);
        if (row == null)
        {
            return null;
        }

        return new TrackedPersonScope
        {
            PersonId = row.Id,
            ScopeKey = row.ScopeKey,
            DisplayName = string.IsNullOrWhiteSpace(row.DisplayName) ? row.CanonicalName : row.DisplayName
        };
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException postgres
           && string.Equals(postgres.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal);

    private static string NormalizeRequired(string? value, string fallback = "")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeAuditValue(string? value, string fallback = "unknown")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private sealed class TrackedPersonScope
    {
        public Guid PersonId { get; init; }
        public string ScopeKey { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
    }
}

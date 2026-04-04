using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Core.Configuration;
using TgAssistant.Host.Startup;

namespace TgAssistant.Host.OperatorApi;

public static class OperatorApiEndpointExtensions
{
    public static IEndpointRouteBuilder MapOperatorApi(this IEndpointRouteBuilder endpoints)
    {
        var runtimeRoleSelection = endpoints.ServiceProvider.GetRequiredService<RuntimeRoleSelection>();
        if (!runtimeRoleSelection.Has(RuntimeWorkloadRole.Ops))
        {
            return endpoints;
        }

        var group = endpoints.MapGroup("/api/operator");

        group.MapPost("/tracked-persons/query", async (
            HttpContext httpContext,
            OperatorTrackedPersonQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryTrackedPersonsAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/tracked-persons/select", async (
            HttpContext httpContext,
            OperatorTrackedPersonSelectionRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.SelectTrackedPersonAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/alerts/query", async (
            HttpContext httpContext,
            OperatorAlertsQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            OperatorAlertsProjectionBuilder projectionBuilder,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            var result = await projectionBuilder.BuildAsync(request, auth.OperatorIdentity, auth.Session, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/handoff/consume", async (
            HttpContext httpContext,
            OperatorResolutionHandoffConsumeRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOptions<WebSettings> webSettings,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var requestedMode = OperatorModeTypes.Normalize(request.ActiveMode);
            var auth = await webAuthResolver.ResolveAsync(httpContext, requestedMode, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            var trackedPersonId = request.TrackedPersonId;
            var scopeItemKey = NormalizeOptional(request.ScopeItemKey);
            var sourceSessionId = NormalizeOptional(request.OperatorSessionId);
            var handoffToken = NormalizeOptional(request.HandoffToken);
            var signingSecret = OperatorHandoffTokenCodec.ResolveSigningSecret(webSettings.Value);
            var ttlMinutes = Math.Clamp(webSettings.Value.HandoffTokenTtlMinutes, 1, 24 * 60);
            if (trackedPersonId == Guid.Empty)
            {
                return Results.BadRequest(new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "tracked_person_id_required",
                    Session = auth.Session,
                    ActiveMode = requestedMode
                });
            }

            if (string.IsNullOrWhiteSpace(scopeItemKey))
            {
                return Results.BadRequest(new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "scope_item_key_required",
                    Session = auth.Session,
                    ActiveMode = requestedMode
                });
            }

            if (string.IsNullOrWhiteSpace(sourceSessionId))
            {
                return Results.BadRequest(new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "operator_session_id_required",
                    Session = auth.Session,
                    ActiveMode = requestedMode
                });
            }

            if (string.IsNullOrWhiteSpace(signingSecret))
            {
                return Results.Json(
                    new OperatorResolutionHandoffConsumeResult
                    {
                        Accepted = false,
                        FailureReason = "handoff_signing_secret_missing",
                        Session = auth.Session,
                        ActiveMode = requestedMode
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var isValidTelegramToken = OperatorHandoffTokenCodec.TryValidateToken(
                handoffToken,
                OperatorHandoffTokenCodec.TelegramResolutionContext,
                trackedPersonId,
                scopeItemKey,
                sourceSessionId,
                signingSecret,
                ttlMinutes);
            var isValidAssistantToken = !isValidTelegramToken && OperatorHandoffTokenCodec.TryValidateToken(
                handoffToken,
                OperatorHandoffTokenCodec.AssistantResolutionContext,
                trackedPersonId,
                scopeItemKey,
                sourceSessionId,
                signingSecret,
                ttlMinutes);
            if (!isValidTelegramToken && !isValidAssistantToken)
            {
                return Results.Json(
                    new OperatorResolutionHandoffConsumeResult
                    {
                        Accepted = false,
                        FailureReason = "handoff_token_invalid",
                        Session = auth.Session,
                        ActiveMode = requestedMode
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var currentSession = auth.Session ?? new OperatorSessionContext();
            if (currentSession.ActiveTrackedPersonId != Guid.Empty
                && currentSession.ActiveTrackedPersonId != trackedPersonId)
            {
                return Results.Json(
                    new OperatorResolutionHandoffConsumeResult
                    {
                        Accepted = false,
                        FailureReason = "session_active_tracked_person_mismatch",
                        Session = currentSession,
                        ActiveMode = requestedMode
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (!string.IsNullOrWhiteSpace(currentSession.ActiveScopeItemKey)
                && !string.Equals(currentSession.ActiveScopeItemKey.Trim(), scopeItemKey, StringComparison.Ordinal))
            {
                return Results.Json(
                    new OperatorResolutionHandoffConsumeResult
                    {
                        Accepted = false,
                        FailureReason = "session_scope_item_mismatch",
                        Session = currentSession,
                        ActiveMode = requestedMode
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var session = currentSession;
            if (session.ActiveTrackedPersonId == Guid.Empty)
            {
                var selectRequest = new OperatorTrackedPersonSelectionRequest
                {
                    TrackedPersonId = trackedPersonId,
                    RequestedAtUtc = DateTime.UtcNow,
                    OperatorIdentity = auth.OperatorIdentity,
                    Session = auth.Session ?? new OperatorSessionContext()
                };
                var selection = await service.SelectTrackedPersonAsync(selectRequest, ct);
                if (!selection.Accepted)
                {
                    return ToResult(selection.Accepted, selection.FailureReason, new OperatorResolutionHandoffConsumeResult
                    {
                        Accepted = false,
                        FailureReason = selection.FailureReason,
                        Session = selection.Session,
                        ActiveMode = requestedMode
                    });
                }

                session = selection.Session ?? currentSession;
            }

            session.ActiveTrackedPersonId = trackedPersonId;
            session.ActiveScopeItemKey = scopeItemKey;
            session.ActiveMode = OperatorModeTypes.IsSupported(requestedMode)
                ? requestedMode
                : OperatorModeTypes.ResolutionDetail;

            var handoffResult = new OperatorResolutionHandoffConsumeResult
            {
                Accepted = true,
                Session = session,
                ActiveTrackedPersonId = session.ActiveTrackedPersonId,
                ActiveScopeItemKey = session.ActiveScopeItemKey,
                ActiveMode = session.ActiveMode
            };

            webAuthResolver.PersistSession(httpContext, handoffResult.Session, handoffResult.ActiveMode);
            return Results.Ok(handoffResult);
        });

        group.MapPost("/persons/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceListQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceListAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/summary/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceSummaryQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceSummaryAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/dossier/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceDossierQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceDossierAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/profile/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceProfileQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceProfileAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/pair-dynamics/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspacePairDynamicsQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspacePairDynamicsAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/timeline/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceTimelineQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceTimelineAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/evidence/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceEvidenceQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceEvidenceAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/revisions/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceRevisionsQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceRevisionsAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/resolution/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceResolutionQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryPersonWorkspaceResolutionAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/queue/query", async (
            HttpContext httpContext,
            OperatorResolutionQueueQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.GetResolutionQueueAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/detail/query", async (
            HttpContext httpContext,
            OperatorResolutionDetailQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionDetail, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.GetResolutionDetailAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionDetail);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/actions", async (
            HttpContext httpContext,
            ResolutionActionRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var requestedMode = ResolveActionMode(request.ActionType);
            var auth = await webAuthResolver.ResolveAsync(httpContext, requestedMode, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.SubmitResolutionActionAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, requestedMode);
            return ToResult(result.Accepted, result.FailureReason ?? result.Action.FailureReason, result);
        });

        group.MapPost("/offline-events/query", async (
            HttpContext httpContext,
            OperatorOfflineEventQueryApiRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.OfflineEvent, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.QueryOfflineEventsAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.OfflineEvent);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/offline-events/detail", async (
            HttpContext httpContext,
            OperatorOfflineEventDetailQueryRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.OfflineEvent, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.GetOfflineEventDetailAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.OfflineEvent);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/offline-events/refine", async (
            HttpContext httpContext,
            OperatorOfflineEventRefinementRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.OfflineEvent, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.SubmitOfflineEventRefinementAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.OfflineEvent);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/offline-events/timeline-linkage", async (
            HttpContext httpContext,
            OperatorOfflineEventTimelineLinkageUpdateRequest request,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.OfflineEvent, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            request.OperatorIdentity = auth.OperatorIdentity;
            request.Session = auth.Session;
            var result = await service.SubmitOfflineEventTimelineLinkageUpdateAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.OfflineEvent);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        return endpoints;
    }

    private static IResult ToResult(bool accepted, string? failureReason, object body)
    {
        if (accepted)
        {
            return Results.Ok(body);
        }

        return MapFailureStatusCode(failureReason) switch
        {
            StatusCodes.Status401Unauthorized => Results.Json(body, statusCode: StatusCodes.Status401Unauthorized),
            StatusCodes.Status403Forbidden => Results.Json(body, statusCode: StatusCodes.Status403Forbidden),
            StatusCodes.Status404NotFound => Results.Json(body, statusCode: StatusCodes.Status404NotFound),
            _ => Results.BadRequest(body)
        };
    }

    private static int MapFailureStatusCode(string? failureReason)
    {
        return failureReason switch
        {
            "session_expired" => StatusCodes.Status401Unauthorized,
            "session_active_tracked_person_mismatch" => StatusCodes.Status403Forbidden,
            "session_scope_item_mismatch" => StatusCodes.Status403Forbidden,
            "unfinished_step_tracked_person_mismatch" => StatusCodes.Status403Forbidden,
            "unfinished_step_scope_item_mismatch" => StatusCodes.Status403Forbidden,
            "action_not_allowed_from_mode" => StatusCodes.Status403Forbidden,
            "handoff_token_invalid" => StatusCodes.Status403Forbidden,
            "handoff_signing_secret_missing" => StatusCodes.Status503ServiceUnavailable,
            "tracked_person_not_found_or_inactive" => StatusCodes.Status404NotFound,
            "scope_item_not_found" => StatusCodes.Status404NotFound,
            "session_active_tracked_person_not_available" => StatusCodes.Status404NotFound,
            "preferred_tracked_person_not_available" => StatusCodes.Status404NotFound,
            "offline_event_not_found" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private static IResult ToAuthFailureResult(WebOperatorAuthResult auth)
    {
        return Results.Json(
            new
            {
                accepted = false,
                failureReason = auth.FailureReason,
                auditEventId = auth.AuditEventId,
                session = auth.Session
            },
            statusCode: auth.StatusCode);
    }

    private static string ResolveActionMode(string? actionType)
    {
        var normalizedActionType = ResolutionActionTypes.Normalize(actionType);
        return normalizedActionType switch
        {
            ResolutionActionTypes.Evidence => OperatorModeTypes.Evidence,
            ResolutionActionTypes.Clarify => OperatorModeTypes.Clarification,
            _ => OperatorModeTypes.ResolutionDetail
        };
    }

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

}

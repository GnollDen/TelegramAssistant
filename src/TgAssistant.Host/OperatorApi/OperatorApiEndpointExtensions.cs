using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
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
            "tracked_person_not_found_or_inactive" => StatusCodes.Status404NotFound,
            "scope_item_not_found" => StatusCodes.Status404NotFound,
            "session_active_tracked_person_not_available" => StatusCodes.Status404NotFound,
            "preferred_tracked_person_not_available" => StatusCodes.Status404NotFound,
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
}

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
            OperatorTrackedPersonQueryRequest request,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.QueryTrackedPersonsAsync(request, ct);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/tracked-persons/select", async (
            OperatorTrackedPersonSelectionRequest request,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.SelectTrackedPersonAsync(request, ct);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/queue/query", async (
            OperatorResolutionQueueQueryRequest request,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetResolutionQueueAsync(request, ct);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/detail/query", async (
            OperatorResolutionDetailQueryRequest request,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.GetResolutionDetailAsync(request, ct);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/actions", async (
            ResolutionActionRequest request,
            IOperatorResolutionApplicationService service,
            CancellationToken ct) =>
        {
            var result = await service.SubmitResolutionActionAsync(request, ct);
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
}

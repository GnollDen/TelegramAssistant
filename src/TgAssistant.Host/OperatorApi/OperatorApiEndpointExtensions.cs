using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Core.Configuration;
using TgAssistant.Host.Startup;
using TgAssistant.Infrastructure.Database;

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
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapGet("/home/summary", async (
            HttpContext httpContext,
            WebOperatorAuthSessionResolver webAuthResolver,
            IOperatorResolutionApplicationService service,
            OperatorAlertsProjectionBuilder alertsProjectionBuilder,
            IResolutionReadService resolutionReadService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var auth = await webAuthResolver.ResolveAsync(httpContext, OperatorModeTypes.ResolutionQueue, ct);
            if (!auth.Accepted)
            {
                return ToAuthFailureResult(auth);
            }

            var settings = configuration
                .GetSection(OperatorHomeSummarySettings.Section)
                .Get<OperatorHomeSummarySettings>() ?? new OperatorHomeSummarySettings();

            var degradedSources = new HashSet<string>(StringComparer.Ordinal);
            OperatorHomeSummaryNavigationCounts? navigationCounts = null;
            string? systemStatus = null;
            int? criticalUnresolvedCount = null;
            int? activeTrackedPersonCount = null;
            List<OperatorHomeSummaryRecentUpdate>? recentUpdates = null;
            var session = auth.Session;
            var activeTrackedPersonId = session.ActiveTrackedPersonId == Guid.Empty
                ? (Guid?)null
                : session.ActiveTrackedPersonId;
            int? resolutionCount = null;
            int? personsCount = null;
            int? alertsCount = null;
            int? offlineEventsCount = null;

            if (!settings.Enabled || settings.ForceDegradedSummary)
            {
                degradedSources.UnionWith(OperatorHomeSummaryDegradedSources.FullOrder);
            }
            else
            {
                try
                {
                    var trackedPersons = await service.QueryTrackedPersonsAsync(
                        new OperatorTrackedPersonQueryRequest
                        {
                            OperatorIdentity = auth.OperatorIdentity,
                            Session = session,
                            Limit = 50
                        },
                        ct);
                    if (!trackedPersons.Accepted)
                    {
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.ActiveTrackedPersonCount);
                    }
                    else
                    {
                        session = trackedPersons.Session;
                        activeTrackedPersonCount = trackedPersons.TrackedPersons.Count;
                        personsCount = trackedPersons.TrackedPersons.Count;

                        var selectedTrackedPerson = trackedPersons.ActiveTrackedPerson
                            ?? trackedPersons.TrackedPersons.FirstOrDefault();
                        if (selectedTrackedPerson != null)
                        {
                            activeTrackedPersonId = selectedTrackedPerson.TrackedPersonId;
                            if (session.ActiveTrackedPersonId != selectedTrackedPerson.TrackedPersonId)
                            {
                                var selected = await service.SelectTrackedPersonAsync(
                                    new OperatorTrackedPersonSelectionRequest
                                    {
                                        OperatorIdentity = auth.OperatorIdentity,
                                        Session = session,
                                        TrackedPersonId = selectedTrackedPerson.TrackedPersonId,
                                        RequestedAtUtc = DateTime.UtcNow
                                    },
                                    ct);
                                if (selected.Accepted)
                                {
                                    session = selected.Session;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                    degradedSources.Add(OperatorHomeSummaryDegradedSources.ActiveTrackedPersonCount);
                }

                if (activeTrackedPersonId.HasValue)
                {
                    session.ActiveTrackedPersonId = activeTrackedPersonId.Value;
                    session.ActiveMode = OperatorModeTypes.ResolutionQueue;

                    try
                    {
                        var queueResult = await service.GetResolutionQueueAsync(
                            new OperatorResolutionQueueQueryRequest
                            {
                                OperatorIdentity = auth.OperatorIdentity,
                                Session = session,
                                TrackedPersonId = activeTrackedPersonId,
                                SortBy = ResolutionQueueSortFields.Priority,
                                SortDirection = ResolutionSortDirections.Desc,
                                Limit = 50
                            },
                            ct);
                        if (!queueResult.Accepted)
                        {
                            degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                            degradedSources.Add(OperatorHomeSummaryDegradedSources.SystemStatus);
                            degradedSources.Add(OperatorHomeSummaryDegradedSources.CriticalUnresolvedCount);
                        }
                        else
                        {
                            session = queueResult.Session;
                            resolutionCount = Math.Max(0, queueResult.Queue.TotalOpenCount);

                            if (resolutionReadService is not ResolutionReadProjectionService projection)
                            {
                                throw new InvalidOperationException("Resolution read service does not expose canonical home summary owners.");
                            }

                            var summaryReadModel = projection.BuildOperatorHomeSummaryReadModel(queueResult.Queue);
                            criticalUnresolvedCount = Math.Max(0, summaryReadModel.CriticalUnresolvedCount);

                            var runtimeState = OperatorHomeSummarySystemStatuses.Normalize(queueResult.Queue.RuntimeState?.State);
                            if (OperatorHomeSummarySystemStatuses.IsSupported(runtimeState))
                            {
                                systemStatus = runtimeState;
                            }
                            else
                            {
                                degradedSources.Add(OperatorHomeSummaryDegradedSources.SystemStatus);
                            }
                        }
                    }
                    catch
                    {
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.SystemStatus);
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.CriticalUnresolvedCount);
                    }

                    try
                    {
                        session.ActiveTrackedPersonId = activeTrackedPersonId.Value;
                        session.ActiveMode = OperatorModeTypes.OfflineEvent;
                        var offlineEventResult = await service.QueryOfflineEventsAsync(
                            new OperatorOfflineEventQueryApiRequest
                            {
                                OperatorIdentity = auth.OperatorIdentity,
                                Session = session,
                                TrackedPersonId = activeTrackedPersonId.Value,
                                Statuses = [],
                                SortBy = OperatorOfflineEventSortFields.UpdatedAt,
                                SortDirection = ResolutionSortDirections.Desc,
                                Limit = 100
                            },
                            ct);
                        if (!offlineEventResult.Accepted)
                        {
                            degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                        }
                        else
                        {
                            session = offlineEventResult.Session;
                            offlineEventsCount = Math.Max(0, offlineEventResult.OfflineEvents.TotalCount);
                        }
                    }
                    catch
                    {
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                    }
                }
                else
                {
                    resolutionCount ??= 0;
                    offlineEventsCount ??= 0;
                    criticalUnresolvedCount ??= 0;
                    systemStatus ??= OperatorHomeSummarySystemStatuses.Normal;
                }

                try
                {
                    var alertsResult = await alertsProjectionBuilder.BuildAsync(
                        new OperatorAlertsQueryRequest
                        {
                            TrackedPersonId = null,
                            EscalationBoundary = OperatorAlertsEscalationFilters.All,
                            PersonLimit = 50,
                            AlertsPerPersonLimit = 12
                        },
                        auth.OperatorIdentity,
                        session,
                        ct);
                    if (!alertsResult.Accepted)
                    {
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                        degradedSources.Add(OperatorHomeSummaryDegradedSources.RecentUpdates);
                    }
                    else
                    {
                        alertsCount = Math.Max(0, alertsResult.Summary.TotalAlerts);
                        recentUpdates = alertsResult.Groups
                            .SelectMany(group => group.Alerts.Select(alert => new OperatorHomeSummaryRecentUpdate
                            {
                                Id = $"alert:{group.TrackedPerson.TrackedPersonId:N}:{alert.ScopeItemKey}:{alert.UpdatedAtUtc:O}",
                                OccurredAtUtc = alert.UpdatedAtUtc,
                                Summary = $"{group.TrackedPerson.DisplayName}: {alert.Title}",
                                TargetUrl = alert.ResolutionUrl
                            }))
                            .Where(update => IsAllowedHomeTargetUrl(update.TargetUrl))
                            .OrderByDescending(update => update.OccurredAtUtc)
                            .ThenByDescending(update => update.Id, StringComparer.Ordinal)
                            .Take(5)
                            .ToList();
                    }
                }
                catch
                {
                    degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
                    degradedSources.Add(OperatorHomeSummaryDegradedSources.RecentUpdates);
                }
            }

            if (!degradedSources.Contains(OperatorHomeSummaryDegradedSources.NavigationCounts)
                && resolutionCount.HasValue
                && personsCount.HasValue
                && alertsCount.HasValue
                && offlineEventsCount.HasValue)
            {
                navigationCounts = new OperatorHomeSummaryNavigationCounts
                {
                    Resolution = Math.Max(0, resolutionCount.Value),
                    Persons = Math.Max(0, personsCount.Value),
                    Alerts = Math.Max(0, alertsCount.Value),
                    OfflineEvents = Math.Max(0, offlineEventsCount.Value)
                };
            }
            else
            {
                degradedSources.Add(OperatorHomeSummaryDegradedSources.NavigationCounts);
            }

            if (!degradedSources.Contains(OperatorHomeSummaryDegradedSources.SystemStatus)
                && string.IsNullOrWhiteSpace(systemStatus))
            {
                degradedSources.Add(OperatorHomeSummaryDegradedSources.SystemStatus);
            }

            if (!degradedSources.Contains(OperatorHomeSummaryDegradedSources.CriticalUnresolvedCount)
                && !criticalUnresolvedCount.HasValue)
            {
                degradedSources.Add(OperatorHomeSummaryDegradedSources.CriticalUnresolvedCount);
            }

            if (!degradedSources.Contains(OperatorHomeSummaryDegradedSources.ActiveTrackedPersonCount)
                && !activeTrackedPersonCount.HasValue)
            {
                degradedSources.Add(OperatorHomeSummaryDegradedSources.ActiveTrackedPersonCount);
            }

            if (!degradedSources.Contains(OperatorHomeSummaryDegradedSources.RecentUpdates))
            {
                recentUpdates ??= [];
            }

            var orderedDegradedSources = OperatorHomeSummaryDegradedSources.FullOrder
                .Where(degradedSources.Contains)
                .ToList();

            webAuthResolver.PersistSession(httpContext, session, OperatorModeTypes.ResolutionQueue);
            return Results.Ok(new OperatorHomeSummaryApiResponse
            {
                NavigationCounts = orderedDegradedSources.Contains(OperatorHomeSummaryDegradedSources.NavigationCounts, StringComparer.Ordinal)
                    ? null
                    : navigationCounts,
                SystemStatus = orderedDegradedSources.Contains(OperatorHomeSummaryDegradedSources.SystemStatus, StringComparer.Ordinal)
                    ? null
                    : systemStatus,
                CriticalUnresolvedCount = orderedDegradedSources.Contains(OperatorHomeSummaryDegradedSources.CriticalUnresolvedCount, StringComparer.Ordinal)
                    ? null
                    : criticalUnresolvedCount,
                ActiveTrackedPersonCount = orderedDegradedSources.Contains(OperatorHomeSummaryDegradedSources.ActiveTrackedPersonCount, StringComparer.Ordinal)
                    ? null
                    : activeTrackedPersonCount,
                RecentUpdates = orderedDegradedSources.Contains(OperatorHomeSummaryDegradedSources.RecentUpdates, StringComparer.Ordinal)
                    ? null
                    : recentUpdates,
                DegradedSources = orderedDegradedSources
            });
        });

        group.MapPost("/resolution/handoff/consume", async (
            HttpContext httpContext,
            OperatorResolutionHandoffConsumeRequest request,
            OperatorResolutionHandoffConsumeService handoffConsumeService,
            CancellationToken ct) =>
        {
            var result = await handoffConsumeService.ConsumeAsync(httpContext, request, ct);
            return Results.Json(result.Payload, statusCode: result.StatusCode);
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

        group.MapPost("/person-workspace/person-history/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceHistoryQueryRequest request,
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
            var result = await service.QueryPersonWorkspaceHistoryAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionQueue);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/person-workspace/current-world/query", async (
            HttpContext httpContext,
            OperatorPersonWorkspaceCurrentWorldQueryRequest request,
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
            var result = await service.QueryPersonWorkspaceCurrentWorldAsync(request, ct);
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

        group.MapPost("/resolution/conflict-session/start", async (
            HttpContext httpContext,
            OperatorConflictResolutionSessionStartRequest request,
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
            var result = await service.StartConflictResolutionSessionAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionDetail);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/conflict-session/respond", async (
            HttpContext httpContext,
            OperatorConflictResolutionSessionRespondRequest request,
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
            var result = await service.RespondConflictResolutionSessionAsync(request, ct);
            webAuthResolver.PersistSession(httpContext, result.Session, OperatorModeTypes.ResolutionDetail);
            return ToResult(result.Accepted, result.FailureReason, result);
        });

        group.MapPost("/resolution/conflict-session/query", async (
            HttpContext httpContext,
            OperatorConflictResolutionSessionQueryRequest request,
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
            var result = await service.QueryConflictResolutionSessionAsync(request, ct);
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
                return ToOfflineEventSingleItemAuthFailureResult(auth);
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
                return ToOfflineEventSingleItemAuthFailureResult(auth);
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
                return ToOfflineEventSingleItemAuthFailureResult(auth);
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
            StatusCodes.Status409Conflict => Results.Json(body, statusCode: StatusCodes.Status409Conflict),
            _ => Results.BadRequest(body)
        };
    }

    internal static int MapFailureStatusCodeForTesting(string? failureReason)
        => MapFailureStatusCode(failureReason);

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
            "conflict_session_not_found" => StatusCodes.Status404NotFound,
            "session_active_tracked_person_not_available" => StatusCodes.Status404NotFound,
            "preferred_tracked_person_not_available" => StatusCodes.Status404NotFound,
            "offline_event_not_found" => StatusCodes.Status404NotFound,
            "scope_not_enabled" => StatusCodes.Status403Forbidden,
            "unsupported_resolution_item" => StatusCodes.Status403Forbidden,
            "conflict_session_not_ready_for_commit" => StatusCodes.Status409Conflict,
            "conflict_verdict_revision_mismatch" => StatusCodes.Status409Conflict,
            "conflict_session_handoff_mismatch" => StatusCodes.Status409Conflict,
            "conflict_session_not_waiting_for_answer" => StatusCodes.Status409Conflict,
            "answer_budget_exceeded" => StatusCodes.Status409Conflict,
            "question_mismatch" => StatusCodes.Status409Conflict,
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

    internal static IResult ToOfflineEventSingleItemAuthFailureResultForTesting(WebOperatorAuthResult auth)
        => ToOfflineEventSingleItemAuthFailureResult(auth);

    private static IResult ToOfflineEventSingleItemAuthFailureResult(WebOperatorAuthResult auth)
    {
        return Results.Json(
            new
            {
                accepted = false,
                failureReason = auth.FailureReason,
                session = auth.Session,
                offlineEvent = new OperatorOfflineEventSingleItemView
                {
                    ScopeBound = false,
                    Found = false
                }
            },
            statusCode: auth.StatusCode);
    }

    private static bool IsAllowedHomeTargetUrl(string? targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return false;
        }

        var normalized = targetUrl.Trim();
        if (!normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var queryIndex = normalized.IndexOf('?', StringComparison.Ordinal);
        var path = queryIndex >= 0 ? normalized[..queryIndex] : normalized;
        var query = queryIndex >= 0 ? normalized[(queryIndex + 1)..] : string.Empty;

        if (string.Equals(path, "/operator", StringComparison.Ordinal)
            || string.Equals(path, "/operator/persons", StringComparison.Ordinal)
            || string.Equals(path, "/operator/alerts", StringComparison.Ordinal)
            || string.Equals(path, "/operator/offline-events", StringComparison.Ordinal))
        {
            return string.IsNullOrEmpty(query);
        }

        var parameters = ParseQuery(query);
        if (string.Equals(path, "/operator/person-workspace", StringComparison.Ordinal))
        {
            return parameters.Count == 1
                && parameters.TryGetValue("trackedPersonId", out var trackedPerson)
                && Guid.TryParse(trackedPerson, out _);
        }

        if (!string.Equals(path, "/operator/resolution", StringComparison.Ordinal))
        {
            return false;
        }

        if (parameters.Count == 0)
        {
            return true;
        }

        if (parameters.Count != 3
            || !parameters.TryGetValue("trackedPersonId", out var trackedPersonId)
            || !Guid.TryParse(trackedPersonId, out _)
            || !parameters.TryGetValue("scopeItemKey", out var scopeItemKey)
            || string.IsNullOrWhiteSpace(scopeItemKey)
            || !parameters.TryGetValue("activeMode", out var activeMode))
        {
            return false;
        }

        return string.Equals(activeMode, "resolution_queue", StringComparison.Ordinal)
            || string.Equals(activeMode, "resolution_detail", StringComparison.Ordinal)
            || string.Equals(activeMode, "assistant", StringComparison.Ordinal);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var index = pair.IndexOf('=', StringComparison.Ordinal);
            var key = index >= 0 ? pair[..index] : pair;
            var value = index >= 0 ? pair[(index + 1)..] : string.Empty;
            if (string.IsNullOrWhiteSpace(key) || values.ContainsKey(key))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }

            values[key] = Uri.UnescapeDataString(value);
        }

        return values;
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

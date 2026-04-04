using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.OperatorApi;

public sealed class OperatorAlertsProjectionBuilder
{
    private readonly IOperatorResolutionApplicationService _resolutionService;
    private readonly IOperatorAlertPolicyService _alertPolicyService;

    public OperatorAlertsProjectionBuilder(
        IOperatorResolutionApplicationService resolutionService,
        IOperatorAlertPolicyService alertPolicyService)
    {
        _resolutionService = resolutionService;
        _alertPolicyService = alertPolicyService;
    }

    public async Task<OperatorAlertsQueryResult> BuildAsync(
        OperatorAlertsQueryRequest request,
        OperatorIdentityContext identity,
        OperatorSessionContext session,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(session);

        var nowUtc = DateTime.UtcNow;
        var boundary = OperatorAlertsEscalationFilters.Normalize(request.EscalationBoundary);
        if (string.IsNullOrWhiteSpace(boundary))
        {
            return CreateRejectedResult("alerts_boundary_not_supported", session, nowUtc);
        }

        var personLimit = Math.Clamp(request.PersonLimit, 1, 50);
        var alertsPerPersonLimit = Math.Clamp(request.AlertsPerPersonLimit, 1, 12);
        var normalizedSearch = NormalizeOptional(request.Search);
        var trackedPersons = await _resolutionService.QueryTrackedPersonsAsync(
            new OperatorTrackedPersonQueryRequest
            {
                OperatorIdentity = identity,
                Session = CloneSession(session, nowUtc),
                Limit = Math.Max(personLimit, request.TrackedPersonId.HasValue ? 50 : personLimit)
            },
            ct);
        if (!trackedPersons.Accepted)
        {
            return CreateRejectedResult(trackedPersons.FailureReason ?? "tracked_person_query_rejected", session, nowUtc);
        }

        var candidates = trackedPersons.TrackedPersons
            .Where(person => !request.TrackedPersonId.HasValue || person.TrackedPersonId == request.TrackedPersonId.Value)
            .Take(personLimit)
            .ToList();
        if (request.TrackedPersonId.HasValue && candidates.Count == 0)
        {
            return CreateRejectedResult("tracked_person_not_found_or_inactive", session, nowUtc);
        }

        var groups = new List<OperatorAlertGroupView>();
        foreach (var person in candidates)
        {
            var queue = await _resolutionService.GetResolutionQueueAsync(
                new OperatorResolutionQueueQueryRequest
                {
                    OperatorIdentity = identity,
                    Session = CreateScopedSession(session, person.TrackedPersonId, nowUtc),
                    TrackedPersonId = person.TrackedPersonId,
                    SortBy = ResolutionQueueSortFields.Priority,
                    SortDirection = ResolutionSortDirections.Desc,
                    Limit = Math.Max(alertsPerPersonLimit * 4, 40)
                },
                ct);
            if (!queue.Accepted)
            {
                continue;
            }

            var isActiveTrackedPersonScope = request.TrackedPersonId.HasValue
                && request.TrackedPersonId.Value == person.TrackedPersonId;
            var alerts = queue.Queue.Items
                .Select(item => new
                {
                    Item = item,
                    Decision = EvaluateAlertPolicy(item, queue.Queue, isActiveTrackedPersonScope)
                })
                .Where(entry => entry.Decision.CreateWebAlert)
                .Where(entry => boundary == OperatorAlertsEscalationFilters.All
                    || string.Equals(entry.Decision.EscalationBoundary, boundary, StringComparison.Ordinal))
                .Where(entry => MatchesSearch(person, entry.Item, entry.Decision, normalizedSearch))
                .OrderBy(entry => BoundaryWeight(entry.Decision.EscalationBoundary))
                .ThenBy(entry => PriorityWeight(entry.Item.Priority))
                .ThenByDescending(entry => entry.Item.UpdatedAtUtc)
                .Take(alertsPerPersonLimit)
                .Select(entry => BuildAlertItem(person, entry.Item, entry.Decision))
                .ToList();
            if (alerts.Count == 0)
            {
                continue;
            }

            groups.Add(new OperatorAlertGroupView
            {
                TrackedPerson = person,
                PersonWorkspaceUrl = BuildPersonWorkspaceUrl(person.TrackedPersonId),
                ResolutionQueueUrl = BuildResolutionQueueUrl(person.TrackedPersonId),
                AlertCount = alerts.Count,
                TelegramPushCount = alerts.Count(alert => string.Equals(alert.EscalationBoundary, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal)),
                WebOnlyCount = alerts.Count(alert => string.Equals(alert.EscalationBoundary, OperatorAlertEscalationBoundaries.WebOnly, StringComparison.Ordinal)),
                Alerts = alerts
            });
        }

        groups = groups
            .OrderBy(group => group.TelegramPushCount == 0 ? 1 : 0)
            .ThenByDescending(group => group.AlertCount)
            .ThenByDescending(group => group.TrackedPerson.LastUnresolvedAtUtc ?? group.TrackedPerson.RecentUpdateAtUtc)
            .ThenBy(group => group.TrackedPerson.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var allAlerts = groups
            .SelectMany(group => group.Alerts)
            .ToList();

        return new OperatorAlertsQueryResult
        {
            Accepted = true,
            FailureReason = null,
            Session = CreateResponseSession(session, request.TrackedPersonId, nowUtc),
            GeneratedAtUtc = nowUtc,
            Summary = new OperatorAlertsSummaryView
            {
                TrackedPersonCount = candidates.Count,
                GroupCount = groups.Count,
                TotalAlerts = groups.Sum(group => group.AlertCount),
                TelegramPushCount = groups.Sum(group => group.TelegramPushCount),
                WebOnlyCount = groups.Sum(group => group.WebOnlyCount),
                RequiresAcknowledgementCount = allAlerts.Count(alert => alert.RequiresAcknowledgement),
                EnterResolutionCount = allAlerts.Count(alert => alert.EnterResolutionContext),
                EscalationBoundary = boundary,
                TopReasons = BuildTopReasonFacets(allAlerts, request.TrackedPersonId, boundary),
                BoundaryBreakdown = BuildBoundaryFacets(allAlerts, request.TrackedPersonId, normalizedSearch)
            },
            Groups = groups
        };
    }

    private OperatorAlertItemView BuildAlertItem(
        OperatorTrackedPersonScopeSummary person,
        ResolutionItemSummary item,
        OperatorAlertPolicyDecision decision)
    {
        return new OperatorAlertItemView
        {
            ScopeItemKey = item.ScopeItemKey,
            ItemType = item.ItemType,
            Title = item.Title,
            Summary = item.Summary,
            WhyItMatters = item.WhyItMatters,
            AffectedFamily = item.AffectedFamily,
            AffectedObjectRef = item.AffectedObjectRef,
            TrustFactor = item.TrustFactor,
            Status = item.Status,
            EvidenceCount = item.EvidenceCount,
            UpdatedAtUtc = item.UpdatedAtUtc,
            Priority = item.Priority,
            RecommendedNextAction = item.RecommendedNextAction,
            AlertRuleId = decision.RuleId,
            AlertReason = decision.Reason,
            EscalationBoundary = decision.EscalationBoundary,
            PushTelegram = decision.PushTelegram,
            RequiresAcknowledgement = decision.RequiresAcknowledgement,
            EnterResolutionContext = decision.EnterResolutionContext,
            ResolutionUrl = BuildResolutionDetailUrl(person.TrackedPersonId, item.ScopeItemKey),
            PersonWorkspaceUrl = BuildPersonWorkspaceUrl(person.TrackedPersonId)
        };
    }

    private OperatorAlertPolicyDecision EvaluateAlertPolicy(
        ResolutionItemSummary item,
        ResolutionQueueResult queue,
        bool isActiveTrackedPersonScope)
    {
        var sourceClass = ResolveAlertSourceClass(item);
        return _alertPolicyService.Evaluate(new OperatorAlertPolicyInput
        {
            SourceClass = sourceClass,
            ScopeKey = queue.ScopeKey,
            TrackedPersonId = queue.TrackedPersonId,
            ScopeItemKey = item.ScopeItemKey,
            ItemType = item.ItemType,
            Priority = item.Priority,
            RuntimeState = sourceClass == OperatorAlertSourceClasses.RuntimeControlState ? queue.RuntimeState?.State : null,
            RuntimeDefectClass = sourceClass == OperatorAlertSourceClasses.RuntimeDefect ? ResolveRuntimeDefectClass(item) : null,
            RuntimeDefectSeverity = sourceClass == OperatorAlertSourceClasses.RuntimeDefect ? item.Priority : null,
            IsBlockingWorkflow = IsBlockingAlertItem(item),
            IsActiveTrackedPersonScope = isActiveTrackedPersonScope,
            IsMaterializationFailure = sourceClass == OperatorAlertSourceClasses.MaterializationFailure,
            IsStateTransitionOnly = false
        });
    }

    private static bool MatchesSearch(
        OperatorTrackedPersonScopeSummary person,
        ResolutionItemSummary item,
        OperatorAlertPolicyDecision decision,
        string? normalizedSearch)
    {
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return true;
        }

        return Matches(person.DisplayName, normalizedSearch)
            || Matches(person.ScopeKey, normalizedSearch)
            || Matches(item.Title, normalizedSearch)
            || Matches(item.Summary, normalizedSearch)
            || Matches(item.WhyItMatters, normalizedSearch)
            || Matches(item.ItemType, normalizedSearch)
            || Matches(item.Priority, normalizedSearch)
            || Matches(item.Status, normalizedSearch)
            || Matches(item.ScopeItemKey, normalizedSearch)
            || Matches(decision.RuleId, normalizedSearch)
            || Matches(decision.Reason, normalizedSearch)
            || Matches(decision.EscalationBoundary, normalizedSearch);
    }

    private static bool Matches(string? value, string normalizedSearch)
        => !string.IsNullOrWhiteSpace(value)
            && value.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase);

    private static string ResolveAlertSourceClass(ResolutionItemSummary item)
    {
        if (string.Equals(item.ItemType, ResolutionItemTypes.MissingData, StringComparison.Ordinal))
        {
            return OperatorAlertSourceClasses.MaterializationFailure;
        }

        if (string.Equals(item.ItemType, ResolutionItemTypes.Review, StringComparison.Ordinal))
        {
            if (string.Equals(item.AffectedFamily, "runtime_control", StringComparison.Ordinal)
                || item.Title.StartsWith("Runtime operating in ", StringComparison.Ordinal))
            {
                return OperatorAlertSourceClasses.RuntimeControlState;
            }

            if (string.Equals(item.AffectedFamily, RuntimeDefectClasses.ControlPlane, StringComparison.OrdinalIgnoreCase)
                || item.Title.Contains("runtime", StringComparison.OrdinalIgnoreCase))
            {
                return OperatorAlertSourceClasses.RuntimeDefect;
            }
        }

        return OperatorAlertSourceClasses.ResolutionBlocker;
    }

    private static string? ResolveRuntimeDefectClass(ResolutionItemSummary item)
    {
        if (string.Equals(item.AffectedFamily, RuntimeDefectClasses.ControlPlane, StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeDefectClasses.ControlPlane;
        }

        if (item.Title.Contains("control plane", StringComparison.OrdinalIgnoreCase))
        {
            return RuntimeDefectClasses.ControlPlane;
        }

        return RuntimeDefectClasses.Data;
    }

    private static bool IsBlockingAlertItem(ResolutionItemSummary item)
    {
        if (string.Equals(item.Priority, ResolutionItemPriorities.Critical, StringComparison.Ordinal))
        {
            return true;
        }

        return item.Status switch
        {
            ResolutionItemStatuses.Blocked => true,
            ResolutionItemStatuses.AttentionRequired => true,
            ResolutionItemStatuses.Degraded => true,
            _ => string.Equals(item.ItemType, ResolutionItemTypes.Clarification, StringComparison.Ordinal)
                 || string.Equals(item.ItemType, ResolutionItemTypes.BlockedBranch, StringComparison.Ordinal)
        };
    }

    private static int BoundaryWeight(string boundary)
        => string.Equals(boundary, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal) ? 0 : 1;

    private static int PriorityWeight(string priority)
        => string.Equals(priority, ResolutionItemPriorities.Critical, StringComparison.Ordinal) ? 0
            : string.Equals(priority, ResolutionItemPriorities.High, StringComparison.Ordinal) ? 1
            : string.Equals(priority, ResolutionItemPriorities.Medium, StringComparison.Ordinal) ? 2
            : 3;

    private static List<OperatorAlertsFacetCountView> BuildTopReasonFacets(
        IReadOnlyCollection<OperatorAlertItemView> alerts,
        Guid? trackedPersonId,
        string boundary)
    {
        return alerts
            .GroupBy(
                alert => NormalizeOptional(alert.AlertRuleId) ?? "unknown_rule",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var label = group
                    .Select(alert => NormalizeOptional(alert.AlertReason))
                    .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
                    ?? HumanizeToken(group.Key);
                return new OperatorAlertsFacetCountView
                {
                    Key = group.Key,
                    Label = label,
                    Count = group.Count(),
                    AlertsUrl = BuildAlertsUrl(trackedPersonId, boundary, group.Key)
                };
            })
            .OrderByDescending(facet => facet.Count)
            .ThenBy(facet => facet.Key, StringComparer.Ordinal)
            .Take(3)
            .ToList();
    }

    private static List<OperatorAlertsFacetCountView> BuildBoundaryFacets(
        IReadOnlyCollection<OperatorAlertItemView> alerts,
        Guid? trackedPersonId,
        string? normalizedSearch)
    {
        return alerts
            .GroupBy(
                alert => NormalizeOptional(alert.EscalationBoundary) ?? OperatorAlertsEscalationFilters.All,
                StringComparer.Ordinal)
            .Select(group => new OperatorAlertsFacetCountView
            {
                Key = group.Key,
                Label = HumanizeBoundary(group.Key),
                Count = group.Count(),
                AlertsUrl = BuildAlertsUrl(trackedPersonId, group.Key, normalizedSearch)
            })
            .OrderByDescending(facet => facet.Count)
            .ThenBy(facet => facet.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static string HumanizeBoundary(string boundary)
        => string.Equals(boundary, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal)
            ? "Telegram + acknowledge"
            : string.Equals(boundary, OperatorAlertEscalationBoundaries.WebOnly, StringComparison.Ordinal)
                ? "Web-only"
                : HumanizeToken(boundary);

    private static string HumanizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return string.Join(
            ' ',
            value.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..]));
    }

    private static string BuildAlertsUrl(Guid? trackedPersonId, string boundary, string? search)
    {
        var parameters = new List<string>();
        if (trackedPersonId.HasValue && trackedPersonId.Value != Guid.Empty)
        {
            parameters.Add($"trackedPersonId={Uri.EscapeDataString(trackedPersonId.Value.ToString("D"))}");
        }

        if (!string.IsNullOrWhiteSpace(boundary)
            && !string.Equals(boundary, OperatorAlertsEscalationFilters.All, StringComparison.Ordinal))
        {
            parameters.Add($"boundary={Uri.EscapeDataString(boundary)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parameters.Add($"search={Uri.EscapeDataString(search)}");
        }

        return parameters.Count == 0
            ? "/operator/alerts"
            : $"/operator/alerts?{string.Join("&", parameters)}";
    }

    private static string BuildResolutionDetailUrl(Guid trackedPersonId, string scopeItemKey)
    {
        var encodedPerson = Uri.EscapeDataString(trackedPersonId.ToString("D"));
        var encodedScopeItemKey = Uri.EscapeDataString(scopeItemKey);
        return $"/operator/resolution?trackedPersonId={encodedPerson}&scopeItemKey={encodedScopeItemKey}&activeMode={Uri.EscapeDataString(OperatorModeTypes.ResolutionDetail)}";
    }

    private static string BuildResolutionQueueUrl(Guid trackedPersonId)
        => $"/operator/resolution?trackedPersonId={Uri.EscapeDataString(trackedPersonId.ToString("D"))}";

    private static string BuildPersonWorkspaceUrl(Guid trackedPersonId)
        => $"/operator/person-workspace?trackedPersonId={Uri.EscapeDataString(trackedPersonId.ToString("D"))}";

    private static OperatorAlertsQueryResult CreateRejectedResult(string reason, OperatorSessionContext session, DateTime nowUtc)
        => new()
        {
            Accepted = false,
            FailureReason = reason,
            Session = CreateResponseSession(session, null, nowUtc),
            GeneratedAtUtc = nowUtc
        };

    private static OperatorSessionContext CreateScopedSession(OperatorSessionContext session, Guid trackedPersonId, DateTime nowUtc)
    {
        var clone = CloneSession(session, nowUtc);
        clone.ActiveTrackedPersonId = trackedPersonId;
        clone.ActiveScopeItemKey = null;
        clone.ActiveMode = OperatorModeTypes.ResolutionQueue;
        clone.UnfinishedStep = null;
        return clone;
    }

    private static OperatorSessionContext CreateResponseSession(OperatorSessionContext session, Guid? trackedPersonId, DateTime nowUtc)
    {
        var clone = CloneSession(session, nowUtc);
        if (trackedPersonId.HasValue && trackedPersonId.Value != Guid.Empty)
        {
            clone.ActiveTrackedPersonId = trackedPersonId.Value;
            clone.ActiveMode = OperatorModeTypes.ResolutionQueue;
            clone.UnfinishedStep = null;
        }

        return clone;
    }

    private static OperatorSessionContext CloneSession(OperatorSessionContext session, DateTime nowUtc)
    {
        var clone = new OperatorSessionContext
        {
            OperatorSessionId = session.OperatorSessionId,
            Surface = session.Surface,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            ActiveTrackedPersonId = session.ActiveTrackedPersonId,
            ActiveScopeItemKey = session.ActiveScopeItemKey,
            ActiveMode = session.ActiveMode
        };

        if (session.UnfinishedStep != null)
        {
            clone.UnfinishedStep = new OperatorWorkflowStepContext
            {
                StepKind = session.UnfinishedStep.StepKind,
                StepState = session.UnfinishedStep.StepState,
                StartedAtUtc = session.UnfinishedStep.StartedAtUtc,
                BoundTrackedPersonId = session.UnfinishedStep.BoundTrackedPersonId,
                BoundScopeItemKey = session.UnfinishedStep.BoundScopeItemKey
            };
        }

        return clone;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

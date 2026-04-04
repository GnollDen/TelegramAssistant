using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Host.OperatorWeb;
using TgAssistant.Infrastructure.Database;

namespace TgAssistant.Host.Launch;

public static class Opint009WebAlertsSmokeRunner
{
    internal static readonly Guid PrimaryTrackedPersonId = Guid.Parse("99999999-aaaa-bbbb-cccc-111111111111");
    internal static readonly Guid SecondaryTrackedPersonId = Guid.Parse("99999999-aaaa-bbbb-cccc-222222222222");

    public static async Task<Opint009WebAlertsSmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new Opint009WebAlertsSmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var projectionBuilder = CreateProjectionBuilder();
            var identity = CreateIdentityContext("opint-009-c1-operator", "OPINT-009-C1 Operator", "opint-009-c1-smoke");
            var session = CreateSessionContext("web:opint009c1");

            var allResult = await projectionBuilder.BuildAsync(
                new OperatorAlertsQueryRequest
                {
                    EscalationBoundary = OperatorAlertsEscalationFilters.All,
                    PersonLimit = 10,
                    AlertsPerPersonLimit = 6
                },
                identity,
                session,
                ct);
            Ensure(allResult.Accepted, "OPINT-009-C1 smoke failed: alerts query should be accepted.");
            Ensure(allResult.Groups.Count == 2, "OPINT-009-C1 smoke failed: expected alerts grouped for two tracked persons.");
            Ensure(allResult.Summary.TotalAlerts == 3, "OPINT-009-C1 smoke failed: expected exactly three web-visible alerts.");
            Ensure(allResult.Groups.All(group => !string.IsNullOrWhiteSpace(group.PersonWorkspaceUrl)), "OPINT-009-C1 smoke failed: person links missing on group cards.");
            Ensure(allResult.Groups.SelectMany(group => group.Alerts).All(alert => !string.IsNullOrWhiteSpace(alert.ResolutionUrl)), "OPINT-009-C1 smoke failed: resolution links missing on alert cards.");
            Ensure(allResult.Groups.SelectMany(group => group.Alerts).All(alert => !string.Equals(alert.Title, "Non-critical review should stay hidden", StringComparison.Ordinal)), "OPINT-009-C1 smoke failed: suppressed non-critical alert leaked into web list.");

            var searchResult = await projectionBuilder.BuildAsync(
                new OperatorAlertsQueryRequest
                {
                    Search = "Second Person",
                    EscalationBoundary = OperatorAlertsEscalationFilters.All,
                    PersonLimit = 10,
                    AlertsPerPersonLimit = 6
                },
                identity,
                session,
                ct);
            Ensure(searchResult.Accepted, "OPINT-009-C1 smoke failed: search query should be accepted.");
            Ensure(searchResult.Groups.Count == 1, "OPINT-009-C1 smoke failed: search filter should narrow to one person group.");
            Ensure(string.Equals(searchResult.Groups[0].TrackedPerson.DisplayName, "OPINT-009-C1 Second Person", StringComparison.Ordinal), "OPINT-009-C1 smoke failed: search filter returned the wrong group.");

            var trackedPersonResult = await projectionBuilder.BuildAsync(
                new OperatorAlertsQueryRequest
                {
                    TrackedPersonId = PrimaryTrackedPersonId,
                    EscalationBoundary = OperatorAlertsEscalationFilters.TelegramPushAcknowledge,
                    PersonLimit = 10,
                    AlertsPerPersonLimit = 6
                },
                identity,
                session,
                ct);
            Ensure(trackedPersonResult.Accepted, "OPINT-009-C1 smoke failed: tracked-person query should be accepted.");
            Ensure(trackedPersonResult.Groups.Count == 1, "OPINT-009-C1 smoke failed: tracked-person filter should isolate one group.");
            Ensure(trackedPersonResult.Summary.TelegramPushCount == 1, "OPINT-009-C1 smoke failed: active tracked-person scope should surface telegram-bound critical alert.");
            Ensure(trackedPersonResult.Groups[0].Alerts.Count == 1, "OPINT-009-C1 smoke failed: tracked-person telegram filter should return one alert.");
            Ensure(string.Equals(trackedPersonResult.Groups[0].Alerts[0].EscalationBoundary, OperatorAlertEscalationBoundaries.TelegramPushAcknowledge, StringComparison.Ordinal), "OPINT-009-C1 smoke failed: wrong escalation boundary for active tracked-person alert.");

            var invalidBoundaryResult = await projectionBuilder.BuildAsync(
                new OperatorAlertsQueryRequest
                {
                    EscalationBoundary = "invalid-boundary"
                },
                identity,
                session,
                ct);
            Ensure(!invalidBoundaryResult.Accepted && string.Equals(invalidBoundaryResult.FailureReason, "alerts_boundary_not_supported", StringComparison.Ordinal), "OPINT-009-C1 smoke failed: invalid boundary should be rejected.");

            var shellHtml = OperatorAlertsWebShell.Html;
            Ensure(shellHtml.Contains("/api/operator/alerts/query", StringComparison.Ordinal), "OPINT-009-C1 smoke failed: web shell does not target alerts query API.");
            Ensure(shellHtml.Contains("/operator/person-workspace", StringComparison.Ordinal), "OPINT-009-C1 smoke failed: web shell omits person workspace link contract.");
            Ensure(shellHtml.Contains("/operator/resolution", StringComparison.Ordinal), "OPINT-009-C1 smoke failed: web shell omits resolution link contract.");
            Ensure(shellHtml.Contains("Grouped Alerts", StringComparison.Ordinal), "OPINT-009-C1 smoke failed: grouped alerts shell section is missing.");

            report.AllChecksPassed = true;
            report.GroupCount = allResult.Groups.Count;
            report.TotalAlerts = allResult.Summary.TotalAlerts;
            report.TelegramBoundAlertsForActiveScope = trackedPersonResult.Summary.TelegramPushCount;
            report.WebOnlyAlerts = allResult.Summary.WebOnlyCount;
            report.SearchMatchedPerson = searchResult.Groups[0].TrackedPerson.DisplayName;
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "OPINT-009-C1 web alerts smoke failed: grouped/filterable critical alert surface regressed.",
                fatal);
        }

        return report;
    }

    internal static OperatorAlertsProjectionBuilder CreateProjectionBuilder()
        => new(
            new StubOperatorResolutionApplicationService(PrimaryTrackedPersonId, SecondaryTrackedPersonId),
            new OperatorAlertPolicyService());

    internal static OperatorIdentityContext CreateIdentityContext(string operatorId, string displayName, string surfaceSubject)
        => new()
        {
            OperatorId = operatorId,
            OperatorDisplay = displayName,
            SurfaceSubject = surfaceSubject,
            AuthSource = "smoke",
            AuthTimeUtc = DateTime.UtcNow
        };

    internal static OperatorSessionContext CreateSessionContext(string sessionId)
        => new()
        {
            OperatorSessionId = sessionId,
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow,
            ActiveMode = OperatorModeTypes.ResolutionQueue
        };

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-009-c1-web-alerts-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StubOperatorResolutionApplicationService : IOperatorResolutionApplicationService
    {
        private readonly Guid _trackedPersonA;
        private readonly Guid _trackedPersonB;

        public StubOperatorResolutionApplicationService(Guid trackedPersonA, Guid trackedPersonB)
        {
            _trackedPersonA = trackedPersonA;
            _trackedPersonB = trackedPersonB;
        }

        public Task<OperatorTrackedPersonQueryResult> QueryTrackedPersonsAsync(OperatorTrackedPersonQueryRequest request, CancellationToken ct = default)
        {
            var persons = new List<OperatorTrackedPersonScopeSummary>
            {
                CreateTrackedPerson(_trackedPersonA, "OPINT-009-C1 Primary Person", "chat:opint-009-c1:primary"),
                CreateTrackedPerson(_trackedPersonB, "OPINT-009-C1 Second Person", "chat:opint-009-c1:second")
            };

            var session = CloneSession(request.Session);
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;
            session.ActiveTrackedPersonId = request.PreferredTrackedPersonId ?? Guid.Empty;

            return Task.FromResult(new OperatorTrackedPersonQueryResult
            {
                Accepted = true,
                AutoSelected = false,
                SelectionSource = "list",
                ActiveTrackedPersonId = request.PreferredTrackedPersonId,
                ActiveTrackedPerson = persons.FirstOrDefault(person => person.TrackedPersonId == request.PreferredTrackedPersonId),
                Session = session,
                TrackedPersons = persons
            });
        }

        public Task<OperatorTrackedPersonSelectionResult> SelectTrackedPersonAsync(OperatorTrackedPersonSelectionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not select tracked person.");

        public Task<OperatorResolutionQueueQueryResult> GetResolutionQueueAsync(OperatorResolutionQueueQueryRequest request, CancellationToken ct = default)
        {
            var trackedPersonId = request.TrackedPersonId ?? Guid.Empty;
            var queue = trackedPersonId == _trackedPersonA
                ? BuildPrimaryQueue()
                : trackedPersonId == _trackedPersonB
                    ? BuildSecondaryQueue()
                    : null;

            if (queue == null)
            {
                return Task.FromResult(new OperatorResolutionQueueQueryResult
                {
                    Accepted = false,
                    FailureReason = "tracked_person_not_found_or_inactive",
                    Session = CloneSession(request.Session)
                });
            }

            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;
            session.ActiveScopeItemKey = null;

            return Task.FromResult(new OperatorResolutionQueueQueryResult
            {
                Accepted = true,
                Session = session,
                Queue = queue
            });
        }

        public Task<OperatorResolutionDetailQueryResult> GetResolutionDetailAsync(OperatorResolutionDetailQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query resolution detail.");

        public Task<OperatorResolutionActionResultEnvelope> SubmitResolutionActionAsync(ResolutionActionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not submit resolution actions.");

        public Task<OperatorPersonWorkspaceListQueryResult> QueryPersonWorkspaceListAsync(OperatorPersonWorkspaceListQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query person workspace list.");

        public Task<OperatorPersonWorkspaceSummaryQueryResult> QueryPersonWorkspaceSummaryAsync(OperatorPersonWorkspaceSummaryQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query person workspace summary.");

        public Task<OperatorPersonWorkspaceDossierQueryResult> QueryPersonWorkspaceDossierAsync(OperatorPersonWorkspaceDossierQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query dossier.");

        public Task<OperatorPersonWorkspaceProfileQueryResult> QueryPersonWorkspaceProfileAsync(OperatorPersonWorkspaceProfileQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query profile.");

        public Task<OperatorPersonWorkspacePairDynamicsQueryResult> QueryPersonWorkspacePairDynamicsAsync(OperatorPersonWorkspacePairDynamicsQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query pair dynamics.");

        public Task<OperatorPersonWorkspaceTimelineQueryResult> QueryPersonWorkspaceTimelineAsync(OperatorPersonWorkspaceTimelineQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query timeline.");

        public Task<OperatorPersonWorkspaceEvidenceQueryResult> QueryPersonWorkspaceEvidenceAsync(OperatorPersonWorkspaceEvidenceQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query evidence.");

        public Task<OperatorPersonWorkspaceRevisionsQueryResult> QueryPersonWorkspaceRevisionsAsync(OperatorPersonWorkspaceRevisionsQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query revisions.");

        public Task<OperatorPersonWorkspaceResolutionQueryResult> QueryPersonWorkspaceResolutionAsync(OperatorPersonWorkspaceResolutionQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query person workspace resolution.");

        public Task<OperatorOfflineEventQueryApiResult> QueryOfflineEventsAsync(OperatorOfflineEventQueryApiRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query offline events.");

        public Task<OperatorOfflineEventDetailQueryResultEnvelope> GetOfflineEventDetailAsync(OperatorOfflineEventDetailQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not query offline-event detail.");

        public Task<OperatorOfflineEventRefinementResult> SubmitOfflineEventRefinementAsync(OperatorOfflineEventRefinementRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not refine offline events.");

        public Task<OperatorOfflineEventTimelineLinkageUpdateResult> SubmitOfflineEventTimelineLinkageUpdateAsync(OperatorOfflineEventTimelineLinkageUpdateRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-C1 smoke should not update timeline linkage.");

        private ResolutionQueueResult BuildPrimaryQueue()
        {
            return new ResolutionQueueResult
            {
                ScopeBound = true,
                TrackedPersonId = _trackedPersonA,
                ScopeKey = "chat:opint-009-c1:primary",
                TrackedPersonDisplayName = "OPINT-009-C1 Primary Person",
                TotalOpenCount = 3,
                FilteredCount = 3,
                Items =
                [
                    new ResolutionItemSummary
                    {
                        ScopeItemKey = "resolution:clarification:opint-009-c1-primary-critical",
                        ItemType = ResolutionItemTypes.Clarification,
                        Title = "Critical clarification blocks progression",
                        Summary = "Primary person needs a workflow-blocking clarification before bounded progression can continue.",
                        WhyItMatters = "This alert should link directly into resolution detail.",
                        AffectedFamily = "resolution",
                        AffectedObjectRef = "clarification:primary",
                        TrustFactor = 0.94f,
                        Status = ResolutionItemStatuses.Blocked,
                        EvidenceCount = 3,
                        UpdatedAtUtc = DateTime.UtcNow,
                        Priority = ResolutionItemPriorities.Critical,
                        RecommendedNextAction = ResolutionActionTypes.OpenWeb,
                        AvailableActions = [ResolutionActionTypes.OpenWeb]
                    },
                    new ResolutionItemSummary
                    {
                        ScopeItemKey = "resolution:missing-data:opint-009-c1-primary-web",
                        ItemType = ResolutionItemTypes.Review,
                        Title = "Runtime data defect needs web follow-up",
                        Summary = "A bounded runtime data defect requires operator review for the primary person.",
                        WhyItMatters = "Operators need a grouped web alert even when Telegram should stay quiet.",
                        AffectedFamily = RuntimeDefectClasses.Data,
                        AffectedObjectRef = "runtime-defect:data:primary",
                        TrustFactor = 0.83f,
                        Status = ResolutionItemStatuses.AttentionRequired,
                        EvidenceCount = 1,
                        UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-4),
                        Priority = ResolutionItemPriorities.Critical,
                        RecommendedNextAction = ResolutionActionTypes.OpenWeb,
                        AvailableActions = [ResolutionActionTypes.OpenWeb]
                    },
                    new ResolutionItemSummary
                    {
                        ScopeItemKey = "resolution:review:opint-009-c1-primary-hidden",
                        ItemType = ResolutionItemTypes.Review,
                        Title = "Non-critical review should stay hidden",
                        Summary = "This review is informative but should not surface by default.",
                        WhyItMatters = "Suppression rules should keep it off the base web alerts surface.",
                        AffectedFamily = "profile",
                        AffectedObjectRef = "profile:hidden",
                        TrustFactor = 0.71f,
                        Status = ResolutionItemStatuses.Open,
                        EvidenceCount = 1,
                        UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-12),
                        Priority = ResolutionItemPriorities.High,
                        RecommendedNextAction = ResolutionActionTypes.OpenWeb,
                        AvailableActions = [ResolutionActionTypes.OpenWeb]
                    }
                ]
            };
        }

        private ResolutionQueueResult BuildSecondaryQueue()
        {
            return new ResolutionQueueResult
            {
                ScopeBound = true,
                TrackedPersonId = _trackedPersonB,
                ScopeKey = "chat:opint-009-c1:second",
                TrackedPersonDisplayName = "OPINT-009-C1 Second Person",
                TotalOpenCount = 1,
                FilteredCount = 1,
                Items =
                [
                    new ResolutionItemSummary
                    {
                        ScopeItemKey = "resolution:blocked-branch:opint-009-c1-second-critical",
                        ItemType = ResolutionItemTypes.BlockedBranch,
                        Title = "Second person branch remains blocked",
                        Summary = "A blocked branch still requires workflow attention for the second person.",
                        WhyItMatters = "This verifies grouping and search across multiple tracked persons.",
                        AffectedFamily = "branching",
                        AffectedObjectRef = "branch:second",
                        TrustFactor = 0.89f,
                        Status = ResolutionItemStatuses.Blocked,
                        EvidenceCount = 2,
                        UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                        Priority = ResolutionItemPriorities.Critical,
                        RecommendedNextAction = ResolutionActionTypes.OpenWeb,
                        AvailableActions = [ResolutionActionTypes.OpenWeb]
                    }
                ]
            };
        }

        private static OperatorTrackedPersonScopeSummary CreateTrackedPerson(Guid trackedPersonId, string displayName, string scopeKey)
        {
            return new OperatorTrackedPersonScopeSummary
            {
                TrackedPersonId = trackedPersonId,
                DisplayName = displayName,
                ScopeKey = scopeKey,
                EvidenceCount = 4,
                UnresolvedCount = 2,
                HasUnresolved = true,
                RecentUpdateAtUtc = DateTime.UtcNow,
                LastUnresolvedAtUtc = DateTime.UtcNow.AddMinutes(-3),
                UpdatedAtUtc = DateTime.UtcNow
            };
        }

        private static OperatorSessionContext CloneSession(OperatorSessionContext session)
        {
            return new OperatorSessionContext
            {
                OperatorSessionId = session.OperatorSessionId,
                Surface = session.Surface,
                AuthenticatedAtUtc = session.AuthenticatedAtUtc,
                LastSeenAtUtc = session.LastSeenAtUtc,
                ExpiresAtUtc = session.ExpiresAtUtc,
                ActiveTrackedPersonId = session.ActiveTrackedPersonId,
                ActiveScopeItemKey = session.ActiveScopeItemKey,
                ActiveMode = session.ActiveMode,
                UnfinishedStep = session.UnfinishedStep == null
                    ? null
                    : new OperatorWorkflowStepContext
                    {
                        StepKind = session.UnfinishedStep.StepKind,
                        StepState = session.UnfinishedStep.StepState,
                        StartedAtUtc = session.UnfinishedStep.StartedAtUtc,
                        BoundTrackedPersonId = session.UnfinishedStep.BoundTrackedPersonId,
                        BoundScopeItemKey = session.UnfinishedStep.BoundScopeItemKey
                    }
            };
        }
    }
}

public sealed class Opint009WebAlertsSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public int GroupCount { get; set; }
    public int TotalAlerts { get; set; }
    public int WebOnlyAlerts { get; set; }
    public int TelegramBoundAlertsForActiveScope { get; set; }
    public string SearchMatchedPerson { get; set; } = string.Empty;
}

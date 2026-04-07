using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint009TelegramAlertsSmokeRunner
{
    public static async Task<Opint009TelegramAlertsSmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        const long ownerUserId = 900009;
        const string handoffSecret = "opint-009-b-smoke-token";
        var trackedPersonId = Guid.Parse("99999999-1111-2222-3333-444444444444");
        const string trackedPersonDisplayName = "OPINT-009-B Tracked Person";
        const string scopeKey = "chat:opint-009-b-smoke";
        const string criticalScopeItemKey = "resolution:clarification:opint-009-b-critical";
        const string nonCriticalScopeItemKey = "resolution:review:opint-009-b-noncritical";
        var resolvedOutputPath = ResolveOutputPath(outputPath);

        var sessionStore = new TelegramOperatorSessionStore();
        var workflow = new TelegramOperatorWorkflowService(
            Options.Create(new TelegramSettings { OwnerUserId = ownerUserId }),
            Options.Create(new WebSettings
            {
                Url = "https://operator.example.test",
                OperatorAccessToken = handoffSecret
            }),
            sessionStore,
            new StubOperatorResolutionApplicationService(
                trackedPersonId,
                trackedPersonDisplayName,
                scopeKey,
                criticalScopeItemKey,
                nonCriticalScopeItemKey),
            new OperatorAlertPolicyService(),
            new OperatorAssistantResponseGenerationService(Options.Create(new WebSettings
            {
                Url = "https://operator.example.test",
                OperatorAccessToken = handoffSecret
            })),
            new StubAssistantContextAssemblyService(),
            new NoopOperatorOfflineEventRepository(),
            new OfflineEventClarificationPolicy(),
            new NoopOperatorSessionAuditService(),
            NullLogger<TelegramOperatorWorkflowService>.Instance);

        var report = new Opint009TelegramAlertsSmokeReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            var startResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = ownerUserId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-009-B Operator",
                    MessageText = "/start"
                },
                ct);

            Ensure(startResponse.Messages.Count > 0, "OPINT-009-B smoke failed: /start did not produce a mode card.");
            Ensure(
                FlattenButtons(startResponse.Messages[0]).Any(button => string.Equals(button.Text, "Alerts", StringComparison.Ordinal)),
                "OPINT-009-B smoke failed: mode card omitted Alerts.");

            var alertsEntry = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = ownerUserId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-009-B Operator",
                    CallbackData = "mode:alerts",
                    CallbackQueryId = "opint-009-b-mode-alerts"
                },
                ct);

            Ensure(alertsEntry.Messages.Count >= 2, "OPINT-009-B smoke failed: alerts mode did not render summary plus card.");
            Ensure(
                alertsEntry.Messages[0].Text.Contains("Alerts Mode", StringComparison.Ordinal)
                    && alertsEntry.Messages[0].Text.Contains($"Active tracked person: {trackedPersonDisplayName}", StringComparison.Ordinal),
                "OPINT-009-B smoke failed: alerts summary did not bind tracked-person context.");
            Ensure(
                alertsEntry.Messages.Any(message => message.Text.Contains("Critical clarification requires acknowledgement", StringComparison.Ordinal)),
                "OPINT-009-B smoke failed: critical alert card was not rendered.");
            Ensure(
                alertsEntry.Messages.All(message => !message.Text.Contains("Non-critical review should stay out of Telegram", StringComparison.Ordinal)),
                "OPINT-009-B smoke failed: non-critical alert leaked into Telegram alert surface.");

            var criticalCard = alertsEntry.Messages
                .First(message => message.Text.Contains("Critical clarification requires acknowledgement", StringComparison.Ordinal));
            var criticalButtons = FlattenButtons(criticalCard);
            var acknowledgeButton = criticalButtons.FirstOrDefault(button => string.Equals(button.Text, "Acknowledge", StringComparison.Ordinal));
            var openWebButton = criticalButtons.FirstOrDefault(button => string.Equals(button.Text, "Open in Web", StringComparison.Ordinal));
            Ensure(acknowledgeButton?.CallbackData != null, "OPINT-009-B smoke failed: alert card omitted acknowledge callback.");
            Ensure(!string.IsNullOrWhiteSpace(openWebButton?.Url), "OPINT-009-B smoke failed: alert card omitted Open in Web URL.");
            var acknowledgeCallbackData = acknowledgeButton!.CallbackData!;

            var handoffUri = new Uri(openWebButton!.Url!, UriKind.Absolute);
            report.OpenInWebUrl = handoffUri.ToString();
            var query = ParseQuery(handoffUri.Query);
            var snapshot = sessionStore.GetSnapshot(ownerUserId)
                ?? throw new InvalidOperationException("OPINT-009-B smoke failed: session snapshot missing after alerts entry.");
            Ensure(
                string.Equals(query["tracked_person_id"], trackedPersonId.ToString("D"), StringComparison.Ordinal),
                "OPINT-009-B smoke failed: Open in Web tracked_person_id mismatch.");
            Ensure(
                string.Equals(query["scope_item_key"], criticalScopeItemKey, StringComparison.Ordinal),
                "OPINT-009-B smoke failed: Open in Web scope_item_key mismatch.");
            Ensure(
                string.Equals(query["operator_session_id"], snapshot.OperatorSessionId, StringComparison.Ordinal),
                "OPINT-009-B smoke failed: Open in Web operator_session_id mismatch.");
            Ensure(
                string.Equals(query["active_mode"], OperatorModeTypes.ResolutionDetail, StringComparison.Ordinal),
                "OPINT-009-B smoke failed: Open in Web active_mode mismatch.");
            Ensure(
                string.Equals(query["target_api"], "/api/operator/resolution/detail/query", StringComparison.Ordinal),
                "OPINT-009-B smoke failed: Open in Web target_api mismatch.");
            Ensure(
                OperatorHandoffTokenCodec.TryValidateToken(
                    query["handoff_token"],
                    OperatorHandoffTokenCodec.TelegramResolutionContext,
                    trackedPersonId,
                    criticalScopeItemKey,
                    snapshot.OperatorSessionId,
                    handoffSecret,
                    tokenTtlMinutes: 30),
                "OPINT-009-B smoke failed: Open in Web handoff token was invalid.");

            var acknowledgedResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = ownerUserId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-009-B Operator",
                    CallbackData = acknowledgeCallbackData,
                    CallbackQueryId = "opint-009-b-alert-ack"
                },
                ct);

            Ensure(
                acknowledgedResponse.Messages[0].Text.Contains("Acknowledged alert for Critical clarification requires acknowledgement.", StringComparison.Ordinal),
                "OPINT-009-B smoke failed: acknowledgement summary note was missing.");
            Ensure(
                acknowledgedResponse.Messages.Any(message => message.Text.Contains("Acknowledged: yes", StringComparison.Ordinal)),
                "OPINT-009-B smoke failed: alert card was not marked acknowledged.");

            var acknowledgedSnapshot = sessionStore.GetSnapshot(ownerUserId)
                ?? throw new InvalidOperationException("OPINT-009-B smoke failed: session snapshot missing after acknowledgement.");
            Ensure(
                string.Equals(acknowledgedSnapshot.SurfaceMode, TelegramOperatorSurfaceModes.Alerts, StringComparison.Ordinal),
                "OPINT-009-B smoke failed: surface mode was not retained as alerts.");
            Ensure(
                acknowledgedSnapshot.ActiveTrackedPersonId == trackedPersonId,
                "OPINT-009-B smoke failed: tracked-person context was not retained after acknowledgement.");

            report.AllChecksPassed = true;
            report.ActiveTrackedPersonId = acknowledgedSnapshot.ActiveTrackedPersonId;
            report.OperatorSessionId = acknowledgedSnapshot.OperatorSessionId;
            report.CriticalAlertScopeItemKey = criticalScopeItemKey;
            report.NonCriticalAlertSuppressed = true;
            report.AcknowledgementRetainedContext = true;
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
                "OPINT-009-B Telegram alerts smoke failed: critical-alert acknowledgement or handoff contract regressed.",
                fatal);
        }

        return report;
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-009-b-telegram-alerts-smoke-report.json"));
    }

    private static IReadOnlyList<TelegramOperatorButton> FlattenButtons(TelegramOperatorMessage message)
        => message.Buttons.SelectMany(row => row).ToList();

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static void Ensure(bool condition, string failureMessage)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    private sealed class StubOperatorResolutionApplicationService : IOperatorResolutionApplicationService
    {
        private readonly Guid _trackedPersonId;
        private readonly string _displayName;
        private readonly string _scopeKey;
        private readonly string _criticalScopeItemKey;
        private readonly string _nonCriticalScopeItemKey;

        public StubOperatorResolutionApplicationService(
            Guid trackedPersonId,
            string displayName,
            string scopeKey,
            string criticalScopeItemKey,
            string nonCriticalScopeItemKey)
        {
            _trackedPersonId = trackedPersonId;
            _displayName = displayName;
            _scopeKey = scopeKey;
            _criticalScopeItemKey = criticalScopeItemKey;
            _nonCriticalScopeItemKey = nonCriticalScopeItemKey;
        }

        public Task<OperatorTrackedPersonQueryResult> QueryTrackedPersonsAsync(OperatorTrackedPersonQueryRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;

            return Task.FromResult(new OperatorTrackedPersonQueryResult
            {
                Accepted = true,
                AutoSelected = true,
                SelectionSource = "auto_single",
                ActiveTrackedPersonId = _trackedPersonId,
                ActiveTrackedPerson = CreateTrackedPersonSummary(),
                Session = session,
                TrackedPersons = [CreateTrackedPersonSummary()]
            });
        }

        public Task<OperatorTrackedPersonSelectionResult> SelectTrackedPersonAsync(OperatorTrackedPersonSelectionRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;
            session.ActiveScopeItemKey = null;
            session.UnfinishedStep = null;

            return Task.FromResult(new OperatorTrackedPersonSelectionResult
            {
                Accepted = request.TrackedPersonId == _trackedPersonId,
                FailureReason = request.TrackedPersonId == _trackedPersonId ? null : "tracked_person_not_found_or_inactive",
                ScopeChanged = true,
                ActiveTrackedPerson = request.TrackedPersonId == _trackedPersonId ? CreateTrackedPersonSummary() : null,
                Session = session
            });
        }

        public Task<OperatorResolutionQueueQueryResult> GetResolutionQueueAsync(OperatorResolutionQueueQueryRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;
            session.ActiveScopeItemKey = null;

            return Task.FromResult(new OperatorResolutionQueueQueryResult
            {
                Accepted = true,
                Session = session,
                Queue = new ResolutionQueueResult
                {
                    ScopeBound = true,
                    TrackedPersonId = _trackedPersonId,
                    ScopeKey = _scopeKey,
                    TrackedPersonDisplayName = _displayName,
                    TotalOpenCount = 2,
                    FilteredCount = 2,
                    Items =
                    [
                        new ResolutionItemSummary
                        {
                            ScopeItemKey = _criticalScopeItemKey,
                            ItemType = ResolutionItemTypes.Clarification,
                            Title = "Critical clarification requires acknowledgement",
                            Summary = "A critical clarification is blocking bounded workflow progression.",
                            WhyItMatters = "Operator acknowledgement is required before deep analysis handoff.",
                            AffectedFamily = "stage8_bootstrap",
                            AffectedObjectRef = "bootstrap:critical",
                            TrustFactor = 0.91f,
                            Status = ResolutionItemStatuses.Blocked,
                            EvidenceCount = 2,
                            UpdatedAtUtc = DateTime.UtcNow,
                            Priority = ResolutionItemPriorities.Critical,
                            RecommendedNextAction = ResolutionActionTypes.OpenWeb,
                            AvailableActions = [ResolutionActionTypes.OpenWeb]
                        },
                        new ResolutionItemSummary
                        {
                            ScopeItemKey = _nonCriticalScopeItemKey,
                            ItemType = ResolutionItemTypes.Review,
                            Title = "Non-critical review should stay out of Telegram",
                            Summary = "This review item is informative but non-blocking.",
                            WhyItMatters = "It belongs in deeper web review, not Telegram push.",
                            AffectedFamily = "profile",
                            AffectedObjectRef = "profile:noncritical",
                            TrustFactor = 0.72f,
                            Status = ResolutionItemStatuses.Open,
                            EvidenceCount = 1,
                            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                            Priority = ResolutionItemPriorities.High,
                            RecommendedNextAction = ResolutionActionTypes.OpenWeb,
                            AvailableActions = [ResolutionActionTypes.OpenWeb]
                        }
                    ]
                }
            });
        }

        public Task<OperatorResolutionDetailQueryResult> GetResolutionDetailAsync(OperatorResolutionDetailQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query resolution detail directly.");

        public Task<OperatorResolutionActionResultEnvelope> SubmitResolutionActionAsync(ResolutionActionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not submit resolution actions.");

        public Task<OperatorConflictResolutionSessionResultEnvelope> StartConflictResolutionSessionAsync(
            OperatorConflictResolutionSessionStartRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not start AI conflict sessions.");

        public Task<OperatorConflictResolutionSessionResultEnvelope> RespondConflictResolutionSessionAsync(
            OperatorConflictResolutionSessionRespondRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not respond to AI conflict sessions.");

        public Task<OperatorConflictResolutionSessionResultEnvelope> QueryConflictResolutionSessionAsync(
            OperatorConflictResolutionSessionQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query AI conflict sessions.");

        public Task<OperatorPersonWorkspaceListQueryResult> QueryPersonWorkspaceListAsync(OperatorPersonWorkspaceListQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace list.");

        public Task<OperatorPersonWorkspaceSummaryQueryResult> QueryPersonWorkspaceSummaryAsync(OperatorPersonWorkspaceSummaryQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace summary.");

        public Task<OperatorPersonWorkspaceDossierQueryResult> QueryPersonWorkspaceDossierAsync(OperatorPersonWorkspaceDossierQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace dossier.");

        public Task<OperatorPersonWorkspaceProfileQueryResult> QueryPersonWorkspaceProfileAsync(OperatorPersonWorkspaceProfileQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace profile.");

        public Task<OperatorPersonWorkspacePairDynamicsQueryResult> QueryPersonWorkspacePairDynamicsAsync(OperatorPersonWorkspacePairDynamicsQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace pair dynamics.");

        public Task<OperatorPersonWorkspaceTimelineQueryResult> QueryPersonWorkspaceTimelineAsync(OperatorPersonWorkspaceTimelineQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace timeline.");

        public Task<OperatorPersonWorkspaceEvidenceQueryResult> QueryPersonWorkspaceEvidenceAsync(OperatorPersonWorkspaceEvidenceQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace evidence.");

        public Task<OperatorPersonWorkspaceRevisionsQueryResult> QueryPersonWorkspaceRevisionsAsync(OperatorPersonWorkspaceRevisionsQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace revisions.");

        public Task<OperatorPersonWorkspaceResolutionQueryResult> QueryPersonWorkspaceResolutionAsync(OperatorPersonWorkspaceResolutionQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace resolution.");

        public Task<OperatorPersonWorkspaceHistoryQueryResult> QueryPersonWorkspaceHistoryAsync(OperatorPersonWorkspaceHistoryQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query person workspace history.");

        public Task<OperatorOfflineEventQueryApiResult> QueryOfflineEventsAsync(OperatorOfflineEventQueryApiRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query offline events.");

        public Task<OperatorOfflineEventDetailQueryResultEnvelope> GetOfflineEventDetailAsync(OperatorOfflineEventDetailQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not query offline-event detail.");

        public Task<OperatorOfflineEventRefinementResult> SubmitOfflineEventRefinementAsync(OperatorOfflineEventRefinementRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not refine offline events.");

        public Task<OperatorOfflineEventTimelineLinkageUpdateResult> SubmitOfflineEventTimelineLinkageUpdateAsync(OperatorOfflineEventTimelineLinkageUpdateRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not update offline-event timeline linkage.");

        private OperatorTrackedPersonScopeSummary CreateTrackedPersonSummary()
        {
            return new OperatorTrackedPersonScopeSummary
            {
                TrackedPersonId = _trackedPersonId,
                ScopeKey = _scopeKey,
                DisplayName = _displayName,
                EvidenceCount = 4,
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

    private sealed class StubAssistantContextAssemblyService : IOperatorAssistantContextAssemblyService
    {
        public Task<OperatorAssistantResponseEnvelope> BuildBoundedResponseAsync(
            OperatorAssistantContextAssemblyRequest request,
            DateTime? generatedAtUtc = null,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-009-B smoke should not invoke assistant context assembly.");
    }

    private sealed class NoopOperatorSessionAuditService : IOperatorSessionAuditService
    {
        public Task<Guid> RecordSessionEventAsync(OperatorSessionAuditRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoopOperatorOfflineEventRepository : IOperatorOfflineEventRepository
    {
        public Task<OperatorOfflineEventRecord> UpsertDraftAsync(
            OperatorOfflineEventDraftUpsertRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new OperatorOfflineEventRecord());

        public Task<OperatorOfflineEventRecord> SaveFinalAsync(
            OperatorOfflineEventFinalSaveRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new OperatorOfflineEventRecord());

        public Task<OperatorOfflineEventRecord> CreateAsync(OperatorOfflineEventCreateRequest request, CancellationToken ct = default)
            => Task.FromResult(new OperatorOfflineEventRecord());

        public Task<OperatorOfflineEventRecord?> GetByIdAsync(Guid offlineEventId, CancellationToken ct = default)
            => Task.FromResult<OperatorOfflineEventRecord?>(null);

        public Task<OperatorOfflineEventRecord?> GetByIdWithinScopeAsync(Guid offlineEventId, string scopeKey, Guid trackedPersonId, CancellationToken ct = default)
            => Task.FromResult<OperatorOfflineEventRecord?>(null);

        public Task<OperatorOfflineEventQueryResult> QueryAsync(OperatorOfflineEventQueryRequest request, CancellationToken ct = default)
            => Task.FromResult(new OperatorOfflineEventQueryResult());

        public Task<OperatorOfflineEventRefinementRecord?> RefineWithinScopeAsync(
            Guid offlineEventId,
            string scopeKey,
            Guid trackedPersonId,
            string? summary,
            string? recordingReference,
            bool clearRecordingReference,
            string? refinementNote,
            OperatorIdentityContext operatorIdentity,
            OperatorSessionContext session,
            DateTime refinedAtUtc,
            CancellationToken ct = default)
            => Task.FromResult<OperatorOfflineEventRefinementRecord?>(null);

        public Task<OperatorOfflineEventTimelineLinkageUpdateRecord?> UpdateTimelineLinkageWithinScopeAsync(
            Guid offlineEventId,
            string scopeKey,
            Guid trackedPersonId,
            string linkageStatus,
            string? targetFamily,
            string? targetRef,
            string? linkageNote,
            OperatorIdentityContext operatorIdentity,
            OperatorSessionContext session,
            DateTime updatedAtUtc,
            CancellationToken ct = default)
            => Task.FromResult<OperatorOfflineEventTimelineLinkageUpdateRecord?>(null);
    }
}

public sealed class Opint009TelegramAlertsSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public Guid ActiveTrackedPersonId { get; set; }
    public string OperatorSessionId { get; set; } = string.Empty;
    public string CriticalAlertScopeItemKey { get; set; } = string.Empty;
    public string OpenInWebUrl { get; set; } = string.Empty;
    public bool NonCriticalAlertSuppressed { get; set; }
    public bool AcknowledgementRetainedContext { get; set; }
}

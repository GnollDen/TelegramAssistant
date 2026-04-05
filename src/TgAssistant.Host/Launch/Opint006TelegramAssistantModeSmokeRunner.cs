using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint006TelegramAssistantModeSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        const long ownerUserId = 900001;
        var trackedPersonId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        const string trackedPersonDisplayName = "OPINT-006-C Tracked Person";
        const string scopeKey = "chat:opint-006-c-smoke";
        const string scopeItemKey = "resolution:clarification:opint-006-c";

        var sessionStore = new TelegramOperatorSessionStore();
        var resolutionService = new StubOperatorResolutionApplicationService(trackedPersonId, trackedPersonDisplayName, scopeKey);
        var assistantContextService = new StubAssistantContextAssemblyService(trackedPersonId, scopeKey, scopeItemKey);
        var workflow = new TelegramOperatorWorkflowService(
            Options.Create(new TelegramSettings { OwnerUserId = ownerUserId }),
            Options.Create(new WebSettings { Url = "https://operator.example.test", OperatorAccessToken = "opint-006-c-smoke-token" }),
            sessionStore,
            resolutionService,
            new OperatorAlertPolicyService(),
            new OperatorAssistantResponseGenerationService(Options.Create(new WebSettings { Url = "https://operator.example.test", OperatorAccessToken = "opint-006-c-smoke-token" })),
            assistantContextService,
            new NoopOperatorOfflineEventRepository(),
            new OfflineEventClarificationPolicy(),
            new NoopOperatorSessionAuditService(),
            NullLogger<TelegramOperatorWorkflowService>.Instance);

        var startResponse = await workflow.HandleInteractionAsync(
            new TelegramOperatorInteraction
            {
                ChatId = ownerUserId,
                UserId = ownerUserId,
                IsPrivateChat = true,
                UserDisplayName = "OPINT-006-C Operator",
                MessageText = "/start"
            },
            ct);

        Ensure(startResponse.Messages.Count > 0, "OPINT-006-C smoke failed: /start did not produce a mode card.");
        var startButtons = FlattenButtons(startResponse.Messages[0]);
        Ensure(startButtons.Any(button => string.Equals(button.Text, "Assistant", StringComparison.Ordinal)),
            "OPINT-006-C smoke failed: mode card omitted Assistant.");

        var assistantEntry = await workflow.HandleInteractionAsync(
            new TelegramOperatorInteraction
            {
                ChatId = ownerUserId,
                UserId = ownerUserId,
                IsPrivateChat = true,
                UserDisplayName = "OPINT-006-C Operator",
                CallbackData = "mode:assistant",
                CallbackQueryId = "opint-006-c-mode-assistant"
            },
            ct);

        Ensure(assistantEntry.Messages.Count > 0, "OPINT-006-C smoke failed: Assistant mode entry produced no response.");
        Ensure(
            assistantEntry.Messages[0].Text.Contains("Assistant Mode", StringComparison.Ordinal)
                && assistantEntry.Messages[0].Text.Contains($"Active tracked person: {trackedPersonDisplayName}", StringComparison.Ordinal),
            "OPINT-006-C smoke failed: assistant mode did not bind active tracked-person context.");

        var assistantAnswer = await workflow.HandleInteractionAsync(
            new TelegramOperatorInteraction
            {
                ChatId = ownerUserId,
                UserId = ownerUserId,
                IsPrivateChat = true,
                UserDisplayName = "OPINT-006-C Operator",
                MessageText = "What is the current bounded recommendation?"
            },
            ct);

        Ensure(assistantAnswer.Messages.Count > 0, "OPINT-006-C smoke failed: assistant question produced no response.");
        var answerMessage = assistantAnswer.Messages[0];
        AssertContainsInOrder(
            answerMessage.Text,
            "Short Answer",
            "What Is Known",
            "What It Means",
            "Recommendation",
            "Trust: ");

        Ensure(
            answerMessage.Text.Contains("[Fact | ", StringComparison.Ordinal)
                && answerMessage.Text.Contains("[Inference | ", StringComparison.Ordinal)
                && answerMessage.Text.Contains("[Hypothesis | ", StringComparison.Ordinal)
                && answerMessage.Text.Contains("[Recommendation | ", StringComparison.Ordinal),
            "OPINT-006-C smoke failed: assistant response missing required truth labels.");

        var buttons = FlattenButtons(answerMessage);
        var openWebButton = buttons.FirstOrDefault(button => string.Equals(button.Text, "Open in Web", StringComparison.Ordinal));
        Ensure(openWebButton != null, "OPINT-006-C smoke failed: assistant response omitted Open in Web control.");
        Ensure(!string.IsNullOrWhiteSpace(openWebButton!.Url), "OPINT-006-C smoke failed: Open in Web control did not include bounded URL handoff.");

        var handoffUrl = openWebButton.Url!;
        Ensure(handoffUrl.StartsWith("https://operator.example.test/operator/resolution?", StringComparison.Ordinal),
            "OPINT-006-C smoke failed: Open in Web URL did not target /operator/resolution.");
        Ensure(handoffUrl.Contains($"tracked_person_id={Uri.EscapeDataString(trackedPersonId.ToString("D"))}", StringComparison.Ordinal),
            "OPINT-006-C smoke failed: Open in Web URL missing tracked_person_id.");
        Ensure(handoffUrl.Contains($"scope_item_key={Uri.EscapeDataString(scopeItemKey)}", StringComparison.Ordinal),
            "OPINT-006-C smoke failed: Open in Web URL missing scope_item_key.");
        Ensure(handoffUrl.Contains("handoff_token=", StringComparison.Ordinal),
            "OPINT-006-C smoke failed: Open in Web URL missing handoff_token.");

        var request = assistantContextService.LastRequest
            ?? throw new InvalidOperationException("OPINT-006-C smoke failed: assistant context assembly was not invoked.");
        Ensure(request.TrackedPersonId == trackedPersonId, "OPINT-006-C smoke failed: assistant request tracked-person scope mismatch.");
        Ensure(string.Equals(request.ScopeKey, scopeKey, StringComparison.Ordinal), "OPINT-006-C smoke failed: assistant request scope_key mismatch.");

        var snapshot = sessionStore.GetSnapshot(ownerUserId)
            ?? throw new InvalidOperationException("OPINT-006-C smoke failed: session snapshot missing.");
        Ensure(string.Equals(snapshot.SurfaceMode, TelegramOperatorSurfaceModes.Assistant, StringComparison.Ordinal),
            "OPINT-006-C smoke failed: surface mode is not assistant after assistant interaction.");
        Ensure(snapshot.ActiveTrackedPersonId == trackedPersonId,
            "OPINT-006-C smoke failed: active tracked person was not retained in assistant session state.");
        Ensure(string.Equals(snapshot.ActiveMode, OperatorModeTypes.Assistant, StringComparison.Ordinal),
            "OPINT-006-C smoke failed: active mode is not assistant after assistant interaction.");
    }

    private static IReadOnlyList<TelegramOperatorButton> FlattenButtons(TelegramOperatorMessage message)
        => message.Buttons.SelectMany(row => row).ToList();

    private static void AssertContainsInOrder(string value, params string[] required)
    {
        var cursor = 0;
        foreach (var entry in required)
        {
            var index = value.IndexOf(entry, cursor, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException($"OPINT-006-C smoke failed: required section '{entry}' is missing.");
            }

            cursor = index + entry.Length;
        }
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

        public StubOperatorResolutionApplicationService(Guid trackedPersonId, string displayName, string scopeKey)
        {
            _trackedPersonId = trackedPersonId;
            _displayName = displayName;
            _scopeKey = scopeKey;
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
                ActiveTrackedPerson = new OperatorTrackedPersonScopeSummary
                {
                    TrackedPersonId = _trackedPersonId,
                    ScopeKey = _scopeKey,
                    DisplayName = _displayName,
                    EvidenceCount = 3,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                Session = session,
                TrackedPersons =
                [
                    new OperatorTrackedPersonScopeSummary
                    {
                        TrackedPersonId = _trackedPersonId,
                        ScopeKey = _scopeKey,
                        DisplayName = _displayName,
                        EvidenceCount = 3,
                        UpdatedAtUtc = DateTime.UtcNow
                    }
                ]
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
                ActiveTrackedPerson = request.TrackedPersonId == _trackedPersonId
                    ? new OperatorTrackedPersonScopeSummary
                    {
                        TrackedPersonId = _trackedPersonId,
                        ScopeKey = _scopeKey,
                        DisplayName = _displayName,
                        EvidenceCount = 3,
                        UpdatedAtUtc = DateTime.UtcNow
                    }
                    : null,
                Session = session
            });
        }

        public Task<OperatorPersonWorkspaceListQueryResult> QueryPersonWorkspaceListAsync(
            OperatorPersonWorkspaceListQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace list.");

        public Task<OperatorPersonWorkspaceSummaryQueryResult> QueryPersonWorkspaceSummaryAsync(
            OperatorPersonWorkspaceSummaryQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace summary.");

        public Task<OperatorPersonWorkspaceDossierQueryResult> QueryPersonWorkspaceDossierAsync(
            OperatorPersonWorkspaceDossierQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace dossier.");

        public Task<OperatorPersonWorkspaceProfileQueryResult> QueryPersonWorkspaceProfileAsync(
            OperatorPersonWorkspaceProfileQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace profile.");

        public Task<OperatorPersonWorkspacePairDynamicsQueryResult> QueryPersonWorkspacePairDynamicsAsync(
            OperatorPersonWorkspacePairDynamicsQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace pair dynamics.");

        public Task<OperatorPersonWorkspaceTimelineQueryResult> QueryPersonWorkspaceTimelineAsync(
            OperatorPersonWorkspaceTimelineQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace timeline.");

        public Task<OperatorPersonWorkspaceEvidenceQueryResult> QueryPersonWorkspaceEvidenceAsync(
            OperatorPersonWorkspaceEvidenceQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace evidence.");

        public Task<OperatorPersonWorkspaceRevisionsQueryResult> QueryPersonWorkspaceRevisionsAsync(
            OperatorPersonWorkspaceRevisionsQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace revisions.");

        public Task<OperatorPersonWorkspaceResolutionQueryResult> QueryPersonWorkspaceResolutionAsync(
            OperatorPersonWorkspaceResolutionQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query person workspace resolution.");

        public Task<OperatorResolutionQueueQueryResult> GetResolutionQueueAsync(OperatorResolutionQueueQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not use resolution queue query path.");

        public Task<OperatorResolutionDetailQueryResult> GetResolutionDetailAsync(OperatorResolutionDetailQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not use resolution detail query path.");

        public Task<OperatorResolutionActionResultEnvelope> SubmitResolutionActionAsync(ResolutionActionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not submit resolution actions.");

        public Task<OperatorOfflineEventQueryApiResult> QueryOfflineEventsAsync(
            OperatorOfflineEventQueryApiRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query offline events.");

        public Task<OperatorOfflineEventDetailQueryResultEnvelope> GetOfflineEventDetailAsync(
            OperatorOfflineEventDetailQueryRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not query offline-event detail.");

        public Task<OperatorOfflineEventRefinementResult> SubmitOfflineEventRefinementAsync(
            OperatorOfflineEventRefinementRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not submit offline-event refinement.");

        public Task<OperatorOfflineEventTimelineLinkageUpdateResult> SubmitOfflineEventTimelineLinkageUpdateAsync(
            OperatorOfflineEventTimelineLinkageUpdateRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-006-C smoke should not update offline-event timeline linkage.");

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
        private readonly Guid _trackedPersonId;
        private readonly string _scopeKey;
        private readonly string _scopeItemKey;
        private readonly OperatorAssistantResponseGenerationService _responseGenerationService =
            new(Options.Create(new WebSettings { Url = "https://operator.example.test", OperatorAccessToken = "opint-006-c-smoke-token" }));

        public StubAssistantContextAssemblyService(Guid trackedPersonId, string scopeKey, string scopeItemKey)
        {
            _trackedPersonId = trackedPersonId;
            _scopeKey = scopeKey;
            _scopeItemKey = scopeItemKey;
        }

        public OperatorAssistantContextAssemblyRequest? LastRequest { get; private set; }

        public Task<OperatorAssistantResponseEnvelope> BuildBoundedResponseAsync(
            OperatorAssistantContextAssemblyRequest request,
            DateTime? generatedAtUtc = null,
            CancellationToken ct = default)
        {
            LastRequest = request;
            if (request.TrackedPersonId != _trackedPersonId)
            {
                throw new InvalidOperationException(OperatorAssistantFailureReasons.TrackedPersonScopeMismatch);
            }

            if (!string.Equals(request.ScopeKey, _scopeKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(OperatorAssistantFailureReasons.ReadModelScopeMismatch);
            }

            var response = _responseGenerationService.BuildResponse(
                new OperatorAssistantResponseGenerationRequest
                {
                    OperatorIdentity = request.OperatorIdentity,
                    Session = request.Session,
                    TrackedPersonId = request.TrackedPersonId,
                    ScopeKey = request.ScopeKey,
                    Question = request.Question,
                    ShortAnswer = new OperatorAssistantStatementInput
                    {
                        Text = "Bounded evidence suggests near-term follow-up is needed.",
                        TruthLabel = OperatorAssistantTruthLabels.Inference,
                        TrustPercent = 73
                    },
                    WhatIsKnown =
                    [
                        new OperatorAssistantStatementInput
                        {
                            Text = "Active bounded scope includes one high-priority clarification item.",
                            TruthLabel = OperatorAssistantTruthLabels.Fact,
                            TrustPercent = 91,
                            EvidenceRefs = ["evidence:opint-006-c:1"]
                        }
                    ],
                    WhatItMeans =
                    [
                        new OperatorAssistantStatementInput
                        {
                            Text = "Unresolved clarification pressure remains elevated.",
                            TruthLabel = OperatorAssistantTruthLabels.Inference,
                            TrustPercent = 71
                        },
                        new OperatorAssistantStatementInput
                        {
                            Text = "Clearing this branch may reduce follow-up churn.",
                            TruthLabel = OperatorAssistantTruthLabels.Hypothesis,
                            TrustPercent = 59
                        }
                    ],
                    Recommendation = new OperatorAssistantStatementInput
                    {
                        Text = "Open in Web and inspect evidence before deciding.",
                        TruthLabel = OperatorAssistantTruthLabels.Recommendation,
                        TrustPercent = 69
                    },
                    TrustPercent = 69,
                    OpenInWebEnabled = true,
                    OpenInWebTargetApi = request.OpenInWebTargetApi,
                    OpenInWebScopeItemKey = _scopeItemKey,
                    OpenInWebActiveMode = request.OpenInWebActiveMode
                },
                generatedAtUtc);

            response.Guardrails.ScopeBounded = true;
            response.Guardrails.McpDependent = false;
            response.Guardrails.ReadModelBounded = true;
            response.Guardrails.ReadModelAudit =
            [
                new OperatorAssistantReadModelAuditEntry
                {
                    ReadModel = "resolution_queue",
                    Bounded = true,
                    TrackedPersonId = _trackedPersonId,
                    ScopeKey = _scopeKey,
                    ScopeItemKey = string.Empty,
                    RecordCount = 1,
                    OperatorSessionId = request.Session.OperatorSessionId,
                    ObservedAtUtc = generatedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow,
                    Notes = null
                }
            ];

            return Task.FromResult(response);
        }
    }

    private sealed class NoopOperatorSessionAuditService : IOperatorSessionAuditService
    {
        public Task<Guid> RecordSessionEventAsync(OperatorSessionAuditRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoopOperatorOfflineEventRepository : IOperatorOfflineEventRepository
    {
        public Task<OperatorOfflineEventRecord> CreateAsync(
            OperatorOfflineEventCreateRequest request,
            CancellationToken ct = default)
            => Task.FromResult(new OperatorOfflineEventRecord());

        public Task<OperatorOfflineEventRecord?> GetByIdAsync(
            Guid offlineEventId,
            CancellationToken ct = default)
            => Task.FromResult<OperatorOfflineEventRecord?>(null);

        public Task<OperatorOfflineEventRecord?> GetByIdWithinScopeAsync(
            Guid offlineEventId,
            string scopeKey,
            Guid trackedPersonId,
            CancellationToken ct = default)
            => Task.FromResult<OperatorOfflineEventRecord?>(null);

        public Task<OperatorOfflineEventQueryResult> QueryAsync(
            OperatorOfflineEventQueryRequest request,
            CancellationToken ct = default)
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

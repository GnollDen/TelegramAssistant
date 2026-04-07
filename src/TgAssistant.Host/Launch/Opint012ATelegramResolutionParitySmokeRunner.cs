using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint012ATelegramResolutionParitySmokeRunner
{
    public static async Task<Opint012ATelegramResolutionParitySmokeReport> RunAsync(
        string? outputPath = null,
        CancellationToken ct = default)
    {
        const long ownerUserId = 900012;
        var trackedPersonId = Guid.Parse("12121212-1212-1212-1212-121212121212");
        const string trackedPersonDisplayName = "OPINT-012-A Tracked Person";
        const string scopeKey = "chat:opint-012-a-smoke";
        const string scopeItemKey = "resolution:review:opint-012-a";
        var nowUtc = DateTime.UtcNow;

        var report = new Opint012ATelegramResolutionParitySmokeReport
        {
            GeneratedAtUtc = nowUtc,
            OutputPath = ResolveOutputPath(outputPath),
            ScopeKey = scopeKey,
            ScopeItemKey = scopeItemKey,
            TrackedPersonId = trackedPersonId
        };

        var sessionStore = new TelegramOperatorSessionStore();
        var resolutionService = new StubOperatorResolutionApplicationService(
            trackedPersonId,
            trackedPersonDisplayName,
            scopeKey,
            scopeItemKey);
        var workflow = new TelegramOperatorWorkflowService(
            Options.Create(new TelegramSettings { OwnerUserId = ownerUserId }),
            Options.Create(new WebSettings { Url = "https://operator.example.test", OperatorAccessToken = "opint-012-a-smoke-token" }),
            sessionStore,
            resolutionService,
            new OperatorAlertPolicyService(),
            new NoopOperatorAssistantResponseGenerationService(),
            new NoopAssistantContextAssemblyService(),
            new NoopOperatorOfflineEventRepository(),
            new OfflineEventClarificationPolicy(),
            new NoopOperatorSessionAuditService(),
            NullLogger<TelegramOperatorWorkflowService>.Instance);

        try
        {
            var startResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = ownerUserId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-012-A Operator",
                    MessageText = "/start"
                },
                ct);
            Ensure(startResponse.Messages.Count > 0, "OPINT-012-A smoke failed: /start did not produce a mode card.");

            var resolutionEntry = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = ownerUserId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-012-A Operator",
                    CallbackData = "mode:resolution",
                    CallbackQueryId = "opint-012-a-mode-resolution"
                },
                ct);
            Ensure(resolutionEntry.Messages.Count >= 2, "OPINT-012-A smoke failed: resolution queue did not render expected card output.");
            report.QueueHeaderText = resolutionEntry.Messages[0].Text;
            report.QueueCardText = resolutionEntry.Messages[1].Text;

            var cardButtons = FlattenButtons(resolutionEntry.Messages[1]);
            var evidenceButton = cardButtons.FirstOrDefault(button =>
                !string.IsNullOrWhiteSpace(button.CallbackData)
                && button.CallbackData.StartsWith("ra:evidence:", StringComparison.Ordinal)
                && button.Text.Contains("Факты", StringComparison.Ordinal));
            Ensure(evidenceButton != null && !string.IsNullOrWhiteSpace(evidenceButton.CallbackData),
                "OPINT-012-A smoke failed: resolution card omitted the evidence action callback.");

            var evidenceResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = ownerUserId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-012-A Operator",
                    CallbackData = evidenceButton!.CallbackData,
                    CallbackQueryId = "opint-012-a-evidence"
                },
                ct);

            Ensure(evidenceResponse.Messages.Count > 0, "OPINT-012-A smoke failed: evidence preview did not render.");
            var evidenceText = evidenceResponse.Messages[0].Text;
            report.EvidencePreviewText = evidenceText;

            Ensure(
                evidenceText.Contains("Интерпретация:", StringComparison.Ordinal)
                && evidenceText.Contains("[Inference] [74%]", StringComparison.Ordinal)
                && evidenceText.Contains("[Hypothesis]", StringComparison.Ordinal)
                && evidenceText.Contains("Рекомендация: [Recommendation] [63%]", StringComparison.Ordinal),
                "OPINT-012-A smoke failed: Telegram interpretation preview is not using display label/trust percent parity.");
            Ensure(
                !evidenceText.Contains("0.74", StringComparison.Ordinal)
                && !evidenceText.Contains("0.63", StringComparison.Ordinal)
                && !evidenceText.Contains("[]", StringComparison.Ordinal),
                "OPINT-012-A smoke failed: Telegram preview still exposes raw float trust or fabricated empty labels.");

            report.AllChecksPassed = true;
        }
        catch (Exception ex)
        {
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
            throw;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report.OutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(report.OutputPath, json, ct);
        }

        return report;
    }

    private static IReadOnlyList<TelegramOperatorButton> FlattenButtons(TelegramOperatorMessage message)
        => message.Buttons.SelectMany(row => row).ToList();

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-012-a-smoke-report.json"));
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
        private readonly Guid _trackedPersonId;
        private readonly string _trackedPersonDisplayName;
        private readonly string _scopeKey;
        private readonly string _scopeItemKey;

        public StubOperatorResolutionApplicationService(Guid trackedPersonId, string trackedPersonDisplayName, string scopeKey, string scopeItemKey)
        {
            _trackedPersonId = trackedPersonId;
            _trackedPersonDisplayName = trackedPersonDisplayName;
            _scopeKey = scopeKey;
            _scopeItemKey = scopeItemKey;
        }

        public Task<OperatorTrackedPersonQueryResult> QueryTrackedPersonsAsync(OperatorTrackedPersonQueryRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;

            var trackedPerson = new OperatorTrackedPersonScopeSummary
            {
                TrackedPersonId = _trackedPersonId,
                ScopeKey = _scopeKey,
                DisplayName = _trackedPersonDisplayName,
                EvidenceCount = 3,
                UpdatedAtUtc = DateTime.UtcNow
            };

            return Task.FromResult(new OperatorTrackedPersonQueryResult
            {
                Accepted = true,
                AutoSelected = true,
                SelectionSource = "auto_single",
                ActiveTrackedPersonId = _trackedPersonId,
                ActiveTrackedPerson = trackedPerson,
                Session = session,
                TrackedPersons = [trackedPerson]
            });
        }

        public Task<OperatorTrackedPersonSelectionResult> SelectTrackedPersonAsync(OperatorTrackedPersonSelectionRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;

            return Task.FromResult(new OperatorTrackedPersonSelectionResult
            {
                Accepted = true,
                ActiveTrackedPerson = new OperatorTrackedPersonScopeSummary
                {
                    TrackedPersonId = _trackedPersonId,
                    ScopeKey = _scopeKey,
                    DisplayName = _trackedPersonDisplayName,
                    EvidenceCount = 3,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                Session = session
            });
        }

        public Task<OperatorResolutionQueueQueryResult> GetResolutionQueueAsync(OperatorResolutionQueueQueryRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.ResolutionQueue;

            return Task.FromResult(new OperatorResolutionQueueQueryResult
            {
                Accepted = true,
                Session = session,
                Queue = new ResolutionQueueResult
                {
                    ScopeBound = true,
                    TrackedPersonId = _trackedPersonId,
                    ScopeKey = _scopeKey,
                    TrackedPersonDisplayName = _trackedPersonDisplayName,
                    TotalOpenCount = 1,
                    FilteredCount = 1,
                    Items =
                    [
                        new ResolutionItemSummary
                        {
                            ScopeItemKey = _scopeItemKey,
                            ItemType = ResolutionItemTypes.Review,
                            Title = "OPINT-012-A parity card",
                            Summary = "Validate Telegram display label and trust parity.",
                            WhyItMatters = "Parity must match upstream display contract.",
                            HumanShortTitle = "Parity check",
                            WhatHappened = "Interpretation generated mixed-confidence claims.",
                            WhyOperatorAnswerNeeded = "Operator must review bounded claims before action.",
                            WhatToDoPrompt = "Open facts and confirm label/trust rendering.",
                            EvidenceHint = "Three bounded facts are available.",
                            AffectedFamily = "resolution",
                            AffectedObjectRef = _scopeItemKey,
                            TrustFactor = 0.71f,
                            Status = ResolutionItemStatuses.AttentionRequired,
                            EvidenceCount = 3,
                            UpdatedAtUtc = DateTime.UtcNow,
                            Priority = ResolutionItemPriorities.High,
                            RecommendedNextAction = ResolutionActionTypes.Evidence,
                            AvailableActions =
                            [
                                ResolutionActionTypes.Evidence,
                                ResolutionActionTypes.Approve,
                                ResolutionActionTypes.OpenWeb
                            ]
                        }
                    ]
                }
            });
        }

        public Task<OperatorResolutionDetailQueryResult> GetResolutionDetailAsync(OperatorResolutionDetailQueryRequest request, CancellationToken ct = default)
        {
            var session = CloneSession(request.Session);
            session.ActiveTrackedPersonId = _trackedPersonId;
            session.ActiveMode = OperatorModeTypes.Evidence;
            session.ActiveScopeItemKey = _scopeItemKey;

            return Task.FromResult(new OperatorResolutionDetailQueryResult
            {
                Accepted = true,
                Session = session,
                Detail = new ResolutionDetailResult
                {
                    ScopeBound = true,
                    ItemFound = true,
                    Item = new ResolutionItemDetail
                    {
                        ScopeItemKey = _scopeItemKey,
                        ItemType = ResolutionItemTypes.Review,
                        Title = "OPINT-012-A parity card",
                        Summary = "Validate Telegram display label and trust parity.",
                        WhyItMatters = "Parity must match upstream display contract.",
                        AffectedFamily = "resolution",
                        AffectedObjectRef = _scopeItemKey,
                        TrustFactor = 0.71f,
                        Status = ResolutionItemStatuses.AttentionRequired,
                        EvidenceCount = 3,
                        UpdatedAtUtc = DateTime.UtcNow,
                        Priority = ResolutionItemPriorities.High,
                        InterpretationLoop = new ResolutionInterpretationLoopResult
                        {
                            Applied = true,
                            ContextSufficient = true,
                            InterpretationSummary = "Interpretation produces bounded uncertainty labels.",
                            KeyClaims =
                            [
                                new ResolutionInterpretationClaim
                                {
                                    ClaimType = ResolutionInterpretationClaimTypes.Inference,
                                    DisplayLabel = OperatorAssistantTruthLabels.Inference,
                                    TrustPercent = 74,
                                    Summary = "Likely mismatch is limited to display formatting.",
                                    EvidenceRefs = ["evidence:opint-012-a:1"]
                                },
                                new ResolutionInterpretationClaim
                                {
                                    ClaimType = ResolutionInterpretationClaimTypes.Hypothesis,
                                    DisplayLabel = OperatorAssistantTruthLabels.Hypothesis,
                                    TrustPercent = null,
                                    Summary = "A future rendering path could diverge if not locked now.",
                                    EvidenceRefs = ["evidence:opint-012-a:2"]
                                }
                            ],
                            ReviewRecommendation = new ResolutionInterpretationReviewRecommendation
                            {
                                Decision = ResolutionInterpretationReviewRecommendations.Review,
                                DisplayLabel = OperatorAssistantTruthLabels.Recommendation,
                                TrustPercent = 63,
                                Reason = "Confirm Telegram uses upstream label and trust_percent fields as-is."
                            }
                        },
                        Evidence =
                        [
                            new ResolutionEvidenceSummary
                            {
                                EvidenceItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                                ObservedAtUtc = DateTime.UtcNow.AddHours(-3),
                                SourceRef = "900012:1",
                                SourceLabel = "Realtime message 1 in chat 900012",
                                Summary = "Evidence proving confidence-to-percent mapping is bounded.",
                                TrustFactor = 0.74f
                            },
                            new ResolutionEvidenceSummary
                            {
                                EvidenceItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                                ObservedAtUtc = DateTime.UtcNow.AddHours(-2),
                                SourceRef = "900012:2",
                                SourceLabel = "Realtime message 2 in chat 900012",
                                Summary = "Evidence where confidence is absent and percent must be omitted.",
                                TrustFactor = 0.41f
                            }
                        ]
                    }
                }
            });
        }

        public Task<OperatorResolutionActionResultEnvelope> SubmitResolutionActionAsync(ResolutionActionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not submit resolution actions.");

        public Task<OperatorPersonWorkspaceListQueryResult> QueryPersonWorkspaceListAsync(OperatorPersonWorkspaceListQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace list.");

        public Task<OperatorPersonWorkspaceSummaryQueryResult> QueryPersonWorkspaceSummaryAsync(OperatorPersonWorkspaceSummaryQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace summary.");

        public Task<OperatorPersonWorkspaceDossierQueryResult> QueryPersonWorkspaceDossierAsync(OperatorPersonWorkspaceDossierQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace dossier.");

        public Task<OperatorPersonWorkspaceProfileQueryResult> QueryPersonWorkspaceProfileAsync(OperatorPersonWorkspaceProfileQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace profile.");

        public Task<OperatorPersonWorkspacePairDynamicsQueryResult> QueryPersonWorkspacePairDynamicsAsync(OperatorPersonWorkspacePairDynamicsQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace pair dynamics.");

        public Task<OperatorPersonWorkspaceTimelineQueryResult> QueryPersonWorkspaceTimelineAsync(OperatorPersonWorkspaceTimelineQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace timeline.");

        public Task<OperatorPersonWorkspaceEvidenceQueryResult> QueryPersonWorkspaceEvidenceAsync(OperatorPersonWorkspaceEvidenceQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace evidence.");

        public Task<OperatorPersonWorkspaceRevisionsQueryResult> QueryPersonWorkspaceRevisionsAsync(OperatorPersonWorkspaceRevisionsQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace revisions.");

        public Task<OperatorPersonWorkspaceResolutionQueryResult> QueryPersonWorkspaceResolutionAsync(OperatorPersonWorkspaceResolutionQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query person workspace resolution.");

        public Task<OperatorConflictResolutionSessionResultEnvelope> StartConflictResolutionSessionAsync(OperatorConflictResolutionSessionStartRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not start AI conflict sessions.");

        public Task<OperatorConflictResolutionSessionResultEnvelope> RespondConflictResolutionSessionAsync(OperatorConflictResolutionSessionRespondRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not respond to AI conflict sessions.");

        public Task<OperatorConflictResolutionSessionResultEnvelope> QueryConflictResolutionSessionAsync(OperatorConflictResolutionSessionQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query AI conflict sessions.");

        public Task<OperatorOfflineEventQueryApiResult> QueryOfflineEventsAsync(OperatorOfflineEventQueryApiRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query offline events.");

        public Task<OperatorOfflineEventDetailQueryResultEnvelope> GetOfflineEventDetailAsync(OperatorOfflineEventDetailQueryRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not query offline-event detail.");

        public Task<OperatorOfflineEventRefinementResult> SubmitOfflineEventRefinementAsync(OperatorOfflineEventRefinementRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not refine offline events.");

        public Task<OperatorOfflineEventTimelineLinkageUpdateResult> SubmitOfflineEventTimelineLinkageUpdateAsync(OperatorOfflineEventTimelineLinkageUpdateRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not update offline-event linkage.");

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

    private sealed class NoopOperatorAssistantResponseGenerationService : IOperatorAssistantResponseGenerationService
    {
        public OperatorAssistantResponseEnvelope BuildResponse(OperatorAssistantResponseGenerationRequest request, DateTime? generatedAtUtc = null)
            => throw new NotSupportedException("OPINT-012-A smoke should not generate assistant responses.");

        public IReadOnlyList<string> Validate(OperatorAssistantResponseEnvelope response)
            => throw new NotSupportedException("OPINT-012-A smoke should not validate assistant responses.");

        public string RenderTelegram(OperatorAssistantResponseEnvelope response)
            => throw new NotSupportedException("OPINT-012-A smoke should not render assistant responses.");
    }

    private sealed class NoopAssistantContextAssemblyService : IOperatorAssistantContextAssemblyService
    {
        public Task<OperatorAssistantResponseEnvelope> BuildBoundedResponseAsync(
            OperatorAssistantContextAssemblyRequest request,
            DateTime? generatedAtUtc = null,
            CancellationToken ct = default)
            => throw new NotSupportedException("OPINT-012-A smoke should not assemble assistant context.");
    }

    private sealed class NoopOperatorSessionAuditService : IOperatorSessionAuditService
    {
        public Task<Guid> RecordSessionEventAsync(OperatorSessionAuditRequest request, CancellationToken ct = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoopOperatorOfflineEventRepository : IOperatorOfflineEventRepository
    {
        public Task<OperatorOfflineEventRecord> UpsertDraftAsync(OperatorOfflineEventDraftUpsertRequest request, CancellationToken ct = default)
            => Task.FromResult(new OperatorOfflineEventRecord());

        public Task<OperatorOfflineEventRecord> SaveFinalAsync(OperatorOfflineEventFinalSaveRequest request, CancellationToken ct = default)
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

public sealed class Opint012ATelegramResolutionParitySmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string QueueHeaderText { get; set; } = string.Empty;
    public string QueueCardText { get; set; } = string.Empty;
    public string EvidencePreviewText { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class AiConflictResolutionSessionV1ProofRunner
{
    private const string ScopeKey = "chat:885574984";
    private const string OperatorId = "ai-conflict-v1-proof";
    private const string OperatorDisplay = "AI Conflict V1 Proof";
    private const string SurfaceSubject = "validation";
    private const string AuthSource = "local_runtime_validation";
    private static readonly string[] RequiredAuditKeys =
    [
        "context_manifest",
        "retrieval_requests",
        "retrieval_results",
        "model_id",
        "model_version",
        "prompt_tokens",
        "completion_tokens",
        "total_tokens",
        "cost_usd",
        "normalization_status",
        "gate_decision"
    ];

    public static async Task<AiConflictResolutionSessionV1ProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new AiConflictResolutionSessionV1ProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            ScopeKey = ScopeKey
        };

        Exception? fatal = null;
        try
        {
            var dbFactory = services.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
            var appService = services.GetRequiredService<IOperatorResolutionApplicationService>();

            var nowUtc = DateTime.UtcNow;
            var sessionId = $"web:ai-conflict-v1-proof:{Guid.NewGuid():N}";
            var identity = BuildIdentity(nowUtc);
            var session = BuildSession(sessionId, nowUtc, nowUtc.AddMinutes(20));

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var trackedPerson = await db.Persons
                .AsNoTracking()
                .Where(x => x.ScopeKey == ScopeKey
                    && x.Status == "active"
                    && x.PersonType == "tracked_person")
                .OrderByDescending(x => x.UpdatedAt)
                .Select(x => new { x.Id, x.DisplayName })
                .FirstOrDefaultAsync(ct);
            Ensure(trackedPerson != null, $"Active tracked person was not found for scope '{ScopeKey}'.");

            report.TrackedPersonId = trackedPerson!.Id;
            report.TrackedPersonDisplayName = trackedPerson.DisplayName;

            var query = await appService.QueryTrackedPersonsAsync(
                new OperatorTrackedPersonQueryRequest
                {
                    OperatorIdentity = identity,
                    Session = session,
                    PreferredTrackedPersonId = trackedPerson.Id,
                    Limit = 50
                },
                ct);
            Ensure(query.Accepted, $"Tracked-person query failed: {query.FailureReason ?? "unknown"}");

            var selection = await appService.SelectTrackedPersonAsync(
                new OperatorTrackedPersonSelectionRequest
                {
                    OperatorIdentity = identity,
                    Session = query.Session,
                    TrackedPersonId = trackedPerson.Id,
                    RequestedAtUtc = nowUtc
                },
                ct);
            Ensure(selection.Accepted, $"Tracked-person selection failed: {selection.FailureReason ?? "unknown"}");

            var queue = await appService.GetResolutionQueueAsync(
                new OperatorResolutionQueueQueryRequest
                {
                    OperatorIdentity = identity,
                    Session = selection.Session,
                    TrackedPersonId = trackedPerson.Id,
                    ItemTypes = [ResolutionItemTypes.Contradiction, ResolutionItemTypes.Review],
                    SortBy = ResolutionQueueSortFields.UpdatedAt,
                    SortDirection = ResolutionSortDirections.Desc,
                    Limit = 50
                },
                ct);
            Ensure(queue.Accepted, $"Resolution queue query failed: {queue.FailureReason ?? "unknown"}");

            var candidateDetails = new List<(ResolutionItemDetail Detail, OperatorSessionContext Session)>();
            foreach (var summary in queue.Queue.Items)
            {
                var detail = await appService.GetResolutionDetailAsync(
                    new OperatorResolutionDetailQueryRequest
                    {
                        OperatorIdentity = identity,
                        Session = queue.Session,
                        TrackedPersonId = trackedPerson.Id,
                        ScopeItemKey = summary.ScopeItemKey,
                        EvidenceLimit = 8,
                        EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                        EvidenceSortDirection = ResolutionSortDirections.Desc
                    },
                    ct);
                if (!detail.Accepted || !detail.Detail.ItemFound || detail.Detail.Item == null)
                {
                    continue;
                }

                if (!IsEligible(detail.Detail.Item))
                {
                    continue;
                }

                candidateDetails.Add((detail.Detail.Item, detail.Session));
            }

            Ensure(candidateDetails.Count > 0, "No eligible contradiction/review item was found for AI conflict session V1 proof.");

            Exception? lastCandidateError = null;
            var sessionContractSatisfied = false;
            foreach (var candidate in candidateDetails)
            {
                var candidateAttempt = new AiConflictResolutionSessionV1CandidateAttempt
                {
                    ScopeItemKey = candidate.Detail.ScopeItemKey,
                    ItemType = candidate.Detail.ItemType,
                    SourceKind = candidate.Detail.SourceKind
                };
                report.CandidateAttempts.Add(candidateAttempt);

                try
                {
                    var started = await appService.StartConflictResolutionSessionAsync(
                        new OperatorConflictResolutionSessionStartRequest
                        {
                            RequestId = $"ai-conflict-session-proof-start-{Guid.NewGuid():N}",
                            OperatorIdentity = identity,
                            Session = candidate.Session,
                            TrackedPersonId = trackedPerson.Id,
                            ScopeItemKey = candidate.Detail.ScopeItemKey
                        },
                        ct);
                    Ensure(started.Accepted && started.ConflictSession != null, $"Conflict-session start failed: {started.FailureReason ?? "unknown"}");

                    candidateAttempt.StartAccepted = true;
                    var finalEnvelope = started;
                    if (string.Equals(started.ConflictSession.State, ResolutionConflictSessionStates.AwaitingOperatorAnswer, StringComparison.Ordinal)
                        && started.ConflictSession.OperatorQuestion != null)
                    {
                        var responded = await appService.RespondConflictResolutionSessionAsync(
                            new OperatorConflictResolutionSessionRespondRequest
                            {
                                RequestId = $"ai-conflict-session-proof-respond-{Guid.NewGuid():N}",
                                OperatorIdentity = identity,
                                Session = started.Session,
                                ConflictSessionId = started.ConflictSession.ConflictSessionId,
                                QuestionKey = started.ConflictSession.OperatorQuestion.QuestionKey,
                                AnswerValue = "Подтверждаю, что нужна аккуратная нормализация и ручная проверка.",
                                AnswerKind = "free_text"
                            },
                            ct);
                        Ensure(responded.Accepted && responded.ConflictSession != null, $"Conflict-session respond failed: {responded.FailureReason ?? "unknown"}");
                        finalEnvelope = responded;
                        candidateAttempt.OperatorAnswerCaptured = true;
                    }

                    Ensure(finalEnvelope.ConflictSession != null, "Final conflict-session payload is empty.");
                    Ensure(finalEnvelope.ConflictSession.FinalVerdict != null, "Final verdict was not produced.");
                    Ensure(finalEnvelope.ConflictSession.AuditTrail.Count > 0, "Conflict-session audit trail is empty.");
                    EnsureAuditContract(finalEnvelope.ConflictSession.AuditTrail);
                    EnsureStructuredVerdictProofRows(report, finalEnvelope.ConflictSession.FinalVerdict);
                    EnsureToolContractProofRows(report, finalEnvelope.ConflictSession.ScopeItemKey);
                    EnsureFallbackMappingProofRows(report);
                    await EnsureApiSurfaceProofRowsAsync(
                        report,
                        dbFactory,
                        appService,
                        identity,
                        finalEnvelope.Session,
                        trackedPerson.Id,
                        finalEnvelope.ConflictSession.ScopeItemKey,
                        candidate.Detail.ItemType,
                        candidate.Detail.SourceKind,
                        ct);

                    if (!sessionContractSatisfied)
                    {
                        report.ScopeItemKey = candidate.Detail.ScopeItemKey;
                        report.ItemType = candidate.Detail.ItemType;
                        report.SourceKind = candidate.Detail.SourceKind;
                        report.StartAccepted = true;
                        report.ConflictSessionId = started.ConflictSession!.ConflictSessionId;
                        report.InitialCasePacket = started.ConflictSession.InitialCasePacket;
                        report.StartState = started.ConflictSession.State;
                        report.StartStateReason = started.ConflictSession.StateReason;
                        report.AuditEntriesAfterStart = started.ConflictSession.AuditTrail.Count;
                        report.OperatorQuestion = started.ConflictSession.OperatorQuestion;
                        report.OperatorAnswerCaptured = candidateAttempt.OperatorAnswerCaptured;
                        report.OperatorAnswer = finalEnvelope.ConflictSession.OperatorAnswer;
                        report.FinalState = finalEnvelope.ConflictSession.State;
                        report.FinalVerdict = finalEnvelope.ConflictSession.FinalVerdict;
                        report.AuditEntriesFinal = finalEnvelope.ConflictSession.AuditTrail.Count;
                        report.RequiredAuditKeysPresent = true;
                        sessionContractSatisfied = true;
                    }

                    var proposal = finalEnvelope.ConflictSession.FinalVerdict.NormalizationProposal;
                    var action = await appService.SubmitResolutionActionAsync(
                        new ResolutionActionRequest
                        {
                            RequestId = $"ai-conflict-session-proof-apply-{Guid.NewGuid():N}",
                            OperatorIdentity = identity,
                            Session = finalEnvelope.Session,
                            TrackedPersonId = trackedPerson.Id,
                            ScopeItemKey = finalEnvelope.ConflictSession.ScopeItemKey,
                            ActionType = proposal.RecommendedAction,
                            Explanation = proposal.Explanation,
                            ClarificationPayload = proposal.ClarificationPayload,
                            ConflictResolutionSessionId = finalEnvelope.ConflictSession.ConflictSessionId,
                            ConflictVerdictRevision = finalEnvelope.ConflictSession.Revision,
                            ConflictVerdict = finalEnvelope.ConflictSession.FinalVerdict,
                            SubmittedAtUtc = DateTime.UtcNow
                        },
                        ct);
                    Ensure(action.Accepted && action.Action.Accepted, $"Action apply via existing path failed: {action.FailureReason ?? action.Action.FailureReason ?? "unknown"}");

                    await using var verifyDb = await dbFactory.CreateDbContextAsync(ct);
                    var actionRow = await verifyDb.OperatorResolutionActions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == action.Action.ActionId, ct);
                    Ensure(actionRow != null, "Applied action row was not persisted.");
                    var sessionRow = await verifyDb.OperatorResolutionConflictSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == finalEnvelope.ConflictSession.ConflictSessionId, ct);
                    Ensure(sessionRow != null, "Conflict-session row was not persisted.");

                    candidateAttempt.ApplyAccepted = true;
                    candidateAttempt.Passed = true;
                    report.ApplyAccepted = true;
                    report.ApplyActionId = action.Action.ActionId;
                    report.ApplyActionType = action.Action.ActionType;
                    report.ApplyConflictSessionId = action.Action.ConflictResolutionSessionId;
                    report.StoredActionConflictLinkPresent = actionRow!.ConflictResolutionSessionId.HasValue;
                    report.StoredActionConflictRevision = actionRow.ConflictVerdictRevision;
                    report.StoredSessionStatus = sessionRow!.Status;
                    report.StoredSessionFinalActionId = sessionRow.FinalActionId;
                    report.StoredSessionFinalActionRequestId = sessionRow.FinalActionRequestId;
                    report.DeterministicApplyPathConfirmed = report.StoredActionConflictLinkPresent
                        && sessionRow.FinalActionId == action.Action.ActionId
                        && string.Equals(sessionRow.Status, ResolutionConflictSessionStates.HandedOff, StringComparison.Ordinal);
                    break;
                }
                catch (Exception candidateEx)
                {
                    lastCandidateError = candidateEx;
                    candidateAttempt.Passed = false;
                    candidateAttempt.FailureReason = candidateEx.Message;
                }
            }

            if (!sessionContractSatisfied)
            {
                throw new InvalidOperationException(
                    "No eligible queue item completed conflict-session proof session/audit contract.",
                    lastCandidateError);
            }

            if (!report.DeterministicApplyPathConfirmed && lastCandidateError != null)
            {
                report.ApplyPathNonBlockingFailureReason = lastCandidateError.Message;
            }

            report.Passed = report.StartAccepted
                && report.FinalVerdict != null
                && report.RequiredAuditKeysPresent
                && report.StructuredVerdictProofRows.All(x => x.Passed)
                && report.ToolProofRows.All(x => x.Passed)
                && report.ApiSurfaceProofRows.All(x => x.Passed)
                && report.FallbackMappingProofRows.All(x => x.Passed);
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.Passed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.Passed)
        {
            throw new InvalidOperationException(
                "AI conflict resolution session V1 bounded proof failed.",
                fatal);
        }

        return report;
    }

    private static bool IsEligible(ResolutionItemDetail item)
    {
        if (string.Equals(item.ItemType, ResolutionItemTypes.Contradiction, StringComparison.Ordinal))
        {
            return true;
        }

        return string.Equals(item.ItemType, ResolutionItemTypes.Review, StringComparison.Ordinal)
            && string.Equals(item.SourceKind, "durable_object_metadata", StringComparison.Ordinal);
    }

    private static OperatorIdentityContext BuildIdentity(DateTime authTimeUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = OperatorId,
            OperatorDisplay = OperatorDisplay,
            SurfaceSubject = SurfaceSubject,
            AuthSource = AuthSource,
            AuthTimeUtc = authTimeUtc
        };
    }

    private static OperatorSessionContext BuildSession(string sessionId, DateTime authTimeUtc, DateTime expiresAtUtc)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = sessionId,
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = authTimeUtc,
            LastSeenAtUtc = authTimeUtc,
            ExpiresAtUtc = expiresAtUtc,
            ActiveMode = OperatorModeTypes.ResolutionQueue
        };
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath.Trim());
        }

        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "TgAssistant.Host",
            "artifacts",
            "resolution-interpretation-loop",
            "ai-conflict-resolution-session-v1-proof.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void EnsureAuditContract(IEnumerable<ResolutionInterpretationAuditEntry> auditTrail)
    {
        foreach (var entry in auditTrail)
        {
            var json = JsonSerializer.SerializeToElement(entry);
            foreach (var key in RequiredAuditKeys)
            {
                Ensure(json.TryGetProperty(key, out _), $"Audit row is missing required key '{key}'.");
            }
        }
    }

    private static void EnsureStructuredVerdictProofRows(
        AiConflictResolutionSessionV1ProofReport report,
        ResolutionConflictSessionVerdict verdict)
    {
        var structured = verdict.StructuredVerdict
            ?? throw new InvalidOperationException("Final verdict is missing structured_verdict.");

        report.StructuredVerdictProofRows =
        [
            BuildStructuredVerdictRow(
                caseId: "structured_verdict_valid",
                expectedDecision: "accept",
                candidate: structured),
            BuildStructuredVerdictRow(
                caseId: "structured_verdict_invalid_decision_rejected",
                expectedDecision: "reject",
                candidate: CloneStructuredVerdict(structured, decision: "invalid_decision")),
            BuildStructuredVerdictRow(
                caseId: "structured_verdict_invalid_publication_state_rejected",
                expectedDecision: "reject",
                candidate: CloneStructuredVerdict(structured, publicationState: "invalid_publication_state")),
            BuildStructuredVerdictRow(
                caseId: "structured_verdict_missing_scope_item_key_rejected",
                expectedDecision: "reject",
                candidate: CloneStructuredVerdict(structured, scopeItemKey: ""))
        ];

        if (report.StructuredVerdictProofRows.Any(x => !x.Passed))
        {
            throw new InvalidOperationException("Structured verdict contract proof rows include failures.");
        }
    }

    private static AiConflictResolutionStructuredVerdictProofRow BuildStructuredVerdictRow(
        string caseId,
        string expectedDecision,
        ConflictResolutionStructuredVerdict candidate)
    {
        var accepted = ConflictResolutionStructuredVerdictContract.TryValidate(candidate, out var reason);
        var actualDecision = accepted ? "accept" : "reject";
        return new AiConflictResolutionStructuredVerdictProofRow
        {
            CaseId = caseId,
            ExpectedDecision = expectedDecision,
            ActualDecision = actualDecision,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
            Passed = string.Equals(expectedDecision, actualDecision, StringComparison.Ordinal)
        };
    }

    private static ConflictResolutionStructuredVerdict CloneStructuredVerdict(
        ConflictResolutionStructuredVerdict source,
        string? decision = null,
        string? publicationState = null,
        string? scopeItemKey = null)
    {
        return new ConflictResolutionStructuredVerdict
        {
            VerdictId = source.VerdictId,
            ScopeKey = source.ScopeKey,
            ScopeItemKey = scopeItemKey ?? source.ScopeItemKey,
            CarryForwardCaseId = source.CarryForwardCaseId,
            Decision = decision ?? source.Decision,
            PublicationState = publicationState ?? source.PublicationState,
            ClaimRows = source.ClaimRows
                .Select(row => new ConflictResolutionStructuredClaimRow
                {
                    ClaimType = row.ClaimType,
                    Summary = row.Summary,
                    EvidenceRefs = [.. row.EvidenceRefs]
                })
                .ToList(),
            UncertaintyRows = [.. source.UncertaintyRows],
            NormalizationPlan = new ConflictResolutionStructuredNormalizationPlan
            {
                RecommendedAction = source.NormalizationPlan.RecommendedAction,
                Explanation = source.NormalizationPlan.Explanation,
                ClarificationPayload = source.NormalizationPlan.ClarificationPayload
            },
            EvidenceRefs = [.. source.EvidenceRefs],
            CreatedAtUtc = source.CreatedAtUtc
        };
    }

    private static void EnsureToolContractProofRows(
        AiConflictResolutionSessionV1ProofReport report,
        string scopeItemKey)
    {
        report.ToolProofRows =
        [
            BuildToolProofRow(
                caseId: "allowed_tool_get_evidence_refs_accepted",
                expectedDecision: ConflictResolutionSessionToolDecisions.Accepted,
                request: new ResolutionConflictSessionToolRequest
                {
                    ToolName = ConflictResolutionSessionToolNames.GetEvidenceRefs,
                    RequestScope = scopeItemKey,
                    RequestItems = []
                },
                scopeItemKey: scopeItemKey),
            BuildToolProofRow(
                caseId: "tool_not_allowed_rejected",
                expectedDecision: ConflictResolutionSessionToolDecisions.ToolNotAllowed,
                request: new ResolutionConflictSessionToolRequest
                {
                    ToolName = "raw_sql_tool",
                    RequestScope = scopeItemKey,
                    RequestItems = ["select * from messages"]
                },
                scopeItemKey: scopeItemKey),
            BuildToolProofRow(
                caseId: "cross_scope_tool_request_rejected",
                expectedDecision: ConflictResolutionSessionToolDecisions.CrossScopeToolRequestRejected,
                request: new ResolutionConflictSessionToolRequest
                {
                    ToolName = ConflictResolutionSessionToolNames.GetNeighborMessages,
                    RequestScope = "scope:other",
                    RequestItems = ["885574984:293950"]
                },
                scopeItemKey: scopeItemKey),
            BuildToolProofRow(
                caseId: "followup_budget_exceeded_rejected",
                expectedDecision: ConflictResolutionSessionToolDecisions.FollowUpBudgetExceededRejected,
                request: new ResolutionConflictSessionToolRequest
                {
                    ToolName = ConflictResolutionSessionToolNames.AskOperatorQuestion,
                    RequestScope = scopeItemKey,
                    RequestItems = ["Can you confirm this contradiction?"]
                },
                scopeItemKey: scopeItemKey,
                usedQuestionCount: ConflictResolutionSessionToolContract.MaxFollowUpQuestions,
                usedAnswerCount: ConflictResolutionSessionToolContract.MaxFollowUpAnswers)
        ];

        if (report.ToolProofRows.Any(x => !x.Passed))
        {
            throw new InvalidOperationException("Conflict-session tool contract proof rows include failures.");
        }
    }

    private static void EnsureFallbackMappingProofRows(AiConflictResolutionSessionV1ProofReport report)
    {
        report.FallbackMappingProofRows =
        [
            BuildFallbackMappingRow(
                caseId: "schema_invalid_to_manual_review",
                violationReason: ConflictResolutionSessionVerdictViolationReasons.SchemaInvalid,
                expectedFallbackReason: ConflictResolutionSessionFallbackReasons.ManualReviewRequired,
                expectedPublicationState: ConflictResolutionStructuredPublicationStates.ManualReviewRequired),
            BuildFallbackMappingRow(
                caseId: "budget_exceeded_to_escalation_only",
                violationReason: ConflictResolutionSessionVerdictViolationReasons.BudgetExceeded,
                expectedFallbackReason: ConflictResolutionSessionFallbackReasons.EscalationOnly,
                expectedPublicationState: ConflictResolutionStructuredPublicationStates.EscalationOnly),
            BuildFallbackMappingRow(
                caseId: "scope_rejected_to_scope_rejected",
                violationReason: ConflictResolutionSessionVerdictViolationReasons.ScopeRejected,
                expectedFallbackReason: ConflictResolutionSessionFallbackReasons.ScopeRejected,
                expectedPublicationState: ConflictResolutionStructuredPublicationStates.ManualReviewRequired),
            BuildFallbackMappingRow(
                caseId: "publication_honesty_block_to_insufficient_evidence",
                violationReason: ConflictResolutionSessionVerdictViolationReasons.PublicationHonestyBlock,
                expectedFallbackReason: ConflictResolutionSessionFallbackReasons.InsufficientEvidence,
                expectedPublicationState: ConflictResolutionStructuredPublicationStates.InsufficientEvidence)
        ];

        if (report.FallbackMappingProofRows.Any(x => !x.Passed))
        {
            throw new InvalidOperationException("Conflict-session fallback mapping proof rows include failures.");
        }
    }

    private static async Task EnsureApiSurfaceProofRowsAsync(
        AiConflictResolutionSessionV1ProofReport report,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IOperatorResolutionApplicationService appService,
        OperatorIdentityContext identity,
        OperatorSessionContext session,
        Guid trackedPersonId,
        string scopeItemKey,
        string itemType,
        string sourceKind,
        CancellationToken ct)
    {
        var fixtures = new[]
        {
            new ApiSurfaceFixture(
                "query_surface_fallback_manual_review",
                ResolutionConflictSessionStates.Fallback,
                ConflictResolutionSessionFallbackReasons.ManualReviewRequired,
                ConflictResolutionStructuredPublicationStates.ManualReviewRequired,
                ConflictResolutionStructuredDecisionTypes.Defer),
            new ApiSurfaceFixture(
                "query_surface_needs_web_review_insufficient_evidence",
                ResolutionConflictSessionStates.NeedsWebReview,
                ConflictResolutionSessionFallbackReasons.InsufficientEvidence,
                ConflictResolutionStructuredPublicationStates.InsufficientEvidence,
                ConflictResolutionStructuredDecisionTypes.Defer)
        };

        var nowUtc = DateTime.UtcNow;
        var fixtureRows = new List<DbOperatorResolutionConflictSession>();
        try
        {
            await using (var writeDb = await dbFactory.CreateDbContextAsync(ct))
            {
                foreach (var fixture in fixtures)
                {
                    var verdict = BuildApiSurfaceFixtureVerdict(
                        scopeItemKey,
                        nowUtc,
                        fixture.ExpectedState,
                        fixture.ExpectedStateReason,
                        fixture.ExpectedPublicationState,
                        fixture.ExpectedDecision);
                    var row = new DbOperatorResolutionConflictSession
                    {
                        Id = Guid.NewGuid(),
                        RequestId = $"ai-conflict-v1-proof:surface:{fixture.CaseId}:{Guid.NewGuid():N}",
                        ScopeKey = ScopeKey,
                        TrackedPersonId = trackedPersonId,
                        ScopeItemKey = scopeItemKey,
                        ItemType = itemType,
                        SourceKind = sourceKind,
                        SourceRef = $"proof:{fixture.CaseId}",
                        OperatorId = identity.OperatorId,
                        OperatorSessionId = session.OperatorSessionId,
                        Surface = OperatorSurfaceTypes.Web,
                        Status = fixture.ExpectedState,
                        StateReason = fixture.ExpectedStateReason,
                        Revision = 1,
                        QuestionCount = 0,
                        AnswerCount = 0,
                        ModelCallCount = 1,
                        CasePacketJson = JsonSerializer.Serialize(new ResolutionConflictSessionCasePacket
                        {
                            Item = new ResolutionItemDetail
                            {
                                ScopeItemKey = scopeItemKey,
                                ItemType = itemType,
                                SourceKind = sourceKind,
                                SourceRef = $"proof:{fixture.CaseId}"
                            },
                            Evidence = [],
                            Notes = [],
                            DurableContextSummaries = []
                        }),
                        VerdictJson = JsonSerializer.Serialize(verdict),
                        NormalizationProposalJson = JsonSerializer.Serialize(verdict.NormalizationProposal),
                        AuditTrailJson = "[]",
                        StartedAtUtc = nowUtc.AddMinutes(-1),
                        ExpiresAtUtc = nowUtc.AddMinutes(20),
                        UpdatedAtUtc = nowUtc,
                        CompletedAtUtc = nowUtc
                    };

                    fixtureRows.Add(row);
                    writeDb.OperatorResolutionConflictSessions.Add(row);
                }

                await writeDb.SaveChangesAsync(ct);
            }

            var querySession = new OperatorSessionContext
            {
                OperatorSessionId = session.OperatorSessionId,
                Surface = session.Surface,
                AuthenticatedAtUtc = session.AuthenticatedAtUtc,
                LastSeenAtUtc = nowUtc,
                ExpiresAtUtc = session.ExpiresAtUtc,
                ActiveMode = OperatorModeTypes.ResolutionDetail,
                ActiveTrackedPersonId = trackedPersonId,
                ActiveScopeItemKey = scopeItemKey
            };

            foreach (var fixture in fixtures)
            {
                var row = fixtureRows.First(x => x.StateReason == fixture.ExpectedStateReason && x.Status == fixture.ExpectedState);
                var queryResult = await appService.QueryConflictResolutionSessionAsync(
                    new OperatorConflictResolutionSessionQueryRequest
                    {
                        OperatorIdentity = identity,
                        Session = querySession,
                        ConflictSessionId = row.Id
                    },
                    ct);

                var actualState = queryResult.ConflictSession?.State;
                var actualStateReason = queryResult.ConflictSession?.StateReason;
                var actualPublicationState = queryResult.ConflictSession?.FinalVerdict?.StructuredVerdict?.PublicationState;
                var passed = queryResult.Accepted
                    && queryResult.ConflictSession != null
                    && string.Equals(fixture.ExpectedState, actualState, StringComparison.Ordinal)
                    && string.Equals(fixture.ExpectedStateReason, actualStateReason, StringComparison.Ordinal)
                    && string.Equals(fixture.ExpectedPublicationState, actualPublicationState, StringComparison.Ordinal);

                report.ApiSurfaceProofRows.Add(new AiConflictResolutionApiSurfaceProofRow
                {
                    CaseId = fixture.CaseId,
                    ExpectedState = fixture.ExpectedState,
                    ActualState = actualState ?? string.Empty,
                    ExpectedStateReason = fixture.ExpectedStateReason,
                    ActualStateReason = actualStateReason ?? string.Empty,
                    ExpectedPublicationState = fixture.ExpectedPublicationState,
                    ActualPublicationState = actualPublicationState ?? string.Empty,
                    Passed = passed,
                    FailureReason = passed ? null : queryResult.FailureReason ?? "query_surface_contract_mismatch"
                });
            }
        }
        finally
        {
            if (fixtureRows.Count > 0)
            {
                await using var cleanupDb = await dbFactory.CreateDbContextAsync(ct);
                var ids = fixtureRows.Select(x => x.Id).ToList();
                if (ids.Count > 0)
                {
                    var rows = await cleanupDb.OperatorResolutionConflictSessions
                        .Where(x => ids.Contains(x.Id))
                        .ToListAsync(ct);
                    if (rows.Count > 0)
                    {
                        cleanupDb.OperatorResolutionConflictSessions.RemoveRange(rows);
                        await cleanupDb.SaveChangesAsync(ct);
                    }
                }
            }
        }

        if (report.ApiSurfaceProofRows.Any(x => !x.Passed))
        {
            throw new InvalidOperationException("Conflict-session API surface proof rows include failures.");
        }
    }

    private static ResolutionConflictSessionVerdict BuildApiSurfaceFixtureVerdict(
        string scopeItemKey,
        DateTime nowUtc,
        string state,
        string fallbackReason,
        string publicationState,
        string decision)
    {
        var verdict = new ResolutionConflictSessionVerdict
        {
            ResolutionVerdict = state,
            RemainingUncertainties =
            [
                $"AI conflict session returned deterministic fallback: {fallbackReason}."
            ],
            NormalizationProposal = new ResolutionConflictNormalizationProposal
            {
                RecommendedAction = ResolutionActionTypes.Clarify,
                Explanation = $"Fallback: {fallbackReason}"
            },
            ConfidenceCalibration = new ResolutionConflictConfidenceCalibration
            {
                ConfidenceScore = 0f,
                Rationale = "fallback"
            }
        };
        verdict.StructuredVerdict = new ConflictResolutionStructuredVerdict
        {
            VerdictId = Guid.NewGuid().ToString("N"),
            ScopeKey = ScopeKey,
            ScopeItemKey = scopeItemKey,
            CarryForwardCaseId = null,
            Decision = decision,
            PublicationState = publicationState,
            ClaimRows = [],
            UncertaintyRows = [.. verdict.RemainingUncertainties],
            NormalizationPlan = new ConflictResolutionStructuredNormalizationPlan
            {
                RecommendedAction = verdict.NormalizationProposal.RecommendedAction,
                Explanation = verdict.NormalizationProposal.Explanation,
                ClarificationPayload = null
            },
            EvidenceRefs = [],
            CreatedAtUtc = nowUtc
        };
        return verdict;
    }

    private static AiConflictResolutionFallbackMappingProofRow BuildFallbackMappingRow(
        string caseId,
        string violationReason,
        string expectedFallbackReason,
        string expectedPublicationState)
    {
        var actualFallbackReason = ConflictResolutionSessionFallbackMapping.MapViolationToFallbackReason(violationReason);
        var actualPublicationState = ConflictResolutionSessionFallbackMapping.ResolvePublicationState(actualFallbackReason);
        return new AiConflictResolutionFallbackMappingProofRow
        {
            CaseId = caseId,
            ViolationReason = violationReason,
            ExpectedFallbackReason = expectedFallbackReason,
            ActualFallbackReason = actualFallbackReason,
            ExpectedPublicationState = expectedPublicationState,
            ActualPublicationState = actualPublicationState,
            Passed = string.Equals(expectedFallbackReason, actualFallbackReason, StringComparison.Ordinal)
                && string.Equals(expectedPublicationState, actualPublicationState, StringComparison.Ordinal)
        };
    }

    private static AiConflictResolutionToolProofRow BuildToolProofRow(
        string caseId,
        string expectedDecision,
        ResolutionConflictSessionToolRequest request,
        string scopeItemKey,
        int usedQuestionCount = 0,
        int usedAnswerCount = 0)
    {
        var accepted = ConflictResolutionSessionToolContract.TryNormalize(
            request,
            scopeItemKey,
            out _,
            out var decision);
        var actualDecision = accepted ? ConflictResolutionSessionToolDecisions.Accepted : decision;
        if (accepted
            && !ConflictResolutionSessionToolContract.TryValidateFollowUpBudget(
                request.ToolName,
                usedQuestionCount,
                usedAnswerCount,
                ConflictResolutionSessionToolContract.MaxFollowUpQuestions,
                ConflictResolutionSessionToolContract.MaxFollowUpAnswers,
                out var followUpDecision))
        {
            actualDecision = followUpDecision;
        }

        return new AiConflictResolutionToolProofRow
        {
            CaseId = caseId,
            ExpectedDecision = expectedDecision,
            ActualDecision = actualDecision,
            Passed = string.Equals(expectedDecision, actualDecision, StringComparison.Ordinal)
        };
    }
}

public sealed class AiConflictResolutionSessionV1ProofReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? TrackedPersonId { get; set; }
    public string? TrackedPersonDisplayName { get; set; }
    public string? ScopeItemKey { get; set; }
    public string? ItemType { get; set; }
    public string? SourceKind { get; set; }
    public bool StartAccepted { get; set; }
    public Guid? ConflictSessionId { get; set; }
    public string? StartState { get; set; }
    public string? StartStateReason { get; set; }
    public int AuditEntriesAfterStart { get; set; }
    public ResolutionConflictSessionCasePacket? InitialCasePacket { get; set; }
    public ResolutionConflictSessionQuestion? OperatorQuestion { get; set; }
    public bool OperatorAnswerCaptured { get; set; }
    public ResolutionConflictSessionOperatorInput? OperatorAnswer { get; set; }
    public string? FinalState { get; set; }
    public int AuditEntriesFinal { get; set; }
    public ResolutionConflictSessionVerdict? FinalVerdict { get; set; }
    public bool RequiredAuditKeysPresent { get; set; }
    public bool ApplyAccepted { get; set; }
    public Guid? ApplyActionId { get; set; }
    public string? ApplyActionType { get; set; }
    public Guid? ApplyConflictSessionId { get; set; }
    public bool StoredActionConflictLinkPresent { get; set; }
    public int? StoredActionConflictRevision { get; set; }
    public string? StoredSessionStatus { get; set; }
    public Guid? StoredSessionFinalActionId { get; set; }
    public string? StoredSessionFinalActionRequestId { get; set; }
    public bool DeterministicApplyPathConfirmed { get; set; }
    public string? ApplyPathNonBlockingFailureReason { get; set; }
    public List<AiConflictResolutionStructuredVerdictProofRow> StructuredVerdictProofRows { get; set; } = [];
    public List<AiConflictResolutionToolProofRow> ToolProofRows { get; set; } = [];
    public List<AiConflictResolutionApiSurfaceProofRow> ApiSurfaceProofRows { get; set; } = [];
    public List<AiConflictResolutionFallbackMappingProofRow> FallbackMappingProofRows { get; set; } = [];
    public List<AiConflictResolutionSessionV1CandidateAttempt> CandidateAttempts { get; set; } = [];
    public bool Passed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class AiConflictResolutionSessionV1CandidateAttempt
{
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string SourceKind { get; set; } = string.Empty;
    public bool StartAccepted { get; set; }
    public bool OperatorAnswerCaptured { get; set; }
    public bool ApplyAccepted { get; set; }
    public bool Passed { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class AiConflictResolutionStructuredVerdictProofRow
{
    public string CaseId { get; set; } = string.Empty;
    public string ExpectedDecision { get; set; } = string.Empty;
    public string ActualDecision { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool Passed { get; set; }
}

public sealed class AiConflictResolutionToolProofRow
{
    public string CaseId { get; set; } = string.Empty;
    public string ExpectedDecision { get; set; } = string.Empty;
    public string ActualDecision { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

public sealed class AiConflictResolutionApiSurfaceProofRow
{
    public string CaseId { get; set; } = string.Empty;
    public string ExpectedState { get; set; } = string.Empty;
    public string ActualState { get; set; } = string.Empty;
    public string ExpectedStateReason { get; set; } = string.Empty;
    public string ActualStateReason { get; set; } = string.Empty;
    public string ExpectedPublicationState { get; set; } = string.Empty;
    public string ActualPublicationState { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public bool Passed { get; set; }
}

public sealed class AiConflictResolutionFallbackMappingProofRow
{
    public string CaseId { get; set; } = string.Empty;
    public string ViolationReason { get; set; } = string.Empty;
    public string ExpectedFallbackReason { get; set; } = string.Empty;
    public string ActualFallbackReason { get; set; } = string.Empty;
    public string ExpectedPublicationState { get; set; } = string.Empty;
    public string ActualPublicationState { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

internal sealed record ApiSurfaceFixture(
    string CaseId,
    string ExpectedState,
    string ExpectedStateReason,
    string ExpectedPublicationState,
    string ExpectedDecision);

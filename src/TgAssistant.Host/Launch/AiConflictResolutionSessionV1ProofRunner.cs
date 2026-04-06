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

            ResolutionItemDetail? eligibleItem = null;
            OperatorSessionContext? eligibleSession = null;
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

                eligibleItem = detail.Detail.Item;
                eligibleSession = detail.Session;
                break;
            }

            Ensure(eligibleItem != null, "No eligible contradiction/review item was found for AI conflict session V1 proof.");
            Ensure(eligibleSession != null, "Eligible session state was not captured.");

            report.ScopeItemKey = eligibleItem!.ScopeItemKey;
            report.ItemType = eligibleItem.ItemType;
            report.SourceKind = eligibleItem.SourceKind;

            var started = await appService.StartConflictResolutionSessionAsync(
                new OperatorConflictResolutionSessionStartRequest
                {
                    RequestId = $"ai-conflict-session-proof-start-{Guid.NewGuid():N}",
                    OperatorIdentity = identity,
                    Session = eligibleSession!,
                    TrackedPersonId = trackedPerson.Id,
                    ScopeItemKey = eligibleItem.ScopeItemKey
                },
                ct);
            Ensure(started.Accepted && started.ConflictSession != null, $"Conflict-session start failed: {started.FailureReason ?? "unknown"}");

            report.StartAccepted = true;
            report.ConflictSessionId = started.ConflictSession.ConflictSessionId;
            report.InitialCasePacket = started.ConflictSession.InitialCasePacket;
            report.StartState = started.ConflictSession.State;
            report.StartStateReason = started.ConflictSession.StateReason;
            report.AuditEntriesAfterStart = started.ConflictSession.AuditTrail.Count;

            var finalEnvelope = started;
            if (string.Equals(started.ConflictSession.State, ResolutionConflictSessionStates.AwaitingOperatorAnswer, StringComparison.Ordinal)
                && started.ConflictSession.OperatorQuestion != null)
            {
                report.OperatorQuestion = started.ConflictSession.OperatorQuestion;
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
                report.OperatorAnswerCaptured = true;
                report.OperatorAnswer = responded.ConflictSession.OperatorAnswer;
            }

            Ensure(finalEnvelope.ConflictSession != null, "Final conflict-session payload is empty.");
            Ensure(finalEnvelope.ConflictSession.FinalVerdict != null, "Final verdict was not produced.");

            report.FinalState = finalEnvelope.ConflictSession.State;
            report.FinalVerdict = finalEnvelope.ConflictSession.FinalVerdict;
            report.AuditEntriesFinal = finalEnvelope.ConflictSession.AuditTrail.Count;

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
                    ConflictResolutionSessionId = finalEnvelope.ConflictSession.ConflictSessionId,
                    ConflictVerdictRevision = finalEnvelope.ConflictSession.Revision,
                    ConflictVerdict = finalEnvelope.ConflictSession.FinalVerdict,
                    SubmittedAtUtc = DateTime.UtcNow
                },
                ct);
            Ensure(action.Accepted && action.Action.Accepted, $"Action apply via existing path failed: {action.FailureReason ?? action.Action.FailureReason ?? "unknown"}");

            report.ApplyAccepted = true;
            report.ApplyActionId = action.Action.ActionId;
            report.ApplyActionType = action.Action.ActionType;
            report.ApplyConflictSessionId = action.Action.ConflictResolutionSessionId;

            await using var verifyDb = await dbFactory.CreateDbContextAsync(ct);
            var actionRow = await verifyDb.OperatorResolutionActions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == action.Action.ActionId, ct);
            Ensure(actionRow != null, "Applied action row was not persisted.");
            var sessionRow = await verifyDb.OperatorResolutionConflictSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == finalEnvelope.ConflictSession.ConflictSessionId, ct);
            Ensure(sessionRow != null, "Conflict-session row was not persisted.");

            report.StoredActionConflictLinkPresent = actionRow!.ConflictResolutionSessionId.HasValue;
            report.StoredActionConflictRevision = actionRow.ConflictVerdictRevision;
            report.StoredSessionStatus = sessionRow!.Status;
            report.StoredSessionFinalActionId = sessionRow.FinalActionId;
            report.StoredSessionFinalActionRequestId = sessionRow.FinalActionRequestId;
            report.DeterministicApplyPathConfirmed = report.StoredActionConflictLinkPresent
                && sessionRow.FinalActionId == action.Action.ActionId
                && string.Equals(sessionRow.Status, ResolutionConflictSessionStates.HandedOff, StringComparison.Ordinal);

            report.Passed = report.StartAccepted
                && report.FinalVerdict != null
                && report.ApplyAccepted
                && report.DeterministicApplyPathConfirmed;
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
    public bool Passed { get; set; }
    public string? FatalError { get; set; }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint007OfflineEventClarificationOrchestrationSmokeRunner
{
    private const string ActiveStatus = "active";
    private const string OperatorPersonType = "operator_root";
    private const string TrackedPersonType = "tracked_person";
    private const string LinkType = "opint_007_b3_validation";

    public static async Task<Opint007OfflineEventClarificationOrchestrationSmokeReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var dbFactory = services.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
        var operatorResolutionService = services.GetRequiredService<IOperatorResolutionApplicationService>();
        var settings = services.GetRequiredService<IOptions<TelegramSettings>>().Value;
        var workflow = services.GetRequiredService<TelegramOperatorWorkflowService>();
        var sessionStore = services.GetRequiredService<TelegramOperatorSessionStore>();

        if (settings.OwnerUserId <= 0)
        {
            throw new InvalidOperationException(
                "OPINT-007-B3 smoke requires Telegram:OwnerUserId. Set Telegram__OwnerUserId for the runtime invocation.");
        }

        var runId = Guid.NewGuid().ToString("N");
        var scopeKey = $"opint-007-b3-smoke:{runId}";
        var nowUtc = DateTime.UtcNow;
        var ownerUserId = settings.OwnerUserId;
        var seed = new Opint007B3SeedState
        {
            ScopeKey = scopeKey,
            OperatorPersonId = Guid.NewGuid(),
            TrackedPersonId = Guid.NewGuid(),
            TrackedDisplayName = $"000 OPINT-007-B3 Alpha {runId[..6]}",
            AuthorizedChatId = ownerUserId
        };

        var report = new Opint007OfflineEventClarificationOrchestrationSmokeReport
        {
            GeneratedAtUtc = nowUtc,
            OutputPath = resolvedOutputPath,
            ScopeKey = scopeKey,
            SummaryInput = "Spoke offline about missed deadlines and agreed to regroup Monday morning.",
            RecordingReferenceInput = "voice-note://offline/opint-007-b3-sample",
            ClarificationAnswerOne = "We agreed to regroup Monday at 10am and split the work by owner.",
            ClarificationAnswerTwo = "We agreed to regroup Monday at 10am and split the work by owner."
        };

        Exception? fatal = null;
        try
        {
            await SeedAsync(dbFactory, seed, ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    MessageText = "/start"
                },
                ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "mode:offline_event",
                    CallbackQueryId = $"mode-offline-{runId}"
                },
                ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = $"offline:tracked:{seed.TrackedPersonId:D}",
                    CallbackQueryId = $"offline-tracked-{runId}"
                },
                ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:capture-summary",
                    CallbackQueryId = $"offline-summary-prompt-{runId}"
                },
                ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    MessageText = report.SummaryInput
                },
                ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:capture-recording",
                    CallbackQueryId = $"offline-recording-prompt-{runId}"
                },
                ct);

            await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    MessageText = report.RecordingReferenceInput
                },
                ct);

            var draftSave = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:save",
                    CallbackQueryId = $"offline-save-draft-{runId}"
                },
                ct);
            report.DraftSaveResult = MapStep(draftSave);
            report.DraftOfflineEventId = TryParseSavedOfflineEventId(report.DraftSaveResult.PrimaryText);
            Ensure(report.DraftOfflineEventId.HasValue, "Draft save response did not include saved event id.");
            var draftOfflineEventId = report.DraftOfflineEventId.GetValueOrDefault();
            report.RejectedFinalSaveStoredBefore = await LoadStoredAsync(dbFactory, draftOfflineEventId, ct);
            Ensure(report.RejectedFinalSaveStoredBefore != null, "Stored draft offline event was not found before rejection validation.");
            report.RejectedFinalSaveSessionBefore = sessionStore.GetSnapshot(seed.AuthorizedChatId);
            Ensure(report.RejectedFinalSaveSessionBefore != null, "Session snapshot was not found before rejection validation.");

            var rejectedFinalSave = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:save-final",
                    CallbackQueryId = $"offline-save-final-rejected-{runId}"
                },
                ct);
            report.RejectedFinalSaveResult = MapStep(rejectedFinalSave);
            Ensure(
                report.RejectedFinalSaveResult.PrimaryText.Contains("Save rejected:", StringComparison.Ordinal),
                "Rejected final save did not emit an explicit save-rejected note.");

            report.RejectedFinalSaveStoredAfter = await LoadStoredAsync(dbFactory, draftOfflineEventId, ct);
            Ensure(report.RejectedFinalSaveStoredAfter != null, "Stored draft offline event was not found after rejection validation.");
            EnsureStoredUnchanged(report.RejectedFinalSaveStoredBefore!, report.RejectedFinalSaveStoredAfter!);

            report.RejectedFinalSaveSessionAfter = sessionStore.GetSnapshot(seed.AuthorizedChatId);
            Ensure(report.RejectedFinalSaveSessionAfter != null, "Session snapshot was not found after rejection validation.");
            Ensure(
                report.RejectedFinalSaveSessionAfter!.ActiveTrackedPersonId == report.RejectedFinalSaveSessionBefore!.ActiveTrackedPersonId,
                "Rejected final save unexpectedly changed active tracked person in session.");
            Ensure(
                string.Equals(report.RejectedFinalSaveSessionAfter.ActiveMode, report.RejectedFinalSaveSessionBefore.ActiveMode, StringComparison.Ordinal),
                "Rejected final save unexpectedly changed active mode in session.");
            Ensure(
                string.Equals(report.RejectedFinalSaveSessionAfter.SurfaceMode, report.RejectedFinalSaveSessionBefore.SurfaceMode, StringComparison.Ordinal),
                "Rejected final save unexpectedly changed Telegram surface mode.");

            var firstClarifyPrompt = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:clarify-next",
                    CallbackQueryId = $"offline-clarify-1-{runId}"
                },
                ct);
            report.FirstClarificationPrompt = MapStep(firstClarifyPrompt);
            Ensure(
                report.FirstClarificationPrompt.PrimaryText.Contains("Question:", StringComparison.Ordinal),
                "First clarification question prompt was not rendered.");

            var firstAnswer = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    MessageText = report.ClarificationAnswerOne
                },
                ct);
            report.FirstClarificationAnswer = MapStep(firstAnswer);
            Ensure(
                report.FirstClarificationAnswer.PrimaryText.Contains("Clarification answer captured", StringComparison.Ordinal),
                "First clarification answer acknowledgement was not rendered.");

            var secondClarifyPrompt = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:clarify-next",
                    CallbackQueryId = $"offline-clarify-2-{runId}"
                },
                ct);
            report.SecondClarificationPrompt = MapStep(secondClarifyPrompt);
            Ensure(
                report.SecondClarificationPrompt.PrimaryText.Contains("Question:", StringComparison.Ordinal),
                "Second clarification question prompt was not rendered.");

            var secondAnswer = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    MessageText = report.ClarificationAnswerTwo
                },
                ct);
            report.SecondClarificationAnswer = MapStep(secondAnswer);
            Ensure(
                report.SecondClarificationAnswer.PrimaryText.Contains("Clarification stop triggered (repetition)", StringComparison.Ordinal),
                "Clarification stop rule did not trigger in Telegram orchestration.");

            var finalSave = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B3 Operator",
                    CallbackData = "offline:save-final",
                    CallbackQueryId = $"offline-save-final-{runId}"
                },
                ct);
            report.FinalSaveResult = MapStep(finalSave);
            report.SavedOfflineEventId = TryParseSavedOfflineEventId(report.FinalSaveResult.PrimaryText);
            Ensure(report.SavedOfflineEventId.HasValue, "Final save response did not include saved event id.");
            var savedOfflineEventId = report.SavedOfflineEventId.GetValueOrDefault();
            Ensure(
                report.DraftOfflineEventId.GetValueOrDefault() == savedOfflineEventId,
                "Final save did not preserve the persisted draft offline-event id.");
            report.StoredEvent = await LoadStoredAsync(dbFactory, savedOfflineEventId, ct);
            Ensure(report.StoredEvent != null, "Stored offline event was not found.");
            Ensure(
                string.Equals(report.StoredEvent!.Status, OperatorOfflineEventStatuses.Saved, StringComparison.Ordinal),
                "Stored offline event was not persisted as saved.");
            Ensure(report.StoredEvent.SavedAtUtc.HasValue, "Stored offline event saved_at_utc was not set.");
            Ensure(report.StoredEvent.Confidence.HasValue, "Stored offline event confidence was not set.");
            Ensure(report.StoredEvent.Confidence > 0f, "Stored offline event confidence was not positive.");
            Ensure(
                string.Equals(report.StoredEvent.Summary, report.SummaryInput, StringComparison.Ordinal),
                "Stored offline event summary changed unexpectedly after final save.");
            Ensure(
                string.Equals(report.StoredEvent.RecordingReference, report.RecordingReferenceInput, StringComparison.Ordinal),
                "Stored offline event recording reference changed unexpectedly after final save.");

            using var clarificationDoc = JsonDocument.Parse(report.StoredEvent.ClarificationStateJson);
            var root = clarificationDoc.RootElement;
            Ensure(
                (root.TryGetProperty("history", out var historyElement)
                    || root.TryGetProperty("History", out historyElement))
                && historyElement.ValueKind == JsonValueKind.Array
                && historyElement.GetArrayLength() >= 2,
                "Stored clarification history did not include captured answer entries.");
            Ensure(
                (root.TryGetProperty("stopReason", out var stopReasonElement)
                    || root.TryGetProperty("StopReason", out stopReasonElement))
                && string.Equals(stopReasonElement.GetString(), OfflineEventClarificationStopReasons.Repetition, StringComparison.Ordinal),
                "Stored clarification stopReason mismatch.");
            Ensure(
                (root.TryGetProperty("loopStatus", out var loopStatusElement)
                    || root.TryGetProperty("LoopStatus", out loopStatusElement))
                && string.Equals(loopStatusElement.GetString(), OfflineEventClarificationLoopStatuses.Stopped, StringComparison.Ordinal),
                "Stored clarification loopStatus mismatch.");

            report.ApiContractProofRows = await BuildApiContractProofRowsAsync(
                operatorResolutionService,
                seed,
                savedOfflineEventId,
                ct);
            Ensure(report.ApiContractProofRows.Count >= 7, "API contract proof matrix must include at least 7 rows (200/400x3/401/403/404).");
            Ensure(report.ApiContractProofRows.All(x => x.Passed), "API contract proof matrix contains a failing row.");

            report.AllChecksPassed = true;
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            report.CleanupCompleted = await CleanupAsync(dbFactory, seed, report.SavedOfflineEventId, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "OPINT-007-B3 orchestration smoke failed: Telegram clarification loop or final save behavior is incomplete.",
                fatal);
        }

        return report;
    }

    private static async Task SeedAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint007B3SeedState seed,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        db.Persons.AddRange(
            new DbPerson
            {
                Id = seed.OperatorPersonId,
                ScopeKey = seed.ScopeKey,
                PersonType = OperatorPersonType,
                DisplayName = $"OPINT-007-B3 Operator {seed.ScopeKey[^6..]}",
                CanonicalName = $"OPINT-007-B3 Operator {seed.ScopeKey[^6..]}",
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            },
            new DbPerson
            {
                Id = seed.TrackedPersonId,
                ScopeKey = seed.ScopeKey,
                PersonType = TrackedPersonType,
                DisplayName = seed.TrackedDisplayName,
                CanonicalName = seed.TrackedDisplayName,
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

        await db.SaveChangesAsync(ct);

        db.PersonOperatorLinks.Add(
            new DbPersonOperatorLink
            {
                ScopeKey = seed.ScopeKey,
                OperatorPersonId = seed.OperatorPersonId,
                PersonId = seed.TrackedPersonId,
                LinkType = LinkType,
                Status = ActiveStatus,
                SourceBindingType = "opint_007_b3_smoke",
                SourceBindingValue = $"{seed.ScopeKey}:alpha",
                SourceBindingNormalized = $"{seed.ScopeKey}:alpha",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Opint007B3StoredOfflineEvent?> LoadStoredAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Guid offlineEventId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.OperatorOfflineEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == offlineEventId, ct);
        if (row == null)
        {
            return null;
        }

        return new Opint007B3StoredOfflineEvent
        {
            OfflineEventId = row.Id,
            Status = row.Status,
            Summary = row.SummaryText,
            RecordingReference = row.RecordingReference,
            ClarificationStateJson = row.ClarificationStateJson,
            Confidence = row.Confidence,
            SavedAtUtc = row.SavedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc
        };
    }

    private static void EnsureStoredUnchanged(
        Opint007B3StoredOfflineEvent before,
        Opint007B3StoredOfflineEvent after)
    {
        Ensure(before.OfflineEventId == after.OfflineEventId, "Rejected final save unexpectedly changed offline-event id.");
        Ensure(string.Equals(before.Status, after.Status, StringComparison.Ordinal), "Rejected final save unexpectedly changed offline-event status.");
        Ensure(string.Equals(before.Summary, after.Summary, StringComparison.Ordinal), "Rejected final save unexpectedly changed offline-event summary.");
        Ensure(string.Equals(before.RecordingReference, after.RecordingReference, StringComparison.Ordinal), "Rejected final save unexpectedly changed offline-event recording reference.");
        Ensure(string.Equals(before.ClarificationStateJson, after.ClarificationStateJson, StringComparison.Ordinal), "Rejected final save unexpectedly changed clarification state payload.");
        Ensure(before.Confidence == after.Confidence, "Rejected final save unexpectedly changed offline-event confidence.");
        Ensure(before.SavedAtUtc == after.SavedAtUtc, "Rejected final save unexpectedly changed saved_at_utc.");
        Ensure(before.UpdatedAtUtc == after.UpdatedAtUtc, "Rejected final save unexpectedly changed updated_at_utc.");
    }

    private static async Task<bool> CleanupAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint007B3SeedState seed,
        Guid? offlineEventId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (offlineEventId.HasValue)
        {
            var events = await db.OperatorOfflineEvents
                .Where(x => x.Id == offlineEventId.Value)
                .ToListAsync(ct);
            db.OperatorOfflineEvents.RemoveRange(events);
        }

        var links = await db.PersonOperatorLinks
            .Where(x => x.ScopeKey == seed.ScopeKey && x.OperatorPersonId == seed.OperatorPersonId)
            .ToListAsync(ct);
        db.PersonOperatorLinks.RemoveRange(links);

        var persons = await db.Persons
            .Where(x => x.ScopeKey == seed.ScopeKey
                && (x.Id == seed.OperatorPersonId || x.Id == seed.TrackedPersonId))
            .ToListAsync(ct);
        db.Persons.RemoveRange(persons);

        await db.SaveChangesAsync(ct);
        return true;
    }

    private static Opint007StepResult MapStep(TelegramOperatorResponse response)
    {
        var message = response.Messages.FirstOrDefault();
        return new Opint007StepResult
        {
            PrimaryText = message?.Text ?? string.Empty,
            ButtonLabels = message?.Buttons.SelectMany(x => x).Select(x => x.Text).ToList() ?? []
        };
    }

    private static Guid? TryParseSavedOfflineEventId(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}");
        return match.Success && Guid.TryParse(match.Value, out var id)
            ? id
            : null;
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-007-b3-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task<List<Opint007ApiContractProofRow>> BuildApiContractProofRowsAsync(
        IOperatorResolutionApplicationService service,
        Opint007B3SeedState seed,
        Guid savedOfflineEventId,
        CancellationToken ct)
    {
        var rows = new List<Opint007ApiContractProofRow>();
        var nowUtc = DateTime.UtcNow;
        var session = BuildProofSession(seed.TrackedPersonId, savedOfflineEventId, nowUtc);
        var identity = BuildProofIdentity(nowUtc);

        var detailSuccess = await service.GetOfflineEventDetailAsync(
            new OperatorOfflineEventDetailQueryRequest
            {
                TrackedPersonId = seed.TrackedPersonId,
                OfflineEventId = savedOfflineEventId,
                OperatorIdentity = identity,
                Session = session
            },
            ct);
        rows.Add(BuildProofRow(
            caseId: "detail_200",
            endpoint: "/api/operator/offline-events/detail",
            accepted: detailSuccess.Accepted,
            failureReason: detailSuccess.FailureReason,
            offlineEvent: detailSuccess.OfflineEvent,
            expectedStatusCode: StatusCodes.Status200OK,
            expectedFailureReason: null,
            expectedScopeBound: true,
            expectedFound: true,
            expectedRecordFieldsNull: false));

        var detailScopeMismatch = await service.GetOfflineEventDetailAsync(
            new OperatorOfflineEventDetailQueryRequest
            {
                TrackedPersonId = Guid.NewGuid(),
                OfflineEventId = savedOfflineEventId,
                OperatorIdentity = identity,
                Session = BuildProofSession(seed.TrackedPersonId, savedOfflineEventId, nowUtc)
            },
            ct);
        rows.Add(BuildProofRow(
            caseId: "detail_403_scope_mismatch",
            endpoint: "/api/operator/offline-events/detail",
            accepted: detailScopeMismatch.Accepted,
            failureReason: detailScopeMismatch.FailureReason,
            offlineEvent: detailScopeMismatch.OfflineEvent,
            expectedStatusCode: StatusCodes.Status403Forbidden,
            expectedFailureReason: "session_scope_item_mismatch",
            expectedScopeBound: false,
            expectedFound: false,
            expectedRecordFieldsNull: true));

        var missingOfflineEventId = Guid.NewGuid();
        var detailNotFound = await service.GetOfflineEventDetailAsync(
            new OperatorOfflineEventDetailQueryRequest
            {
                TrackedPersonId = seed.TrackedPersonId,
                OfflineEventId = missingOfflineEventId,
                OperatorIdentity = identity,
                Session = BuildProofSession(seed.TrackedPersonId, missingOfflineEventId, nowUtc)
            },
            ct);
        rows.Add(BuildProofRow(
            caseId: "detail_404_not_found",
            endpoint: "/api/operator/offline-events/detail",
            accepted: detailNotFound.Accepted,
            failureReason: detailNotFound.FailureReason,
            offlineEvent: detailNotFound.OfflineEvent,
            expectedStatusCode: StatusCodes.Status404NotFound,
            expectedFailureReason: "offline_event_not_found",
            expectedScopeBound: true,
            expectedFound: false,
            expectedRecordFieldsNull: true));

        var invalidStatus = await service.SubmitOfflineEventTimelineLinkageUpdateAsync(
            new OperatorOfflineEventTimelineLinkageUpdateRequest
            {
                TrackedPersonId = seed.TrackedPersonId,
                OfflineEventId = savedOfflineEventId,
                LinkageStatus = "invalid_status",
                TargetFamily = "resolution",
                TargetRef = "resolution:validation",
                LinkageNote = "verification",
                SubmittedAtUtc = nowUtc,
                OperatorIdentity = identity,
                Session = BuildProofSession(seed.TrackedPersonId, savedOfflineEventId, nowUtc)
            },
            ct);
        rows.Add(BuildProofRow(
            caseId: "timeline_400_invalid_status",
            endpoint: "/api/operator/offline-events/timeline-linkage",
            accepted: invalidStatus.Accepted,
            failureReason: invalidStatus.FailureReason,
            offlineEvent: invalidStatus.OfflineEvent,
            expectedStatusCode: StatusCodes.Status400BadRequest,
            expectedFailureReason: "unsupported_offline_event_timeline_linkage_status",
            expectedScopeBound: true,
            expectedFound: true,
            expectedRecordFieldsNull: false));

        var invalidFamily = await service.SubmitOfflineEventTimelineLinkageUpdateAsync(
            new OperatorOfflineEventTimelineLinkageUpdateRequest
            {
                TrackedPersonId = seed.TrackedPersonId,
                OfflineEventId = savedOfflineEventId,
                LinkageStatus = "linked",
                TargetFamily = "invalid_family",
                TargetRef = "resolution:validation",
                LinkageNote = "verification",
                SubmittedAtUtc = nowUtc,
                OperatorIdentity = identity,
                Session = BuildProofSession(seed.TrackedPersonId, savedOfflineEventId, nowUtc)
            },
            ct);
        rows.Add(BuildProofRow(
            caseId: "timeline_400_invalid_family",
            endpoint: "/api/operator/offline-events/timeline-linkage",
            accepted: invalidFamily.Accepted,
            failureReason: invalidFamily.FailureReason,
            offlineEvent: invalidFamily.OfflineEvent,
            expectedStatusCode: StatusCodes.Status400BadRequest,
            expectedFailureReason: "invalid_target_family",
            expectedScopeBound: true,
            expectedFound: true,
            expectedRecordFieldsNull: false));

        var invalidRef = await service.SubmitOfflineEventTimelineLinkageUpdateAsync(
            new OperatorOfflineEventTimelineLinkageUpdateRequest
            {
                TrackedPersonId = seed.TrackedPersonId,
                OfflineEventId = savedOfflineEventId,
                LinkageStatus = "linked",
                TargetFamily = "resolution",
                TargetRef = string.Empty,
                LinkageNote = "verification",
                SubmittedAtUtc = nowUtc,
                OperatorIdentity = identity,
                Session = BuildProofSession(seed.TrackedPersonId, savedOfflineEventId, nowUtc)
            },
            ct);
        rows.Add(BuildProofRow(
            caseId: "timeline_400_invalid_target_ref",
            endpoint: "/api/operator/offline-events/timeline-linkage",
            accepted: invalidRef.Accepted,
            failureReason: invalidRef.FailureReason,
            offlineEvent: invalidRef.OfflineEvent,
            expectedStatusCode: StatusCodes.Status400BadRequest,
            expectedFailureReason: "invalid_target_ref",
            expectedScopeBound: true,
            expectedFound: true,
            expectedRecordFieldsNull: false));

        var authFailure = WebOperatorAuthResult.Failure(
            statusCode: StatusCodes.Status401Unauthorized,
            failureReason: "auth_denied",
            auditEventId: null,
            session: BuildProofSession(seed.TrackedPersonId, savedOfflineEventId, nowUtc),
            operatorIdentity: BuildProofIdentity(nowUtc));
        var authFailureResult = OperatorApiEndpointExtensions.ToOfflineEventSingleItemAuthFailureResultForTesting(authFailure);
        var authFailureEnvelope = await ExecuteOfflineEventEnvelopeAsync(authFailureResult);
        var authFieldsNull = IsRecordBackedFieldsNull(authFailureEnvelope.OfflineEvent);
        var authPassed =
            authFailureEnvelope.StatusCode == StatusCodes.Status401Unauthorized
            && string.Equals(authFailureEnvelope.FailureReason, "auth_denied", StringComparison.Ordinal)
            && authFailureEnvelope.OfflineEvent.ScopeBound == false
            && authFailureEnvelope.OfflineEvent.Found == false
            && authFieldsNull;
        rows.Add(new Opint007ApiContractProofRow
        {
            CaseId = "detail_401_auth_failure",
            Endpoint = "/api/operator/offline-events/detail",
            ExpectedStatusCode = StatusCodes.Status401Unauthorized,
            ActualStatusCode = authFailureEnvelope.StatusCode,
            ExpectedFailureReason = "auth_denied",
            ActualFailureReason = authFailureEnvelope.FailureReason,
            ExpectedScopeBound = false,
            ActualScopeBound = authFailureEnvelope.OfflineEvent.ScopeBound,
            ExpectedFound = false,
            ActualFound = authFailureEnvelope.OfflineEvent.Found,
            ExpectedRecordFieldsNull = true,
            ActualRecordFieldsNull = authFieldsNull,
            Passed = authPassed
        });

        return rows;
    }

    private static OperatorIdentityContext BuildProofIdentity(DateTime nowUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = "opint-007-b3-smoke-operator",
            OperatorDisplay = "OPINT-007-B3 Smoke Operator",
            SurfaceSubject = "opint-007-b3-smoke",
            AuthSource = "opint-007-b3-smoke",
            AuthTimeUtc = nowUtc
        };
    }

    private static OperatorSessionContext BuildProofSession(Guid trackedPersonId, Guid offlineEventId, DateTime nowUtc)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = $"opint-007-b3-smoke:{Guid.NewGuid():N}",
            Surface = OperatorSurfaceTypes.Web,
            ActiveMode = OperatorModeTypes.OfflineEvent,
            ActiveTrackedPersonId = trackedPersonId,
            ActiveScopeItemKey = $"offline_event:{offlineEventId:D}",
            AuthenticatedAtUtc = nowUtc.AddMinutes(-2),
            LastSeenAtUtc = nowUtc.AddMinutes(-1),
            ExpiresAtUtc = nowUtc.AddHours(1)
        };
    }

    private static Opint007ApiContractProofRow BuildProofRow(
        string caseId,
        string endpoint,
        bool accepted,
        string? failureReason,
        OperatorOfflineEventSingleItemView offlineEvent,
        int expectedStatusCode,
        string? expectedFailureReason,
        bool expectedScopeBound,
        bool expectedFound,
        bool expectedRecordFieldsNull)
    {
        var actualStatusCode = accepted
            ? StatusCodes.Status200OK
            : OperatorApiEndpointExtensions.MapFailureStatusCodeForTesting(failureReason);
        var actualRecordFieldsNull = IsRecordBackedFieldsNull(offlineEvent);
        return new Opint007ApiContractProofRow
        {
            CaseId = caseId,
            Endpoint = endpoint,
            ExpectedStatusCode = expectedStatusCode,
            ActualStatusCode = actualStatusCode,
            ExpectedFailureReason = expectedFailureReason,
            ActualFailureReason = failureReason,
            ExpectedScopeBound = expectedScopeBound,
            ActualScopeBound = offlineEvent.ScopeBound,
            ExpectedFound = expectedFound,
            ActualFound = offlineEvent.Found,
            ExpectedRecordFieldsNull = expectedRecordFieldsNull,
            ActualRecordFieldsNull = actualRecordFieldsNull,
            Passed = expectedStatusCode == actualStatusCode
                && string.Equals(expectedFailureReason, failureReason, StringComparison.Ordinal)
                && expectedScopeBound == offlineEvent.ScopeBound
                && expectedFound == offlineEvent.Found
                && expectedRecordFieldsNull == actualRecordFieldsNull
        };
    }

    private static bool IsRecordBackedFieldsNull(OperatorOfflineEventSingleItemView offlineEvent)
    {
        return offlineEvent.Id is null
               && offlineEvent.TrackedPersonId is null
               && offlineEvent.ScopeKey is null
               && offlineEvent.Summary is null
               && offlineEvent.Confidence is null
               && offlineEvent.ClarificationHistoryCount is null
               && offlineEvent.StopReason is null
               && offlineEvent.LinkageTargetFamily is null
               && offlineEvent.LinkageTargetRef is null;
    }

    private static async Task<Opint007ExecutedOfflineEventEnvelope> ExecuteOfflineEventEnvelopeAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        var requestServices = new ServiceCollection()
            .AddOptions()
            .AddLogging()
            .BuildServiceProvider();
        context.RequestServices = requestServices;
        await using var body = new MemoryStream();
        context.Response.Body = body;
        await result.ExecuteAsync(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var payload = await reader.ReadToEndAsync();
        var envelope = JsonSerializer.Deserialize<Opint007ExecutedOfflineEventEnvelope>(
            payload,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new Opint007ExecutedOfflineEventEnvelope();
        envelope.StatusCode = context.Response.StatusCode;
        return envelope;
    }
}

public sealed class Opint007OfflineEventClarificationOrchestrationSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string SummaryInput { get; set; } = string.Empty;
    public string RecordingReferenceInput { get; set; } = string.Empty;
    public string ClarificationAnswerOne { get; set; } = string.Empty;
    public string ClarificationAnswerTwo { get; set; } = string.Empty;
    public Opint007StepResult FirstClarificationPrompt { get; set; } = new();
    public Opint007StepResult FirstClarificationAnswer { get; set; } = new();
    public Opint007StepResult SecondClarificationPrompt { get; set; } = new();
    public Opint007StepResult SecondClarificationAnswer { get; set; } = new();
    public Opint007StepResult DraftSaveResult { get; set; } = new();
    public Guid? DraftOfflineEventId { get; set; }
    public Opint007StepResult RejectedFinalSaveResult { get; set; } = new();
    public TelegramOperatorSessionSnapshot? RejectedFinalSaveSessionBefore { get; set; }
    public TelegramOperatorSessionSnapshot? RejectedFinalSaveSessionAfter { get; set; }
    public Opint007B3StoredOfflineEvent? RejectedFinalSaveStoredBefore { get; set; }
    public Opint007B3StoredOfflineEvent? RejectedFinalSaveStoredAfter { get; set; }
    public Opint007StepResult FinalSaveResult { get; set; } = new();
    public Guid? SavedOfflineEventId { get; set; }
    public Opint007B3StoredOfflineEvent? StoredEvent { get; set; }
    public List<Opint007ApiContractProofRow> ApiContractProofRows { get; set; } = [];
    public bool CleanupCompleted { get; set; }
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class Opint007B3StoredOfflineEvent
{
    public Guid OfflineEventId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? RecordingReference { get; set; }
    public string ClarificationStateJson { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public DateTime? SavedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

internal sealed class Opint007B3SeedState
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid OperatorPersonId { get; set; }
    public Guid TrackedPersonId { get; set; }
    public string TrackedDisplayName { get; set; } = string.Empty;
    public long AuthorizedChatId { get; set; }
}

public sealed class Opint007ApiContractProofRow
{
    public string CaseId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int ExpectedStatusCode { get; set; }
    public int ActualStatusCode { get; set; }
    public string? ExpectedFailureReason { get; set; }
    public string? ActualFailureReason { get; set; }
    public bool ExpectedScopeBound { get; set; }
    public bool ActualScopeBound { get; set; }
    public bool ExpectedFound { get; set; }
    public bool ActualFound { get; set; }
    public bool ExpectedRecordFieldsNull { get; set; }
    public bool ActualRecordFieldsNull { get; set; }
    public bool Passed { get; set; }
}

public sealed class Opint007ExecutedOfflineEventEnvelope
{
    public int StatusCode { get; set; }
    public bool Accepted { get; set; }
    public string? FailureReason { get; set; }
    public OperatorOfflineEventSingleItemView OfflineEvent { get; set; } = new();
}

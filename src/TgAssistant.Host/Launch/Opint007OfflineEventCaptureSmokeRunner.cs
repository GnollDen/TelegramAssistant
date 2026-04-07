using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint007OfflineEventCaptureSmokeRunner
{
    private const string ActiveStatus = "active";
    private const string OperatorPersonType = "operator_root";
    private const string TrackedPersonType = "tracked_person";
    private const string LinkType = "opint_007_b1_validation";

    public static async Task<Opint007OfflineEventCaptureSmokeReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var dbFactory = services.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
        var settings = services.GetRequiredService<IOptions<TelegramSettings>>().Value;
        var workflow = services.GetRequiredService<TelegramOperatorWorkflowService>();
        var sessionStore = services.GetRequiredService<TelegramOperatorSessionStore>();

        if (settings.OwnerUserId <= 0)
        {
            throw new InvalidOperationException(
                "OPINT-007-B1 smoke requires Telegram:OwnerUserId. Set Telegram__OwnerUserId for the runtime invocation.");
        }

        var runId = Guid.NewGuid().ToString("N");
        var scopeKey = $"opint-007-b1-smoke:{runId}";
        var nowUtc = DateTime.UtcNow;
        var ownerUserId = settings.OwnerUserId;

        var seed = new Opint007SeedState
        {
            ScopeKey = scopeKey,
            OperatorPersonId = Guid.NewGuid(),
            FirstTrackedPersonId = Guid.NewGuid(),
            SecondTrackedPersonId = Guid.NewGuid(),
            FirstTrackedDisplayName = $"000 OPINT-007-B1 Alpha {runId[..6]}",
            SecondTrackedDisplayName = $"000 OPINT-007-B1 Bravo {runId[..6]}",
            AuthorizedChatId = ownerUserId
        };

        var report = new Opint007OfflineEventCaptureSmokeReport
        {
            GeneratedAtUtc = nowUtc,
            OutputPath = resolvedOutputPath,
            ScopeKey = scopeKey,
            OwnerUserId = ownerUserId,
            AuthorizedChatId = seed.AuthorizedChatId,
            SummaryInput = "Met in person after work; agreed to de-escalate and check in tomorrow.",
            RecordingReferenceInput = "  voice-note://offline/opint-007-b1-sample  "
        };

        Exception? fatal = null;
        try
        {
            await SeedAsync(dbFactory, seed, ct);

            var startResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    MessageText = "/start"
                },
                ct);
            report.ModeCard = MapStep(startResponse);
            Ensure(report.ModeCard.ButtonLabels.Contains("Offline Event"), "Mode selection card omitted Offline Event.");

            var entryResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    CallbackData = "mode:offline_event",
                    CallbackQueryId = $"mode-offline-{runId}"
                },
                ct);
            report.OfflineEntry = MapStep(entryResponse);
            Ensure(
                report.OfflineEntry.PrimaryText.Contains("requires an explicit active tracked person", StringComparison.OrdinalIgnoreCase)
                    || report.OfflineEntry.PrimaryText.Contains("Select the tracked person", StringComparison.OrdinalIgnoreCase),
                "Offline Event entry did not require tracked-person selection.");

            var selectResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    CallbackData = $"offline:tracked:{seed.FirstTrackedPersonId:D}",
                    CallbackQueryId = $"offline-tracked-{runId}"
                },
                ct);
            report.TrackedPersonSelection = MapStep(selectResponse);
            Ensure(
                report.TrackedPersonSelection.PrimaryText.Contains($"Active tracked person: {seed.FirstTrackedDisplayName}", StringComparison.Ordinal),
                "Offline tracked-person selection was not reflected in the rendered context.");

            var summaryPromptResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    CallbackData = "offline:capture-summary",
                    CallbackQueryId = $"offline-summary-prompt-{runId}"
                },
                ct);
            report.SummaryPrompt = MapStep(summaryPromptResponse);
            Ensure(
                report.SummaryPrompt.PrimaryText.Contains("offline-event summary", StringComparison.OrdinalIgnoreCase),
                "Offline summary prompt was not rendered.");

            var summarySubmitResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    MessageText = report.SummaryInput
                },
                ct);
            report.SummaryCaptured = MapStep(summarySubmitResponse);
            Ensure(
                report.SummaryCaptured.PrimaryText.Contains("Summary updated.", StringComparison.Ordinal),
                "Offline summary update acknowledgement was missing.");

            var recordingPromptResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    CallbackData = "offline:capture-recording",
                    CallbackQueryId = $"offline-recording-prompt-{runId}"
                },
                ct);
            report.RecordingPrompt = MapStep(recordingPromptResponse);
            Ensure(
                report.RecordingPrompt.PrimaryText.Contains("recording reference", StringComparison.OrdinalIgnoreCase),
                "Offline recording-reference prompt was not rendered.");

            var recordingSubmitResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    MessageText = report.RecordingReferenceInput
                },
                ct);
            report.RecordingCaptured = MapStep(recordingSubmitResponse);
            Ensure(
                report.RecordingCaptured.PrimaryText.Contains("Recording reference updated.", StringComparison.Ordinal),
                "Recording reference update acknowledgement was missing.");

            var saveResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    CallbackData = "offline:save",
                    CallbackQueryId = $"offline-save-{runId}"
                },
                ct);
            report.SaveResult = MapStep(saveResponse);
            report.SavedOfflineEventId = TryParseSavedOfflineEventId(report.SaveResult.PrimaryText);
            Ensure(report.SavedOfflineEventId.HasValue, "Offline save response did not include saved event id.");
            var savedOfflineEventId = report.SavedOfflineEventId.GetValueOrDefault();

            var resaveResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-007-B1 Operator",
                    CallbackData = "offline:save",
                    CallbackQueryId = $"offline-resave-{runId}"
                },
                ct);
            report.ResaveResult = MapStep(resaveResponse);
            report.ResavedOfflineEventId = TryParseSavedOfflineEventId(report.ResaveResult.PrimaryText);
            Ensure(report.ResavedOfflineEventId.HasValue, "Offline re-save response did not include saved event id.");
            Ensure(
                report.ResavedOfflineEventId.GetValueOrDefault() == savedOfflineEventId,
                "Offline draft re-save did not upsert the same persisted offline-event record id.");

            var snapshot = sessionStore.GetSnapshot(seed.AuthorizedChatId);
            Ensure(snapshot != null, "Telegram operator session snapshot was not retained.");
            report.SessionSnapshot = snapshot!;
            Ensure(
                snapshot!.ActiveTrackedPersonId == seed.FirstTrackedPersonId,
                "Telegram operator session did not retain offline tracked-person context.");
            Ensure(
                string.Equals(snapshot.ActiveMode, OperatorModeTypes.OfflineEvent, StringComparison.Ordinal),
                "Telegram operator session did not retain offline_event active mode.");
            Ensure(
                string.Equals(snapshot.SurfaceMode, TelegramOperatorSurfaceModes.OfflineEvent, StringComparison.Ordinal),
                "Telegram surface mode did not remain in offline_event mode.");

            report.StoredDraft = await LoadStoredDraftAsync(dbFactory, savedOfflineEventId, ct);
            Ensure(report.StoredDraft != null, "Stored offline-event draft was not found.");
            Ensure(
                string.Equals(report.StoredDraft!.ScopeKey, scopeKey, StringComparison.Ordinal),
                "Stored offline-event draft scope_key mismatch.");
            Ensure(
                report.StoredDraft.TrackedPersonId == seed.FirstTrackedPersonId,
                "Stored offline-event draft tracked_person_id mismatch.");
            Ensure(
                string.Equals(report.StoredDraft.Status, OperatorOfflineEventStatuses.Draft, StringComparison.Ordinal),
                "Stored offline-event status is not draft.");
            Ensure(
                string.Equals(report.StoredDraft.Summary, report.SummaryInput, StringComparison.Ordinal),
                "Stored offline-event summary mismatch.");
            Ensure(
                string.Equals(report.StoredDraft.RecordingReference, report.RecordingReferenceInput.Trim(), StringComparison.Ordinal),
                "Stored offline-event recording reference mismatch.");
            Ensure(
                !report.StoredDraft.SavedAtUtc.HasValue,
                "Stored offline-event draft saved_at_utc must remain null.");
            Ensure(
                string.Equals(report.StoredDraft.OperatorSessionId, snapshot.OperatorSessionId, StringComparison.Ordinal),
                "Stored offline-event operator_session_id mismatch.");
            Ensure(
                string.Equals(report.StoredDraft.ActiveMode, OperatorModeTypes.OfflineEvent, StringComparison.Ordinal),
                "Stored offline-event active_mode mismatch.");
            Ensure(
                string.Equals(report.StoredDraft.Surface, OperatorSurfaceTypes.Telegram, StringComparison.Ordinal),
                "Stored offline-event surface mismatch.");
            Ensure(
                !string.IsNullOrWhiteSpace(report.StoredDraft.ClarificationStateJson)
                    && !string.Equals(report.StoredDraft.ClarificationStateJson, "{}", StringComparison.Ordinal),
                "Stored offline-event clarification state was empty.");
            using (var clarificationDoc = JsonDocument.Parse(report.StoredDraft.ClarificationStateJson))
            {
                var root = clarificationDoc.RootElement;
                Ensure(
                    (root.TryGetProperty("questions", out var questionsElement)
                        || root.TryGetProperty("Questions", out questionsElement))
                    && questionsElement.ValueKind == JsonValueKind.Array
                    && questionsElement.GetArrayLength() > 0,
                    "Stored offline-event clarification state is missing ranked questions.");
                Ensure(
                    (root.TryGetProperty("nextQuestionKey", out var nextQuestionKeyElement)
                        || root.TryGetProperty("NextQuestionKey", out nextQuestionKeyElement))
                    && nextQuestionKeyElement.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(nextQuestionKeyElement.GetString()),
                    "Stored offline-event clarification state is missing nextQuestionKey.");
            }
            Ensure(
                report.StoredDraft.Confidence.HasValue,
                "Stored offline-event confidence was not set from clarification policy state.");

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
            report.CleanupCompleted = await CleanupAsync(dbFactory, seed, report.SavedOfflineEventId, sessionStore.GetSnapshot(seed.AuthorizedChatId), ct);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "OPINT-007-B1 offline-event capture smoke failed: Telegram intake/persistence validation is incomplete.",
                fatal);
        }

        return report;
    }

    private static async Task SeedAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint007SeedState seed,
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
                DisplayName = $"OPINT-007-B1 Operator {seed.ScopeKey[^6..]}",
                CanonicalName = $"OPINT-007-B1 Operator {seed.ScopeKey[^6..]}",
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            },
            new DbPerson
            {
                Id = seed.FirstTrackedPersonId,
                ScopeKey = seed.ScopeKey,
                PersonType = TrackedPersonType,
                DisplayName = seed.FirstTrackedDisplayName,
                CanonicalName = seed.FirstTrackedDisplayName,
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            },
            new DbPerson
            {
                Id = seed.SecondTrackedPersonId,
                ScopeKey = seed.ScopeKey,
                PersonType = TrackedPersonType,
                DisplayName = seed.SecondTrackedDisplayName,
                CanonicalName = seed.SecondTrackedDisplayName,
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

        await db.SaveChangesAsync(ct);

        db.PersonOperatorLinks.AddRange(
            new DbPersonOperatorLink
            {
                ScopeKey = seed.ScopeKey,
                OperatorPersonId = seed.OperatorPersonId,
                PersonId = seed.FirstTrackedPersonId,
                LinkType = LinkType,
                Status = ActiveStatus,
                SourceBindingType = "opint_007_b1_smoke",
                SourceBindingValue = $"{seed.ScopeKey}:alpha",
                SourceBindingNormalized = $"{seed.ScopeKey}:alpha",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            },
            new DbPersonOperatorLink
            {
                ScopeKey = seed.ScopeKey,
                OperatorPersonId = seed.OperatorPersonId,
                PersonId = seed.SecondTrackedPersonId,
                LinkType = LinkType,
                Status = ActiveStatus,
                SourceBindingType = "opint_007_b1_smoke",
                SourceBindingValue = $"{seed.ScopeKey}:bravo",
                SourceBindingNormalized = $"{seed.ScopeKey}:bravo",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Opint007StoredDraft?> LoadStoredDraftAsync(
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

        return new Opint007StoredDraft
        {
            OfflineEventId = row.Id,
            ScopeKey = row.ScopeKey,
            TrackedPersonId = row.TrackedPersonId,
            Summary = row.SummaryText,
            RecordingReference = row.RecordingReference,
            Status = row.Status,
            OperatorSessionId = row.OperatorSessionId,
            ActiveMode = row.ActiveMode,
            Surface = row.Surface,
            CapturePayloadJson = row.CapturePayloadJson,
            ClarificationStateJson = row.ClarificationStateJson,
            Confidence = row.Confidence,
            SavedAtUtc = row.SavedAtUtc
        };
    }

    private static async Task<bool> CleanupAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint007SeedState seed,
        Guid? offlineEventId,
        TelegramOperatorSessionSnapshot? snapshot,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (offlineEventId.HasValue)
        {
            var eventRows = await db.OperatorOfflineEvents
                .Where(x => x.Id == offlineEventId.Value)
                .ToListAsync(ct);
            db.OperatorOfflineEvents.RemoveRange(eventRows);
        }

        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.OperatorSessionId))
        {
            var sessionAudits = await db.OperatorAuditEvents
                .Where(x => x.OperatorSessionId == snapshot.OperatorSessionId)
                .ToListAsync(ct);
            db.OperatorAuditEvents.RemoveRange(sessionAudits);
        }

        var links = await db.PersonOperatorLinks
            .Where(x => x.ScopeKey == seed.ScopeKey
                && x.OperatorPersonId == seed.OperatorPersonId)
            .ToListAsync(ct);
        db.PersonOperatorLinks.RemoveRange(links);

        var persons = await db.Persons
            .Where(x => x.ScopeKey == seed.ScopeKey
                && (x.Id == seed.OperatorPersonId
                    || x.Id == seed.FirstTrackedPersonId
                    || x.Id == seed.SecondTrackedPersonId))
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

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-007-b1-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint007OfflineEventCaptureSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public long OwnerUserId { get; set; }
    public long AuthorizedChatId { get; set; }
    public string SummaryInput { get; set; } = string.Empty;
    public string RecordingReferenceInput { get; set; } = string.Empty;
    public Opint007StepResult ModeCard { get; set; } = new();
    public Opint007StepResult OfflineEntry { get; set; } = new();
    public Opint007StepResult TrackedPersonSelection { get; set; } = new();
    public Opint007StepResult SummaryPrompt { get; set; } = new();
    public Opint007StepResult SummaryCaptured { get; set; } = new();
    public Opint007StepResult RecordingPrompt { get; set; } = new();
    public Opint007StepResult RecordingCaptured { get; set; } = new();
    public Opint007StepResult SaveResult { get; set; } = new();
    public Opint007StepResult ResaveResult { get; set; } = new();
    public Guid? SavedOfflineEventId { get; set; }
    public Guid? ResavedOfflineEventId { get; set; }
    public TelegramOperatorSessionSnapshot SessionSnapshot { get; set; } = new();
    public Opint007StoredDraft? StoredDraft { get; set; }
    public bool CleanupCompleted { get; set; }
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class Opint007StepResult
{
    public string PrimaryText { get; set; } = string.Empty;
    public List<string> ButtonLabels { get; set; } = [];
}

public sealed class Opint007StoredDraft
{
    public Guid OfflineEventId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public Guid TrackedPersonId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? RecordingReference { get; set; }
    public string Status { get; set; } = string.Empty;
    public string OperatorSessionId { get; set; } = string.Empty;
    public string ActiveMode { get; set; } = string.Empty;
    public string Surface { get; set; } = string.Empty;
    public string CapturePayloadJson { get; set; } = string.Empty;
    public string ClarificationStateJson { get; set; } = string.Empty;
    public float? Confidence { get; set; }
    public DateTime? SavedAtUtc { get; set; }
}

internal sealed class Opint007SeedState
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid OperatorPersonId { get; set; }
    public Guid FirstTrackedPersonId { get; set; }
    public Guid SecondTrackedPersonId { get; set; }
    public string FirstTrackedDisplayName { get; set; } = string.Empty;
    public string SecondTrackedDisplayName { get; set; } = string.Empty;
    public long AuthorizedChatId { get; set; }
}

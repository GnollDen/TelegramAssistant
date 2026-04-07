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
        var settings = services.GetRequiredService<IOptions<TelegramSettings>>().Value;
        var workflow = services.GetRequiredService<TelegramOperatorWorkflowService>();

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
            SavedAtUtc = row.SavedAtUtc
        };
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
    public Opint007StepResult FinalSaveResult { get; set; } = new();
    public Guid? SavedOfflineEventId { get; set; }
    public Opint007B3StoredOfflineEvent? StoredEvent { get; set; }
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
}

internal sealed class Opint007B3SeedState
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid OperatorPersonId { get; set; }
    public Guid TrackedPersonId { get; set; }
    public string TrackedDisplayName { get; set; } = string.Empty;
    public long AuthorizedChatId { get; set; }
}

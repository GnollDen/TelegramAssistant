using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Infrastructure.Database.Ef;
using TgAssistant.Telegram.Operator;

namespace TgAssistant.Host.Launch;

public static class Opint004TelegramModeSmokeRunner
{
    private const string ActiveStatus = "active";
    private const string OperatorPersonType = "operator_root";
    private const string TrackedPersonType = "tracked_person";
    private const string LinkType = "opint_004_a_validation";

    public static async Task<Opint004TelegramModeSmokeReport> RunAsync(
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
                "OPINT-004-A smoke requires Telegram:OwnerUserId. Set Telegram__OwnerUserId for the runtime invocation.");
        }

        var runId = Guid.NewGuid().ToString("N");
        var scopeKey = $"opint-004-a-smoke:{runId}";
        var nowUtc = DateTime.UtcNow;
        var ownerUserId = settings.OwnerUserId;
        var unauthorizedUserId = ownerUserId + 1;

        var seed = new Opint004SeedState
        {
            ScopeKey = scopeKey,
            OperatorPersonId = Guid.NewGuid(),
            FirstTrackedPersonId = Guid.NewGuid(),
            SecondTrackedPersonId = Guid.NewGuid(),
            FirstTrackedDisplayName = $"000 OPINT-004-A Alpha {runId[..6]}",
            SecondTrackedDisplayName = $"000 OPINT-004-A Bravo {runId[..6]}",
            AuthorizedChatId = ownerUserId,
            UnauthorizedChatId = unauthorizedUserId
        };

        var report = new Opint004TelegramModeSmokeReport
        {
            GeneratedAtUtc = nowUtc,
            OutputPath = resolvedOutputPath,
            ScopeKey = scopeKey,
            OwnerUserId = ownerUserId,
            AuthorizedChatId = seed.AuthorizedChatId,
            UnauthorizedChatId = seed.UnauthorizedChatId
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
                    UserDisplayName = "OPINT-004-A Operator",
                    MessageText = "/start"
                },
                ct);
            report.ModeCard = MapStep(startResponse);
            Ensure(
                report.ModeCard.PrimaryText.Contains("Choose a mode.", StringComparison.Ordinal),
                "Mode selection card was not rendered.");
            Ensure(report.ModeCard.ButtonLabels.Contains("Resolution"), "Mode selection card omitted Resolution.");

            var resolutionResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-004-A Operator",
                    CallbackData = "mode:resolution",
                    CallbackQueryId = $"mode-resolution-{runId}"
                },
                ct);
            report.ResolutionEntry = MapStep(resolutionResponse);
            Ensure(
                report.ResolutionEntry.PrimaryText.Contains("explicit active tracked person", StringComparison.OrdinalIgnoreCase)
                    || report.ResolutionEntry.PrimaryText.Contains("Select the tracked person", StringComparison.OrdinalIgnoreCase),
                "Resolution entry did not require tracked-person selection.");
            Ensure(
                report.ResolutionEntry.ButtonLabels.Contains(seed.FirstTrackedDisplayName)
                    && report.ResolutionEntry.ButtonLabels.Contains(seed.SecondTrackedDisplayName),
                "Resolution entry did not surface both tracked-person choices.");

            var selectFirstResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-004-A Operator",
                    CallbackData = $"tracked:{seed.FirstTrackedPersonId:D}",
                    CallbackQueryId = $"tracked-one-{runId}"
                },
                ct);
            report.FirstSelection = MapStep(selectFirstResponse);
            Ensure(
                report.FirstSelection.PrimaryText.Contains($"Active tracked person: {seed.FirstTrackedDisplayName}", StringComparison.Ordinal),
                "First tracked-person selection did not become explicit in resolution mode.");

            var switchResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-004-A Operator",
                    CallbackData = "resolution:switch-person",
                    CallbackQueryId = $"switch-person-{runId}"
                },
                ct);
            report.SwitchPicker = MapStep(switchResponse);
            Ensure(
                report.SwitchPicker.PrimaryText.Contains("Select the active tracked person", StringComparison.OrdinalIgnoreCase),
                "Switch-person control did not return to tracked-person selection.");

            var selectSecondResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.AuthorizedChatId,
                    UserId = ownerUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "OPINT-004-A Operator",
                    CallbackData = $"tracked:{seed.SecondTrackedPersonId:D}",
                    CallbackQueryId = $"tracked-two-{runId}"
                },
                ct);
            report.SecondSelection = MapStep(selectSecondResponse);
            Ensure(
                report.SecondSelection.PrimaryText.Contains($"Active tracked person: {seed.SecondTrackedDisplayName}", StringComparison.Ordinal),
                "Second tracked-person selection did not replace the active resolution context.");

            var unauthorizedResponse = await workflow.HandleInteractionAsync(
                new TelegramOperatorInteraction
                {
                    ChatId = seed.UnauthorizedChatId,
                    UserId = unauthorizedUserId,
                    IsPrivateChat = true,
                    UserDisplayName = "Unauthorized User",
                    MessageText = "/start"
                },
                ct);
            report.UnauthorizedAttempt = MapStep(unauthorizedResponse);
            Ensure(
                report.UnauthorizedAttempt.PrimaryText.Contains("Access denied", StringComparison.OrdinalIgnoreCase),
                "Unauthorized Telegram attempt was not denied.");

            var snapshot = sessionStore.GetSnapshot(seed.AuthorizedChatId);
            Ensure(snapshot != null, "Telegram operator session snapshot was not retained.");
            report.SessionSnapshot = snapshot!;
            Ensure(
                snapshot!.ActiveTrackedPersonId == seed.SecondTrackedPersonId,
                "Telegram operator session did not retain the second tracked-person selection.");
            Ensure(
                string.Equals(snapshot.ActiveMode, "resolution_queue", StringComparison.Ordinal),
                "Telegram operator session did not retain resolution_queue mode.");
            Ensure(
                string.Equals(snapshot.SurfaceMode, TelegramOperatorSurfaceModes.Resolution, StringComparison.Ordinal),
                "Telegram surface mode did not remain in resolution.");

            report.AuditChecks = await LoadAuditChecksAsync(dbFactory, seed, snapshot, ct);
            Ensure(report.AuditChecks.AcceptedTrackedPersonSwitchCount >= 2, "Expected two tracked-person switch audit events.");
            Ensure(report.AuditChecks.UnauthorizedDeniedCount >= 1, "Expected at least one unauthorized deny audit event.");
            Ensure(report.AuditChecks.SessionAuthenticatedCount >= 1, "Expected at least one session-authenticated audit event.");

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
            report.CleanupCompleted = await CleanupAsync(dbFactory, seed, sessionStore.GetSnapshot(seed.AuthorizedChatId), ct);
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "OPINT-004-A Telegram mode smoke failed: mode selection or tracked-person context validation is incomplete.",
                fatal);
        }

        return report;
    }

    private static async Task SeedAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint004SeedState seed,
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
                DisplayName = $"OPINT-004-A Operator {seed.ScopeKey[^6..]}",
                CanonicalName = $"OPINT-004-A Operator {seed.ScopeKey[^6..]}",
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
                SourceBindingType = "opint_004_a_smoke",
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
                SourceBindingType = "opint_004_a_smoke",
                SourceBindingValue = $"{seed.ScopeKey}:bravo",
                SourceBindingNormalized = $"{seed.ScopeKey}:bravo",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

        await db.SaveChangesAsync(ct);
    }

    private static async Task<Opint004AuditChecks> LoadAuditChecksAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint004SeedState seed,
        TelegramOperatorSessionSnapshot snapshot,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var acceptedTrackedPersonSwitchCount = await db.OperatorAuditEvents
            .AsNoTracking()
            .CountAsync(
                x => x.OperatorSessionId == snapshot.OperatorSessionId
                    && x.SessionEventType == "tracked_person_switch"
                    && x.DecisionOutcome == "accepted",
                ct);

        var sessionAuthenticatedCount = await db.OperatorAuditEvents
            .AsNoTracking()
            .CountAsync(
                x => x.OperatorSessionId == snapshot.OperatorSessionId
                    && x.SessionEventType == "session_authenticated"
                    && x.DecisionOutcome == "accepted",
                ct);

        var unauthorizedDeniedCount = await db.OperatorAuditEvents
            .AsNoTracking()
            .CountAsync(
                x => x.RequestId.StartsWith($"auth-denied:{seed.UnauthorizedChatId}:")
                    && x.SessionEventType == "auth_denied"
                    && x.DecisionOutcome == "denied",
                ct);

        return new Opint004AuditChecks
        {
            AcceptedTrackedPersonSwitchCount = acceptedTrackedPersonSwitchCount,
            SessionAuthenticatedCount = sessionAuthenticatedCount,
            UnauthorizedDeniedCount = unauthorizedDeniedCount
        };
    }

    private static async Task<bool> CleanupAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        Opint004SeedState seed,
        TelegramOperatorSessionSnapshot? snapshot,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.OperatorSessionId))
        {
            var sessionAudits = await db.OperatorAuditEvents
                .Where(x => x.OperatorSessionId == snapshot.OperatorSessionId)
                .ToListAsync(ct);
            db.OperatorAuditEvents.RemoveRange(sessionAudits);
        }

        var unauthorizedAudits = await db.OperatorAuditEvents
            .Where(x =>
                x.RequestId.StartsWith($"auth-denied:{seed.UnauthorizedChatId}:")
                || x.RequestId.StartsWith($"session-authenticated:{seed.AuthorizedChatId}:")
                || x.RequestId.StartsWith($"mode-switch:{seed.AuthorizedChatId}:"))
            .ToListAsync(ct);
        db.OperatorAuditEvents.RemoveRange(unauthorizedAudits);

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

    private static Opint004StepResult MapStep(TelegramOperatorResponse response)
    {
        var message = response.Messages.FirstOrDefault();
        return new Opint004StepResult
        {
            PrimaryText = message?.Text ?? string.Empty,
            ButtonLabels = message?.Buttons.SelectMany(x => x).Select(x => x.Text).ToList() ?? []
        };
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-004-a-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint004TelegramModeSmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public long OwnerUserId { get; set; }
    public long AuthorizedChatId { get; set; }
    public long UnauthorizedChatId { get; set; }
    public Opint004StepResult ModeCard { get; set; } = new();
    public Opint004StepResult ResolutionEntry { get; set; } = new();
    public Opint004StepResult FirstSelection { get; set; } = new();
    public Opint004StepResult SwitchPicker { get; set; } = new();
    public Opint004StepResult SecondSelection { get; set; } = new();
    public Opint004StepResult UnauthorizedAttempt { get; set; } = new();
    public TelegramOperatorSessionSnapshot SessionSnapshot { get; set; } = new();
    public Opint004AuditChecks AuditChecks { get; set; } = new();
    public bool CleanupCompleted { get; set; }
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class Opint004StepResult
{
    public string PrimaryText { get; set; } = string.Empty;
    public List<string> ButtonLabels { get; set; } = [];
}

public sealed class Opint004AuditChecks
{
    public int AcceptedTrackedPersonSwitchCount { get; set; }
    public int SessionAuthenticatedCount { get; set; }
    public int UnauthorizedDeniedCount { get; set; }
}

internal sealed class Opint004SeedState
{
    public string ScopeKey { get; set; } = string.Empty;
    public Guid OperatorPersonId { get; set; }
    public Guid FirstTrackedPersonId { get; set; }
    public Guid SecondTrackedPersonId { get; set; }
    public string FirstTrackedDisplayName { get; set; } = string.Empty;
    public string SecondTrackedDisplayName { get; set; } = string.Empty;
    public long AuthorizedChatId { get; set; }
    public long UnauthorizedChatId { get; set; }
}

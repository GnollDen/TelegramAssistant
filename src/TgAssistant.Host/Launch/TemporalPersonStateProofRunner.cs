using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class TemporalPersonStateProofRunner
{
    private const string TriggerKind = "temporal_person_state_proof";

    public static async Task<TemporalPersonStateProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new TemporalPersonStateProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            using var scope = services.CreateScope();
            var temporalRepository = scope.ServiceProvider.GetRequiredService<ITemporalPersonStateRepository>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();

            report.ScopeKey = $"proof:phb_006a:{DateTime.UtcNow:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";
            report.TrackedPersonId = await EnsureTrackedPersonAsync(dbFactory, report.ScopeKey, ct);
            report.SubjectRef = $"person:{report.TrackedPersonId:D}:timeline_episode";
            report.ProfileSubjectRef = $"person:{report.TrackedPersonId:D}";

            var timelinePresence = await UpsertStateAsync(
                temporalRepository,
                report.ScopeKey,
                report.TrackedPersonId,
                report.SubjectRef,
                Stage7TimelineTemporalFactTypes.TimelinePrimaryActivity,
                TemporalPersonStateFactCategories.EventConditioned,
                "present_during_event",
                expectedDecision: "insert_open",
                ct);
            report.Rows.Add(timelinePresence.Row);

            var timelineAbsence = await UpsertStateAsync(
                temporalRepository,
                report.ScopeKey,
                report.TrackedPersonId,
                report.SubjectRef,
                Stage7TimelineTemporalFactTypes.TimelinePrimaryActivity,
                TemporalPersonStateFactCategories.EventConditioned,
                "absent_outside_event",
                expectedDecision: "supersede_open",
                ct);
            report.Rows.Add(timelineAbsence.Row);

            var profileInitial = await UpsertStateAsync(
                temporalRepository,
                report.ScopeKey,
                report.TrackedPersonId,
                report.ProfileSubjectRef,
                Stage7DossierProfileTemporalFactTypes.ProfileStatus,
                TemporalPersonStateFactCategories.Stable,
                "needs_context",
                expectedDecision: "insert_open",
                ct);
            report.Rows.Add(profileInitial.Row);

            var profileChanged = await UpsertStateAsync(
                temporalRepository,
                report.ScopeKey,
                report.TrackedPersonId,
                report.ProfileSubjectRef,
                Stage7DossierProfileTemporalFactTypes.ProfileStatus,
                TemporalPersonStateFactCategories.Stable,
                "stable",
                expectedDecision: "supersede_open",
                ct);
            report.Rows.Add(profileChanged.Row);

            report.Passed = report.Rows.All(x => x.Passed);
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
            throw new InvalidOperationException("Temporal person-state proof failed.", fatal);
        }

        return report;
    }

    private static async Task<Guid> EnsureTrackedPersonAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string scopeKey,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.Persons
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.PersonType == "tracked_person" && x.Status == "active")
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        if (existing != null)
        {
            return existing.Id;
        }

        var now = DateTime.UtcNow;
        var row = new DbPerson
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            PersonType = "tracked_person",
            DisplayName = "PHB-006A Proof Person",
            CanonicalName = "phb_006a_proof_person",
            Status = "active",
            MetadataJson = "{}",
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Persons.Add(row);
        await db.SaveChangesAsync(ct);
        return row.Id;
    }

    private static async Task<TemporalProofWriteOutcome> UpsertStateAsync(
        ITemporalPersonStateRepository temporalRepository,
        string scopeKey,
        Guid trackedPersonId,
        string subjectRef,
        string factType,
        string factCategory,
        string nextValue,
        string expectedDecision,
        CancellationToken ct)
    {
        var openState = await temporalRepository.GetOpenStateAsync(scopeKey, subjectRef, factType, ct);
        var inserted = await temporalRepository.InsertAsync(
            new TemporalPersonStateWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                SubjectRef = subjectRef,
                FactType = factType,
                FactCategory = factCategory,
                Value = nextValue,
                ValidFromUtc = DateTime.UtcNow,
                StateStatus = TemporalPersonStateStatuses.Open,
                SupersedesStateId = openState?.Id,
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:{factType}:{Guid.NewGuid():N}"
            },
            ct);

        if (openState != null)
        {
            _ = await temporalRepository.UpdateSupersessionAsync(
                new TemporalPersonStateSupersessionUpdateRequest
                {
                    ScopeKey = scopeKey,
                    TrackedPersonId = trackedPersonId,
                    PreviousStateId = openState.Id,
                    SupersededByStateId = inserted.Id,
                    SupersededAtUtc = inserted.ValidFromUtc,
                    NextStatus = TemporalPersonStateStatuses.Superseded
                },
                ct);
        }

        var actualDecision = openState == null ? "insert_open" : "supersede_open";
        var previousAfter = openState == null
            ? null
            : await FindStateByIdAsync(
                temporalRepository,
                scopeKey,
                trackedPersonId,
                subjectRef,
                factType,
                openState.Id,
                ct);

        var row = new TemporalPersonStateProofRow
        {
            CaseId = $"{factType}:{nextValue}",
            ScopeKey = scopeKey,
            SubjectRef = subjectRef,
            FactType = factType,
            PreviousStateId = openState?.Id,
            NewStateId = inserted.Id,
            ExpectedDecision = expectedDecision,
            ActualDecision = actualDecision,
            SupersedesStateId = inserted.SupersedesStateId,
            SupersededByStateId = previousAfter?.SupersededByStateId,
            Reason = openState == null ? "open_state_inserted" : "open_state_superseded",
            Passed = string.Equals(expectedDecision, actualDecision, StringComparison.Ordinal)
                && (openState == null
                    ? inserted.SupersedesStateId == null
                    : inserted.SupersedesStateId == openState.Id
                      && previousAfter?.SupersededByStateId == inserted.Id
                      && string.Equals(previousAfter.StateStatus, TemporalPersonStateStatuses.Superseded, StringComparison.Ordinal))
        };

        return new TemporalProofWriteOutcome
        {
            Row = row
        };
    }

    private static async Task<TemporalPersonState?> FindStateByIdAsync(
        ITemporalPersonStateRepository temporalRepository,
        string scopeKey,
        Guid trackedPersonId,
        string subjectRef,
        string factType,
        Guid stateId,
        CancellationToken ct)
    {
        var states = await temporalRepository.QueryScopedAsync(
            new TemporalPersonStateScopeQuery
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                SubjectRef = subjectRef,
                FactType = factType,
                Limit = 50
            },
            ct);
        return states.FirstOrDefault(x => x.Id == stateId);
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        var cwd = Directory.GetCurrentDirectory();
        var hostArtifactsRoot = string.Equals(Path.GetFileName(cwd), "TgAssistant.Host", StringComparison.Ordinal)
            ? Path.Combine(cwd, "artifacts")
            : Path.Combine(cwd, "src", "TgAssistant.Host", "artifacts");

        return Path.GetFullPath(Path.Combine(
            hostArtifactsRoot,
            "phase-b",
            "temporal-person-state-proof.json"));
    }

    private sealed class TemporalProofWriteOutcome
    {
        public TemporalPersonStateProofRow Row { get; init; } = new();
    }
}

public sealed class TemporalPersonStateProofReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("profile_subject_ref")]
    public string ProfileSubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("fatal_error")]
    public string? FatalError { get; set; }

    [JsonPropertyName("rows")]
    public List<TemporalPersonStateProofRow> Rows { get; set; } = [];
}

public sealed class TemporalPersonStateProofRow
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("subject_ref")]
    public string SubjectRef { get; set; } = string.Empty;

    [JsonPropertyName("fact_type")]
    public string FactType { get; set; } = string.Empty;

    [JsonPropertyName("previous_state_id")]
    public Guid? PreviousStateId { get; set; }

    [JsonPropertyName("new_state_id")]
    public Guid? NewStateId { get; set; }

    [JsonPropertyName("expected_decision")]
    public string ExpectedDecision { get; set; } = string.Empty;

    [JsonPropertyName("actual_decision")]
    public string ActualDecision { get; set; } = string.Empty;

    [JsonPropertyName("supersedes_state_id")]
    public Guid? SupersedesStateId { get; set; }

    [JsonPropertyName("superseded_by_state_id")]
    public Guid? SupersededByStateId { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}

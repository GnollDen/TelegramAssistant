using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class PersonHistoryProofRunner
{
    private const string TriggerKind = "person_history_proof";
    private const string ExpectedPublishable = "publishable";
    private const string ExpectedInsufficientEvidence = "insufficient_evidence";

    public static async Task<PersonHistoryProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new PersonHistoryProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            using var scope = services.CreateScope();
            var appService = scope.ServiceProvider.GetRequiredService<IOperatorResolutionApplicationService>();
            var temporalRepository = scope.ServiceProvider.GetRequiredService<ITemporalPersonStateRepository>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();

            report.ScopeKey = $"proof:phb_006b:{DateTime.UtcNow:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";
            report.TrackedPersonId = await EnsureTrackedPersonAsync(dbFactory, report.ScopeKey, ct);

            var subjectRef = $"person:{report.TrackedPersonId:D}";
            var factType = Stage7DossierProfileTemporalFactTypes.ProfileStatus;
            var validFromBase = DateTime.UtcNow.AddMinutes(-5);
            var firstState = await temporalRepository.InsertAsync(
                new TemporalPersonStateWriteRequest
                {
                    ScopeKey = report.ScopeKey,
                    TrackedPersonId = report.TrackedPersonId,
                    SubjectRef = subjectRef,
                    FactType = factType,
                    FactCategory = TemporalPersonStateFactCategories.Stable,
                    Value = "needs_context",
                    ValidFromUtc = validFromBase,
                    StateStatus = TemporalPersonStateStatuses.Open,
                    EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                    TriggerKind = TriggerKind,
                    TriggerRef = $"{TriggerKind}:{factType}:initial"
                },
                ct);

            var secondState = await temporalRepository.InsertAsync(
                new TemporalPersonStateWriteRequest
                {
                    ScopeKey = report.ScopeKey,
                    TrackedPersonId = report.TrackedPersonId,
                    SubjectRef = subjectRef,
                    FactType = factType,
                    FactCategory = TemporalPersonStateFactCategories.Stable,
                    Value = "stable",
                    ValidFromUtc = validFromBase.AddMinutes(1),
                    StateStatus = TemporalPersonStateStatuses.Open,
                    EvidenceRefs = Array.Empty<string>(),
                    SupersedesStateId = firstState.Id,
                    TriggerKind = TriggerKind,
                    TriggerRef = $"{TriggerKind}:{factType}:replacement"
                },
                ct);
            _ = await temporalRepository.UpdateSupersessionAsync(
                new TemporalPersonStateSupersessionUpdateRequest
                {
                    ScopeKey = report.ScopeKey,
                    TrackedPersonId = report.TrackedPersonId,
                    PreviousStateId = firstState.Id,
                    SupersededByStateId = secondState.Id,
                    SupersededAtUtc = secondState.ValidFromUtc,
                    NextStatus = TemporalPersonStateStatuses.Superseded
                },
                ct);

            var nowUtc = DateTime.UtcNow;
            var apiResult = await appService.QueryPersonWorkspaceHistoryAsync(
                new OperatorPersonWorkspaceHistoryQueryRequest
                {
                    TrackedPersonId = report.TrackedPersonId,
                    FactType = factType,
                    OperatorIdentity = BuildOperatorIdentity(nowUtc),
                    Session = BuildOperatorSession(nowUtc)
                },
                ct);

            if (!apiResult.Accepted)
            {
                throw new InvalidOperationException($"person_history_query_failed:{apiResult.FailureReason ?? "unknown"}");
            }

            var expectedOrder = $"{secondState.Id:D}>{firstState.Id:D}";
            var actualOrder = string.Join(">", apiResult.History.Rows.Take(2).Select(row => row.StateId.ToString("D")));
            var openVsHistoricalSeparated = apiResult.History.OpenRows == 1
                && apiResult.History.HistoricalRows >= 1;

            var currentRow = apiResult.History.Rows.FirstOrDefault(row => row.StateId == secondState.Id);
            var supersededRow = apiResult.History.Rows.FirstOrDefault(row => row.StateId == firstState.Id);
            if (currentRow == null || supersededRow == null)
            {
                throw new InvalidOperationException("person_history_rows_missing");
            }

            report.Cases.Add(new PersonHistoryProofCase
            {
                CaseId = "current_open_row_publication_and_order",
                ScopeKey = report.ScopeKey,
                TrackedPersonId = report.TrackedPersonId,
                StateId = currentRow.StateId,
                ExpectedPublicationState = ExpectedInsufficientEvidence,
                ActualPublicationState = currentRow.PublicationState,
                ExpectedHistoryOrder = expectedOrder,
                ActualHistoryOrder = actualOrder,
                Reason = openVsHistoricalSeparated
                    ? "open_current_row_and_historical_rows_are_separated"
                    : "open_vs_historical_separation_failed",
                Passed = string.Equals(currentRow.PublicationState, ExpectedInsufficientEvidence, StringComparison.Ordinal)
                    && string.Equals(actualOrder, expectedOrder, StringComparison.Ordinal)
                    && openVsHistoricalSeparated
            });

            report.Cases.Add(new PersonHistoryProofCase
            {
                CaseId = "superseded_row_publication_and_order",
                ScopeKey = report.ScopeKey,
                TrackedPersonId = report.TrackedPersonId,
                StateId = supersededRow.StateId,
                ExpectedPublicationState = ExpectedPublishable,
                ActualPublicationState = supersededRow.PublicationState,
                ExpectedHistoryOrder = expectedOrder,
                ActualHistoryOrder = actualOrder,
                Reason = "historical_row_is_retained_in_ordered_history",
                Passed = string.Equals(supersededRow.PublicationState, ExpectedPublishable, StringComparison.Ordinal)
                    && string.Equals(actualOrder, expectedOrder, StringComparison.Ordinal)
                    && openVsHistoricalSeparated
            });

            report.Passed = report.Cases.All(x => x.Passed);
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
            throw new InvalidOperationException("Person-history proof failed.", fatal);
        }

        return report;
    }

    private static OperatorIdentityContext BuildOperatorIdentity(DateTime nowUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = "proof-operator",
            OperatorDisplay = "Proof Operator",
            SurfaceSubject = "person_history_proof",
            AuthSource = "proof",
            AuthTimeUtc = nowUtc.AddMinutes(-1)
        };
    }

    private static OperatorSessionContext BuildOperatorSession(DateTime nowUtc)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = $"person-history-proof:{Guid.NewGuid():N}",
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = nowUtc.AddMinutes(-1),
            LastSeenAtUtc = nowUtc,
            ActiveMode = OperatorModeTypes.ResolutionQueue
        };
    }

    private static async Task<Guid> EnsureTrackedPersonAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string scopeKey,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var operatorPerson = await db.Persons
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.PersonType == "operator" && x.Status == "active")
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        var operatorPersonId = operatorPerson?.Id ?? Guid.NewGuid();
        if (operatorPerson == null)
        {
            db.Persons.Add(new DbPerson
            {
                Id = operatorPersonId,
                ScopeKey = scopeKey,
                PersonType = "operator",
                DisplayName = "000 PHB-006B Proof Operator",
                CanonicalName = "000_phb_006b_proof_operator",
                Status = "active",
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
            await db.SaveChangesAsync(ct);
        }

        var trackedPerson = await db.Persons
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey && x.PersonType == "tracked_person" && x.Status == "active")
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        var trackedPersonId = trackedPerson?.Id ?? Guid.NewGuid();

        if (trackedPerson == null)
        {
            db.Persons.Add(new DbPerson
            {
                Id = trackedPersonId,
                ScopeKey = scopeKey,
                PersonType = "tracked_person",
                DisplayName = "000 PHB-006B Proof Person",
                CanonicalName = "000_phb_006b_proof_person",
                Status = "active",
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
            await db.SaveChangesAsync(ct);
        }
        else if (!trackedPerson.DisplayName.StartsWith("000 ", StringComparison.Ordinal))
        {
            var trackedPersonMutable = await db.Persons.FirstOrDefaultAsync(x => x.Id == trackedPersonId, ct);
            if (trackedPersonMutable != null)
            {
                trackedPersonMutable.DisplayName = "000 PHB-006B Proof Person";
                trackedPersonMutable.CanonicalName = "000_phb_006b_proof_person";
                trackedPersonMutable.UpdatedAt = nowUtc;
                await db.SaveChangesAsync(ct);
            }
        }

        var hasLink = await db.PersonOperatorLinks
            .AsNoTracking()
            .AnyAsync(x =>
                x.ScopeKey == scopeKey
                && x.OperatorPersonId == operatorPersonId
                && x.PersonId == trackedPersonId
                && x.Status == "active",
                ct);
        if (!hasLink)
        {
            db.PersonOperatorLinks.Add(new DbPersonOperatorLink
            {
                ScopeKey = scopeKey,
                OperatorPersonId = operatorPersonId,
                PersonId = trackedPersonId,
                LinkType = "operator_context",
                Status = "active",
                SourceBindingType = "proof",
                SourceBindingValue = "phb-006b",
                SourceBindingNormalized = "phb-006b",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
            await db.SaveChangesAsync(ct);
        }

        return trackedPersonId;
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

        return Path.GetFullPath(Path.Combine(hostArtifactsRoot, "phase-b", "person-history-proof.json"));
    }
}

public sealed class PersonHistoryProofReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("fatal_error")]
    public string? FatalError { get; set; }

    [JsonPropertyName("cases")]
    public List<PersonHistoryProofCase> Cases { get; set; } = [];
}

public sealed class PersonHistoryProofCase
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("state_id")]
    public Guid StateId { get; set; }

    [JsonPropertyName("expected_publication_state")]
    public string ExpectedPublicationState { get; set; } = string.Empty;

    [JsonPropertyName("actual_publication_state")]
    public string ActualPublicationState { get; set; } = string.Empty;

    [JsonPropertyName("expected_history_order")]
    public string ExpectedHistoryOrder { get; set; } = string.Empty;

    [JsonPropertyName("actual_history_order")]
    public string ActualHistoryOrder { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Host.OperatorApi;
using TgAssistant.Host.OperatorWeb;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class CurrentWorldApproximationProofRunner
{
    private const string TriggerKind = "current_world_approximation_proof";
    private const string ActiveStatus = "active";
    private const string PersonTypeOperator = "operator";
    private const string PersonTypeTracked = "tracked_person";

    public static async Task<CurrentWorldApproximationProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new CurrentWorldApproximationProofReport
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

            await RunPublishableCaseAsync(report, appService, temporalRepository, dbFactory, ct);
            await RunDisagreementCaseAsync(report, appService, temporalRepository, dbFactory, ct);
            await RunInsufficientEvidenceCaseAsync(report, appService, dbFactory, ct);
            await RunInactiveDroppedOutCoverageCaseAsync(report, appService, temporalRepository, dbFactory, ct);
            await RunCrossScopeRejectedCaseAsync(report, appService, dbFactory, ct);
            RunWebContractProofRows(report);

            report.Passed = report.Cases.All(x => x.Passed) && report.WebProofRows.All(x => x.Passed);
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
            throw new InvalidOperationException("Current-world approximation proof failed.", fatal);
        }

        return report;
    }

    private static async Task RunPublishableCaseAsync(
        CurrentWorldApproximationProofReport report,
        IOperatorResolutionApplicationService appService,
        ITemporalPersonStateRepository temporalRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("publishable");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var subjectRef = $"person:{trackedPersonId:D}";
        var nowUtc = DateTime.UtcNow;

        await temporalRepository.InsertAsync(
            new TemporalPersonStateWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                SubjectRef = subjectRef,
                FactType = TemporalSingleValuedFactFamilies.ProfileStatus,
                FactCategory = TemporalPersonStateFactCategories.Stable,
                Value = "active",
                ValidFromUtc = nowUtc.AddMinutes(-6),
                StateStatus = TemporalPersonStateStatuses.Open,
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:publishable:status"
            },
            ct);

        await temporalRepository.InsertAsync(
            new TemporalPersonStateWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                SubjectRef = $"{subjectRef}:timeline_episode",
                FactType = TemporalSingleValuedFactFamilies.TimelinePrimaryActivity,
                FactCategory = TemporalPersonStateFactCategories.Temporal,
                Value = "active_now:planning_trip",
                ValidFromUtc = nowUtc.AddMinutes(-5),
                StateStatus = TemporalPersonStateStatuses.Open,
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:publishable:activity"
            },
            ct);

        var session = BuildOperatorSession(nowUtc, trackedPersonId);
        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = session
            },
            ct);

        var actualHttpStatus = ResolveHttpStatus(result.Accepted, result.FailureReason);
        report.Cases.Add(new CurrentWorldApproximationProofCase
        {
            CaseId = "publishable",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            AsOfUtc = result.CurrentWorld.AsOfUtc == default ? nowUtc : result.CurrentWorld.AsOfUtc,
            ExpectedHttpStatus = StatusCodes.Status200OK,
            ActualHttpStatus = actualHttpStatus,
            ExpectedPublicationState = CurrentWorldApproximationPublicationStates.Publishable,
            ActualPublicationState = result.CurrentWorld.PublicationState,
            ActivePersonCount = result.CurrentWorld.ActivePersonCount,
            InactivePersonCount = result.CurrentWorld.InactivePersonCount,
            DroppedOutPersonCount = result.CurrentWorld.DroppedOutPersonCount,
            ActiveRelationCount = result.CurrentWorld.ActiveRelationCount,
            ActiveConditionCount = result.CurrentWorld.ActiveConditionCount,
            RecentChangeCount = result.CurrentWorld.RecentChangeCount,
            Reason = "publishable_snapshot_computed_from_temporal_surfaces",
            Passed = result.Accepted
                && actualHttpStatus == StatusCodes.Status200OK
                && string.Equals(result.CurrentWorld.PublicationState, CurrentWorldApproximationPublicationStates.Publishable, StringComparison.Ordinal)
                && result.CurrentWorld.ActivePersonCount >= 1
                && result.CurrentWorld.ActiveConditionCount >= 1
        });
    }

    private static async Task RunDisagreementCaseAsync(
        CurrentWorldApproximationProofReport report,
        IOperatorResolutionApplicationService appService,
        ITemporalPersonStateRepository temporalRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("disagreement");
        var (operatorPersonId, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;

        await temporalRepository.InsertAsync(
            new TemporalPersonStateWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                SubjectRef = $"person:{trackedPersonId:D}",
                FactType = TemporalSingleValuedFactFamilies.RelationshipState,
                FactCategory = TemporalPersonStateFactCategories.Temporal,
                Value = "together",
                ValidFromUtc = nowUtc.AddMinutes(-4),
                StateStatus = TemporalPersonStateStatuses.Open,
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:disagreement:temporal_relationship"
            },
            ct);

        await SeedPairSurfaceAsync(dbFactory, scopeKey, operatorPersonId, trackedPersonId, ct);

        var session = BuildOperatorSession(nowUtc, trackedPersonId);
        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = session
            },
            ct);

        var actualHttpStatus = ResolveHttpStatus(result.Accepted, result.FailureReason);
        report.Cases.Add(new CurrentWorldApproximationProofCase
        {
            CaseId = "disagreement_unresolved",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            AsOfUtc = result.CurrentWorld.AsOfUtc == default ? nowUtc : result.CurrentWorld.AsOfUtc,
            ExpectedHttpStatus = StatusCodes.Status200OK,
            ActualHttpStatus = actualHttpStatus,
            ExpectedPublicationState = CurrentWorldApproximationPublicationStates.Unresolved,
            ActualPublicationState = result.CurrentWorld.PublicationState,
            ActivePersonCount = result.CurrentWorld.ActivePersonCount,
            InactivePersonCount = result.CurrentWorld.InactivePersonCount,
            DroppedOutPersonCount = result.CurrentWorld.DroppedOutPersonCount,
            ActiveRelationCount = result.CurrentWorld.ActiveRelationCount,
            ActiveConditionCount = result.CurrentWorld.ActiveConditionCount,
            RecentChangeCount = result.CurrentWorld.RecentChangeCount,
            Reason = "temporal_vs_pair_disagreement_surfaces_unresolved_without_winner_selection",
            Passed = result.Accepted
                && actualHttpStatus == StatusCodes.Status200OK
                && string.Equals(result.CurrentWorld.PublicationState, CurrentWorldApproximationPublicationStates.Unresolved, StringComparison.Ordinal)
                && result.CurrentWorld.UncertaintyRefs.Any(x => string.Equals(x, "current_world:unresolved:temporal_vs_pair_relationship_disagreement", StringComparison.Ordinal))
        });
    }

    private static async Task RunInsufficientEvidenceCaseAsync(
        CurrentWorldApproximationProofReport report,
        IOperatorResolutionApplicationService appService,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("insufficient");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;

        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = BuildOperatorSession(nowUtc, trackedPersonId)
            },
            ct);

        var actualHttpStatus = ResolveHttpStatus(result.Accepted, result.FailureReason);
        report.Cases.Add(new CurrentWorldApproximationProofCase
        {
            CaseId = "insufficient_evidence",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            AsOfUtc = result.CurrentWorld.AsOfUtc == default ? nowUtc : result.CurrentWorld.AsOfUtc,
            ExpectedHttpStatus = StatusCodes.Status200OK,
            ActualHttpStatus = actualHttpStatus,
            ExpectedPublicationState = CurrentWorldApproximationPublicationStates.InsufficientEvidence,
            ActualPublicationState = result.CurrentWorld.PublicationState,
            ActivePersonCount = result.CurrentWorld.ActivePersonCount,
            InactivePersonCount = result.CurrentWorld.InactivePersonCount,
            DroppedOutPersonCount = result.CurrentWorld.DroppedOutPersonCount,
            ActiveRelationCount = result.CurrentWorld.ActiveRelationCount,
            ActiveConditionCount = result.CurrentWorld.ActiveConditionCount,
            RecentChangeCount = result.CurrentWorld.RecentChangeCount,
            Reason = "insufficient_evidence_is_explicit_when_publishable_content_is_empty",
            Passed = result.Accepted
                && actualHttpStatus == StatusCodes.Status200OK
                && string.Equals(result.CurrentWorld.PublicationState, CurrentWorldApproximationPublicationStates.InsufficientEvidence, StringComparison.Ordinal)
                && result.CurrentWorld.ActivePersonCount == 0
                && result.CurrentWorld.InactivePersonCount == 0
                && result.CurrentWorld.ActiveRelationCount == 0
                && result.CurrentWorld.ActiveConditionCount == 0
        });
    }

    private static async Task RunInactiveDroppedOutCoverageCaseAsync(
        CurrentWorldApproximationProofReport report,
        IOperatorResolutionApplicationService appService,
        ITemporalPersonStateRepository temporalRepository,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("inactive");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;

        await temporalRepository.InsertAsync(
            new TemporalPersonStateWriteRequest
            {
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                SubjectRef = $"person:{trackedPersonId:D}",
                FactType = TemporalSingleValuedFactFamilies.ProfileStatus,
                FactCategory = TemporalPersonStateFactCategories.Stable,
                Value = "dropped_out",
                ValidFromUtc = nowUtc.AddMinutes(-3),
                StateStatus = TemporalPersonStateStatuses.Open,
                EvidenceRefs = [$"evidence:{Guid.NewGuid():D}"],
                TriggerKind = TriggerKind,
                TriggerRef = $"{TriggerKind}:inactive:dropped_out"
            },
            ct);

        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = BuildOperatorSession(nowUtc, trackedPersonId)
            },
            ct);

        var actualHttpStatus = ResolveHttpStatus(result.Accepted, result.FailureReason);
        report.Cases.Add(new CurrentWorldApproximationProofCase
        {
            CaseId = "inactive_or_dropped_out_person_coverage",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            AsOfUtc = result.CurrentWorld.AsOfUtc == default ? nowUtc : result.CurrentWorld.AsOfUtc,
            ExpectedHttpStatus = StatusCodes.Status200OK,
            ActualHttpStatus = actualHttpStatus,
            ExpectedPublicationState = CurrentWorldApproximationPublicationStates.Publishable,
            ActualPublicationState = result.CurrentWorld.PublicationState,
            ActivePersonCount = result.CurrentWorld.ActivePersonCount,
            InactivePersonCount = result.CurrentWorld.InactivePersonCount,
            DroppedOutPersonCount = result.CurrentWorld.DroppedOutPersonCount,
            ActiveRelationCount = result.CurrentWorld.ActiveRelationCount,
            ActiveConditionCount = result.CurrentWorld.ActiveConditionCount,
            RecentChangeCount = result.CurrentWorld.RecentChangeCount,
            Reason = "inactive_and_dropped_out_people_are_explicitly_surfaced",
            Passed = result.Accepted
                && actualHttpStatus == StatusCodes.Status200OK
                && result.CurrentWorld.InactivePersonCount >= 1
                && result.CurrentWorld.DroppedOutPersonCount >= 1
        });
    }

    private static async Task RunCrossScopeRejectedCaseAsync(
        CurrentWorldApproximationProofReport report,
        IOperatorResolutionApplicationService appService,
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        CancellationToken ct)
    {
        var scopeKey = BuildScopeKey("cross_scope");
        var (_, trackedPersonId) = await EnsureTrackedPersonAsync(dbFactory, scopeKey, ct);
        var nowUtc = DateTime.UtcNow;
        var mismatchedTrackedPersonId = Guid.NewGuid();

        var result = await appService.QueryPersonWorkspaceCurrentWorldAsync(
            new OperatorPersonWorkspaceCurrentWorldQueryRequest
            {
                TrackedPersonId = trackedPersonId,
                OperatorIdentity = BuildOperatorIdentity(nowUtc),
                Session = BuildOperatorSession(nowUtc, mismatchedTrackedPersonId)
            },
            ct);

        var actualHttpStatus = ResolveHttpStatus(result.Accepted, result.FailureReason);
        report.Cases.Add(new CurrentWorldApproximationProofCase
        {
            CaseId = "unauth_or_cross_scope_rejected",
            ScopeKey = scopeKey,
            TrackedPersonId = trackedPersonId,
            AsOfUtc = nowUtc,
            ExpectedHttpStatus = StatusCodes.Status403Forbidden,
            ActualHttpStatus = actualHttpStatus,
            ExpectedPublicationState = "session_active_tracked_person_mismatch",
            ActualPublicationState = result.FailureReason ?? "accepted",
            ActivePersonCount = 0,
            InactivePersonCount = 0,
            DroppedOutPersonCount = 0,
            ActiveRelationCount = 0,
            ActiveConditionCount = 0,
            RecentChangeCount = 0,
            Reason = "current_world_query_rejects_cross_scope_session_mismatch",
            Passed = !result.Accepted
                && actualHttpStatus == StatusCodes.Status403Forbidden
                && string.Equals(result.FailureReason, "session_active_tracked_person_mismatch", StringComparison.Ordinal)
        });
    }

    private static int ResolveHttpStatus(bool accepted, string? failureReason)
        => accepted ? StatusCodes.Status200OK : OperatorApiEndpointExtensions.MapFailureStatusCodeForTesting(failureReason);

    private static void RunWebContractProofRows(CurrentWorldApproximationProofReport report)
    {
        var html = typeof(OperatorWebEndpointExtensions)
            .GetField("OperatorPersonWorkspaceShellHtml", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.GetRawConstantValue() as string;

        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("Current-world approximation web proof failed: person-workspace shell HTML constant not found.");
        }

        AddWebProofRow(
            report,
            caseId: "publishable",
            checkId: "current_world_tab_marker_present",
            reason: "web_shell_exposes_current_world_tab_and_content_region",
            passed: html.Contains("id=\"tab-current-world\"", StringComparison.Ordinal)
                && html.Contains("id=\"current-world-content\"", StringComparison.Ordinal));

        AddWebProofRow(
            report,
            caseId: "publishable",
            checkId: "current_world_api_route_wired",
            reason: "web_shell_calls_bounded_current_world_api_route",
            passed: html.Contains("operatorPostJson(\"/api/operator/person-workspace/current-world/query\"", StringComparison.Ordinal));

        AddWebProofRow(
            report,
            caseId: "publishable",
            checkId: "current_world_publishable_honesty_copy",
            reason: "web_shell_surfaces_publishable_state_copy_without_claiming_semantic_ownership",
            passed: html.Contains("Current-world output is publishable and composed from bounded temporal/pair/timeline read surfaces.", StringComparison.Ordinal));

        AddWebProofRow(
            report,
            caseId: "disagreement_unresolved",
            checkId: "current_world_unresolved_honesty_copy",
            reason: "web_shell_surfaces_unresolved_disagreement_without_winner_selection",
            passed: html.Contains("Current-world output is unresolved due to cross-surface disagreement; no semantic winner is selected.", StringComparison.Ordinal));

        AddWebProofRow(
            report,
            caseId: "insufficient_evidence",
            checkId: "current_world_insufficient_evidence_honesty_copy",
            reason: "web_shell_surfaces_insufficient_evidence_honesty_state",
            passed: html.Contains("Current-world output is explicitly insufficient_evidence; publishable content is intentionally withheld.", StringComparison.Ordinal));

        AddWebProofRow(
            report,
            caseId: "inactive_or_dropped_out_person_coverage",
            checkId: "current_world_inactive_dropped_out_render_contract",
            reason: "web_shell_has_explicit_inactive_and_dropped_out_render_copy",
            passed: html.Contains("<h3>Inactive Person:", StringComparison.Ordinal)
                && html.Contains("<p><strong>Dropped out:</strong>", StringComparison.Ordinal));
    }

    private static void AddWebProofRow(
        CurrentWorldApproximationProofReport report,
        string caseId,
        string checkId,
        string reason,
        bool passed)
    {
        report.WebProofRows.Add(new CurrentWorldApproximationWebProofRow
        {
            CaseId = caseId,
            CheckId = checkId,
            Reason = reason,
            Passed = passed
        });
    }

    private static string BuildScopeKey(string caseKey)
        => $"proof:phb_014a:{caseKey}:{DateTime.UtcNow:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";

    private static OperatorIdentityContext BuildOperatorIdentity(DateTime nowUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = "proof-operator",
            OperatorDisplay = "Proof Operator",
            SurfaceSubject = "current_world_approximation_proof",
            AuthSource = "proof",
            AuthTimeUtc = nowUtc.AddMinutes(-1)
        };
    }

    private static OperatorSessionContext BuildOperatorSession(DateTime nowUtc, Guid activeTrackedPersonId)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = $"current-world-proof:{Guid.NewGuid():N}",
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = nowUtc.AddMinutes(-1),
            LastSeenAtUtc = nowUtc,
            ActiveMode = OperatorModeTypes.ResolutionQueue,
            ActiveTrackedPersonId = activeTrackedPersonId
        };
    }

    private static async Task<(Guid OperatorPersonId, Guid TrackedPersonId)> EnsureTrackedPersonAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string scopeKey,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var operatorPerson = await db.Persons
            .FirstOrDefaultAsync(x =>
                x.ScopeKey == scopeKey
                && x.PersonType == PersonTypeOperator
                && x.Status == ActiveStatus,
                ct);
        if (operatorPerson == null)
        {
            operatorPerson = new DbPerson
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                PersonType = PersonTypeOperator,
                DisplayName = "PHB-014A Proof Operator",
                CanonicalName = "phb_014a_proof_operator",
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            db.Persons.Add(operatorPerson);
        }

        var trackedPerson = await db.Persons
            .FirstOrDefaultAsync(x =>
                x.ScopeKey == scopeKey
                && x.PersonType == PersonTypeTracked
                && x.Status == ActiveStatus,
                ct);
        if (trackedPerson == null)
        {
            trackedPerson = new DbPerson
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                PersonType = PersonTypeTracked,
                DisplayName = "PHB-014A Proof Tracked Person",
                CanonicalName = "phb_014a_proof_tracked_person",
                Status = ActiveStatus,
                MetadataJson = "{}",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };
            db.Persons.Add(trackedPerson);
        }

        await db.SaveChangesAsync(ct);

        var operatorLink = await db.PersonOperatorLinks
            .FirstOrDefaultAsync(x =>
                x.ScopeKey == scopeKey
                && x.OperatorPersonId == operatorPerson.Id
                && x.PersonId == trackedPerson.Id
                && x.Status == ActiveStatus,
                ct);
        if (operatorLink == null)
        {
            db.PersonOperatorLinks.Add(new DbPersonOperatorLink
            {
                ScopeKey = scopeKey,
                OperatorPersonId = operatorPerson.Id,
                PersonId = trackedPerson.Id,
                LinkType = "operator_tracked",
                Status = ActiveStatus,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
        }

        await db.SaveChangesAsync(ct);
        return (operatorPerson.Id, trackedPerson.Id);
    }

    private static async Task SeedPairSurfaceAsync(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        string scopeKey,
        Guid operatorPersonId,
        Guid trackedPersonId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var nowUtc = DateTime.UtcNow;

        var metadata = new DbDurableObjectMetadata
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            ObjectFamily = Stage7DurableObjectFamilies.PairDynamics,
            ObjectKey = $"pair:{operatorPersonId:D}:{trackedPersonId:D}:{Stage7PairDynamicsTypes.OperatorTrackedPair}",
            Status = ActiveStatus,
            TruthLayer = "working",
            PromotionState = "accepted",
            OwnerPersonId = trackedPersonId,
            RelatedPersonId = operatorPersonId,
            Confidence = 0.71f,
            Coverage = 0.65f,
            Freshness = 0.74f,
            Stability = 0.59f,
            DecayPolicyJson = "{}",
            ContradictionMarkersJson = "[]",
            MetadataJson = "{}",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };

        db.DurableObjectMetadata.Add(metadata);
        db.DurablePairDynamics.Add(new DbDurablePairDynamics
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            LeftPersonId = operatorPersonId,
            RightPersonId = trackedPersonId,
            DurableObjectMetadataId = metadata.Id,
            PairDynamicsType = Stage7PairDynamicsTypes.OperatorTrackedPair,
            Status = ActiveStatus,
            CurrentRevisionNumber = 1,
            CurrentRevisionHash = $"proof:{Guid.NewGuid():N}",
            SummaryJson = "{}",
            PayloadJson = JsonSerializer.Serialize(new
            {
                dimensions = new[]
                {
                    new
                    {
                        key = TemporalSingleValuedFactFamilies.RelationshipState,
                        value = "separated"
                    }
                }
            }),
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        });

        await db.SaveChangesAsync(ct);
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath.Trim());
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "artifacts",
            "phase-b",
            "current-world-approximation-proof.json"));
    }
}

public sealed class CurrentWorldApproximationProofReport
{
    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("output_path")]
    public string OutputPath { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("fatal_error")]
    public string? FatalError { get; set; }

    [JsonPropertyName("cases")]
    public List<CurrentWorldApproximationProofCase> Cases { get; set; } = [];

    [JsonPropertyName("web_proof_rows")]
    public List<CurrentWorldApproximationWebProofRow> WebProofRows { get; set; } = [];
}

public sealed class CurrentWorldApproximationProofCase
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("as_of_utc")]
    public DateTime AsOfUtc { get; set; }

    [JsonPropertyName("expected_http_status")]
    public int ExpectedHttpStatus { get; set; }

    [JsonPropertyName("actual_http_status")]
    public int ActualHttpStatus { get; set; }

    [JsonPropertyName("expected_publication_state")]
    public string ExpectedPublicationState { get; set; } = string.Empty;

    [JsonPropertyName("actual_publication_state")]
    public string ActualPublicationState { get; set; } = string.Empty;

    [JsonPropertyName("active_person_count")]
    public int ActivePersonCount { get; set; }

    [JsonPropertyName("inactive_person_count")]
    public int InactivePersonCount { get; set; }

    [JsonPropertyName("dropped_out_person_count")]
    public int DroppedOutPersonCount { get; set; }

    [JsonPropertyName("active_relation_count")]
    public int ActiveRelationCount { get; set; }

    [JsonPropertyName("active_condition_count")]
    public int ActiveConditionCount { get; set; }

    [JsonPropertyName("recent_change_count")]
    public int RecentChangeCount { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}

public sealed class CurrentWorldApproximationWebProofRow
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("check_id")]
    public string CheckId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}

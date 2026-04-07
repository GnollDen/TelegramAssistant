using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class IterativeReintegrationProofRunner
{
    public static async Task<IterativeReintegrationProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new IterativeReintegrationProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath
        };

        Exception? fatal = null;
        try
        {
            using var scope = services.CreateScope();
            var reintegrationService = scope.ServiceProvider.GetRequiredService<IResolutionCaseReintegrationService>();
            var stage8RecomputeQueueRepository = scope.ServiceProvider.GetRequiredService<IStage8RecomputeQueueRepository>();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();

            report.ScopeKey = $"proof:phb_009:{DateTime.UtcNow:yyyyMMddHHmmssfff}:{Guid.NewGuid():N}";
            report.TrackedPersonId = await EnsureTrackedPersonAsync(dbFactory, report.ScopeKey, ct);

            var unresolvedScopeItemKey = "scope_item:unresolved_to_pass2";
            var unresolvedPass1 = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = unresolvedScopeItemKey,
                    TrackedPersonId = report.TrackedPersonId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.NeedsMoreContext,
                    UnresolvedResidueJson = """{"reason":"missing_evidence","owner":"proof"}"""
                },
                ct);

            report.Rows.Add(BuildAllowedRow(
                caseId: "unresolved_to_pass2",
                passIndex: 1,
                entry: unresolvedPass1,
                expectedPreviousStatus: null,
                expectedNextStatus: IterativeCaseStatuses.NeedsMoreContext,
                expectedCarryForwardCaseId: unresolvedPass1.CarryForwardCaseId,
                reason: "pass1_unresolved_recorded"));

            var unresolvedPass2 = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = unresolvedScopeItemKey,
                    TrackedPersonId = report.TrackedPersonId,
                    CarryForwardCaseId = unresolvedPass1.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.Stage8RecomputeRequest,
                    NextStatus = IterativeCaseStatuses.DeferredToNextPass,
                    ExpectedPreviousLedgerEntryId = unresolvedPass1.Id,
                    UnresolvedResidueJson = """{"reason":"waiting_for_next_pass","owner":"proof"}"""
                },
                ct);

            report.Rows.Add(BuildAllowedRow(
                caseId: "unresolved_to_pass2",
                passIndex: 2,
                entry: unresolvedPass2,
                expectedPreviousStatus: IterativeCaseStatuses.NeedsMoreContext,
                expectedNextStatus: IterativeCaseStatuses.DeferredToNextPass,
                expectedCarryForwardCaseId: unresolvedPass1.CarryForwardCaseId,
                reason: "pass2_deferred_with_stable_case_id"));

            var unresolvedPass3 = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = unresolvedScopeItemKey,
                    TrackedPersonId = report.TrackedPersonId,
                    CarryForwardCaseId = unresolvedPass1.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.Stage8RecomputeRequest,
                    NextStatus = IterativeCaseStatuses.ResolvingAi,
                    ExpectedPreviousLedgerEntryId = unresolvedPass2.Id
                },
                ct);

            report.Rows.Add(BuildAllowedRow(
                caseId: "unresolved_to_pass2",
                passIndex: 3,
                entry: unresolvedPass3,
                expectedPreviousStatus: IterativeCaseStatuses.DeferredToNextPass,
                expectedNextStatus: IterativeCaseStatuses.ResolvingAi,
                expectedCarryForwardCaseId: unresolvedPass1.CarryForwardCaseId,
                reason: "pass3_reentered_ai_resolution_with_stable_case_id"));

            var resolvedScopeItemKey = "scope_item:resolved_by_ai_in_pass2";
            var resolvedPass1 = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = resolvedScopeItemKey,
                    TrackedPersonId = report.TrackedPersonId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.DeferredToNextPass,
                    UnresolvedResidueJson = """{"reason":"insufficient_confidence","owner":"proof"}"""
                },
                ct);

            report.Rows.Add(BuildAllowedRow(
                caseId: "resolved_by_ai_in_pass2",
                passIndex: 1,
                entry: resolvedPass1,
                expectedPreviousStatus: null,
                expectedNextStatus: IterativeCaseStatuses.DeferredToNextPass,
                expectedCarryForwardCaseId: resolvedPass1.CarryForwardCaseId,
                reason: "pass1_deferred_recorded"));

            var resolvedPass2 = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = resolvedScopeItemKey,
                    TrackedPersonId = report.TrackedPersonId,
                    CarryForwardCaseId = resolvedPass1.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.Stage8RecomputeRequest,
                    NextStatus = IterativeCaseStatuses.ResolvingAi,
                    ExpectedPreviousLedgerEntryId = resolvedPass1.Id
                },
                ct);

            report.Rows.Add(BuildAllowedRow(
                caseId: "resolved_by_ai_in_pass2",
                passIndex: 2,
                entry: resolvedPass2,
                expectedPreviousStatus: IterativeCaseStatuses.DeferredToNextPass,
                expectedNextStatus: IterativeCaseStatuses.ResolvingAi,
                expectedCarryForwardCaseId: resolvedPass1.CarryForwardCaseId,
                reason: "pass2_started_ai_resolution"));

            var recomputeQueueItem = await stage8RecomputeQueueRepository.EnqueueAsync(
                new Stage8RecomputeQueueRequest
                {
                    ScopeKey = report.ScopeKey,
                    PersonId = report.TrackedPersonId,
                    TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                    TriggerKind = "iterative_reintegration_proof",
                    TriggerRef = $"iterative-reintegration-proof:{resolvedPass2.Id:D}",
                    Priority = 20
                },
                ct);
            var resolvedPass3 = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = resolvedScopeItemKey,
                    TrackedPersonId = report.TrackedPersonId,
                    CarryForwardCaseId = resolvedPass1.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.ResolvedByAi,
                    ExpectedPreviousLedgerEntryId = resolvedPass2.Id,
                    RecomputeQueueItemId = recomputeQueueItem.Id,
                    RecomputeTargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                    RecomputeTargetRef = $"person:{report.TrackedPersonId:D}"
                },
                ct);

            report.Rows.Add(BuildAllowedRow(
                caseId: "resolved_by_ai_in_pass2",
                passIndex: 3,
                entry: resolvedPass3,
                expectedPreviousStatus: IterativeCaseStatuses.ResolvingAi,
                expectedNextStatus: IterativeCaseStatuses.ResolvedByAi,
                expectedCarryForwardCaseId: resolvedPass1.CarryForwardCaseId,
                reason: "pass2_resolved_by_ai_with_recompute_linkage"));

            var crossScopeBase = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = "scope_item:cross_scope_a",
                    TrackedPersonId = report.TrackedPersonId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.Open
                },
                ct);

            report.Rows.Add(await BuildRejectedRowAsync(
                reintegrationService,
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = "scope_item:cross_scope_b",
                    TrackedPersonId = report.TrackedPersonId,
                    CarryForwardCaseId = crossScopeBase.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.ResolvingAi,
                    ExpectedPreviousLedgerEntryId = crossScopeBase.Id
                },
                caseId: ReintegrationLedgerFailureReasons.CrossScopeLinkageRejected,
                passIndex: 2,
                expectedReason: ReintegrationLedgerFailureReasons.CrossScopeLinkageRejected,
                ct: ct));

            var staleBase = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = "scope_item:stale_linkage",
                    TrackedPersonId = report.TrackedPersonId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.Open
                },
                ct);
            var staleLatest = await reintegrationService.RecordAsync(
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = staleBase.ScopeItemKey,
                    TrackedPersonId = staleBase.TrackedPersonId,
                    CarryForwardCaseId = staleBase.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.Stage8RecomputeRequest,
                    NextStatus = IterativeCaseStatuses.ResolvingAi,
                    ExpectedPreviousLedgerEntryId = staleBase.Id
                },
                ct);

            report.Rows.Add(await BuildRejectedRowAsync(
                reintegrationService,
                new ResolutionCaseReintegrationRecordRequest
                {
                    ScopeKey = report.ScopeKey,
                    ScopeItemKey = staleBase.ScopeItemKey,
                    TrackedPersonId = staleBase.TrackedPersonId,
                    CarryForwardCaseId = staleBase.CarryForwardCaseId,
                    OriginSourceKind = ReintegrationOriginSourceKinds.ResolutionAction,
                    NextStatus = IterativeCaseStatuses.DeferredToNextPass,
                    ExpectedPreviousLedgerEntryId = staleBase.Id
                },
                caseId: ReintegrationLedgerFailureReasons.StaleRecomputeLinkageRejected,
                passIndex: 3,
                expectedReason: ReintegrationLedgerFailureReasons.StaleRecomputeLinkageRejected,
                latestEntry: staleLatest,
                ct));

            report.Passed = report.Rows.All(row => row.Passed);
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
            throw new InvalidOperationException("Iterative reintegration proof failed.", fatal);
        }

        return report;
    }

    private static async Task<IterativeReintegrationProofRow> BuildRejectedRowAsync(
        IResolutionCaseReintegrationService reintegrationService,
        ResolutionCaseReintegrationRecordRequest rejectedRequest,
        string caseId,
        int passIndex,
        string expectedReason,
        ResolutionCaseReintegrationLedgerEntry? latestEntry = null,
        CancellationToken ct = default)
    {
        string actualReason;
        try
        {
            _ = await reintegrationService.RecordAsync(rejectedRequest, ct);
            actualReason = "accepted";
        }
        catch (InvalidOperationException ex)
        {
            actualReason = ex.Message.Trim();
        }

        var passed = string.Equals(actualReason, expectedReason, StringComparison.Ordinal);
        return new IterativeReintegrationProofRow
        {
            CaseId = caseId,
            PassIndex = passIndex,
            ScopeKey = rejectedRequest.ScopeKey,
            ScopeItemKey = rejectedRequest.ScopeItemKey,
            CarryForwardCaseId = rejectedRequest.CarryForwardCaseId,
            LedgerEntryId = null,
            PreviousStatus = latestEntry?.NextStatus,
            NextStatus = rejectedRequest.NextStatus,
            RecomputeTargetFamily = rejectedRequest.RecomputeTargetFamily,
            RecomputeTargetRef = rejectedRequest.RecomputeTargetRef,
            ExpectedDecision = "reject",
            ActualDecision = string.Equals(actualReason, "accepted", StringComparison.Ordinal) ? "allow" : "reject",
            Reason = actualReason,
            Passed = passed
        };
    }

    private static IterativeReintegrationProofRow BuildAllowedRow(
        string caseId,
        int passIndex,
        ResolutionCaseReintegrationLedgerEntry entry,
        string? expectedPreviousStatus,
        string expectedNextStatus,
        string expectedCarryForwardCaseId,
        string reason)
    {
        var previousMatches = string.Equals(entry.PreviousStatus, expectedPreviousStatus, StringComparison.Ordinal);
        var nextMatches = string.Equals(entry.NextStatus, expectedNextStatus, StringComparison.Ordinal);
        var carryMatches = string.Equals(entry.CarryForwardCaseId, expectedCarryForwardCaseId, StringComparison.Ordinal);
        var linkageValid = passIndex == 1
            ? entry.PredecessorLedgerEntryId == null
            : entry.PredecessorLedgerEntryId.HasValue;
        var passed = previousMatches && nextMatches && carryMatches && linkageValid;

        return new IterativeReintegrationProofRow
        {
            CaseId = caseId,
            PassIndex = passIndex,
            ScopeKey = entry.ScopeKey,
            ScopeItemKey = entry.ScopeItemKey,
            CarryForwardCaseId = entry.CarryForwardCaseId,
            LedgerEntryId = entry.Id,
            PreviousStatus = entry.PreviousStatus,
            NextStatus = entry.NextStatus,
            RecomputeTargetFamily = entry.RecomputeTargetFamily,
            RecomputeTargetRef = entry.RecomputeTargetRef,
            ExpectedDecision = "allow",
            ActualDecision = "allow",
            Reason = passed ? reason : "transition_or_identity_mismatch",
            Passed = passed
        };
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

        var nowUtc = DateTime.UtcNow;
        var person = new DbPerson
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            PersonType = "tracked_person",
            DisplayName = "PHB-009 Proof Person",
            CanonicalName = "phb_009_proof_person",
            Status = "active",
            MetadataJson = "{}",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };

        db.Persons.Add(person);
        await db.SaveChangesAsync(ct);
        return person.Id;
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
            "iterative-pass-reintegration-proof.json"));
    }
}

public sealed class IterativeReintegrationProofReport
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

    [JsonPropertyName("rows")]
    public List<IterativeReintegrationProofRow> Rows { get; set; } = [];
}

public sealed class IterativeReintegrationProofRow
{
    [JsonPropertyName("case_id")]
    public string CaseId { get; set; } = string.Empty;

    [JsonPropertyName("pass_index")]
    public int PassIndex { get; set; }

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("scope_item_key")]
    public string ScopeItemKey { get; set; } = string.Empty;

    [JsonPropertyName("carry_forward_case_id")]
    public string CarryForwardCaseId { get; set; } = string.Empty;

    [JsonPropertyName("ledger_entry_id")]
    public Guid? LedgerEntryId { get; set; }

    [JsonPropertyName("previous_status")]
    public string? PreviousStatus { get; set; }

    [JsonPropertyName("next_status")]
    public string NextStatus { get; set; } = string.Empty;

    [JsonPropertyName("recompute_target_family")]
    public string? RecomputeTargetFamily { get; set; }

    [JsonPropertyName("recompute_target_ref")]
    public string? RecomputeTargetRef { get; set; }

    [JsonPropertyName("expected_decision")]
    public string ExpectedDecision { get; set; } = string.Empty;

    [JsonPropertyName("actual_decision")]
    public string ActualDecision { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }
}

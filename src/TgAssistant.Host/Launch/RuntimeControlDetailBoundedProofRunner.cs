using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class RuntimeControlDetailBoundedProofRunner
{
    private const string ScopeKey = RuntimeControlInterpretationPublicationGuard.CanonicalScopeKey;
    private const string ReviewOnlyScopeItemKey = RuntimeControlInterpretationPublicationGuard.ReviewOnlyScopeItemKey;
    private const string PromotionBlockedScopeItemKey = "review:runtime_control_state:promotion_blocked";

    public static async Task<RuntimeControlDetailBoundedProofReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new RuntimeControlDetailBoundedProofReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            ScopeKey = ScopeKey,
            ReviewOnlyScopeItemKey = ReviewOnlyScopeItemKey,
            PromotionBlockedScopeItemKey = PromotionBlockedScopeItemKey
        };

        Exception? fatal = null;
        try
        {
            using var scope = services.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TgAssistantDbContext>>();
            var readService = scope.ServiceProvider.GetRequiredService<IResolutionReadService>();

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var trackedPerson = await db.Persons
                .AsNoTracking()
                .Where(x => x.ScopeKey == ScopeKey
                    && x.Status == "active"
                    && x.PersonType == "tracked_person")
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(ct);
            Ensure(trackedPerson != null, $"Active tracked person was not found for scope '{ScopeKey}'.");

            report.TrackedPersonId = trackedPerson!.Id;
            report.ActiveRuntimeControlState = await db.RuntimeControlStates
                .AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.State)
                .FirstOrDefaultAsync(ct);

            var reviewOnly = await readService.GetDetailAsync(
                new ResolutionDetailRequest
                {
                    TrackedPersonId = trackedPerson.Id,
                    ScopeItemKey = ReviewOnlyScopeItemKey,
                    EvidenceLimit = 5,
                    EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                    EvidenceSortDirection = ResolutionSortDirections.Desc,
                    IncludeInterpretation = true
                },
                ct);
            report.ReviewOnly = reviewOnly.ItemFound && reviewOnly.Item != null
                ? BuildExcerpt(reviewOnly, ReviewOnlyScopeItemKey, "live")
                : await BuildCapturedReviewOnlyReplayAsync(ct);

            Ensure(reviewOnly.ScopeBound, $"Review-only proof is out of bounded scope: {reviewOnly.ScopeFailureReason ?? "unknown"}.");
            Ensure(report.ReviewOnly.ClaimsAndEvidenceEmpty, "Review-only proof no longer reproduces the empty claims/evidence condition.");
            Ensure(report.ReviewOnly.MatchesInsufficientEvidenceFallback, "Review-only detail did not publish the deterministic insufficient-evidence fallback.");

            var promotionBlocked = await readService.GetDetailAsync(
                new ResolutionDetailRequest
                {
                    TrackedPersonId = trackedPerson.Id,
                    ScopeItemKey = PromotionBlockedScopeItemKey,
                    EvidenceLimit = 5,
                    EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                    EvidenceSortDirection = ResolutionSortDirections.Desc,
                    IncludeInterpretation = true
                },
                ct);
            report.PromotionBlocked = BuildExcerpt(promotionBlocked, PromotionBlockedScopeItemKey, "live");

            if (promotionBlocked.ItemFound && promotionBlocked.Item?.InterpretationLoop != null)
            {
                var loop = promotionBlocked.Item.InterpretationLoop;
                report.PromotionBlocked.LiveItemPresent = true;
                report.PromotionBlocked.HasGroundedInterpretation = loop.KeyClaims.Count > 0 && loop.EvidenceRefsUsed.Count > 0;
                Ensure(report.PromotionBlocked.HasGroundedInterpretation, "Promotion-blocked detail is present but no longer has grounded claims/evidence.");
                Ensure(!report.PromotionBlocked.MatchesInsufficientEvidenceFallback, "Promotion-blocked detail was incorrectly suppressed by the bounded runtime-control guard.");
            }
            else
            {
                report.PromotionBlocked.LiveItemPresent = false;
                report.PromotionBlocked.PresenceNote = $"Current active runtime state is '{report.ActiveRuntimeControlState ?? "unknown"}', so the live promotion_blocked item is not materialized in this proof run.";
            }

            var disabledScopeItemKey = ResolveDisabledProofScopeItemKey(reviewOnly, promotionBlocked);
            var disabledReadService = new ResolutionReadProjectionService(
                dbFactory,
                new ThrowingResolutionInterpretationLoopService(),
                Options.Create(new ResolutionInterpretationLoopSettings
                {
                    Enabled = false,
                    CanonicalScopeOnly = true,
                    CanonicalScopeKey = ScopeKey
                }),
                NullLogger<ResolutionReadProjectionService>.Instance);
            var disabledDetail = await disabledReadService.GetDetailAsync(
                new ResolutionDetailRequest
                {
                    TrackedPersonId = trackedPerson.Id,
                    ScopeItemKey = disabledScopeItemKey,
                    EvidenceLimit = 5,
                    EvidenceSortBy = ResolutionEvidenceSortFields.ObservedAt,
                    EvidenceSortDirection = ResolutionSortDirections.Desc,
                    IncludeInterpretation = true
                },
                ct);
            report.DisabledLoop = BuildExcerpt(disabledDetail, disabledScopeItemKey, "disabled_loop");
            var disabledLoop = disabledDetail.Item?.InterpretationLoop;
            Ensure(disabledLoop != null, "Disabled-loop proof did not include interpretation payload.");
            Ensure(disabledLoop.Applied, "Disabled-loop interpretation payload is not applied.");
            Ensure(disabledLoop.UsedFallback, "Disabled-loop interpretation payload must be fallback.");
            Ensure(string.Equals(disabledLoop.FailureReason, "loop_disabled", StringComparison.Ordinal), "Disabled-loop failure_reason must be exactly 'loop_disabled'.");
            Ensure(disabledLoop.AuditTrail.Count > 0, "Disabled-loop fallback must include interpretation audit trail.");
            var disabledAudit = disabledLoop.AuditTrail[0];
            Ensure(disabledAudit.Provider is null, "Disabled-loop audit provider must be null.");
            Ensure(disabledAudit.Model is null, "Disabled-loop audit model must be null.");
            Ensure(disabledAudit.RequestId is null, "Disabled-loop audit request_id must be null.");
            Ensure(disabledAudit.LatencyMs is null, "Disabled-loop audit latency_ms must be null.");
            Ensure(disabledAudit.PromptTokens is null, "Disabled-loop audit prompt_tokens must be null.");
            Ensure(disabledAudit.CompletionTokens is null, "Disabled-loop audit completion_tokens must be null.");
            Ensure(disabledAudit.TotalTokens is null, "Disabled-loop audit total_tokens must be null.");
            Ensure(disabledAudit.CostUsd is null, "Disabled-loop audit cost_usd must be null.");

            report.Passed = true;
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
                "Runtime-control bounded proof failed: review_only fallback behavior is incomplete.",
                fatal);
        }

        return report;
    }

    private static RuntimeControlDetailProofExcerpt BuildExcerpt(
        ResolutionDetailResult detail,
        string scopeItemKey,
        string proofMode)
    {
        var item = detail.Item;
        var loop = item?.InterpretationLoop;
        var claimsEmptyAndEvidenceEmpty = loop != null
            && loop.KeyClaims.Count == 0
            && loop.EvidenceRefsUsed.Count == 0;

        return new RuntimeControlDetailProofExcerpt
        {
            ScopeItemKey = scopeItemKey,
            ProofMode = proofMode,
            ScopeBound = detail.ScopeBound,
            ScopeFailureReason = detail.ScopeFailureReason,
            ItemFound = detail.ItemFound,
            SourceKind = item?.SourceKind,
            SourceRef = item?.SourceRef,
            EvidenceRationaleSummary = item?.EvidenceRationaleSummary,
            AutoResolutionGap = item?.AutoResolutionGap,
            OperatorDecisionFocus = item?.OperatorDecisionFocus,
            RationaleIsHeuristic = item?.RationaleIsHeuristic,
            InterpretationApplied = loop?.Applied,
            InterpretationUsedFallback = loop?.UsedFallback,
            InterpretationSummary = loop?.InterpretationSummary,
            KeyClaims = loop?.KeyClaims.Select(x => x.Summary).ToList() ?? [],
            EvidenceRefsUsed = loop?.EvidenceRefsUsed.ToList() ?? [],
            ReviewRecommendationDecision = loop?.ReviewRecommendation?.Decision,
            ReviewRecommendationReason = loop?.ReviewRecommendation?.Reason,
            InterpretationFailureReason = loop?.FailureReason,
            ClaimsAndEvidenceEmpty = claimsEmptyAndEvidenceEmpty,
            MatchesInsufficientEvidenceFallback =
                claimsEmptyAndEvidenceEmpty
                && string.Equals(item?.EvidenceRationaleSummary, RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceSummary, StringComparison.Ordinal)
                && string.Equals(item?.AutoResolutionGap, RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceGap, StringComparison.Ordinal)
                && string.Equals(item?.OperatorDecisionFocus, RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceDecision, StringComparison.Ordinal)
                && item?.RationaleIsHeuristic == true
                && string.Equals(loop?.InterpretationSummary, RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceSummary, StringComparison.Ordinal)
                && string.Equals(loop?.ReviewRecommendation?.Reason, RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceDecision, StringComparison.Ordinal)
                && string.Equals(loop?.FailureReason, RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceFailureReason, StringComparison.Ordinal)
        };
    }

    private static string ResolveDisabledProofScopeItemKey(
        ResolutionDetailResult reviewOnly,
        ResolutionDetailResult promotionBlocked)
    {
        if (reviewOnly.ItemFound && reviewOnly.Item != null)
        {
            return reviewOnly.Item.ScopeItemKey;
        }

        if (promotionBlocked.ItemFound && promotionBlocked.Item != null)
        {
            return promotionBlocked.Item.ScopeItemKey;
        }

        return ReviewOnlyScopeItemKey;
    }

    private static async Task<RuntimeControlDetailProofExcerpt> BuildCapturedReviewOnlyReplayAsync(CancellationToken ct)
    {
        var capturePath = Path.Combine(
            ResolveRepositoryRoot(),
            "src",
            "TgAssistant.Host",
            "artifacts",
            "resolution-interpretation-loop",
            "resolution-interpretation-live-capture-20260405T201314Z.json");
        Ensure(File.Exists(capturePath), $"Captured review-only artifact not found: {capturePath}");

        var json = await File.ReadAllTextAsync(capturePath, ct);
        var captured = JsonSerializer.Deserialize<CapturedReviewOnlyEnvelope>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        var item = captured?.Detail?.Detail?.Item;
        Ensure(item?.InterpretationLoop != null, "Captured review-only artifact does not contain an interpretation loop payload.");
        var capturedItem = item!;
        var capturedLoop = capturedItem.InterpretationLoop!;
        Ensure(
            RuntimeControlInterpretationPublicationGuard.ShouldSuppress(
                ScopeKey,
                capturedItem.ScopeItemKey,
                capturedItem.SourceKind,
                capturedLoop),
            "Captured review-only artifact no longer matches the bounded insufficient-evidence condition.");

        var suppressedLoop = RuntimeControlInterpretationPublicationGuard.BuildSuppressedInterpretation(capturedLoop);
        return new RuntimeControlDetailProofExcerpt
        {
            ScopeItemKey = capturedItem.ScopeItemKey,
            ProofMode = "captured_replay",
            ScopeBound = true,
            ItemFound = true,
            LiveItemPresent = false,
            PresenceNote = "Live review_only item is not materialized under the current runtime state; this proof replays the canonical captured detail from 2026-04-05 against the current suppression guard.",
            SourceKind = capturedItem.SourceKind,
            SourceRef = capturedItem.SourceRef,
            EvidenceRationaleSummary = RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceSummary,
            AutoResolutionGap = RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceGap,
            OperatorDecisionFocus = RuntimeControlInterpretationPublicationGuard.InsufficientEvidenceDecision,
            RationaleIsHeuristic = true,
            InterpretationApplied = suppressedLoop.Applied,
            InterpretationUsedFallback = suppressedLoop.UsedFallback,
            InterpretationSummary = suppressedLoop.InterpretationSummary,
            KeyClaims = [],
            EvidenceRefsUsed = [],
            ReviewRecommendationDecision = suppressedLoop.ReviewRecommendation.Decision,
            ReviewRecommendationReason = suppressedLoop.ReviewRecommendation.Reason,
            InterpretationFailureReason = suppressedLoop.FailureReason,
            ClaimsAndEvidenceEmpty = true,
            MatchesInsufficientEvidenceFallback = true
        };
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "artifacts",
            "resolution-interpretation-loop",
            "runtime-control-detail-bounded-proof.json"));
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TelegramAssistant.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

file sealed class CapturedReviewOnlyEnvelope
{
    public CapturedReviewOnlyOperatorDetail? Detail { get; set; }
}

file sealed class CapturedReviewOnlyOperatorDetail
{
    public ResolutionDetailResult Detail { get; set; } = new();
}

public sealed class RuntimeControlDetailBoundedProofReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public string ReviewOnlyScopeItemKey { get; set; } = string.Empty;
    public string PromotionBlockedScopeItemKey { get; set; } = string.Empty;
    public Guid? TrackedPersonId { get; set; }
    public string? ActiveRuntimeControlState { get; set; }
    public RuntimeControlDetailProofExcerpt ReviewOnly { get; set; } = new();
    public RuntimeControlDetailProofExcerpt PromotionBlocked { get; set; } = new();
    public RuntimeControlDetailProofExcerpt DisabledLoop { get; set; } = new();
    public bool Passed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class RuntimeControlDetailProofExcerpt
{
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ProofMode { get; set; } = string.Empty;
    public bool ScopeBound { get; set; }
    public string? ScopeFailureReason { get; set; }
    public bool ItemFound { get; set; }
    public bool LiveItemPresent { get; set; }
    public bool HasGroundedInterpretation { get; set; }
    public string? PresenceNote { get; set; }
    public string? SourceKind { get; set; }
    public string? SourceRef { get; set; }
    public string? EvidenceRationaleSummary { get; set; }
    public string? AutoResolutionGap { get; set; }
    public string? OperatorDecisionFocus { get; set; }
    public bool? RationaleIsHeuristic { get; set; }
    public bool? InterpretationApplied { get; set; }
    public bool? InterpretationUsedFallback { get; set; }
    public string? InterpretationSummary { get; set; }
    public List<string> KeyClaims { get; set; } = [];
    public List<string> EvidenceRefsUsed { get; set; } = [];
    public string? ReviewRecommendationDecision { get; set; }
    public string? ReviewRecommendationReason { get; set; }
    public string? InterpretationFailureReason { get; set; }
    public bool ClaimsAndEvidenceEmpty { get; set; }
    public bool MatchesInsufficientEvidenceFallback { get; set; }
}

file sealed class ThrowingResolutionInterpretationLoopService : IResolutionInterpretationLoopService
{
    public Task<ResolutionInterpretationLoopResult> InterpretAsync(
        ResolutionInterpretationLoopRequest request,
        CancellationToken ct = default)
    {
        throw new InvalidOperationException("Interpretation loop should not be called when ResolutionInterpretationLoop:Enabled=false.");
    }
}

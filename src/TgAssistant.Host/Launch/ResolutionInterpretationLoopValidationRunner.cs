using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;

namespace TgAssistant.Host.Launch;

public static class ResolutionInterpretationLoopValidationRunner
{
    private const string ScopeKey = "chat:885574984";
    private const long ChatId = 885574984;
    private static readonly Guid TrackedPersonId = Guid.Parse("88557498-4984-4984-4984-885574984001");

    public static async Task<ResolutionInterpretationLoopValidationReport> RunAsync(
        IServiceProvider _,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new ResolutionInterpretationLoopValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            ScopeKey = ScopeKey,
            ChatId = ChatId,
            TrackedPersonId = TrackedPersonId
        };

        Exception? fatal = null;
        try
        {
            var loopService = new ResolutionInterpretationLoopV1Service(
                new StubResolutionInterpretationModel(),
                Options.Create(new ResolutionInterpretationLoopSettings
                {
                    Enabled = true,
                    CanonicalScopeOnly = true,
                    CanonicalScopeKey = ScopeKey,
                    MaxAdditionalRetrievalRounds = 1,
                    MaxInitialEvidenceItems = 2,
                    MaxRequestedContextItems = 3
                }),
                NullLogger<ResolutionInterpretationLoopV1Service>.Instance);
            var request = BuildRequest();
            report.ScopeItemKey = request.ScopeItemKey;

            var interpretation = await loopService.InterpretAsync(request, ct);

            Ensure(interpretation.Applied, "Interpretation loop result is not marked applied.");
            Ensure(!interpretation.UsedFallback, "Interpretation loop unexpectedly fell back during validation.");
            Ensure(string.IsNullOrWhiteSpace(interpretation.FailureReason), "Happy-path validation must not emit a budget failure reason.");
            Ensure(interpretation.AuditTrail.Count >= 4, "Interpretation audit trail is missing required steps.");
            Ensure(interpretation.AuditTrail.Count(x => string.Equals(x.Step, "retrieval_round", StringComparison.Ordinal)) == 1, "Expected exactly one bounded retrieval round.");
            Ensure(
                interpretation.AuditTrail.Any(x => string.Equals(x.Step, "model_round", StringComparison.Ordinal) && x.RetrievalRound == 1),
                "Expected a final model round after retrieval.");
            var initialModelRound = interpretation.AuditTrail.First(
                x => string.Equals(x.Step, "model_round", StringComparison.Ordinal) && x.RetrievalRound == 0);
            var finalModelRound = interpretation.AuditTrail.First(
                x => string.Equals(x.Step, "model_round", StringComparison.Ordinal) && x.RetrievalRound == 1);
            Ensure(initialModelRound.PromptTokens.HasValue, "Initial model round must include prompt_tokens.");
            Ensure(initialModelRound.CompletionTokens.HasValue, "Initial model round must include completion_tokens.");
            Ensure(initialModelRound.TotalTokens.HasValue, "Initial model round must include total_tokens.");
            Ensure(initialModelRound.CostUsd.HasValue, "Initial model round must include cost_usd on happy path.");
            Ensure(finalModelRound.PromptTokens.HasValue, "Final model round must include prompt_tokens.");
            Ensure(finalModelRound.CompletionTokens.HasValue, "Final model round must include completion_tokens.");
            Ensure(finalModelRound.TotalTokens.HasValue, "Final model round must include total_tokens.");
            Ensure(finalModelRound.CostUsd.HasValue, "Final model round must include cost_usd when provided.");
            Ensure(interpretation.KeyClaims.Count > 0, "Interpretation loop produced no claims.");
            Ensure(interpretation.EvidenceRefsUsed.Count > 0, "Interpretation loop did not emit evidence refs.");
            Ensure(
                interpretation.KeyClaims.All(x => !string.IsNullOrWhiteSpace(x.DisplayLabel)),
                "Each surfaced claim must include display_label.");
            Ensure(
                string.Equals(interpretation.ReviewRecommendation.DisplayLabel, OperatorAssistantTruthLabels.Recommendation, StringComparison.Ordinal),
                $"Review recommendation must include display_label={OperatorAssistantTruthLabels.Recommendation}.");
            Ensure(
                interpretation.ReviewRecommendation.Decision is ResolutionInterpretationReviewRecommendations.Review
                    or ResolutionInterpretationReviewRecommendations.NoReview,
                $"Unexpected review recommendation decision: {interpretation.ReviewRecommendation.Decision}");
            var inferenceClaim = interpretation.KeyClaims.FirstOrDefault(x =>
                string.Equals(x.ClaimType, ResolutionInterpretationClaimTypes.Inference, StringComparison.Ordinal));
            Ensure(inferenceClaim != null, "Validation output is missing an inference claim.");
            Ensure(inferenceClaim.TrustPercent == 74, "Inference claim trust_percent must map from confidence=0.74.");
            var hypothesisClaim = interpretation.KeyClaims.FirstOrDefault(x =>
                string.Equals(x.ClaimType, ResolutionInterpretationClaimTypes.Hypothesis, StringComparison.Ordinal));
            Ensure(hypothesisClaim != null, "Validation output is missing a hypothesis claim.");
            Ensure(hypothesisClaim.TrustPercent == null, "Hypothesis claim trust_percent must be null when confidence is absent.");
            Ensure(
                interpretation.ReviewRecommendation.TrustPercent == 63,
                "Recommendation trust_percent must map from confidence=0.63.");
            var claimValidationRows = BuildClaimValidationRows(interpretation.KeyClaims);
            Ensure(
                claimValidationRows.All(x => x.Supported),
                "Unsupported certainty detected: each surfaced claim must have evidence refs or display_label=Inference/Hypothesis.");

            report.RequestedContextType = interpretation.AuditTrail
                .FirstOrDefault(x => string.Equals(x.Step, "retrieval_round", StringComparison.Ordinal))
                ?.RequestedContextType
                ?? ResolutionInterpretationContextTypes.None;
            report.FinalInterpretation = interpretation.InterpretationSummary;
            report.KeyClaims = interpretation.KeyClaims.Select(x => x.Summary).ToList();
            report.ExplicitUncertainties = interpretation.ExplicitUncertainties;
            report.ReviewRecommendationDecision = interpretation.ReviewRecommendation.Decision;
            report.ReviewRecommendationReason = interpretation.ReviewRecommendation.Reason;
            report.EvidenceRefsUsed = interpretation.EvidenceRefsUsed;
            report.UsedFallback = interpretation.UsedFallback;
            report.FailureReason = interpretation.FailureReason;
            report.NoDirectModelToDurableWrites = true;
            report.AuditTrailPresent = interpretation.AuditTrail.Count > 0;
            report.AuditTrail = interpretation.AuditTrail;
            report.ClaimValidationRows = claimValidationRows;
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
                "Resolution interpretation loop validation failed: bounded loop proof is incomplete.",
                fatal);
        }

        return report;
    }

    private static ResolutionInterpretationLoopRequest BuildRequest()
    {
        return new ResolutionInterpretationLoopRequest
        {
            TrackedPersonId = TrackedPersonId,
            ScopeKey = ScopeKey,
            ScopeItemKey = "resolution:review:validation-seeded-item",
            Item = new ResolutionItemSummary
            {
                ScopeItemKey = "resolution:review:validation-seeded-item",
                ItemType = ResolutionItemTypes.Review,
                Title = "Profile review requires bounded interpretation",
                Summary = "Conflicting profile signals need a bounded interpretation before review surfacing.",
                WhyItMatters = "Operator review quality depends on whether the evidence is coherent or still ambiguous.",
                AffectedFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                AffectedObjectRef = "scope:chat:885574984",
                TrustFactor = 0.71f,
                Status = ResolutionItemStatuses.AttentionRequired,
                EvidenceCount = 4,
                UpdatedAtUtc = DateTime.Parse("2026-04-05T00:00:00Z").ToUniversalTime(),
                Priority = ResolutionItemPriorities.High,
                RecommendedNextAction = ResolutionActionTypes.Evidence,
                AvailableActions = [ResolutionActionTypes.Evidence, ResolutionActionTypes.Clarify]
            },
            SourceKind = "validation_seed",
            SourceRef = "validation:resolution-interpretation-loop-v1",
            RequiredAction = "review_profile_conflict",
            Notes =
            [
                new ResolutionDetailNote
                {
                    Kind = "queue",
                    Text = "Stage8 flagged this profile branch after conflicting confidence shifts."
                },
                new ResolutionDetailNote
                {
                    Kind = "normalization_issue",
                    Text = "A recent bounded pass reported ambiguity around reply intent."
                }
            ],
            Evidence =
            [
                new ResolutionEvidenceSummary
                {
                    EvidenceItemId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    SourceRef = "message:1001",
                    SourceLabel = "message",
                    Summary = "The person explicitly postponed the meeting and asked to revisit next week.",
                    TrustFactor = 0.84f,
                    ObservedAtUtc = DateTime.Parse("2026-04-04T11:15:00Z").ToUniversalTime(),
                    SenderDisplay = "Tracked Person"
                },
                new ResolutionEvidenceSummary
                {
                    EvidenceItemId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    SourceRef = "message:1004",
                    SourceLabel = "message",
                    Summary = "Follow-up replies became shorter and less frequent after the scheduling conflict.",
                    TrustFactor = 0.79f,
                    ObservedAtUtc = DateTime.Parse("2026-04-04T16:45:00Z").ToUniversalTime(),
                    SenderDisplay = "Tracked Person"
                },
                new ResolutionEvidenceSummary
                {
                    EvidenceItemId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    SourceRef = "message:1010",
                    SourceLabel = "message",
                    Summary = "A later message reopened the topic and suggested a specific day for reconnecting.",
                    TrustFactor = 0.83f,
                    ObservedAtUtc = DateTime.Parse("2026-04-05T08:00:00Z").ToUniversalTime(),
                    SenderDisplay = "Tracked Person"
                },
                new ResolutionEvidenceSummary
                {
                    EvidenceItemId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    SourceRef = "message:1012",
                    SourceLabel = "message",
                    Summary = "The latest reply confirms interest but leaves uncertainty about commitment strength.",
                    TrustFactor = 0.74f,
                    ObservedAtUtc = DateTime.Parse("2026-04-05T09:30:00Z").ToUniversalTime(),
                    SenderDisplay = "Tracked Person"
                }
            ],
            DurableContextSummaries =
            [
                "Profile for tracked person: 4 evidence items, 1 contradiction marker, recent high-confidence reply-intent ambiguity.",
                "Dossier summary: scheduling conflict was followed by a bounded re-engagement signal the next day."
            ]
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
            "resolution-interpretation-loop-v1-validation-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static List<ResolutionInterpretationClaimValidationRow> BuildClaimValidationRows(
        IReadOnlyCollection<ResolutionInterpretationClaim> claims)
    {
        return claims.Select(claim =>
        {
            var normalizedType = ResolutionInterpretationClaimTypes.Normalize(claim.ClaimType);
            var normalizedDisplayLabel = OperatorTruthTrustFormatter.NormalizeLabel(claim.DisplayLabel);
            var hasEvidenceRefs = claim.EvidenceRefs.Count > 0;
            var hasExplicitUncertaintyLabel =
                string.Equals(normalizedDisplayLabel, OperatorAssistantTruthLabels.Inference, StringComparison.Ordinal)
                || string.Equals(normalizedDisplayLabel, OperatorAssistantTruthLabels.Hypothesis, StringComparison.Ordinal);

            return new ResolutionInterpretationClaimValidationRow
            {
                ClaimType = normalizedType,
                DisplayLabel = normalizedDisplayLabel,
                ClaimSummary = claim.Summary,
                HasEvidenceRefs = hasEvidenceRefs,
                HasExplicitUncertaintyLabel = hasExplicitUncertaintyLabel,
                TrustPercent = claim.TrustPercent,
                Supported = hasEvidenceRefs || hasExplicitUncertaintyLabel
            };
        }).ToList();
    }

    private sealed class StubResolutionInterpretationModel : IResolutionInterpretationModel
    {
        private int _callCount;

        public Task<ResolutionInterpretationModelResponse> InterpretAsync(
            ResolutionInterpretationModelRequest request,
            CancellationToken ct = default)
        {
            _callCount += 1;
            return Task.FromResult(_callCount switch
            {
                1 => BuildInitialResponse(request),
                2 => BuildFinalResponse(request),
                _ => throw new InvalidOperationException("StubResolutionInterpretationModel was called more than twice.")
            });
        }

        private static ResolutionInterpretationModelResponse BuildInitialResponse(ResolutionInterpretationModelRequest request)
        {
            if (request.RetrievalRound != 0)
            {
                throw new InvalidOperationException("Initial interpretation call used an unexpected retrieval round.");
            }

            if (request.Context.Evidence.Count != 2)
            {
                throw new InvalidOperationException("Initial interpretation call should receive only the bounded initial evidence window.");
            }

            return new ResolutionInterpretationModelResponse
            {
                Provider = "validation-stub",
                Model = "resolution-interpretation-loop-v1-stub",
                RequestId = "validation-initial",
                LatencyMs = 5,
                PromptTokens = 130,
                CompletionTokens = 80,
                TotalTokens = 210,
                CostUsd = 0.0035m,
                Interpretation = new ResolutionInterpretationLoopResult
                {
                    ContextSufficient = false,
                    RequestedContextType = ResolutionInterpretationContextTypes.AdditionalEvidence,
                    InterpretationSummary = "The initial context suggests re-engagement after conflict, but more recent evidence is needed before review guidance is stable.",
                    ExplicitUncertainties =
                    [
                        "The newest bounded messages were not yet considered."
                    ],
                    ReviewRecommendation = new ResolutionInterpretationReviewRecommendation
                    {
                        Decision = ResolutionInterpretationReviewRecommendations.Review,
                        Reason = "More recent evidence is required before reducing review pressure."
                    },
                    EvidenceRefsUsed =
                    [
                        "message:1001",
                        "message:1004"
                    ]
                }
            };
        }

        private static ResolutionInterpretationModelResponse BuildFinalResponse(ResolutionInterpretationModelRequest request)
        {
            if (request.RetrievalRound != 1)
            {
                throw new InvalidOperationException("Final interpretation call used an unexpected retrieval round.");
            }

            if (request.AdditionalContext.Evidence.Count == 0)
            {
                throw new InvalidOperationException("Final interpretation call did not receive additional bounded evidence.");
            }

            return new ResolutionInterpretationModelResponse
            {
                Provider = "validation-stub",
                Model = "resolution-interpretation-loop-v1-stub",
                RequestId = "validation-final",
                LatencyMs = 6,
                PromptTokens = 150,
                CompletionTokens = 95,
                TotalTokens = 245,
                CostUsd = 0.0045m,
                Interpretation = new ResolutionInterpretationLoopResult
                {
                    ContextSufficient = true,
                    RequestedContextType = ResolutionInterpretationContextTypes.None,
                    InterpretationSummary = "The additional bounded evidence shows that the scheduling conflict softened into renewed engagement, but the commitment level still needs operator judgment.",
                    KeyClaims =
                    [
                        new ResolutionInterpretationClaim
                        {
                            ClaimType = ResolutionInterpretationClaimTypes.Inference,
                            DisplayLabel = OperatorAssistantTruthLabels.Inference,
                            Confidence = 0.74f,
                            Summary = "Recent messages support a re-engagement interpretation rather than a clean withdrawal.",
                            EvidenceRefs = ["message:1010", "message:1012"]
                        },
                        new ResolutionInterpretationClaim
                        {
                            ClaimType = ResolutionInterpretationClaimTypes.Hypothesis,
                            DisplayLabel = OperatorAssistantTruthLabels.Hypothesis,
                            Summary = "Engagement may remain unstable if no concrete follow-through appears in the next exchange.",
                            EvidenceRefs = []
                        }
                    ],
                    ExplicitUncertainties =
                    [
                        "The bounded record still does not prove how durable the renewed engagement will be."
                    ],
                    ReviewRecommendation = new ResolutionInterpretationReviewRecommendation
                    {
                        Decision = ResolutionInterpretationReviewRecommendations.Review,
                        DisplayLabel = OperatorAssistantTruthLabels.Recommendation,
                        Confidence = 0.63f,
                        Reason = "Operator review is still recommended because the latest evidence improves the interpretation but does not eliminate ambiguity."
                    },
                    EvidenceRefsUsed =
                    [
                        "message:1010",
                        "message:1012"
                    ]
                }
            };
        }
    }
}

public sealed class ResolutionInterpretationLoopValidationReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public long ChatId { get; set; }
    public Guid TrackedPersonId { get; set; }
    public string ScopeItemKey { get; set; } = string.Empty;
    public string RequestedContextType { get; set; } = ResolutionInterpretationContextTypes.None;
    public string FinalInterpretation { get; set; } = string.Empty;
    public List<string> KeyClaims { get; set; } = [];
    public List<string> ExplicitUncertainties { get; set; } = [];
    public string ReviewRecommendationDecision { get; set; } = string.Empty;
    public string ReviewRecommendationReason { get; set; } = string.Empty;
    public List<string> EvidenceRefsUsed { get; set; } = [];
    public bool UsedFallback { get; set; }
    public string? FailureReason { get; set; }
    public bool NoDirectModelToDurableWrites { get; set; }
    public bool AuditTrailPresent { get; set; }
    public List<ResolutionInterpretationAuditEntry> AuditTrail { get; set; } = [];
    public List<ResolutionInterpretationClaimValidationRow> ClaimValidationRows { get; set; } = [];
    public bool Passed { get; set; }
    public string? FatalError { get; set; }
}

public sealed class ResolutionInterpretationClaimValidationRow
{
    public string ClaimType { get; set; } = string.Empty;
    public string DisplayLabel { get; set; } = string.Empty;
    public string ClaimSummary { get; set; } = string.Empty;
    public bool HasEvidenceRefs { get; set; }
    public bool HasExplicitUncertaintyLabel { get; set; }
    public int? TrustPercent { get; set; }
    public bool Supported { get; set; }
}

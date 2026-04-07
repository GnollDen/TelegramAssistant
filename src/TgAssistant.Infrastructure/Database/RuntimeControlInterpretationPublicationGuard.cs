using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class RuntimeControlInterpretationPublicationGuard
{
    public const string CanonicalScopeKey = "chat:885574984";
    public const string ReviewOnlyScopeItemKey = "review:runtime_control_state:review_only";
    public const string InsufficientEvidenceSummary = "Доказательной опоры для интерпретации текущего runtime-control состояния пока недостаточно: LoopV1 не дал evidence-backed claims для этой карточки.";
    public const string InsufficientEvidenceGap = "Автоматическая интерпретация остановлена: claims и evidence refs отсутствуют, поэтому нужен только escalation-only разбор оператора.";
    public const string InsufficientEvidenceDecision = "Текущее runtime-control состояние требует внимания оператора, но evidence-backed интерпретация пока недоступна; выполните ручную проверку в веб-контуре.";
    public const string InsufficientEvidenceFailureReason = "runtime_control_interpretation_insufficient_evidence";

    public static bool ShouldSuppress(
        string? scopeKey,
        string? scopeItemKey,
        string? sourceKind,
        ResolutionInterpretationLoopResult? interpretationLoop)
    {
        return interpretationLoop is { Applied: true }
               && string.Equals(scopeKey, CanonicalScopeKey, StringComparison.Ordinal)
               && string.Equals(sourceKind, "runtime_control_state", StringComparison.Ordinal)
               && string.Equals(scopeItemKey, ReviewOnlyScopeItemKey, StringComparison.Ordinal)
               && interpretationLoop.KeyClaims.Count == 0
               && interpretationLoop.EvidenceRefsUsed.Count == 0;
    }

    public static ResolutionInterpretationLoopResult BuildSuppressedInterpretation(
        ResolutionInterpretationLoopResult interpretationLoop)
    {
        var uncertainties = interpretationLoop.ExplicitUncertainties
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        uncertainties.Add("Evidence-backed interpretation is not available for this runtime-control detail yet.");

        var auditTrail = new List<ResolutionInterpretationAuditEntry>(interpretationLoop.AuditTrail)
        {
            new()
            {
                Step = "projection_guard",
                RetrievalRound = 0,
                RequestedContextType = ResolutionInterpretationContextTypes.None,
                Status = "suppressed_no_evidence",
                Details = "Runtime-control operator copy was replaced with a deterministic insufficient-evidence fallback because key_claims and evidence_refs_used were both empty.",
                ObservedAtUtc = DateTime.UtcNow
            }
        };

        return new ResolutionInterpretationLoopResult
        {
            Applied = interpretationLoop.Applied,
            UsedFallback = true,
            ContextSufficient = interpretationLoop.ContextSufficient,
            RequestedContextType = interpretationLoop.RequestedContextType,
            InterpretationSummary = InsufficientEvidenceSummary,
            KeyClaims = [],
            ExplicitUncertainties = uncertainties,
            ReviewRecommendation = new ResolutionInterpretationReviewRecommendation
            {
                Decision = ResolutionInterpretationReviewRecommendations.Review,
                DisplayLabel = OperatorAssistantTruthLabels.Recommendation,
                TrustPercent = null,
                Reason = InsufficientEvidenceDecision
            },
            EvidenceRefsUsed = [],
            FailureReason = string.IsNullOrWhiteSpace(interpretationLoop.FailureReason)
                ? InsufficientEvidenceFailureReason
                : interpretationLoop.FailureReason,
            AuditTrail = auditTrail
        };
    }
}

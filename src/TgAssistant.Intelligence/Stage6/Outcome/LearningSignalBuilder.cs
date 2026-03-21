using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Outcome;

public class LearningSignalBuilder : ILearningSignalBuilder
{
    public List<LearningSignal> Build(
        StrategyRecord strategyRecord,
        StrategyOption? primaryOption,
        DraftRecord draftRecord,
        DraftActionMatchResult match,
        ObservedOutcomeAssessment observed,
        string? userLabel)
    {
        var normalizedUser = ObservedOutcomeRecorder.NormalizeLabel(userLabel);
        var finalLabel = ResolveFinalLabel(normalizedUser, observed.Label);
        var signals = new List<LearningSignal>
        {
            new()
            {
                SignalKey = "strategy_helpfulness",
                Value = finalLabel is "positive" or "neutral" ? "likely_helpful" : "likely_not_helpful",
                Confidence = ComputeConfidence(match.MatchScore, observed.Confidence),
                Reason = $"outcome={finalLabel}, strategy_conf={strategyRecord.StrategyConfidence:0.00}"
            },
            new()
            {
                SignalKey = "draft_sendable",
                Value = match.MatchScore >= 0.65f ? "sendable" : "not_sendable",
                Confidence = Math.Clamp((match.MatchScore * 0.7f) + 0.2f, 0f, 1f),
                Reason = $"match_score={match.MatchScore:0.00}, method={match.MatchMethod}"
            },
            new()
            {
                SignalKey = "style_fit",
                Value = match.MatchScore >= 0.7f && finalLabel is "positive" or "neutral" ? "likely_fit" : "poor_fit",
                Confidence = ComputeConfidence(match.MatchScore, observed.Confidence),
                Reason = $"match_score={match.MatchScore:0.00}, final_outcome={finalLabel}, draft_conf={draftRecord.Confidence:0.00}"
            }
        };

        var actionType = primaryOption?.ActionType?.Trim().ToLowerInvariant() ?? "unknown";
        var aggressive = actionType is "invite" or "deepen" or "light_test";
        signals.Add(new LearningSignal
        {
            SignalKey = "action_escalation_timing",
            Value = aggressive && finalLabel is "negative" or "mixed"
                ? "escalated_too_early"
                : "appropriately_timed",
            Confidence = Math.Clamp((observed.Confidence * 0.7f) + 0.2f, 0f, 1f),
            Reason = $"primary_action={actionType}, outcome={finalLabel}"
        });

        return signals;
    }

    private static string ResolveFinalLabel(string userLabel, string systemLabel)
    {
        if (userLabel != "unclear" && systemLabel != "unclear" && userLabel != systemLabel)
        {
            return "mixed";
        }

        return userLabel != "unclear" ? userLabel : systemLabel;
    }

    private static float ComputeConfidence(float matchScore, float observedConfidence)
    {
        return Math.Clamp((matchScore * 0.45f) + (observedConfidence * 0.55f), 0f, 1f);
    }
}

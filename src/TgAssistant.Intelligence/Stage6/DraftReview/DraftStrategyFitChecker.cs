using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.DraftReview;

public class DraftStrategyFitChecker : IDraftStrategyFitChecker
{
    public DraftStrategyFitResult Evaluate(DraftReviewContext context)
    {
        var text = context.CandidateText.ToLowerInvariant();
        var primaryAction = context.PrimaryOption.ActionType;
        var strategyConfidence = context.StrategyRecord.StrategyConfidence;
        var ambiguity = context.CurrentState?.AmbiguityScore ?? 0.6f;
        var avoidance = context.CurrentState?.AvoidanceRiskScore ?? 0.5f;

        var hasPushSignals = HasAny(text, "must", "right now", "ультиматум", "надави", "дожми", "ответь сегодня", "force", "demand");
        var hasEscalationSignals = HasAny(text, "love", "exclusive", "отношения", "люблю", "встречаемся");
        var softStrategy = primaryAction is "wait" or "hold_rapport" or "check_in" or "clarify" or "deescalate" or "repair";
        var escalationStrategy = primaryAction is "invite" or "deepen" or "light_test";

        if (softStrategy && (hasPushSignals || hasEscalationSignals))
        {
            return new DraftStrategyFitResult
            {
                HasMaterialConflict = true,
                ConflictNote = $"candidate is more forceful/escalatory than strategy '{primaryAction}'"
            };
        }

        if (hasPushSignals && (strategyConfidence < 0.66f || ambiguity >= 0.58f || avoidance >= 0.55f))
        {
            return new DraftStrategyFitResult
            {
                HasMaterialConflict = true,
                ConflictNote = $"candidate pressure conflicts with uncertainty profile (confidence={strategyConfidence:0.00}, ambiguity={ambiguity:0.00})"
            };
        }

        if (hasEscalationSignals && !escalationStrategy && ambiguity >= 0.55f)
        {
            return new DraftStrategyFitResult
            {
                HasMaterialConflict = true,
                ConflictNote = "candidate escalation exceeds current ambiguity-safe strategy"
            };
        }

        return new DraftStrategyFitResult
        {
            HasMaterialConflict = false
        };
    }

    private static bool HasAny(string lowerText, params string[] tokens)
    {
        return tokens.Any(token => lowerText.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

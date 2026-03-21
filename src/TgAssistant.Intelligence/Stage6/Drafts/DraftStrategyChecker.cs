using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Drafts;

public class DraftStrategyChecker : IDraftStrategyChecker
{
    private static readonly string[] RiskIntentTokens =
    [
        "push hard",
        "pressure",
        "demand",
        "ultimatum",
        "force",
        "jealous",
        "жестко",
        "дожми",
        "надави",
        "ультиматум",
        "потребуй"
    ];

    public DraftConflictAssessment Evaluate(DraftGenerationContext context, DraftStyledContent styled)
    {
        var notes = (context.UserNotes ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(notes))
        {
            return new DraftConflictAssessment();
        }

        var hasRiskIntent = RiskIntentTokens.Any(token => notes.Contains(token, StringComparison.OrdinalIgnoreCase));
        var primaryAction = context.PrimaryOption.ActionType;
        var strategyConfidence = context.StrategyRecord.StrategyConfidence;
        var ambiguity = context.CurrentState?.AmbiguityScore ?? 0.6f;
        var isSoftPrimary = primaryAction is "wait" or "hold_rapport" or "check_in" or "clarify" or "deescalate" or "repair";

        var shouldConflict = hasRiskIntent && (isSoftPrimary || strategyConfidence < 0.66f || ambiguity >= 0.58f);
        if (!shouldConflict)
        {
            return new DraftConflictAssessment();
        }

        var riskyAlternative = BuildRiskyAlternative(primaryAction, notes);
        return new DraftConflictAssessment
        {
            HasConflict = true,
            Reason = $"user_intent_conflicts_with_strategy_safety: primary={primaryAction}, confidence={strategyConfidence:0.00}, ambiguity={ambiguity:0.00}",
            RiskyIntentAlternative = riskyAlternative
        };
    }

    private static string BuildRiskyAlternative(string actionType, string userNotes)
    {
        if (actionType is "wait" or "clarify" or "deescalate")
        {
            return $"Я хочу ясности сейчас. {Trim(userNotes)}";
        }

        return $"Мне важно быстрее прояснить это. {Trim(userNotes)}";
    }

    private static string Trim(string value)
    {
        var compact = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return compact.Length <= 80 ? compact : compact[..79] + "…";
    }
}

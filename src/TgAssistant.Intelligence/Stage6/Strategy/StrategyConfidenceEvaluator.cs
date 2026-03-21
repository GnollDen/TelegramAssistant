using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class StrategyConfidenceEvaluator : IStrategyConfidenceEvaluator
{
    public StrategyConfidenceAssessment Evaluate(
        StrategyEvaluationContext context,
        IReadOnlyList<StrategyCandidateOption> rankedOptions)
    {
        var stateConfidence = context.CurrentState?.Confidence ?? 0.45f;
        var ambiguity = context.CurrentState?.AmbiguityScore ?? 0.7f;
        var openConflicts = context.Conflicts.Count(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase));
        var conflictLevel = Math.Clamp(openConflicts / 3f, 0f, 1f);

        var top = rankedOptions.FirstOrDefault();
        var second = rankedOptions.Skip(1).FirstOrDefault();
        var optionSeparation = top == null
            ? 0f
            : second == null
                ? 0.5f
                : Math.Clamp(top.FinalScore - second.FinalScore, 0f, 1f);

        var confidence =
            (stateConfidence * 0.45f)
            + (optionSeparation * 0.25f)
            + ((1f - ambiguity) * 0.15f)
            + ((1f - conflictLevel) * 0.15f);
        confidence = Math.Clamp(confidence, 0f, 1f);

        var highUncertainty = context.HighUncertainty
                              || ambiguity >= 0.62f
                              || conflictLevel >= 0.45f
                              || confidence < 0.54f;

        return new StrategyConfidenceAssessment
        {
            Confidence = confidence,
            OptionSeparation = optionSeparation,
            Ambiguity = ambiguity,
            ConflictLevel = conflictLevel,
            HighUncertainty = highUncertainty,
            HorizonAllowed = !highUncertainty && confidence >= 0.62f
        };
    }
}

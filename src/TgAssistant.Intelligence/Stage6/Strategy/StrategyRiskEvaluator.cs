using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class StrategyRiskEvaluator : IStrategyRiskEvaluator
{
    public StrategyRiskAssessment Evaluate(StrategyEvaluationContext context, StrategyCandidateOption option)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var risk = 0.15f;

        var state = context.CurrentState;
        var ambiguity = state?.AmbiguityScore ?? 0.6f;
        var avoidance = state?.AvoidanceRiskScore ?? 0.5f;
        var conflictLevel = Math.Clamp(context.Conflicts.Count(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)) / 3f, 0f, 1f);

        if (ambiguity >= 0.58f)
        {
            labels.Add("high_ambiguity");
            risk += 0.2f;
        }

        if (conflictLevel > 0f)
        {
            labels.Add("active_conflict");
            risk += 0.15f * conflictLevel;
        }

        if (avoidance >= 0.56f)
        {
            labels.Add("avoidance_risk");
            risk += 0.1f;
        }

        var aggressive = option.ActionType is "invite" or "deepen" or "light_test";
        if (aggressive && ambiguity >= 0.5f)
        {
            labels.Add("premature_escalation");
            risk += 0.24f;
        }

        if (option.ActionType is "wait" && context.ClarificationQuestions.Any(x => x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase) && x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)))
        {
            labels.Add("delay_of_clarification");
            risk += 0.08f;
        }

        if (option.ActionType is "boundaries" && context.CurrentState?.WarmthScore > 0.62f)
        {
            labels.Add("overcorrection");
            risk += 0.08f;
        }

        if (option.ActionType is "clarify")
        {
            labels.Add("question_fatigue");
            risk += 0.06f;
        }

        if (labels.Count == 0)
        {
            labels.Add("normal_execution_risk");
        }

        return new StrategyRiskAssessment
        {
            Labels = labels.ToList(),
            RiskScore = Math.Clamp(risk, 0f, 1f)
        };
    }
}

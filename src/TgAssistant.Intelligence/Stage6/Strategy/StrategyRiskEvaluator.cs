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
        var status = state?.RelationshipStatus ?? "ambiguous";
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
        var breakupAwareContact = option.ActionType is "re_establish_contact" or "test_receptivity";
        if (aggressive && ambiguity >= 0.5f)
        {
            labels.Add("premature_escalation");
            risk += 0.24f;
            labels.Add("anxious_overreach_risk");
        }

        if (breakupAwareContact && ambiguity >= 0.6f)
        {
            labels.Add("premature_recontact");
            risk += 0.14f;
        }

        if ((status.Equals("post_breakup", StringComparison.OrdinalIgnoreCase)
                || status.Equals("no_contact", StringComparison.OrdinalIgnoreCase))
            && option.ActionType is "invite" or "deepen")
        {
            labels.Add("state_mismatch_pressure");
            risk += 0.2f;
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

        if (aggressive && (context.HighUncertainty || conflictLevel >= 0.34f))
        {
            labels.Add("contact_at_any_cost_risk");
            risk += 0.12f;
        }

        var pressureSensitive = context.ProfileTraits.Any(x =>
            x.TraitKey == "what_fails"
            && x.ValueLabel.Contains("pressure", StringComparison.OrdinalIgnoreCase));
        if (pressureSensitive && aggressive)
        {
            labels.Add("dignity_risk");
            risk += 0.1f;
        }

        if (ContainsAny(option.Summary, "force", "demand", "push", "prove", "win")
            || ContainsAny(option.Purpose, "retain", "hook", "control", "win"))
        {
            labels.Add("manipulative_gain_risk");
            risk += 0.25f;
        }

        if (option.ActionType is "deescalate" or "clarify" or "hold_rapport" or "wait")
        {
            labels.Add("clarity_dignity_aligned");
            risk = Math.Max(0.05f, risk - 0.04f);
        }

        if (option.ActionType is "acknowledge_separation")
        {
            labels.Add("clarity_dignity_aligned");
            risk = Math.Max(0.05f, risk - 0.06f);
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

    private static bool ContainsAny(string? text, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return needles.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}

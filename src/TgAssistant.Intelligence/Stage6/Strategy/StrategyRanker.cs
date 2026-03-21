using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class StrategyRanker : IStrategyRanker
{
    private static readonly HashSet<string> AggressiveActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "invite",
        "deepen",
        "light_test"
    };

    private static readonly HashSet<string> SoftActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "wait",
        "hold_rapport",
        "check_in",
        "clarify",
        "deescalate",
        "repair"
    };

    public IReadOnlyList<StrategyCandidateOption> Rank(StrategyEvaluationContext context, IReadOnlyList<StrategyCandidateOption> candidates)
    {
        var state = context.CurrentState;
        var ambiguity = state?.AmbiguityScore ?? 0.65f;
        var status = state?.RelationshipStatus ?? "ambiguous";
        var dynamicLabel = state?.DynamicLabel ?? "uncertain_shift";

        var ranked = new List<StrategyCandidateOption>(candidates.Count);
        foreach (var candidate in candidates)
        {
            candidate.StateFit = ComputeStateFit(candidate.ActionType, status, dynamicLabel, ambiguity);
            candidate.ProfileFit = ComputeProfileFit(candidate.ActionType, context);
            candidate.PairPatternFit = ComputePairPatternFit(candidate.ActionType, context);
            candidate.FinalScore =
                (candidate.StateFit * 0.45f)
                + (candidate.ProfileFit * 0.2f)
                + (candidate.PairPatternFit * 0.2f)
                - (candidate.RiskScore * 0.35f);

            ranked.Add(candidate);
        }

        if (context.HighUncertainty)
        {
            ranked = ranked
                .Where(x => !AggressiveActions.Contains(x.ActionType) || x.StateFit >= 0.82f)
                .Where(x => SoftActions.Contains(x.ActionType) || x.ActionType.Equals("boundaries", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var option in ranked)
            {
                if (SoftActions.Contains(option.ActionType) || option.ActionType.Equals("boundaries", StringComparison.OrdinalIgnoreCase))
                {
                    option.FinalScore += 0.08f;
                }
            }
        }

        if (ranked.Count == 0)
        {
            ranked.Add(new StrategyCandidateOption
            {
                ActionType = "wait",
                Summary = "Pause and gather one clarifying signal before stronger action.",
                Purpose = "Fallback safe strategy under uncertainty.",
                RiskLabels = ["normal_execution_risk"],
                RiskScore = 0.25f,
                WhenToUse = "Use when confidence is low.",
                SuccessSigns = "More stable signal appears.",
                FailureSigns = "Uncertainty remains unchanged.",
                StateFit = 0.7f,
                ProfileFit = 0.6f,
                PairPatternFit = 0.6f,
                FinalScore = 0.62f
            });
        }

        var ordered = ranked
            .OrderByDescending(x => x.FinalScore)
            .ThenBy(x => x.RiskScore)
            .ToList();

        var take = context.HighUncertainty ? 3 : 5;
        var limited = ordered.Take(Math.Max(2, Math.Min(take, ordered.Count))).ToList();

        for (var i = 0; i < limited.Count; i++)
        {
            limited[i].IsPrimary = i == 0;
        }

        return limited;
    }

    private static float ComputeStateFit(string actionType, string status, string dynamicLabel, float ambiguity)
    {
        var fit = actionType switch
        {
            "wait" => ambiguity >= 0.6f ? 0.88f : 0.58f,
            "clarify" => ambiguity >= 0.55f ? 0.9f : 0.62f,
            "deescalate" => dynamicLabel is "fragile" or "cooling" ? 0.84f : 0.48f,
            "repair" => status is "fragile_contact" or "detached" ? 0.82f : 0.56f,
            "hold_rapport" => status is "ambiguous" or "warm_platonic" ? 0.8f : 0.58f,
            "check_in" => dynamicLabel is "cooling" or "uncertain_shift" ? 0.79f : 0.62f,
            "warm_reply" => status is "warm_platonic" or "reopening" ? 0.85f : 0.55f,
            "invite" => ambiguity <= 0.5f ? 0.76f : 0.4f,
            "light_test" => ambiguity <= 0.55f ? 0.71f : 0.45f,
            "deepen" => ambiguity <= 0.45f ? 0.75f : 0.35f,
            "boundaries" => status is "detached" or "fragile_contact" ? 0.74f : 0.45f,
            _ => 0.5f
        };

        return Math.Clamp(fit, 0f, 1f);
    }

    private static float ComputeProfileFit(string actionType, StrategyEvaluationContext context)
    {
        var selfTraits = context.ProfileTraits
            .Where(x => x.TraitKey == "communication_style" || x.TraitKey == "conflict_repair_behavior")
            .ToList();

        var style = selfTraits.FirstOrDefault(x => x.TraitKey == "communication_style")?.ValueLabel ?? "balanced_pragmatic";
        var repairBehavior = selfTraits.FirstOrDefault(x => x.TraitKey == "conflict_repair_behavior")?.ValueLabel ?? "low_signal_repair_pattern";

        var fit = 0.58f;
        if (style.Equals("brief_guarded", StringComparison.OrdinalIgnoreCase) && actionType is "check_in" or "hold_rapport" or "clarify")
        {
            fit += 0.16f;
        }

        if (style.Equals("detailed_expressive", StringComparison.OrdinalIgnoreCase) && actionType is "repair" or "warm_reply")
        {
            fit += 0.15f;
        }

        if (repairBehavior.Contains("repair", StringComparison.OrdinalIgnoreCase) && actionType == "repair")
        {
            fit += 0.18f;
        }

        return Math.Clamp(fit, 0f, 1f);
    }

    private static float ComputePairPatternFit(string actionType, StrategyEvaluationContext context)
    {
        var pairTraits = context.ProfileTraits
            .Where(x => x.TraitKey is "repair_capacity" or "distance_recovery" or "contact_rhythm" or "what_works" or "what_fails")
            .ToList();

        var repairCapacity = pairTraits.FirstOrDefault(x => x.TraitKey == "repair_capacity")?.ValueLabel ?? "repair_fragile";
        var distanceRecovery = pairTraits.FirstOrDefault(x => x.TraitKey == "distance_recovery")?.ValueLabel ?? "slow_recovery";

        var fit = 0.55f;
        if (repairCapacity.Equals("repair_capable", StringComparison.OrdinalIgnoreCase) && actionType == "repair")
        {
            fit += 0.2f;
        }

        if (distanceRecovery.Equals("recovers_after_distance", StringComparison.OrdinalIgnoreCase) && actionType is "check_in" or "hold_rapport")
        {
            fit += 0.17f;
        }

        if (pairTraits.Any(x => x.TraitKey == "what_fails" && x.ValueLabel.Contains("pressure", StringComparison.OrdinalIgnoreCase))
            && actionType is "invite" or "deepen")
        {
            fit -= 0.18f;
        }

        return Math.Clamp(fit, 0f, 1f);
    }
}

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class DynamicLabelMapper : IDynamicLabelMapper
{
    public string Map(
        StateScoreResult scores,
        CurrentStateContext context,
        StateConfidenceResult confidence,
        StateSnapshot? previousSnapshot)
    {
        var trend = ComputeTrend(scores, context.HistoricalSnapshots);

        var proposed = MapWithoutHysteresis(scores, trend, confidence, previousSnapshot);
        return ApplyHysteresis(proposed, scores, previousSnapshot, confidence);
    }

    private static string MapWithoutHysteresis(
        StateScoreResult scores,
        float trend,
        StateConfidenceResult confidence,
        StateSnapshot? previousSnapshot)
    {
        var historicalLowReciprocity = contextHistoryLowReciprocity(previousSnapshot, scores);

        if (confidence.HighAmbiguity || scores.Ambiguity >= 0.74f)
        {
            return "uncertain_shift";
        }

        if (scores.Reciprocity <= 0.34f || (scores.Reciprocity <= 0.42f && historicalLowReciprocity))
        {
            return "low_reciprocity";
        }

        if (scores.AvoidanceRisk >= 0.72f || (scores.Warmth <= 0.34f && scores.Responsiveness <= 0.42f))
        {
            return "fragile";
        }

        if (previousSnapshot != null
            && previousSnapshot.AvoidanceRiskScore >= 0.62f
            && scores.Responsiveness >= 0.58f
            && scores.Warmth >= 0.52f)
        {
            return "reengaging";
        }

        if (scores.Openness <= 0.52f && scores.Ambiguity >= 0.56f && scores.Warmth >= 0.42f)
        {
            return "testing_space";
        }

        if (trend >= 0.08f && scores.Warmth >= 0.52f)
        {
            return "warming";
        }

        if (trend <= -0.08f)
        {
            return "cooling";
        }

        return "stable";
    }

    private static string ApplyHysteresis(
        string proposed,
        StateScoreResult scores,
        StateSnapshot? previousSnapshot,
        StateConfidenceResult confidence)
    {
        if (previousSnapshot == null)
        {
            return proposed;
        }

        if (string.Equals(previousSnapshot.DynamicLabel, proposed, StringComparison.OrdinalIgnoreCase))
        {
            return proposed;
        }

        var prevRisk = (previousSnapshot.AmbiguityScore + previousSnapshot.AvoidanceRiskScore) / 2f;
        var nextRisk = (scores.Ambiguity + scores.AvoidanceRisk) / 2f;
        var riskDelta = Math.Abs(prevRisk - nextRisk);
        if (riskDelta < 0.09f && confidence.Confidence < 0.74f)
        {
            return previousSnapshot.DynamicLabel;
        }

        return proposed;
    }

    private static float ComputeTrend(StateScoreResult scores, IReadOnlyList<StateSnapshot> history)
    {
        if (history.Count == 0)
        {
            return 0f;
        }

        var recent = history.OrderByDescending(x => x.AsOf).Take(3).ToList();
        var avgWarmth = (float)recent.Average(x => x.WarmthScore);
        var avgResponsiveness = (float)recent.Average(x => x.ResponsivenessScore);
        return ((scores.Warmth - avgWarmth) + (scores.Responsiveness - avgResponsiveness)) / 2f;
    }

    private static bool contextHistoryLowReciprocity(StateSnapshot? previousSnapshot, StateScoreResult scores)
    {
        if (previousSnapshot == null)
        {
            return scores.Reciprocity <= 0.38f;
        }

        return previousSnapshot.ReciprocityScore <= 0.42f;
    }
}

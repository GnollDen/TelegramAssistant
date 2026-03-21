using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class RelationshipStatusMapper : IRelationshipStatusMapper
{
    public (string Primary, string? Alternative) Map(
        StateScoreResult scores,
        CurrentStateContext context,
        StateConfidenceResult confidence,
        StateSnapshot? previousSnapshot)
    {
        var scored = new List<(string Status, float Score)>
        {
            ("detached", ScoreDetached(scores)),
            ("fragile_contact", ScoreFragileContact(scores)),
            ("romantic_history_distanced", ScoreRomanticHistoryDistanced(scores, context)),
            ("reopening", ScoreReopening(scores, context, previousSnapshot)),
            ("warm_platonic", ScoreWarmPlatonic(scores)),
            ("platonic", ScorePlatonic(scores)),
            ("ambiguous", ScoreAmbiguous(scores, confidence))
        };

        var ordered = scored.OrderByDescending(x => x.Score).ToList();
        var primary = ordered[0].Status;
        var primaryScore = ordered[0].Score;

        string? alternative = null;
        var secondary = ordered[1];
        var competitionGap = primaryScore - secondary.Score;
        var allowAlternative = scores.Ambiguity >= 0.68f || confidence.HighAmbiguity || competitionGap <= 0.1f;
        if (allowAlternative && secondary.Status != primary)
        {
            alternative = secondary.Status;
        }

        primary = ApplyHysteresis(primary, scores, previousSnapshot, confidence);
        if (alternative == primary)
        {
            alternative = null;
        }

        return (primary, alternative);
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

        if (string.Equals(previousSnapshot.RelationshipStatus, proposed, StringComparison.OrdinalIgnoreCase))
        {
            return proposed;
        }

        var oldStability = (previousSnapshot.WarmthScore + previousSnapshot.ReciprocityScore + previousSnapshot.ResponsivenessScore) / 3f;
        var newStability = (scores.Warmth + scores.Reciprocity + scores.Responsiveness) / 3f;
        var delta = Math.Abs(newStability - oldStability);
        if (delta < 0.08f && confidence.Confidence < 0.75f)
        {
            return previousSnapshot.RelationshipStatus;
        }

        return proposed;
    }

    private static float ScoreDetached(StateScoreResult scores)
    {
        return (1f - scores.Warmth) * 0.45f
             + (1f - scores.Responsiveness) * 0.35f
             + scores.AvoidanceRisk * 0.2f;
    }

    private static float ScoreFragileContact(StateScoreResult scores)
    {
        return scores.AvoidanceRisk * 0.4f
             + scores.Ambiguity * 0.25f
             + (1f - scores.Responsiveness) * 0.2f
             + (1f - scores.Reciprocity) * 0.15f;
    }

    private static float ScoreRomanticHistoryDistanced(StateScoreResult scores, CurrentStateContext context)
    {
        var historyWarmth = context.HistoricalSnapshots.Count == 0 ? 0.5f : (float)context.HistoricalSnapshots.Average(x => x.WarmthScore);
        var distanceNow = (1f - scores.Warmth) * 0.4f + (1f - scores.EscalationReadiness) * 0.3f + scores.Ambiguity * 0.3f;
        var historicalRomanticHint = historyWarmth >= 0.66f ? 0.25f : 0f;
        return Math.Clamp(distanceNow + historicalRomanticHint, 0f, 1f);
    }

    private static float ScoreReopening(StateScoreResult scores, CurrentStateContext context, StateSnapshot? previousSnapshot)
    {
        var trend = 0f;
        if (context.HistoricalSnapshots.Count > 0)
        {
            var recent = context.HistoricalSnapshots.OrderByDescending(x => x.AsOf).Take(2).ToList();
            trend = scores.Warmth - (float)recent.Average(x => x.WarmthScore);
        }

        var reopenedFromFragile = previousSnapshot != null
            && (previousSnapshot.RelationshipStatus.Equals("fragile_contact", StringComparison.OrdinalIgnoreCase)
                || previousSnapshot.RelationshipStatus.Equals("detached", StringComparison.OrdinalIgnoreCase));

        return Math.Clamp(
            scores.Responsiveness * 0.25f
            + scores.Warmth * 0.25f
            + scores.EscalationReadiness * 0.2f
            + Math.Max(0f, trend) * 0.2f
            + (reopenedFromFragile ? 0.1f : 0f),
            0f,
            1f);
    }

    private static float ScoreWarmPlatonic(StateScoreResult scores)
    {
        return scores.Warmth * 0.4f
             + scores.Reciprocity * 0.25f
             + scores.Responsiveness * 0.2f
             + (1f - scores.Ambiguity) * 0.15f
             - scores.EscalationReadiness * 0.1f;
    }

    private static float ScorePlatonic(StateScoreResult scores)
    {
        return scores.Warmth * 0.2f
             + scores.Reciprocity * 0.22f
             + (1f - Math.Abs(scores.EscalationReadiness - 0.2f)) * 0.28f
             + (1f - scores.Ambiguity) * 0.2f
             + scores.Responsiveness * 0.1f;
    }

    private static float ScoreAmbiguous(StateScoreResult scores, StateConfidenceResult confidence)
    {
        var baseScore = scores.Ambiguity * 0.48f
                      + scores.EscalationReadiness * 0.2f
                      + scores.Openness * 0.12f
                      + (1f - confidence.Confidence) * 0.2f;

        return Math.Clamp(baseScore, 0f, 1f);
    }
}

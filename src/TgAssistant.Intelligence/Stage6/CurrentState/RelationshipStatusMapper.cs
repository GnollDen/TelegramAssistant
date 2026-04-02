// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class RelationshipStatusMapper : IRelationshipStatusMapper
{
    private static readonly string[] BreakupTokens =
    [
        "breakup",
        "post_breakup",
        "separation",
        "separated",
        "separate",
        "ex_partner",
        "расстав",
        "разрыв",
        "разош",
        "бывш"
    ];

    private static readonly string[] NoContactTokens =
    [
        "no_contact",
        "no contact",
        "zero_contact",
        "zero contact",
        "silent_treatment",
        "ghost",
        "без контакта",
        "нет контакта",
        "не обща",
        "не на связи",
        "игнор"
    ];

    public (string Primary, string? Alternative) Map(
        StateScoreResult scores,
        CurrentStateContext context,
        StateConfidenceResult confidence,
        StateSnapshot? previousSnapshot)
    {
        var scored = new List<(string Status, float Score)>
        {
            ("no_contact", ScoreNoContact(scores, context, previousSnapshot)),
            ("post_breakup", ScorePostBreakup(scores, context, previousSnapshot)),
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

    private static float ScorePostBreakup(StateScoreResult scores, CurrentStateContext context, StateSnapshot? previousSnapshot)
    {
        var breakupEvidence = HasOfflineEvidence(context, BreakupTokens);
        var noContactEvidence = HasOfflineEvidence(context, NoContactTokens);
        var recentTwoWayContact = HasTwoWayRecentContact(context, days: 14);
        var historicalWarmth = context.HistoricalSnapshots.Count == 0
            ? 0.5f
            : (float)context.HistoricalSnapshots.Average(x => x.WarmthScore);

        var baseScore =
            (1f - scores.EscalationReadiness) * 0.2f
            + (1f - scores.Responsiveness) * 0.16f
            + scores.AvoidanceRisk * 0.17f
            + scores.Ambiguity * 0.16f
            + (historicalWarmth >= 0.62f ? 0.08f : 0f);

        if (breakupEvidence)
        {
            baseScore += 0.38f;
        }

        if (noContactEvidence)
        {
            baseScore += 0.08f;
        }

        if (previousSnapshot != null
            && previousSnapshot.RelationshipStatus.Equals("post_breakup", StringComparison.OrdinalIgnoreCase))
        {
            baseScore += 0.08f;
        }

        if (recentTwoWayContact && scores.Warmth >= 0.56f && scores.EscalationReadiness >= 0.52f)
        {
            baseScore -= 0.18f;
        }

        return Math.Clamp(baseScore, 0f, 1f);
    }

    private static float ScoreNoContact(StateScoreResult scores, CurrentStateContext context, StateSnapshot? previousSnapshot)
    {
        var noContactEvidence = HasOfflineEvidence(context, NoContactTokens);
        var breakupEvidence = HasOfflineEvidence(context, BreakupTokens);
        var recentTwoWayContact = HasTwoWayRecentContact(context, days: 10);
        var longContactGap = HasNoRecentMessages(context, days: 21);

        var baseScore =
            (1f - scores.Responsiveness) * 0.28f
            + (1f - scores.Reciprocity) * 0.2f
            + scores.AvoidanceRisk * 0.22f
            + (1f - scores.Warmth) * 0.1f;

        if (noContactEvidence)
        {
            baseScore += 0.42f;
        }

        if (longContactGap)
        {
            baseScore += 0.22f;
        }

        if (breakupEvidence)
        {
            baseScore += 0.06f;
        }

        if (previousSnapshot != null
            && previousSnapshot.RelationshipStatus.Equals("no_contact", StringComparison.OrdinalIgnoreCase))
        {
            baseScore += 0.08f;
        }

        if (recentTwoWayContact)
        {
            baseScore -= 0.24f;
        }

        return Math.Clamp(baseScore, 0f, 1f);
    }

    private static bool HasOfflineEvidence(CurrentStateContext context, IReadOnlyCollection<string> tokens)
    {
        if (context.OfflineEvents.Count == 0)
        {
            return false;
        }

        return context.OfflineEvents.Any(x =>
            ContainsAny(x.EventType, tokens)
            || ContainsAny(x.Title, tokens)
            || ContainsAny(x.UserSummary, tokens)
            || ContainsAny(x.AutoSummary, tokens)
            || ContainsAny(x.ImpactSummary, tokens));
    }

    private static bool HasNoRecentMessages(CurrentStateContext context, int days)
    {
        if (context.RecentMessages.Count == 0)
        {
            return true;
        }

        var cutoff = context.AsOfUtc.AddDays(-days);
        return context.RecentMessages.All(x => x.Timestamp < cutoff);
    }

    private static bool HasTwoWayRecentContact(CurrentStateContext context, int days)
    {
        if (context.RecentMessages.Count == 0)
        {
            return false;
        }

        var cutoff = context.AsOfUtc.AddDays(-days);
        var senders = context.RecentMessages
            .Where(x => x.Timestamp >= cutoff)
            .Select(x => x.SenderId)
            .Distinct()
            .Take(2)
            .Count();
        return senders >= 2;
    }

    private static bool ContainsAny(string? text, IReadOnlyCollection<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

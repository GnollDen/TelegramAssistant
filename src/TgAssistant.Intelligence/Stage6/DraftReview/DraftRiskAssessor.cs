using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.DraftReview;

public class DraftRiskAssessor : IDraftRiskAssessor
{
    public DraftRiskAssessment Assess(DraftReviewContext context, DraftStrategyFitResult strategyFit)
    {
        var text = context.CandidateText;
        var lower = text.ToLowerInvariant();
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        AddIf("overpressure", HasAny(lower, "must", "you need", "right now", "today", "сейчас", "сегодня", "должен", "должна", "ответь"));
        AddIf("overdisclosure", HasAny(lower, "can't live", "obsessed", "не могу без", "одержим", "одержима", "слишком лич"));
        AddIf("premature_escalation", HasAny(lower, "love you", "relationship", "exclusive", "встречаемся", "отношения", "люблю"));
        AddIf("friendship_misread", HasFriendshipMisread(context, lower));
        AddIf("neediness_signal", HasAny(lower, "why ignore", "please answer", "почему игнор", "умоляю", "ответь мне", "ты где"));
        AddIf("ambiguity_increase", HasAmbiguityIncrease(lower));
        AddIf("withdrawal_trigger", HasAny(lower, "you always", "you never", "всегда", "никогда", "виноват", "виновата", "претенз"));
        AddIf("timing_mismatch", HasTimingMismatch(context, lower));

        if (strategyFit.HasMaterialConflict)
        {
            AddScore("overpressure", 2);
            AddScore("timing_mismatch", 2);
        }

        var top = scores
            .Where(x => x.Value > 0)
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(x => x.Key)
            .ToList();
        if (top.Count == 0)
        {
            top.Add("timing_mismatch");
        }

        var riskLabels = top.ToList();
        if (strategyFit.HasMaterialConflict && !riskLabels.Contains("timing_mismatch", StringComparer.OrdinalIgnoreCase))
        {
            riskLabels.Add("timing_mismatch");
        }

        var summary = BuildSummary(context, top, strategyFit);
        return new DraftRiskAssessment
        {
            Summary = summary,
            MainRisks = top,
            RiskLabels = riskLabels
        };

        void AddIf(string label, bool condition)
        {
            if (condition)
            {
                AddScore(label, 3);
            }
        }

        void AddScore(string label, int score)
        {
            scores[label] = scores.TryGetValue(label, out var current) ? current + score : score;
        }
    }

    private static bool HasFriendshipMisread(DraftReviewContext context, string lower)
    {
        var status = context.CurrentState?.RelationshipStatus ?? string.Empty;
        if (status is not ("platonic" or "warm_platonic" or "ambiguous"))
        {
            return false;
        }

        return HasAny(lower, "date", "romantic", "отношения", "пара", "встречаемся");
    }

    private static bool HasAmbiguityIncrease(string lower)
    {
        var markerCount = 0;
        if (lower.Contains('?'))
        {
            markerCount++;
        }

        if (HasAny(lower, "maybe", "whatever", "как хочешь", "может", "не знаю"))
        {
            markerCount++;
        }

        if (HasAny(lower, "idk", "непонятно", "сам не понимаю", "сама не понимаю"))
        {
            markerCount++;
        }

        return markerCount >= 2;
    }

    private static bool HasTimingMismatch(DraftReviewContext context, string lower)
    {
        var asksImmediateAction = HasAny(lower, "today", "tonight", "now", "сегодня", "сейчас", "прямо сейчас");
        var state = context.CurrentState;
        var riskyState = state != null && (state.AvoidanceRiskScore >= 0.55f || state.ExternalPressureScore >= 0.55f || state.ResponsivenessScore <= 0.45f);
        var softStrategy = context.PrimaryOption.ActionType is "wait" or "hold_rapport" or "check_in" or "clarify" or "deescalate" or "repair" or "acknowledge_separation" or "test_receptivity";
        return asksImmediateAction && (riskyState || softStrategy);
    }

    private static string BuildSummary(DraftReviewContext context, IReadOnlyList<string> topRisks, DraftStrategyFitResult strategyFit)
    {
        var strategy = context.PrimaryOption.ActionType;
        var status = context.CurrentState?.RelationshipStatus ?? "unknown";
        var dynamics = context.CurrentState?.DynamicLabel ?? "unknown";

        var baseSummary = $"Draft aligns with current state ({status}/{dynamics}) with caution.";
        if (topRisks.Count > 0)
        {
            baseSummary = $"Main risks relative to strategy '{strategy}': {string.Join(", ", topRisks)}.";
        }

        if (strategyFit.HasMaterialConflict)
        {
            return $"{baseSummary} Strategy conflict noted: {strategyFit.ConflictNote}";
        }

        return baseSummary;
    }

    private static bool HasAny(string lowerText, params string[] tokens)
    {
        return tokens.Any(token => lowerText.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}

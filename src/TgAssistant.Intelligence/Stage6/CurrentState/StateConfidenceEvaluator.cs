using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class StateConfidenceEvaluator : IStateConfidenceEvaluator
{
    public StateConfidenceResult Evaluate(StateScoreResult scores, CurrentStateContext context)
    {
        var coherence = ComputeCoherence(scores);
        var evidenceQuality = ComputeEvidenceQuality(context);
        var conflictLevel = ComputeConflictLevel(scores, context);

        var confidence = 0.22f + coherence * 0.42f + evidenceQuality * 0.34f - conflictLevel * 0.38f;

        var highAmbiguity = scores.Ambiguity >= 0.68f;
        if (highAmbiguity)
        {
            confidence = Math.Min(confidence, 0.56f);
        }

        if (scores.HistoryConflictDetected)
        {
            confidence = Math.Min(confidence, 0.62f);
        }

        return new StateConfidenceResult
        {
            Confidence = Math.Clamp(confidence, 0.05f, 0.94f),
            HighAmbiguity = highAmbiguity,
            HistoryConflictDetected = scores.HistoryConflictDetected,
            ScoreCoherence = coherence,
            EvidenceQuality = evidenceQuality,
            ConflictLevel = conflictLevel
        };
    }

    private static float ComputeCoherence(StateScoreResult scores)
    {
        var positive = (scores.Warmth + scores.Responsiveness + scores.Reciprocity + scores.Openness) / 4f;
        var risk = (scores.Ambiguity + scores.AvoidanceRisk + scores.ExternalPressure) / 3f;
        var tension = Math.Abs(positive - (1f - risk));
        return Math.Clamp(1f - tension, 0f, 1f);
    }

    private static float ComputeEvidenceQuality(CurrentStateContext context)
    {
        var messageScore = Math.Clamp(context.RecentMessages.Count / 40f, 0f, 1f);
        var sessionScore = Math.Clamp(context.RecentSessions.Count / 5f, 0f, 1f);
        var clarificationScore = Math.Clamp(context.ClarificationAnswers.Count / 4f, 0f, 1f);
        var periodScore = context.CurrentPeriod == null ? 0.35f : 0.75f;
        return Math.Clamp(messageScore * 0.42f + sessionScore * 0.2f + clarificationScore * 0.2f + periodScore * 0.18f, 0f, 1f);
    }

    private static float ComputeConflictLevel(StateScoreResult scores, CurrentStateContext context)
    {
        var openConflicts = context.Conflicts.Count(x =>
            x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
            || x.Status.Equals("review", StringComparison.OrdinalIgnoreCase));

        var conflictScore = Math.Clamp(openConflicts * 0.18f, 0f, 1f);
        var ambiguityScore = scores.Ambiguity * 0.35f;
        var historyScore = scores.HistoryConflictDetected ? 0.28f : 0f;
        return Math.Clamp(conflictScore + ambiguityScore + historyScore, 0f, 1f);
    }
}

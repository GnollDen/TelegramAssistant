// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class ProfileConfidenceEvaluator : IProfileConfidenceEvaluator
{
    public ProfileAssessment Evaluate(
        string subjectType,
        IReadOnlyList<ProfileTraitDraft> traits,
        ProfileEvidenceContext context,
        Period? period)
    {
        var messages = context.Messages
            .Where(x => period == null || (x.Timestamp >= period.StartAt && x.Timestamp <= (period.EndAt ?? DateTime.MaxValue)))
            .ToList();
        var answers = context.ClarificationAnswers
            .Where(x => period == null || (x.CreatedAt >= period.StartAt && x.CreatedAt <= (period.EndAt ?? DateTime.MaxValue)))
            .ToList();
        var events = context.OfflineEvents
            .Where(x => period == null || (x.TimestampStart >= period.StartAt && x.TimestampStart <= (period.EndAt ?? DateTime.MaxValue)))
            .ToList();

        var evidenceQuality = Math.Clamp((messages.Count * 0.01f) + (answers.Count * 0.04f) + (events.Count * 0.05f), 0f, 1f);
        var traitConfidence = traits.Count == 0 ? 0.35f : traits.Average(x => x.Confidence);
        var traitStability = traits.Count == 0 ? 0.3f : traits.Average(x => x.Stability);

        var ambiguity = context.StateSnapshots
            .Where(x => period == null || x.PeriodId == period.Id)
            .DefaultIfEmpty()
            .Average(x => x?.AmbiguityScore ?? 0.55f);

        var confidence = (traitConfidence * 0.5f) + (evidenceQuality * 0.35f) + ((1f - ambiguity) * 0.15f);
        var stability = (traitStability * 0.65f) + (evidenceQuality * 0.25f) + ((1f - ambiguity) * 0.1f);

        if (period != null)
        {
            stability -= 0.12f;
        }

        if (traits.Any(x => x.IsSensitive && x.ValueLabel.Equals("insufficient_evidence", StringComparison.OrdinalIgnoreCase)))
        {
            confidence = Math.Min(confidence, 0.62f);
            stability = Math.Min(stability, 0.54f);
        }

        var summary = BuildSummary(subjectType, period != null, traits, ambiguity);

        return new ProfileAssessment
        {
            Confidence = Math.Clamp(confidence, 0.2f, 0.92f),
            Stability = Math.Clamp(stability, 0.2f, 0.9f),
            Summary = summary
        };
    }

    private static string BuildSummary(string subjectType, bool periodSpecific, IReadOnlyList<ProfileTraitDraft> traits, float ambiguity)
    {
        var communication = traits.FirstOrDefault(x => x.TraitKey == "communication_style")?.ValueLabel
                            ?? traits.FirstOrDefault(x => x.TraitKey == "contact_rhythm")?.ValueLabel
                            ?? "mixed";
        var closeness = traits.FirstOrDefault(x => x.TraitKey == "closeness_distance_behavior")?.ValueLabel
                        ?? traits.FirstOrDefault(x => x.TraitKey == "distance_recovery")?.ValueLabel
                        ?? "unclear";
        var repair = traits.FirstOrDefault(x => x.TraitKey == "conflict_repair_behavior")?.ValueLabel
                     ?? traits.FirstOrDefault(x => x.TraitKey == "repair_capacity")?.ValueLabel
                     ?? "low_signal";

        var scope = periodSpecific ? "period" : "global";
        var caution = ambiguity >= 0.6f ? " High ambiguity: treat profile as provisional." : string.Empty;

        return $"{scope}:{subjectType}; communication={communication}; closeness={closeness}; conflict_repair={repair}.{caution}";
    }
}

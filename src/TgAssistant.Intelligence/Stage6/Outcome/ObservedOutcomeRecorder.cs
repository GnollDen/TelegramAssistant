// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Outcome;

public class ObservedOutcomeRecorder : IObservedOutcomeRecorder
{
    private static readonly string[] PositiveTokens = ["thanks", "thank you", "yes", "sure", "great", "warm", "glad", "good", "nice", "appreciate"];
    private static readonly string[] NegativeTokens = ["no", "stop", "don't", "leave", "bad", "cold", "upset", "angry", "busy", "later"];
    private static readonly string[] AmbiguousTokens = ["maybe", "not sure", "unclear", "idk", "hmm"];

    public ObservedOutcomeAssessment Assess(string? followUpText, string? explicitUserLabel)
    {
        var normalizedUser = NormalizeLabel(explicitUserLabel);
        if (string.IsNullOrWhiteSpace(followUpText))
        {
            return new ObservedOutcomeAssessment
            {
                Label = "unclear",
                Confidence = normalizedUser == "unclear" ? 0.4f : 0.3f,
                Reason = "no_follow_up_signal"
            };
        }

        var text = followUpText.Trim().ToLowerInvariant();
        var positive = PositiveTokens.Count(text.Contains);
        var negative = NegativeTokens.Count(text.Contains);
        var ambiguous = AmbiguousTokens.Count(text.Contains);

        string label;
        if (positive > 0 && negative > 0)
        {
            label = "mixed";
        }
        else if (positive > negative && positive > 0)
        {
            label = "positive";
        }
        else if (negative > positive && negative > 0)
        {
            label = "negative";
        }
        else if (ambiguous > 0)
        {
            label = "unclear";
        }
        else
        {
            label = "neutral";
        }

        var baseConfidence = label switch
        {
            "positive" or "negative" => 0.68f,
            "mixed" => 0.56f,
            "neutral" => 0.52f,
            _ => 0.4f
        };

        if (!string.IsNullOrWhiteSpace(normalizedUser) && normalizedUser != "unclear" && normalizedUser != label)
        {
            baseConfidence = Math.Max(0.25f, baseConfidence - 0.2f);
        }

        return new ObservedOutcomeAssessment
        {
            Label = label,
            Confidence = baseConfidence,
            Reason = $"token_heuristic:positive={positive},negative={negative},ambiguous={ambiguous}"
        };
    }

    public static string NormalizeLabel(string? label)
    {
        var normalized = label?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "positive" => "positive",
            "neutral" => "neutral",
            "negative" => "negative",
            "mixed" => "mixed",
            _ => "unclear"
        };
    }
}

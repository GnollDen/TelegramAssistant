// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.DraftReview;

public class NaturalRewriteGenerator : INaturalRewriteGenerator
{
    public string Generate(DraftReviewContext context, DraftRiskAssessment assessment)
    {
        var language = DetectLanguage(context.CandidateText, context.RecentMessages);
        var intent = ExtractIntent(context.CandidateText, language);
        var hasHighRisk = assessment.RiskLabels.Count > 0;
        var style = context.ProfileTraits
            .Where(x => x.TraitKey == "communication_style")
            .OrderByDescending(x => x.Confidence)
            .Select(x => x.ValueLabel)
            .FirstOrDefault() ?? "balanced_pragmatic";
        var warm = (context.CurrentState?.WarmthScore ?? 0.5f) >= 0.56f;

        if (language == "ru")
        {
            if (style == "brief_guarded")
            {
                return hasHighRisk
                    ? $"Привет. {intent} Если удобно, дай знать без спешки."
                    : $"Привет. {intent} Если удобно, дай знать.";
            }

            if (warm)
            {
                return $"Привет! {intent} Буду рад(а) коротко свериться, когда тебе комфортно.";
            }

            return $"Привет. {intent} Можно обсудить в удобный момент.";
        }

        if (style == "brief_guarded")
        {
            return hasHighRisk
                ? $"Hey. {intent} Let me know when convenient, no rush."
                : $"Hey. {intent} Let me know when convenient.";
        }

        return warm
            ? $"Hey! {intent} Happy to sync whenever this feels comfortable for you."
            : $"Hey. {intent} We can talk it through at a convenient time.";
    }

    private static string DetectLanguage(string text, IReadOnlyList<Message> messages)
    {
        var sample = $"{text} {string.Join(" ", messages.OrderByDescending(x => x.Timestamp).Take(6).Select(x => x.Text ?? string.Empty))}";
        var cyrillic = sample.Count(ch => ch is >= '\u0400' and <= '\u04FF');
        var latin = sample.Count(ch => ch is >= 'A' and <= 'z');
        return cyrillic > latin ? "ru" : "en";
    }

    private static string ExtractIntent(string text, string language)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return language == "ru"
                ? "Хочу коротко уточнить один момент."
                : "I want to clarify one short point.";
        }

        var normalized = text
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        var sentence = normalized
            .Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? normalized;

        if (sentence.Length > 150)
        {
            sentence = sentence[..149].TrimEnd() + "…";
        }

        if (!sentence.EndsWith('.') && !sentence.EndsWith('!') && !sentence.EndsWith('?'))
        {
            sentence += ".";
        }

        return sentence;
    }
}

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.DraftReview;

public class SaferRewriteGenerator : ISaferRewriteGenerator
{
    public string Generate(DraftReviewContext context, DraftRiskAssessment assessment, DraftStrategyFitResult strategyFit)
    {
        var language = DetectLanguage(context.CandidateText, context.RecentMessages);
        var intent = ExtractIntent(context.CandidateText, language);
        var action = context.PrimaryOption.ActionType;

        if (language == "ru")
        {
            var baseText = action switch
            {
                "wait" or "hold_rapport" => $"Привет. {intent} Без давления, ответь когда будет удобно.",
                "check_in" or "clarify" => $"Привет. Хочу аккуратно уточнить: {intent.ToLowerInvariant()} Если удобно, дай короткий ответ в своем ритме.",
                "deescalate" or "repair" => $"Привет. Не хочу усиливать напряжение. {intent} Если комфортно, вернемся к этому позже.",
                _ => $"Привет. {intent} Если тебе ок, можно обсудить это спокойно без спешки."
            };

            if (strategyFit.HasMaterialConflict)
            {
                return $"{baseText} С твоей стороны не срочно.";
            }

            return baseText;
        }

        var english = action switch
        {
            "wait" or "hold_rapport" => $"Hey. {intent} No pressure, reply when timing feels good.",
            "check_in" or "clarify" => $"Hey. I want to ask this gently: {intent.ToLowerInvariant()} If convenient, a short reply is enough.",
            "deescalate" or "repair" => $"Hey. I do not want to increase pressure here. {intent} We can return to it when timing is better.",
            _ => $"Hey. {intent} If you're open, we can discuss it calmly and without rush."
        };

        return strategyFit.HasMaterialConflict
            ? $"{english} No urgency from my side."
            : english;
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
                ? "Хочу коротко синхронизироваться"
                : "I want a quick alignment";
        }

        var sentence = text
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim()
            .Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim() ?? text.Trim();

        if (sentence.Length > 140)
        {
            sentence = sentence[..139].TrimEnd() + "…";
        }

        if (!sentence.EndsWith('.') && !sentence.EndsWith('!') && !sentence.EndsWith('?'))
        {
            sentence += ".";
        }

        return sentence;
    }
}

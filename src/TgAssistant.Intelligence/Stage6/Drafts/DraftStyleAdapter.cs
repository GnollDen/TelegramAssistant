using System.Text;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Drafts;

public class DraftStyleAdapter : IDraftStyleAdapter
{
    public DraftStyledContent ApplyStyle(DraftGenerationContext context, DraftContentSet content)
    {
        var communicationStyle = context.ProfileTraits
            .Where(x => x.TraitKey == "communication_style")
            .OrderByDescending(x => x.Confidence)
            .Select(x => x.ValueLabel)
            .FirstOrDefault() ?? "balanced_pragmatic";

        var warmthScore = context.CurrentState?.WarmthScore ?? 0.5f;
        var directnessHint = context.ProfileTraits
            .Where(x => x.TraitKey == "initiative_balance")
            .OrderByDescending(x => x.Confidence)
            .Select(x => x.ValueLabel)
            .FirstOrDefault() ?? "balanced";

        var main = content.MainDraft;
        var alt1 = content.AltDraft1;
        var alt2 = content.AltDraft2;
        var confidence = content.BaseConfidence;
        var notes = new List<string>
        {
            $"communication_style={communicationStyle}",
            $"warmth_score={(context.CurrentState?.WarmthScore ?? 0.5f):0.00}",
            $"directness_hint={directnessHint}"
        };

        switch (communicationStyle)
        {
            case "brief_guarded":
                main = KeepSingleSentence(main, 120);
                alt1 = KeepSingleSentence(alt1, 100);
                alt2 = KeepSingleSentence(alt2, 100);
                notes.Add("length=short");
                notes.Add("pacing=slow");
                break;

            case "detailed_expressive":
                main = ExpandWarmth(main, warmthScore);
                alt1 = ExpandWarmth(alt1, warmthScore);
                notes.Add("length=medium");
                notes.Add("emotional_density=explicit");
                confidence = Math.Clamp(confidence + 0.03f, 0f, 1f);
                break;

            default:
                main = KeepBalanced(main, 180);
                alt1 = KeepBalanced(alt1, 150);
                alt2 = KeepBalanced(alt2, 150);
                notes.Add("length=balanced");
                notes.Add("directness=moderate");
                break;
        }

        if (warmthScore < 0.45f)
        {
            main = Soften(main);
            notes.Add("warmth_guard=enabled");
        }

        return new DraftStyledContent
        {
            MainDraft = main,
            AltDraft1 = alt1,
            AltDraft2 = alt2,
            StyleNotes = string.Join("; ", notes),
            Confidence = confidence
        };
    }

    private static string KeepSingleSentence(string value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var sentence = value.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? value.Trim();
        if (!sentence.EndsWith('.') && !sentence.EndsWith('!') && !sentence.EndsWith('?'))
        {
            sentence += ".";
        }

        return TrimTo(sentence, maxLen);
    }

    private static string KeepBalanced(string value, int maxLen)
    {
        return TrimTo(value, maxLen);
    }

    private static string ExpandWarmth(string value, float warmthScore)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var suffix = warmthScore >= 0.58f
            ? " Мне важно, чтобы тебе было комфортно."
            : " Без давления, в удобном для тебя ритме.";

        var combined = value.Trim();
        if (!combined.EndsWith('.') && !combined.EndsWith('!') && !combined.EndsWith('?'))
        {
            combined += ".";
        }

        return TrimTo(combined + suffix, 220);
    }

    private static string Soften(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var prefixes = new[] { "если тебе ок,", "если удобно,", "без спешки," };
        var prefix = prefixes[Math.Abs(value.GetHashCode(StringComparison.Ordinal)) % prefixes.Length];
        return $"{Capitalize(prefix)} {value.TrimStart()}";
    }

    private static string TrimTo(string value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= maxLen)
        {
            return value.Trim();
        }

        var trimmed = value[..Math.Max(0, maxLen - 1)].TrimEnd();
        return trimmed + "…";
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var builder = new StringBuilder(value);
        builder[0] = char.ToUpperInvariant(builder[0]);
        return builder.ToString();
    }
}

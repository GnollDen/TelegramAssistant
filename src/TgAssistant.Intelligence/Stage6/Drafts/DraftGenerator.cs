// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Drafts;

public class DraftGenerator : IDraftGenerator
{
    public Task<DraftContentSet> GenerateAsync(DraftGenerationContext context, CancellationToken ct = default)
    {
        var language = DetectLanguage(context.RecentMessages);
        var greeting = BuildGreeting(language, context.CurrentState);
        var action = context.PrimaryOption.ActionType;
        var summary = context.PrimaryOption.Summary;
        var purpose = context.PrimaryOption.Purpose;
        var whenToUse = context.PrimaryOption.WhenToUse;
        var userNotes = (context.UserNotes ?? string.Empty).Trim();

        var main = language == "ru"
            ? $"{greeting} {BuildRuMain(action, purpose, userNotes)}"
            : $"{greeting} {BuildEnMain(action, purpose, userNotes)}";

        var alt1 = language == "ru"
            ? BuildRuAlternative(action, variant: 1, summary, whenToUse)
            : BuildEnAlternative(action, variant: 1, summary, whenToUse);

        var alt2 = language == "ru"
            ? BuildRuAlternative(action, variant: 2, summary, whenToUse)
            : BuildEnAlternative(action, variant: 2, summary, whenToUse);

        return Task.FromResult(new DraftContentSet
        {
            MainDraft = main,
            AltDraft1 = alt1,
            AltDraft2 = alt2,
            BaseConfidence = Math.Clamp((context.StrategyRecord.StrategyConfidence * 0.65f) + 0.2f, 0f, 1f)
        });
    }

    private static string DetectLanguage(IReadOnlyList<Message> messages)
    {
        var sample = string.Join(" ", messages
            .OrderByDescending(x => x.Timestamp)
            .Take(12)
            .Select(x => x.Text ?? string.Empty));

        if (string.IsNullOrWhiteSpace(sample))
        {
            return "en";
        }

        var cyrillic = sample.Count(ch => ch is >= '\u0400' and <= '\u04FF');
        var latin = sample.Count(ch => ch is >= 'A' and <= 'z');
        return cyrillic > latin ? "ru" : "en";
    }

    private static string BuildGreeting(string language, StateSnapshot? state)
    {
        var warm = (state?.WarmthScore ?? 0.5f) >= 0.58f;
        if (language == "ru")
        {
            return warm ? "Привет!" : "Привет.";
        }

        return warm ? "Hey!" : "Hey.";
    }

    private static string BuildRuMain(string actionType, string purpose, string userNotes)
    {
        var core = actionType switch
        {
            "wait" => "Пойму, если сейчас не лучшее время. Давай спокойно вернемся к разговору позже.",
            "hold_rapport" => "Просто хотел(а) тепло отметиться без давления. Надеюсь, у тебя более спокойный день.",
            "check_in" => "Коротко check-in: как ты сейчас? Без обязательств отвечать подробно.",
            "clarify" => "Хочу аккуратно уточнить один момент, чтобы не додумывать лишнего.",
            "deescalate" => "Не хочу нагружать разговор. Давай снизим темп и продолжим в комфортном ритме.",
            "repair" => "Кажется, между нами осталась шероховатость. Хочу спокойно это поправить, без давления.",
            "warm_reply" => "Спасибо за отклик, мне было важно это услышать. Рад(а), что мы на связи.",
            "light_test" => "Если ок, можем коротко свериться по планам на неделе.",
            "acknowledge_separation" => "Я признаю, что мы сейчас в периоде после расставания, и не хочу давить на быстрые решения.",
            "test_receptivity" => "Если тебе ок, можно совсем коротко свериться, комфортно ли сейчас поддерживать контакт.",
            "re_establish_contact" => "Пишу спокойно восстановить контакт, без ожиданий и без давления.",
            "invite" => "Если тебе удобно, можно встретиться в легком формате на этой неделе.",
            "deepen" => "Если ты не против, можно немного глубже обсудить то, что для нас важно.",
            "boundaries" => "Мне важно сохранить уважительный темп общения, поэтому обозначу границу по интенсивности.",
            _ => "Хочу написать спокойно и по делу."
        };

        if (string.IsNullOrWhiteSpace(userNotes))
        {
            return core;
        }

        return $"{core} Учел(а) твой комментарий: {TrimUserNote(userNotes)}";
    }

    private static string BuildEnMain(string actionType, string purpose, string userNotes)
    {
        var core = actionType switch
        {
            "wait" => "No pressure from my side. We can continue when timing feels better.",
            "hold_rapport" => "Just wanted to send a warm touchpoint without any pressure.",
            "check_in" => "Quick check-in from me: how are you doing lately?",
            "clarify" => "I want to ask one focused question so I do not misread things.",
            "deescalate" => "I do not want to escalate this. Let's keep the pace calm and clear.",
            "repair" => "I think there is some friction between us, and I'd like to repair it calmly.",
            "warm_reply" => "Thanks for your message, it genuinely means a lot to me.",
            "light_test" => "If you're open, we can do a quick low-pressure check on plans.",
            "acknowledge_separation" => "I acknowledge we're in a post-breakup phase, and I do not want to force fast decisions.",
            "test_receptivity" => "If you're open, we can do a very light check on whether contact feels comfortable now.",
            "re_establish_contact" => "Reaching out to re-establish contact calmly, with no pressure from my side.",
            "invite" => "If it feels right for you, we could do a light meetup this week.",
            "deepen" => "If you're open to it, I'd like one slightly deeper conversation.",
            "boundaries" => "I want to keep communication respectful, so I need to set one boundary on pace.",
            _ => "I wanted to send a calm and clear message."
        };

        if (string.IsNullOrWhiteSpace(userNotes))
        {
            return core;
        }

        return $"{core} I also considered your note: {TrimUserNote(userNotes)}";
    }

    private static string BuildRuAlternative(string actionType, int variant, string summary, string whenToUse)
    {
        return variant switch
        {
            1 => $"Более мягко: {Shorten(summary, 120)}",
            _ => $"Более прямо: предлагаю коротко согласовать следующий шаг без лишних деталей. {Shorten(whenToUse, 90)}"
        };
    }

    private static string BuildEnAlternative(string actionType, int variant, string summary, string whenToUse)
    {
        return variant switch
        {
            1 => $"Softer version: {Shorten(summary, 120)}",
            _ => $"More direct version: let's align on one concrete next step. {Shorten(whenToUse, 90)}"
        };
    }

    private static string Shorten(string text, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = text.Trim();
        return text.Length <= maxLen ? text : text[..Math.Max(0, maxLen - 1)] + "…";
    }

    private static string TrimUserNote(string value)
    {
        var compact = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return compact.Length <= 90 ? compact : compact[..89] + "…";
    }
}

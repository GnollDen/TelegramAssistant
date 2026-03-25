using System.Text.RegularExpressions;

namespace TgAssistant.Intelligence.Stage5;

public static class ExtractionSemanticContract
{
    private static readonly Regex SnakeCaseRegex = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CyrillicRegex = new(@"[\p{IsCyrillic}]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly HashSet<string> AllowedEntityTypes =
    [
        "Person",
        "Organization",
        "Place",
        "Pet",
        "Event"
    ];

    public static readonly HashSet<string> AllowedClaimTypes =
    [
        "fact",
        "intent",
        "preference",
        "relationship",
        "state",
        "need"
    ];

    public static readonly HashSet<string> AllowedCategories =
    [
        "availability",
        "location",
        "schedule",
        "health",
        "work",
        "travel",
        "relationship",
        "contact",
        "finance"
    ];

    public static readonly HashSet<string> AllowedRelationshipTypes =
    [
        "семья",
        "друг",
        "коллега",
        "знакомый",
        "партнер",
        "сосед"
    ];

    private static readonly Dictionary<string, HashSet<string>> AllowedKeysByCategory = new(StringComparer.Ordinal)
    {
        ["availability"] = ["свободное_время", "занятость"],
        ["location"] = ["текущее_местоположение", "shared_location", "домашний_адрес", "рабочий_адрес"],
        ["schedule"] = ["расписание", "время_встречи"],
        ["health"] = ["состояние_здоровья", "принимает_лекарства", "диагноз"],
        ["work"] = ["должность", "место_работы", "команда"],
        ["travel"] = ["план_поездки", "направление"],
        ["relationship"] = ["статус_отношений", "семейное_положение"],
        ["contact"] = ["телефон", "telegram_handle"],
        ["finance"] = ["доход", "расход"]
    };

    public static bool IsSnakeCase(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && SnakeCaseRegex.IsMatch(value);
    }

    public static bool IsLikelyRussianText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && CyrillicRegex.IsMatch(value);
    }

    public static bool IsAllowedKey(string category, string key)
    {
        if (!AllowedKeysByCategory.TryGetValue(category, out var keys))
        {
            return false;
        }

        return keys.Contains(key);
    }
}

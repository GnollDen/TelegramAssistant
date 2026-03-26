using System.Text;
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
        "need",
        "profile_signal"
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
        "family",
        "friend",
        "colleague",
        "acquaintance",
        "partner",
        "neighbor"
    ];

    private static readonly Dictionary<string, HashSet<string>> AllowedKeysByCategory = new(StringComparer.Ordinal)
    {
        ["availability"] = ["free_time", "busy_status"],
        ["location"] = ["current_location", "shared_location", "home_address", "work_address"],
        ["schedule"] = ["schedule", "meeting_time"],
        ["health"] = ["health_status", "medication_usage", "diagnosis"],
        ["work"] = ["job_title", "workplace", "team"],
        ["travel"] = ["travel_plan", "destination"],
        ["relationship"] = ["relationship_status", "family_status"],
        ["contact"] = ["phone", "telegram_handle"],
        ["finance"] = ["income", "expenses"]
    };

    private static readonly Dictionary<string, string> CategoryAliases = new(StringComparer.Ordinal)
    {
        ["доступность"] = "availability",
        ["местоположение"] = "location",
        ["локация"] = "location",
        ["расписание"] = "schedule",
        ["здоровье"] = "health",
        ["работа"] = "work",
        ["поездки"] = "travel",
        ["путешествия"] = "travel",
        ["отношения"] = "relationship",
        ["контакт"] = "contact",
        ["контакты"] = "contact",
        ["финансы"] = "finance"
    };

    private static readonly Dictionary<string, string> ClaimTypeAliases = new(StringComparer.Ordinal)
    {
        ["факт"] = "fact",
        ["намерение"] = "intent",
        ["предпочтение"] = "preference",
        ["отношение"] = "relationship",
        ["состояние"] = "state",
        ["потребность"] = "need",
        ["profile"] = "profile_signal",
        ["status"] = "state",
        ["info"] = "fact",
        ["information"] = "fact",
        ["update"] = "fact"
    };

    private static readonly Dictionary<string, string> RelationshipTypeAliases = new(StringComparer.Ordinal)
    {
        ["семья"] = "family",
        ["друг"] = "friend",
        ["коллега"] = "colleague",
        ["знакомый"] = "acquaintance",
        ["партнер"] = "partner",
        ["сосед"] = "neighbor"
    };

    private static readonly Dictionary<string, string> ObservationTypeAliases = new(StringComparer.Ordinal)
    {
        ["availability_status"] = "availability_update",
        ["location_share"] = "location_update",
        ["contact_shared"] = "contact_share"
    };

    private static readonly Dictionary<string, string> EventTypeAliases = new(StringComparer.Ordinal)
    {
        ["status_update"] = "availability_update",
        ["location_share"] = "location_update"
    };

    private static readonly Dictionary<string, string> ProfileTraitAliases = new(StringComparer.Ordinal)
    {
        ["stress_level"] = "stress_signal",
        ["mood_status"] = "mood_signal",
        ["emotional_attachment"] = "emotional_attachment",
        ["emotsionalnaya_privyazannost"] = "emotional_attachment",
        ["trevozhnost"] = "anxiety",
        ["anxiety"] = "anxiety",
        ["zabotlivyy"] = "supportiveness",
        ["zabotlivaya"] = "supportiveness",
        ["zabotlivost"] = "supportiveness",
        ["supportiveness"] = "supportiveness",
        ["laskovyy"] = "affection",
        ["laskovost"] = "affection",
        ["laskovaya"] = "affection",
        ["affection"] = "affection",
        ["emotsionalnyy"] = "emotionality",
        ["emotsionalnost"] = "emotionality",
        ["emotionality"] = "emotionality",
        ["podderzhivayushchiy"] = "supportive",
        ["supportive"] = "supportive",
        ["emotsionalnaya_uyazvimost"] = "emotional_vulnerability",
        ["emotional_vulnerability"] = "emotional_vulnerability",
        ["emotsionalno_nestabilnyy"] = "emotional_instability",
        ["emotsionalnaya_nestabilnost"] = "emotional_instability",
        ["emotional_instability"] = "emotional_instability",
        ["nenadezhnost"] = "unreliability",
        ["unreliability"] = "unreliability",
        ["druzhelyubnyy"] = "friendliness",
        ["druzhestvennyy"] = "friendliness",
        ["friendliness"] = "friendliness",
        ["affektivnost"] = "affectivity",
        ["affectivity"] = "affectivity",
        ["emotsionalnoe_sostoyanie"] = "emotional_state",
        ["emotional_state"] = "emotional_state",
        ["emotsionalnaya_zabota"] = "emotional_care",
        ["emotional_care"] = "emotional_care",
        ["strakh_odinochestva"] = "fear_of_loneliness",
        ["fear_of_loneliness"] = "fear_of_loneliness",
        ["impulsivnost"] = "impulsiveness",
        ["impulsiveness"] = "impulsiveness",
        ["neuverennost_v_rabote"] = "work_insecurity",
        ["work_insecurity"] = "work_insecurity",
        ["interes_k_zdorovomu_obrazu_zhizni"] = "healthy_lifestyle_interest",
        ["healthy_lifestyle_interest"] = "healthy_lifestyle_interest",
        ["emotsionalnaya_zavisimost"] = "emotional_dependency",
        ["emotsionalnaya_zavisimost_ot_golosa"] = "emotional_dependency",
        ["emotional_dependency"] = "emotional_dependency",
        ["podderzhka_druzey"] = "support_network",
        ["support_network"] = "support_network",
        ["pomogayushchaya"] = "helpfulness",
        ["helpfulness"] = "helpfulness",
        ["neterpelivost"] = "impatience",
        ["impatience"] = "impatience",
        ["malo_druzey"] = "few_friends",
        ["few_friends"] = "few_friends",
        ["uspeshnaya_v_rabote"] = "successful_at_work",
        ["successful_at_work"] = "successful_at_work"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> KeyAliasesByCategory = new(StringComparer.Ordinal)
    {
        ["availability"] = new(StringComparer.Ordinal)
        {
            ["свободное_время"] = "free_time",
            ["занятость"] = "busy_status",
            ["availability_status"] = "busy_status"
        },
        ["location"] = new(StringComparer.Ordinal)
        {
            ["текущее_местоположение"] = "current_location",
            ["домашний_адрес"] = "home_address",
            ["рабочий_адрес"] = "work_address",
            ["current_place"] = "current_location",
            ["share_info"] = "shared_location",
            ["location_share"] = "shared_location",
            ["shared_info"] = "shared_location"
        },
        ["schedule"] = new(StringComparer.Ordinal)
        {
            ["расписание"] = "schedule",
            ["время_встречи"] = "meeting_time",
            ["work_schedule"] = "schedule",
            ["job_schedule"] = "schedule"
        },
        ["health"] = new(StringComparer.Ordinal)
        {
            ["состояние_здоровья"] = "health_status",
            ["принимает_лекарства"] = "medication_usage",
            ["диагноз"] = "diagnosis",
            ["medications"] = "medication_usage"
        },
        ["work"] = new(StringComparer.Ordinal)
        {
            ["должность"] = "job_title",
            ["место_работы"] = "workplace",
            ["команда"] = "team",
            ["work_plan"] = "schedule",
            ["work_plans"] = "schedule"
        },
        ["travel"] = new(StringComparer.Ordinal)
        {
            ["план_поездки"] = "travel_plan",
            ["направление"] = "destination"
        },
        ["relationship"] = new(StringComparer.Ordinal)
        {
            ["статус_отношений"] = "relationship_status",
            ["семейное_положение"] = "family_status"
        },
        ["contact"] = new(StringComparer.Ordinal)
        {
            ["телефон"] = "phone",
            ["phone_number"] = "phone"
        },
        ["finance"] = new(StringComparer.Ordinal)
        {
            ["доход"] = "income",
            ["расход"] = "expenses",
            ["expense"] = "expenses",
            ["salary"] = "income"
        }
    };

    public static bool IsSnakeCase(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && SnakeCaseRegex.IsMatch(value);
    }

    public static bool IsAllowedProfileTrait(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (IsSnakeCase(value))
        {
            return true;
        }

        var normalized = NormalizeProfileToken(value);
        return normalized.Length > 0 && ProfileTraitAliases.ContainsKey(normalized);
    }

    public static bool IsLikelyRussianText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && CyrillicRegex.IsMatch(value);
    }

    public static bool IsAllowedKey(string category, string key)
    {
        var (canonicalCategory, canonicalKey) = CanonicalizeCategoryAndKey(category, key);
        if (!AllowedKeysByCategory.TryGetValue(canonicalCategory, out var keys))
        {
            return false;
        }

        return keys.Contains(canonicalKey);
    }

    public static string CanonicalizeCategory(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return CategoryAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string CanonicalizeClaimType(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return ClaimTypeAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string CanonicalizeRelationshipType(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return RelationshipTypeAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string CanonicalizeObservationType(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return ObservationTypeAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string CanonicalizeEventType(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return EventTypeAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string CanonicalizeTrait(string? value)
    {
        var normalized = NormalizeProfileToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return ProfileTraitAliases.GetValueOrDefault(normalized, normalized);
    }

    public static string CanonicalizeProfileSignalDirection(string? value)
    {
        var normalized = NormalizeProfileToken(value);
        if (normalized.Length == 0)
        {
            return "neutral";
        }

        if (ProfileDirectionAliases.TryGetValue(normalized, out var alias))
        {
            return alias;
        }

        if (normalized.StartsWith("positive", StringComparison.Ordinal))
        {
            return "positive";
        }

        if (normalized.StartsWith("negative", StringComparison.Ordinal))
        {
            return "negative";
        }

        if (normalized.StartsWith("neutral", StringComparison.Ordinal))
        {
            return "neutral";
        }

        if (normalized.StartsWith("mixed", StringComparison.Ordinal))
        {
            return "mixed";
        }

        if (normalized.Contains("positive", StringComparison.Ordinal))
        {
            return "positive";
        }

        if (normalized.Contains("negative", StringComparison.Ordinal))
        {
            return "negative";
        }

        if (normalized.Contains("mixed", StringComparison.Ordinal))
        {
            return "mixed";
        }

        return "neutral";
    }

    public static string CanonicalizeKey(string? category, string? key)
    {
        var canonicalCategory = CanonicalizeCategory(category);
        var normalizedKey = NormalizeToken(key);
        if (canonicalCategory.Length == 0 || normalizedKey.Length == 0)
        {
            return normalizedKey;
        }

        if (KeyAliasesByCategory.TryGetValue(canonicalCategory, out var aliases) &&
            aliases.TryGetValue(normalizedKey, out var canonicalKey))
        {
            return canonicalKey;
        }

        return normalizedKey;
    }

    public static (string Category, string Key) CanonicalizeCategoryAndKey(string? category, string? key)
    {
        var canonicalCategory = CanonicalizeCategory(category);
        var canonicalKey = CanonicalizeKey(canonicalCategory, key);

        // Deterministic drift remap for stable reruns. Keep this list narrow and explicit.
        if (canonicalCategory == "work")
        {
            if (canonicalKey == "busy_status")
            {
                return ("availability", "busy_status");
            }

            if (canonicalKey == "constraints")
            {
                return ("schedule", "schedule");
            }

            if (canonicalKey == "schedule" ||
                canonicalKey == "work_schedule" ||
                canonicalKey == "job_schedule")
            {
                return ("schedule", "schedule");
            }
        }

        if (canonicalCategory == "schedule")
        {
            if (canonicalKey == "busy_status")
            {
                return ("availability", "busy_status");
            }

            if (canonicalKey == "free_time")
            {
                return ("availability", "free_time");
            }
        }

        return (canonicalCategory, canonicalKey);
    }

    public static string? GetUnsupportedKeyDriftHint(string? category, string? key)
    {
        var canonicalCategory = CanonicalizeCategory(category);
        var normalizedKey = NormalizeToken(key);
        if (canonicalCategory == "work" && normalizedKey == "job_status")
        {
            return "unsupported_drift:work.job_status;use_work.job_title_or_schedule.schedule";
        }

        if (canonicalCategory == "work" && normalizedKey == "work")
        {
            return "unsupported_drift:work.work;use_work.job_title_or_work.workplace_or_schedule.schedule_or_availability.free_time";
        }

        if (canonicalCategory == "contact" && normalizedKey == "share_info")
        {
            return "unsupported_drift:contact.share_info;use_contact.phone_or_contact.telegram_handle";
        }

        return null;
    }

    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim()
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(':', '_')
            .Replace(' ', '_');
        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return normalized;
    }

    private static string NormalizeProfileToken(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CyrillicTransliteration.TryGetValue(ch, out var transliterated))
            {
                builder.Append(transliterated);
                continue;
            }

            builder.Append(ch);
        }

        var result = builder.ToString().Trim('_');
        return result;
    }

    private static readonly Dictionary<char, string> CyrillicTransliteration = new()
    {
        ['а'] = "a",
        ['б'] = "b",
        ['в'] = "v",
        ['г'] = "g",
        ['д'] = "d",
        ['е'] = "e",
        ['ё'] = "yo",
        ['ж'] = "zh",
        ['з'] = "z",
        ['и'] = "i",
        ['й'] = "y",
        ['к'] = "k",
        ['л'] = "l",
        ['м'] = "m",
        ['н'] = "n",
        ['о'] = "o",
        ['п'] = "p",
        ['р'] = "r",
        ['с'] = "s",
        ['т'] = "t",
        ['у'] = "u",
        ['ф'] = "f",
        ['х'] = "kh",
        ['ц'] = "ts",
        ['ч'] = "ch",
        ['ш'] = "sh",
        ['щ'] = "shch",
        ['ы'] = "y",
        ['э'] = "e",
        ['ю'] = "yu",
        ['я'] = "ya",
        ['ъ'] = string.Empty,
        ['ь'] = string.Empty
    };

    private static readonly Dictionary<string, string> ProfileDirectionAliases = new(StringComparer.Ordinal)
    {
        ["positive"] = "positive",
        ["negative"] = "negative",
        ["neutral"] = "neutral",
        ["mixed"] = "mixed",
        ["polozhitelnaya"] = "positive",
        ["smeshannyy"] = "mixed",
        ["grust"] = "negative",
        ["redkoe"] = "neutral",
        ["text"] = "neutral",
        ["occasional"] = "neutral"
    };
}

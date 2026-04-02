using System.Text;
using System.Text.RegularExpressions;

namespace TgAssistant.Core.Normalization;

public static partial class EntityAliasNormalizer
{
    private static readonly Dictionary<char, string> CyrillicToLatin = new()
    {
        ['а'] = "a",
        ['б'] = "b",
        ['в'] = "v",
        ['г'] = "g",
        ['д'] = "d",
        ['е'] = "e",
        ['ё'] = "e",
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
        ['х'] = "h",
        ['ц'] = "ts",
        ['ч'] = "ch",
        ['ш'] = "sh",
        ['щ'] = "sch",
        ['ъ'] = string.Empty,
        ['ы'] = "y",
        ['ь'] = string.Empty,
        ['э'] = "e",
        ['ю'] = "yu",
        ['я'] = "ya"
    };

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"[^\p{L}\p{Nd}\s]+", RegexOptions.Compiled)]
    private static partial Regex NoiseCharsRegex();

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.Trim().ToLowerInvariant().Replace('ё', 'е');
        var noNoise = NoiseCharsRegex().Replace(lowered, " ");
        return MultiSpaceRegex().Replace(noNoise, " ").Trim();
    }

    public static string NormalizeForFactValue(string? value)
    {
        return Normalize(value);
    }

    public static IReadOnlyCollection<string> BuildLookupKeys(string? value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return set;
        }

        set.Add(normalized);
        var transliterated = TransliterateCyrillicToLatin(normalized);
        if (transliterated.Length > 0 && !string.Equals(transliterated, normalized, StringComparison.Ordinal))
        {
            set.Add(transliterated);
        }

        return set;
    }

    public static IReadOnlyCollection<string> BuildEntityAliasNormals(
        string entityName,
        IReadOnlyCollection<string>? explicitAliases,
        bool includeShortFirstNameAlias)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        AddLookupKeys(result, entityName);

        if (explicitAliases is not null)
        {
            foreach (var alias in explicitAliases)
            {
                AddLookupKeys(result, alias);
            }
        }

        if (includeShortFirstNameAlias)
        {
            var normalizedName = Normalize(entityName);
            var parts = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length >= 3)
            {
                result.Add(parts[0]);
            }
        }

        return result;
    }

    private static void AddLookupKeys(HashSet<string> result, string? value)
    {
        foreach (var key in BuildLookupKeys(value))
        {
            result.Add(key);
        }
    }

    private static string TransliterateCyrillicToLatin(string normalizedValue)
    {
        var builder = new StringBuilder(normalizedValue.Length);
        var changed = false;
        foreach (var ch in normalizedValue)
        {
            if (CyrillicToLatin.TryGetValue(ch, out var replacement))
            {
                builder.Append(replacement);
                changed = true;
                continue;
            }

            builder.Append(ch);
        }

        if (!changed)
        {
            return string.Empty;
        }

        return MultiSpaceRegex().Replace(builder.ToString(), " ").Trim();
    }
}

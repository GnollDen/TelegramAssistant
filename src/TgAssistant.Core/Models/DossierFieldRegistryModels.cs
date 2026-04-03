namespace TgAssistant.Core.Models;

public static class DossierFieldApprovalStates
{
    public const string Approved = "approved";
    public const string ProposalOnly = "proposal_only";
}

public sealed class DossierFieldAliasDefinition
{
    public string Category { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
}

public sealed class DossierFieldRegistryEntry
{
    public string FamilyKey { get; init; } = string.Empty;
    public string CanonicalCategory { get; init; } = string.Empty;
    public string CanonicalKey { get; init; } = string.Empty;
    public string ApprovalState { get; init; } = DossierFieldApprovalStates.Approved;
    public bool IsSeeded { get; init; } = true;
    public IReadOnlyList<DossierFieldAliasDefinition> Aliases { get; init; } = [];
}

public sealed class DossierFieldRegistryResolution
{
    public string ObservedCategory { get; init; } = string.Empty;
    public string ObservedKey { get; init; } = string.Empty;
    public string FamilyKey { get; init; } = string.Empty;
    public string CanonicalCategory { get; init; } = string.Empty;
    public string CanonicalKey { get; init; } = string.Empty;
    public string ApprovalState { get; init; } = DossierFieldApprovalStates.Approved;
    public bool IsSeeded { get; init; }
}

public sealed class DossierFieldRegistrySnapshot
{
    public List<DossierFieldRegistryResolution> FieldMappings { get; init; } = [];
    public int ApprovedFamilyCount { get; init; }
    public int ProposalFamilyCount { get; init; }
    public int AliasCount { get; init; }
}

public static class DossierFieldRegistryCatalog
{
    private static readonly IReadOnlyList<DossierFieldRegistryEntry> SeededEntriesValue =
    [
        Create(
            "identity",
            "tracked_person_name",
            ("identity", "person_name"),
            ("identity", "full_name"),
            ("identity", "subject_name")),
        Create(
            "graph_context",
            "operator_attachment",
            ("graph_context", "operator_root"),
            ("graph_context", "operator_anchor"),
            ("identity", "operator_name")),
        Create(
            "bootstrap_discovery",
            "linked_person_count",
            ("bootstrap_discovery", "linked_people_count"),
            ("bootstrap_discovery", "network_size")),
        Create(
            "bootstrap_discovery",
            "candidate_identity_count",
            ("bootstrap_discovery", "unresolved_identity_count"),
            ("bootstrap_discovery", "candidate_count")),
        Create(
            "bootstrap_discovery",
            "mention_count",
            ("bootstrap_discovery", "discovered_mentions_count"),
            ("bootstrap_discovery", "mention_total"))
    ];

    private static readonly IReadOnlyDictionary<string, DossierFieldRegistryEntry> AliasLookup =
        SeededEntriesValue
            .SelectMany(entry => EnumerateAliasTokens(entry).Select(aliasToken => new KeyValuePair<string, DossierFieldRegistryEntry>(aliasToken, entry)))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last().Value, StringComparer.Ordinal);

    public static IReadOnlyList<DossierFieldRegistryEntry> SeededEntries => SeededEntriesValue;

    public static DossierFieldRegistryResolution Resolve(string category, string key)
    {
        var normalizedCategory = NormalizeToken(category);
        var normalizedKey = NormalizeToken(key);
        var aliasToken = ComposeAliasToken(normalizedCategory, normalizedKey);

        if (AliasLookup.TryGetValue(aliasToken, out var seededEntry))
        {
            return new DossierFieldRegistryResolution
            {
                ObservedCategory = normalizedCategory,
                ObservedKey = normalizedKey,
                FamilyKey = seededEntry.FamilyKey,
                CanonicalCategory = seededEntry.CanonicalCategory,
                CanonicalKey = seededEntry.CanonicalKey,
                ApprovalState = seededEntry.ApprovalState,
                IsSeeded = seededEntry.IsSeeded
            };
        }

        return new DossierFieldRegistryResolution
        {
            ObservedCategory = normalizedCategory,
            ObservedKey = normalizedKey,
            FamilyKey = aliasToken,
            CanonicalCategory = normalizedCategory,
            CanonicalKey = normalizedKey,
            ApprovalState = DossierFieldApprovalStates.ProposalOnly,
            IsSeeded = false
        };
    }

    public static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var buffer = new System.Text.StringBuilder(value.Length);
        var previousUnderscore = false;

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer.Append(char.ToLowerInvariant(character));
                previousUnderscore = false;
                continue;
            }

            if (previousUnderscore)
            {
                continue;
            }

            buffer.Append('_');
            previousUnderscore = true;
        }

        return buffer.ToString().Trim('_');
    }

    public static string ComposeAliasToken(string category, string key)
        => $"{NormalizeToken(category)}.{NormalizeToken(key)}";

    private static IEnumerable<string> EnumerateAliasTokens(DossierFieldRegistryEntry entry)
    {
        yield return ComposeAliasToken(entry.CanonicalCategory, entry.CanonicalKey);

        foreach (var alias in entry.Aliases)
        {
            yield return ComposeAliasToken(alias.Category, alias.Key);
        }
    }

    private static DossierFieldRegistryEntry Create(
        string canonicalCategory,
        string canonicalKey,
        params (string Category, string Key)[] aliases)
    {
        canonicalCategory = NormalizeToken(canonicalCategory);
        canonicalKey = NormalizeToken(canonicalKey);

        return new DossierFieldRegistryEntry
        {
            FamilyKey = ComposeAliasToken(canonicalCategory, canonicalKey),
            CanonicalCategory = canonicalCategory,
            CanonicalKey = canonicalKey,
            ApprovalState = DossierFieldApprovalStates.Approved,
            IsSeeded = true,
            Aliases = aliases
                .Select(alias => new DossierFieldAliasDefinition
                {
                    Category = NormalizeToken(alias.Category),
                    Key = NormalizeToken(alias.Key)
                })
                .ToArray()
        };
    }
}

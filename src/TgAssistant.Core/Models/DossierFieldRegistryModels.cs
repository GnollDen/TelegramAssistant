namespace TgAssistant.Core.Models;

public static class DossierFieldConditionalFamilies
{
    public const string ProfilePreference = "profile_preference";
    public const string BehaviorPattern = "behavior_pattern";
    public const string StyleDrift = "style_drift";
    public const string PhaseMarker = "phase_marker";

    public static readonly string[] Ordered =
    [
        ProfilePreference,
        BehaviorPattern,
        StyleDrift,
        PhaseMarker
    ];

    private static readonly HashSet<string> Supported = new(Ordered, StringComparer.Ordinal);

    public static bool IsSupported(string? value)
        => !string.IsNullOrWhiteSpace(value) && Supported.Contains(value);
}

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

public sealed class DossierFieldObservedInput
{
    public string ObservedCategory { get; init; } = string.Empty;
    public string ObservedKey { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
}

public sealed class DossierFieldDurableWriteCandidate
{
    public string ObservedCategory { get; set; } = string.Empty;
    public string ObservedKey { get; set; } = string.Empty;
    public string FamilyKey { get; set; } = string.Empty;
    public string CanonicalCategory { get; set; } = string.Empty;
    public string CanonicalKey { get; set; } = string.Empty;
    public string ApprovalState { get; set; } = DossierFieldApprovalStates.ProposalOnly;
    public string PrimaryValue { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public List<string> EvidenceRefs { get; } = [];
    public List<DossierFieldObservedInput> ObservedInputs { get; } = [];
}

public sealed class DossierFieldDurableWritePlan
{
    public List<DossierFieldDurableWriteCandidate> ApprovedFields { get; init; } = [];
    public List<DossierFieldDurableWriteCandidate> ProposalOnlyFields { get; init; } = [];
    public int CollapsedApprovedDuplicateCount { get; init; }
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
            ("bootstrap_discovery", "mention_total")),
        Create(
            "preferences",
            "favorite_food",
            ("preferences", "food_preference"),
            ("preferences", "gastronomic_preferences"))
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

    public static DossierFieldDurableWritePlan BuildDurableWritePlan(
        IEnumerable<NormalizedFactCandidate> facts,
        DossierFieldRegistrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(snapshot);

        var mappingLookup = snapshot.FieldMappings
            .Where(x => !string.IsNullOrWhiteSpace(x.ObservedCategory) && !string.IsNullOrWhiteSpace(x.ObservedKey))
            .GroupBy(x => ComposeAliasToken(x.ObservedCategory, x.ObservedKey), StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var approvedFields = new Dictionary<string, DossierFieldDurableWriteCandidate>(StringComparer.Ordinal);
        var proposalFields = new List<DossierFieldDurableWriteCandidate>();
        var collapsedApprovedDuplicateCount = 0;

        foreach (var fact in facts)
        {
            var observedCategory = NormalizeToken(fact.Category);
            var observedKey = NormalizeToken(fact.Key);
            if (observedCategory.Length == 0 || observedKey.Length == 0)
            {
                continue;
            }

            var observedToken = ComposeAliasToken(observedCategory, observedKey);
            var resolution = mappingLookup.GetValueOrDefault(observedToken)
                ?? Resolve(observedCategory, observedKey);
            var candidate = CreateDurableWriteCandidate(fact, observedCategory, observedKey, resolution);

            if (!string.Equals(candidate.ApprovalState, DossierFieldApprovalStates.Approved, StringComparison.Ordinal))
            {
                proposalFields.Add(candidate);
                continue;
            }

            if (approvedFields.TryGetValue(candidate.FamilyKey, out var existing))
            {
                MergeApprovedCandidate(existing, candidate);
                collapsedApprovedDuplicateCount++;
                continue;
            }

            approvedFields[candidate.FamilyKey] = candidate;
        }

        return new DossierFieldDurableWritePlan
        {
            ApprovedFields = approvedFields.Values
                .OrderBy(x => x.CanonicalCategory, StringComparer.Ordinal)
                .ThenBy(x => x.CanonicalKey, StringComparer.Ordinal)
                .ToList(),
            ProposalOnlyFields = proposalFields
                .OrderBy(x => x.ObservedCategory, StringComparer.Ordinal)
                .ThenBy(x => x.ObservedKey, StringComparer.Ordinal)
                .ToList(),
            CollapsedApprovedDuplicateCount = collapsedApprovedDuplicateCount
        };
    }

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

    private static DossierFieldDurableWriteCandidate CreateDurableWriteCandidate(
        NormalizedFactCandidate fact,
        string observedCategory,
        string observedKey,
        DossierFieldRegistryResolution resolution)
    {
        var candidate = new DossierFieldDurableWriteCandidate
        {
            ObservedCategory = observedCategory,
            ObservedKey = observedKey,
            FamilyKey = resolution.FamilyKey,
            CanonicalCategory = resolution.CanonicalCategory,
            CanonicalKey = resolution.CanonicalKey,
            ApprovalState = resolution.ApprovalState,
            PrimaryValue = fact.Value,
            Confidence = fact.Confidence
        };
        candidate.EvidenceRefs.AddRange(fact.EvidenceRefs.Distinct(StringComparer.Ordinal));
        candidate.ObservedInputs.Add(new DossierFieldObservedInput
        {
            ObservedCategory = observedCategory,
            ObservedKey = observedKey,
            Value = fact.Value,
            Confidence = fact.Confidence,
            EvidenceRefs = fact.EvidenceRefs.Distinct(StringComparer.Ordinal).ToArray()
        });
        return candidate;
    }

    private static void MergeApprovedCandidate(
        DossierFieldDurableWriteCandidate existing,
        DossierFieldDurableWriteCandidate candidate)
    {
        if (candidate.Confidence > existing.Confidence)
        {
            existing.PrimaryValue = candidate.PrimaryValue;
            existing.Confidence = candidate.Confidence;
            existing.ObservedCategory = candidate.ObservedCategory;
            existing.ObservedKey = candidate.ObservedKey;
        }

        foreach (var evidenceRef in candidate.EvidenceRefs)
        {
            if (!existing.EvidenceRefs.Contains(evidenceRef, StringComparer.Ordinal))
            {
                existing.EvidenceRefs.Add(evidenceRef);
            }
        }

        foreach (var observedInput in candidate.ObservedInputs)
        {
            if (existing.ObservedInputs.Any(x =>
                    string.Equals(x.ObservedCategory, observedInput.ObservedCategory, StringComparison.Ordinal)
                    && string.Equals(x.ObservedKey, observedInput.ObservedKey, StringComparison.Ordinal)
                    && string.Equals(x.Value, observedInput.Value, StringComparison.Ordinal)))
            {
                continue;
            }

            existing.ObservedInputs.Add(observedInput);
        }
    }
}

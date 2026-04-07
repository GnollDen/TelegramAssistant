using System.Text.Json.Serialization;

namespace TgAssistant.Core.Models;

public static class OperatorAssistantContractVersions
{
    public const string OpintAssistantV1 = "opint_assistant_v1";
}

public static class OperatorAssistantSurfaces
{
    public const string TelegramAssistant = "telegram_assistant";
}

public static class OperatorAssistantTruthLabels
{
    public const string Fact = "Fact";
    public const string Inference = "Inference";
    public const string Hypothesis = "Hypothesis";
    public const string Recommendation = "Recommendation";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Fact,
        Inference,
        Hypothesis,
        Recommendation
    };

    public static bool IsSupported(string? value)
        => OperatorTruthTrustFormatter.TryNormalizeLabel(value, out _);

    public static bool IsShortAnswerSupported(string? value)
        => string.Equals(OperatorTruthTrustFormatter.NormalizeLabel(value), Fact, StringComparison.Ordinal)
            || string.Equals(OperatorTruthTrustFormatter.NormalizeLabel(value), Inference, StringComparison.Ordinal)
            || string.Equals(OperatorTruthTrustFormatter.NormalizeLabel(value), Hypothesis, StringComparison.Ordinal);

    public static bool IsWhatItMeansSupported(string? value)
        => string.Equals(OperatorTruthTrustFormatter.NormalizeLabel(value), Inference, StringComparison.Ordinal)
            || string.Equals(OperatorTruthTrustFormatter.NormalizeLabel(value), Hypothesis, StringComparison.Ordinal);
}

public static class OperatorTruthTrustFormatter
{
    private static readonly Dictionary<string, string> CanonicalLabelByNormalized = new(StringComparer.Ordinal)
    {
        ["fact"] = OperatorAssistantTruthLabels.Fact,
        ["inference"] = OperatorAssistantTruthLabels.Inference,
        ["hypothesis"] = OperatorAssistantTruthLabels.Hypothesis,
        ["recommendation"] = OperatorAssistantTruthLabels.Recommendation
    };

    public static bool TryNormalizeLabel(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = value.Trim().ToLowerInvariant();
        if (!CanonicalLabelByNormalized.TryGetValue(key, out var canonical))
        {
            return false;
        }

        normalized = canonical;
        return true;
    }

    public static string NormalizeLabel(string? value)
        => TryNormalizeLabel(value, out var normalized) ? normalized : string.Empty;

    public static string NormalizeShortAnswerLabel(string? value)
    {
        var normalized = NormalizeLabel(value);
        return string.Equals(normalized, OperatorAssistantTruthLabels.Fact, StringComparison.Ordinal)
            || string.Equals(normalized, OperatorAssistantTruthLabels.Inference, StringComparison.Ordinal)
            || string.Equals(normalized, OperatorAssistantTruthLabels.Hypothesis, StringComparison.Ordinal)
            ? normalized
            : OperatorAssistantTruthLabels.Inference;
    }

    public static string NormalizeWhatItMeansLabel(string? value)
    {
        var normalized = NormalizeLabel(value);
        return string.Equals(normalized, OperatorAssistantTruthLabels.Inference, StringComparison.Ordinal)
            || string.Equals(normalized, OperatorAssistantTruthLabels.Hypothesis, StringComparison.Ordinal)
            ? normalized
            : OperatorAssistantTruthLabels.Inference;
    }

    public static string NormalizeRecommendationLabel(string? value)
        => string.Equals(NormalizeLabel(value), OperatorAssistantTruthLabels.Recommendation, StringComparison.Ordinal)
            ? OperatorAssistantTruthLabels.Recommendation
            : OperatorAssistantTruthLabels.Recommendation;

    public static int ToTrustPercent(float trustFactor)
    {
        var bounded = Math.Clamp(trustFactor, 0f, 1f);
        return Math.Clamp((int)Math.Round(bounded * 100f, MidpointRounding.AwayFromZero), 0, 100);
    }

    public static int ToTrustPercent(float? trustFactor)
        => ToTrustPercent(trustFactor ?? 0f);

    public static int ClampTrustPercent(int value)
        => Math.Clamp(value, 0, 100);

    public static bool IsTrustPercent(int value)
        => value is >= 0 and <= 100;

    public static string FormatTrustPercent(int trustPercent)
        => $"{ClampTrustPercent(trustPercent)}%";

    public static string FormatTaggedLine(string truthLabel, int trustPercent, string text)
    {
        var label = TryNormalizeLabel(truthLabel, out var normalized)
            ? normalized
            : OperatorAssistantTruthLabels.Inference;
        return $"[{label} | {FormatTrustPercent(trustPercent)}] {text}";
    }
}

public static class OperatorAssistantFailureReasons
{
    public const string TrackedPersonScopeMismatch = "tracked_person_scope_mismatch";
    public const string SessionScopeItemMismatch = "session_scope_item_mismatch";
    public const string MissingActiveTrackedPerson = "missing_active_tracked_person";
    public const string ReadModelScopeUnbounded = "read_model_scope_unbounded";
    public const string ReadModelScopeMismatch = "read_model_scope_mismatch";
    public const string ReadModelScopeItemNotFound = "read_model_scope_item_not_found";
}

public class OperatorAssistantStatementInput
{
    public string Text { get; set; } = string.Empty;
    public string TruthLabel { get; set; } = string.Empty;
    public int? TrustPercent { get; set; }
    public List<string> EvidenceRefs { get; set; } = [];
}

public class OperatorAssistantResponseGenerationRequest : OperatorContractRequestBase
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public OperatorAssistantStatementInput? ShortAnswer { get; set; }
    public List<OperatorAssistantStatementInput> WhatIsKnown { get; set; } = [];
    public List<OperatorAssistantStatementInput> WhatItMeans { get; set; } = [];
    public OperatorAssistantStatementInput? Recommendation { get; set; }
    public int? TrustPercent { get; set; }
    public bool OpenInWebEnabled { get; set; } = true;
    public string OpenInWebTargetApi { get; set; } = "/api/operator/resolution/detail/query";
    public string OpenInWebScopeItemKey { get; set; } = string.Empty;
    public string OpenInWebActiveMode { get; set; } = OperatorModeTypes.ResolutionDetail;
    public string? OpenInWebHandoffToken { get; set; }
}

public class OperatorAssistantContextAssemblyRequest : OperatorContractRequestBase
{
    public Guid TrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public string? ScopeItemKey { get; set; }
    public int QueueLimit { get; set; } = 10;
    public int EvidenceLimit { get; set; } = 3;
    public bool OpenInWebEnabled { get; set; } = true;
    public string OpenInWebTargetApi { get; set; } = "/api/operator/resolution/detail/query";
    public string OpenInWebActiveMode { get; set; } = OperatorModeTypes.ResolutionDetail;
}

public class OperatorAssistantStatement
{
    [JsonPropertyName("truth_label")]
    public string TruthLabel { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("trust_percent")]
    public int TrustPercent { get; set; }

    [JsonPropertyName("evidence_refs")]
    public List<string> EvidenceRefs { get; set; } = [];
}

public class OperatorAssistantResponseSections
{
    [JsonPropertyName("short_answer")]
    public OperatorAssistantStatement ShortAnswer { get; set; } = new();

    [JsonPropertyName("what_is_known")]
    public List<OperatorAssistantStatement> WhatIsKnown { get; set; } = [];

    [JsonPropertyName("what_it_means")]
    public List<OperatorAssistantStatement> WhatItMeans { get; set; } = [];

    [JsonPropertyName("recommendation")]
    public OperatorAssistantStatement Recommendation { get; set; } = new();
}

public class OperatorAssistantOpenInWebContract
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("target_api")]
    public string TargetApi { get; set; } = "/api/operator/resolution/detail/query";

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("scope_item_key")]
    public string ScopeItemKey { get; set; } = string.Empty;

    [JsonPropertyName("active_mode")]
    public string ActiveMode { get; set; } = OperatorModeTypes.ResolutionDetail;

    [JsonPropertyName("handoff_token")]
    public string HandoffToken { get; set; } = string.Empty;
}

public class OperatorAssistantGuardrailContract
{
    [JsonPropertyName("scope_bounded")]
    public bool ScopeBounded { get; set; } = true;

    [JsonPropertyName("mcp_dependent")]
    public bool McpDependent { get; set; }

    [JsonPropertyName("read_model_bounded")]
    public bool ReadModelBounded { get; set; } = true;

    [JsonPropertyName("read_model_audit")]
    public List<OperatorAssistantReadModelAuditEntry> ReadModelAudit { get; set; } = [];
}

public class OperatorAssistantReadModelAuditEntry
{
    [JsonPropertyName("read_model")]
    public string ReadModel { get; set; } = string.Empty;

    [JsonPropertyName("bounded")]
    public bool Bounded { get; set; }

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("scope_item_key")]
    public string ScopeItemKey { get; set; } = string.Empty;

    [JsonPropertyName("record_count")]
    public int RecordCount { get; set; }

    [JsonPropertyName("operator_session_id")]
    public string OperatorSessionId { get; set; } = string.Empty;

    [JsonPropertyName("observed_at_utc")]
    public DateTime ObservedAtUtc { get; set; }

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

public class OperatorAssistantResponseEnvelope
{
    [JsonPropertyName("contract_version")]
    public string ContractVersion { get; set; } = OperatorAssistantContractVersions.OpintAssistantV1;

    [JsonPropertyName("surface")]
    public string Surface { get; set; } = OperatorAssistantSurfaces.TelegramAssistant;

    [JsonPropertyName("tracked_person_id")]
    public Guid TrackedPersonId { get; set; }

    [JsonPropertyName("scope_key")]
    public string ScopeKey { get; set; } = string.Empty;

    [JsonPropertyName("operator_session_id")]
    public string OperatorSessionId { get; set; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; set; } = string.Empty;

    [JsonPropertyName("generated_at_utc")]
    public DateTime GeneratedAtUtc { get; set; }

    [JsonPropertyName("sections")]
    public OperatorAssistantResponseSections Sections { get; set; } = new();

    [JsonPropertyName("trust_percent")]
    public int TrustPercent { get; set; }

    [JsonPropertyName("open_in_web")]
    public OperatorAssistantOpenInWebContract OpenInWeb { get; set; } = new();

    [JsonPropertyName("guardrails")]
    public OperatorAssistantGuardrailContract Guardrails { get; set; } = new();
}

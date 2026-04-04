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
        => !string.IsNullOrWhiteSpace(value) && Supported.Contains(value.Trim());

    public static bool IsShortAnswerSupported(string? value)
        => string.Equals(value, Fact, StringComparison.Ordinal)
            || string.Equals(value, Inference, StringComparison.Ordinal)
            || string.Equals(value, Hypothesis, StringComparison.Ordinal);

    public static bool IsWhatItMeansSupported(string? value)
        => string.Equals(value, Inference, StringComparison.Ordinal)
            || string.Equals(value, Hypothesis, StringComparison.Ordinal);
}

public static class OperatorAssistantFailureReasons
{
    public const string TrackedPersonScopeMismatch = "tracked_person_scope_mismatch";
    public const string SessionScopeItemMismatch = "session_scope_item_mismatch";
    public const string MissingActiveTrackedPerson = "missing_active_tracked_person";
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

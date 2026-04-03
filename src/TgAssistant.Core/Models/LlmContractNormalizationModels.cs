using TgAssistant.Core.Configuration;

namespace TgAssistant.Core.Models;

public enum LlmContractKind
{
    Unspecified = 0,
    EditDiffV1 = 1
}

public enum LlmContractNormalizationStatus
{
    Success = 0,
    SchemaInvalid = 1,
    NormalizationFailed = 2,
    ProviderError = 3
}

public sealed class LlmContractSchemaDescriptor
{
    public LlmContractKind Kind { get; set; }
    public string SchemaRef { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = "{}";
    public string? SystemInstruction { get; set; }
}

public sealed class LlmContractValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public sealed class LlmContractNormalizationRequest
{
    public LlmContractKind ContractKind { get; set; }
    public string RawReasoningPayload { get; set; } = string.Empty;
    public string TaskKey { get; set; } = "contract_normalization";
    public LlmTraceContext Trace { get; set; } = new();
    public LlmExecutionLimits Limits { get; set; } = new();
}

public sealed class LlmContractNormalizationProviderMetadata
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int LatencyMs { get; set; }
    public bool FallbackAttempt { get; set; }
}

public sealed class LlmContractNormalizationAttempt
{
    public int AttemptNumber { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int? LatencyMs { get; set; }
    public bool FallbackAttempt { get; set; }
    public bool Success { get; set; }
    public string? FailureReason { get; set; }
    public LlmGatewayErrorCategory? GatewayErrorCategory { get; set; }
}

public sealed class LlmContractNormalizationDiagnostics
{
    public string? FailureReason { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public List<LlmContractNormalizationAttempt> Attempts { get; set; } = new();
}

public sealed class LlmContractNormalizationResult
{
    public LlmContractNormalizationStatus Status { get; set; }
    public LlmContractKind ContractKind { get; set; }
    public string SchemaRef { get; set; } = string.Empty;
    public string? NormalizedPayloadJson { get; set; }
    public LlmContractNormalizationProviderMetadata? ProviderMetadata { get; set; }
    public LlmContractNormalizationDiagnostics Diagnostics { get; set; } = new();
}

public sealed class LlmGatewayRouteOverride
{
    public string PrimaryProvider { get; set; } = string.Empty;
    public List<LlmGatewayProviderTargetSettings> FallbackProviders { get; set; } = new();
    public Dictionary<string, string> ProviderModelHints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LlmStructuredOutputSchema
{
    public string Name { get; set; } = string.Empty;
    public string SchemaJson { get; set; } = "{}";
    public bool Strict { get; set; } = true;
}

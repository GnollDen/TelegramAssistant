using System.Net;

namespace TgAssistant.Core.Models;

public enum LlmModality
{
    Unspecified = 0,
    TextChat = 1,
    Tools = 2,
    Embeddings = 3,
    Vision = 4,
    AudioTranscription = 5,
    AudioParalinguistics = 6
}

public enum LlmResponseMode
{
    Unspecified = 0,
    Text = 1,
    JsonObject = 2,
    ToolCalls = 3,
    EmbeddingVector = 4,
    StructuredAudio = 5
}

public enum LlmMessageRole
{
    System = 0,
    User = 1,
    Assistant = 2,
    Tool = 3
}

public enum LlmContentPartType
{
    Text = 0,
    ImageUri = 1,
    AudioUri = 2,
    InlineData = 3
}

public enum LlmGatewayErrorCategory
{
    Unknown = 0,
    Auth = 1,
    Quota = 2,
    RateLimit = 3,
    Timeout = 4,
    TransientUpstream = 5,
    SchemaMismatch = 6,
    UnsupportedModality = 7,
    Validation = 8
}

public class LlmGatewayRequest
{
    public LlmModality Modality { get; set; }
    public string TaskKey { get; set; } = string.Empty;
    public string? ModelHint { get; set; }
    public List<LlmGatewayMessage> Messages { get; set; } = new();
    public List<string> EmbeddingInputs { get; set; } = new();
    public LlmExecutionLimits Limits { get; set; } = new();
    public LlmTraceContext Trace { get; set; } = new();
    public List<LlmToolDefinition> ToolDefinitions { get; set; } = new();
    public LlmResponseMode ResponseMode { get; set; }

    public void Validate()
    {
        if (Modality == LlmModality.Unspecified)
        {
            throw CreateValidationException("Gateway request must specify a modality.");
        }

        if (string.IsNullOrWhiteSpace(TaskKey))
        {
            throw CreateValidationException("Gateway request must specify a task key.");
        }

        if (ResponseMode == LlmResponseMode.Unspecified)
        {
            throw CreateValidationException("Gateway request must specify a response mode.");
        }

        switch (Modality)
        {
            case LlmModality.Embeddings:
                if (EmbeddingInputs.Count == 0 || EmbeddingInputs.All(string.IsNullOrWhiteSpace))
                {
                    throw CreateValidationException("Embedding requests must include at least one non-empty input.");
                }

                if (ResponseMode != LlmResponseMode.EmbeddingVector)
                {
                    throw CreateValidationException("Embedding requests must use embedding_vector response mode.");
                }
                break;
            case LlmModality.Tools:
                if (ToolDefinitions.Count == 0)
                {
                    throw CreateValidationException("Tool requests must include at least one tool definition.");
                }

                goto default;
            default:
                if (Messages.Count == 0)
                {
                    throw CreateValidationException("Chat-style requests must include at least one message.");
                }

                if (Messages.All(message => message.ContentParts.Count == 0 && message.ToolCalls.Count == 0))
                {
                    throw CreateValidationException("Chat-style requests must include message content or tool calls.");
                }
                break;
        }
    }

    private LlmGatewayException CreateValidationException(string message)
    {
        return new LlmGatewayException(message)
        {
            Category = LlmGatewayErrorCategory.Validation,
            Modality = Modality,
            Retryable = false
        };
    }
}

public class LlmGatewayResponse
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int LatencyMs { get; set; }
    public LlmGatewayOutput Output { get; set; } = new();
    public LlmUsageInfo Usage { get; set; } = new();
    public bool FallbackApplied { get; set; }
    public string? FallbackFromProvider { get; set; }
    public string? RawProviderPayloadJson { get; set; }
}

public class LlmGatewayOutput
{
    public string? Text { get; set; }
    public List<LlmToolCall> ToolCalls { get; set; } = new();
    public List<float[]> Embeddings { get; set; } = new();
    public string? StructuredPayloadJson { get; set; }
}

public class LlmGatewayMessage
{
    public LlmMessageRole Role { get; set; }
    public string? Name { get; set; }
    public string? ToolCallId { get; set; }
    public List<LlmMessageContentPart> ContentParts { get; set; } = new();
    public List<LlmToolCall> ToolCalls { get; set; } = new();

    public static LlmGatewayMessage FromText(LlmMessageRole role, string text)
    {
        return new LlmGatewayMessage
        {
            Role = role,
            ContentParts = new List<LlmMessageContentPart>
            {
                LlmMessageContentPart.FromText(text)
            }
        };
    }
}

public class LlmMessageContentPart
{
    public LlmContentPartType Type { get; set; }
    public string? Text { get; set; }
    public string? MimeType { get; set; }
    public string? MediaUri { get; set; }
    public string? InlineDataBase64 { get; set; }

    public static LlmMessageContentPart FromText(string text)
    {
        return new LlmMessageContentPart
        {
            Type = LlmContentPartType.Text,
            Text = text
        };
    }
}

public class LlmToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ParametersJsonSchema { get; set; } = "{}";
}

public class LlmToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

public class LlmExecutionLimits
{
    public int? MaxTokens { get; set; }
    public float? Temperature { get; set; }
    public int? TimeoutMs { get; set; }
}

public class LlmTraceContext
{
    public string? PathKey { get; set; }
    public List<string> ScopeTags { get; set; } = new();
    public string? RequestId { get; set; }
    public bool IsImportScope { get; set; }
    public bool IsOptionalPath { get; set; }
}

public class LlmUsageInfo
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? CostUsd { get; set; }
}

public class LlmProviderRequest
{
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public LlmGatewayRequest Request { get; set; } = new();
    public bool IsFallbackAttempt { get; set; }
    public string? FallbackFromProvider { get; set; }
}

public class LlmProviderResult
{
    public string ProviderId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int LatencyMs { get; set; }
    public LlmGatewayOutput Output { get; set; } = new();
    public LlmUsageInfo Usage { get; set; } = new();
    public HttpStatusCode? StatusCode { get; set; }
    public string? RawProviderPayloadJson { get; set; }
}

public class LlmRoutingDecision
{
    public string PrimaryProvider { get; set; } = string.Empty;
    public List<string> FallbackProviders { get; set; } = new();
    public Dictionary<string, string> ProviderModelHints { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string RetryPolicyClass { get; set; } = "default";
    public string TimeoutBudgetClass { get; set; } = "default";
}

public class LlmGatewayException : Exception
{
    public LlmGatewayException(string message)
        : base(message)
    {
    }

    public LlmGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public LlmGatewayErrorCategory Category { get; init; } = LlmGatewayErrorCategory.Unknown;
    public string? Provider { get; init; }
    public LlmModality Modality { get; init; }
    public HttpStatusCode? HttpStatus { get; init; }
    public bool Retryable { get; init; }
    public string? RawReasonCode { get; init; }
}

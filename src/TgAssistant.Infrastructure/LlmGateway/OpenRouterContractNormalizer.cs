using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public class OpenRouterContractNormalizer : ILlmContractNormalizer
{
    private static readonly string[] ShapingModels =
    [
        "openai/gpt-4.1-nano",
        "openai/gpt-4.1-mini"
    ];

    private readonly ILlmGateway _gateway;
    private readonly ILlmContractSchemaProvider _schemaProvider;
    private readonly ILlmContractValidator _validator;
    private readonly ILogger<OpenRouterContractNormalizer> _logger;

    public OpenRouterContractNormalizer(
        ILlmGateway gateway,
        ILlmContractSchemaProvider schemaProvider,
        ILlmContractValidator validator,
        ILogger<OpenRouterContractNormalizer> logger)
    {
        _gateway = gateway;
        _schemaProvider = schemaProvider;
        _validator = validator;
        _logger = logger;
    }

    public async Task<LlmContractNormalizationResult> NormalizeAsync(LlmContractNormalizationRequest request, CancellationToken ct = default)
    {
        var diagnostics = new LlmContractNormalizationDiagnostics();
        if (request.ContractKind == LlmContractKind.Unspecified)
        {
            diagnostics.FailureReason = "contract_kind_unspecified";
            return BuildResult(LlmContractNormalizationStatus.NormalizationFailed, request.ContractKind, string.Empty, diagnostics);
        }

        if (string.IsNullOrWhiteSpace(request.RawReasoningPayload))
        {
            diagnostics.FailureReason = "raw_reasoning_payload_empty";
            return BuildResult(LlmContractNormalizationStatus.NormalizationFailed, request.ContractKind, string.Empty, diagnostics);
        }

        var schema = _schemaProvider.GetSchema(request.ContractKind);
        if (schema is null)
        {
            diagnostics.FailureReason = "schema_not_registered";
            return BuildResult(LlmContractNormalizationStatus.NormalizationFailed, request.ContractKind, string.Empty, diagnostics);
        }

        LlmContractNormalizationResult? latestProviderError = null;
        LlmContractNormalizationResult? latestSchemaError = null;
        LlmContractNormalizationResult? latestNormalizationError = null;

        for (var attempt = 0; attempt < ShapingModels.Length; attempt++)
        {
            var model = ShapingModels[attempt];
            var isFallbackAttempt = attempt > 0;
            try
            {
                var gatewayResponse = await _gateway.ExecuteAsync(BuildShapingRequest(request, schema, model), ct);
                var normalizedPayload = gatewayResponse.Output.StructuredPayloadJson ?? gatewayResponse.Output.Text;
                if (string.IsNullOrWhiteSpace(normalizedPayload) || !LooksLikeJsonObject(normalizedPayload))
                {
                    diagnostics.Attempts.Add(new LlmContractNormalizationAttempt
                    {
                        AttemptNumber = attempt + 1,
                        Provider = gatewayResponse.Provider,
                        Model = gatewayResponse.Model,
                        LatencyMs = gatewayResponse.LatencyMs,
                        FallbackAttempt = isFallbackAttempt,
                        Success = false,
                        FailureReason = "normalization_failed:empty_or_non_json"
                    });
                    latestNormalizationError = BuildFailureResult(
                        LlmContractNormalizationStatus.NormalizationFailed,
                        request.ContractKind,
                        schema.SchemaRef,
                        diagnostics,
                        "normalization_failed");
                    continue;
                }

                var validation = _validator.Validate(request.ContractKind, normalizedPayload);
                if (!validation.IsValid)
                {
                    diagnostics.Attempts.Add(new LlmContractNormalizationAttempt
                    {
                        AttemptNumber = attempt + 1,
                        Provider = gatewayResponse.Provider,
                        Model = gatewayResponse.Model,
                        LatencyMs = gatewayResponse.LatencyMs,
                        FallbackAttempt = isFallbackAttempt,
                        Success = false,
                        FailureReason = "schema_invalid"
                    });
                    diagnostics.ValidationErrors = validation.Errors;
                    latestSchemaError = BuildFailureResult(
                        LlmContractNormalizationStatus.SchemaInvalid,
                        request.ContractKind,
                        schema.SchemaRef,
                        diagnostics,
                        "schema_invalid");
                    continue;
                }

                diagnostics.Attempts.Add(new LlmContractNormalizationAttempt
                {
                    AttemptNumber = attempt + 1,
                    Provider = gatewayResponse.Provider,
                    Model = gatewayResponse.Model,
                    LatencyMs = gatewayResponse.LatencyMs,
                    FallbackAttempt = isFallbackAttempt,
                    Success = true
                });

                _logger.LogInformation(
                    "Contract normalization succeeded. contract_kind={ContractKind}, schema_ref={SchemaRef}, shaping_provider={Provider}, shaping_model={Model}, latency_ms={LatencyMs}, fallback_attempt={FallbackAttempt}, attempts={Attempts}",
                    request.ContractKind,
                    schema.SchemaRef,
                    gatewayResponse.Provider,
                    gatewayResponse.Model,
                    gatewayResponse.LatencyMs,
                    isFallbackAttempt,
                    diagnostics.Attempts.Count);

                return new LlmContractNormalizationResult
                {
                    Status = LlmContractNormalizationStatus.Success,
                    ContractKind = request.ContractKind,
                    SchemaRef = schema.SchemaRef,
                    NormalizedPayloadJson = normalizedPayload,
                    ProviderMetadata = new LlmContractNormalizationProviderMetadata
                    {
                        Provider = gatewayResponse.Provider,
                        Model = gatewayResponse.Model,
                        RequestId = gatewayResponse.RequestId,
                        LatencyMs = gatewayResponse.LatencyMs,
                        FallbackAttempt = isFallbackAttempt
                    },
                    Diagnostics = diagnostics
                };
            }
            catch (LlmGatewayException ex)
            {
                diagnostics.Attempts.Add(new LlmContractNormalizationAttempt
                {
                    AttemptNumber = attempt + 1,
                    Provider = ex.Provider ?? OpenRouterProviderClient.ProviderIdValue,
                    Model = model,
                    FallbackAttempt = isFallbackAttempt,
                    Success = false,
                    FailureReason = "provider_error",
                    GatewayErrorCategory = ex.Category
                });

                latestProviderError = BuildFailureResult(
                    LlmContractNormalizationStatus.ProviderError,
                    request.ContractKind,
                    schema.SchemaRef,
                    diagnostics,
                    "provider_error");

                _logger.LogWarning(
                    ex,
                    "Contract normalization shaping attempt failed. contract_kind={ContractKind}, schema_ref={SchemaRef}, shaping_provider={Provider}, shaping_model={Model}, fallback_attempt={FallbackAttempt}, category={Category}, retryable={Retryable}",
                    request.ContractKind,
                    schema.SchemaRef,
                    ex.Provider ?? OpenRouterProviderClient.ProviderIdValue,
                    model,
                    isFallbackAttempt,
                    ex.Category,
                    ex.Retryable);
            }
        }

        var failure = latestSchemaError ?? latestNormalizationError ?? latestProviderError ??
            BuildFailureResult(LlmContractNormalizationStatus.NormalizationFailed, request.ContractKind, schema.SchemaRef, diagnostics, "normalization_failed");

        _logger.LogWarning(
            "Contract normalization failed. contract_kind={ContractKind}, schema_ref={SchemaRef}, status={Status}, attempts={Attempts}, failure_reason={FailureReason}, validation_errors={ValidationErrors}",
            request.ContractKind,
            schema.SchemaRef,
            failure.Status,
            diagnostics.Attempts.Count,
            failure.Diagnostics.FailureReason ?? "n/a",
            string.Join(" | ", failure.Diagnostics.ValidationErrors));

        return failure;
    }

    private static bool LooksLikeJsonObject(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static LlmContractNormalizationResult BuildFailureResult(
        LlmContractNormalizationStatus status,
        LlmContractKind contractKind,
        string schemaRef,
        LlmContractNormalizationDiagnostics diagnostics,
        string failureReason)
    {
        diagnostics.FailureReason = failureReason;
        return BuildResult(status, contractKind, schemaRef, diagnostics);
    }

    private static LlmContractNormalizationResult BuildResult(
        LlmContractNormalizationStatus status,
        LlmContractKind contractKind,
        string schemaRef,
        LlmContractNormalizationDiagnostics diagnostics)
    {
        return new LlmContractNormalizationResult
        {
            Status = status,
            ContractKind = contractKind,
            SchemaRef = schemaRef,
            Diagnostics = diagnostics
        };
    }

    private static LlmGatewayRequest BuildShapingRequest(
        LlmContractNormalizationRequest request,
        LlmContractSchemaDescriptor schema,
        string model)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = string.IsNullOrWhiteSpace(request.TaskKey) ? "contract_normalization" : request.TaskKey.Trim(),
            ModelHint = model,
            ResponseMode = LlmResponseMode.JsonObject,
            StructuredOutputSchema = new LlmStructuredOutputSchema
            {
                Name = schema.SchemaName,
                SchemaJson = schema.SchemaJson,
                Strict = true
            },
            RouteOverride = new LlmGatewayRouteOverride
            {
                PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
                ProviderModelHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [OpenRouterProviderClient.ProviderIdValue] = model
                }
            },
            Limits = new LlmExecutionLimits
            {
                MaxTokens = request.Limits.MaxTokens ?? 320,
                Temperature = request.Limits.Temperature ?? 0f,
                TimeoutMs = request.Limits.TimeoutMs
            },
            Trace = new LlmTraceContext
            {
                PathKey = string.IsNullOrWhiteSpace(request.Trace.PathKey)
                    ? $"contract_normalization:{request.ContractKind}" : request.Trace.PathKey,
                RequestId = request.Trace.RequestId,
                IsImportScope = request.Trace.IsImportScope,
                IsOptionalPath = request.Trace.IsOptionalPath,
                ScopeTags = request.Trace.ScopeTags.Count > 0
                    ? new List<string>(request.Trace.ScopeTags)
                    :
                    [
                        "contract_normalization",
                        request.ContractKind.ToString().ToLowerInvariant()
                    ]
            },
            Messages =
            [
                LlmGatewayMessage.FromText(
                    LlmMessageRole.System,
                    string.IsNullOrWhiteSpace(schema.SystemInstruction)
                        ? "Transform the provided reasoning output into JSON that strictly matches the schema. Return JSON only."
                        : schema.SystemInstruction!),
                LlmGatewayMessage.FromText(
                    LlmMessageRole.User,
                    $"Normalize this reasoning output into contract '{schema.SchemaRef}'. Return only JSON object.\n\nRAW_REASONING:\n{request.RawReasoningPayload}")
            ]
        };
    }
}

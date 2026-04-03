using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Host.Launch;

public static class LlmContractNormalizationSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        await RunSuccessScenarioAsync(ct);
        await RunSchemaInvalidScenarioAsync(ct);
        await RunFallbackScenarioAsync(ct);
        await RunProviderErrorScenarioAsync(ct);
        await RunBusinessValidationScenarioAsync(ct);
        await RunSummaryContractSuccessScenarioAsync(ct);
        await RunSummaryContractSchemaInvalidScenarioAsync(ct);
    }

    private static async Task RunSuccessScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, _) =>
        {
            var payload = """{"classification":"formatting","summary":"Косметическая правка без изменения смысла.","should_affect_memory":false,"added_important":false,"removed_important":false,"confidence":0.92}""";
            return Task.FromResult(BuildResponse(request, payload));
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(BuildRequest("Reasoning for success."), ct);

        if (result.Status != LlmContractNormalizationStatus.Success)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: success scenario did not return success.");
        }

        if (!string.Equals(result.ProviderMetadata?.Model, "openai/gpt-4.1-nano", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Contract normalization smoke failed: success scenario did not use nano shaping model.");
        }
    }

    private static async Task RunSchemaInvalidScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, _) =>
        {
            var payload = """{"classification":"formatting","summary":"missing required fields"}""";
            return Task.FromResult(BuildResponse(request, payload));
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(BuildRequest("Reasoning for schema-invalid."), ct);

        if (result.Status != LlmContractNormalizationStatus.SchemaInvalid)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: invalid schema scenario did not return schema_invalid.");
        }

        if (result.Diagnostics.ValidationErrors.Count == 0)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: invalid schema scenario did not report validation errors.");
        }
    }

    private static async Task RunFallbackScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, attempt) =>
        {
            if (attempt == 1)
            {
                var badPayload = "not-json";
                return Task.FromResult(BuildResponse(request, badPayload));
            }

            var goodPayload = """{"classification":"important_added","summary":"Добавлено важное время звонка.","should_affect_memory":true,"added_important":true,"removed_important":false,"confidence":0.95}""";
            return Task.FromResult(BuildResponse(request, goodPayload));
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(BuildRequest("Reasoning for fallback."), ct);

        if (result.Status != LlmContractNormalizationStatus.Success)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: fallback scenario did not recover with mini model.");
        }

        if (!string.Equals(result.ProviderMetadata?.Model, "openai/gpt-4.1-mini", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Contract normalization smoke failed: fallback scenario did not switch to mini model.");
        }

        if (result.Diagnostics.Attempts.Count < 2)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: fallback scenario did not record both shaping attempts.");
        }
    }

    private static async Task RunProviderErrorScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, _) =>
        {
            throw new LlmGatewayException("Synthetic provider failure")
            {
                Category = LlmGatewayErrorCategory.TransientUpstream,
                Provider = OpenRouterProviderClient.ProviderIdValue,
                Retryable = true,
                Modality = request.Modality
            };
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(BuildRequest("Reasoning for provider error."), ct);

        if (result.Status != LlmContractNormalizationStatus.ProviderError)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: provider error scenario did not return provider_error.");
        }
    }

    private static async Task RunBusinessValidationScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, _) =>
        {
            var payload = """{"classification":"message_deleted","summary":"Сообщение удалено.","should_affect_memory":false,"added_important":false,"removed_important":false,"confidence":0.93}""";
            return Task.FromResult(BuildResponse(request, payload));
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(BuildRequest("Reasoning for business-rule failure."), ct);

        if (result.Status != LlmContractNormalizationStatus.SchemaInvalid)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: business-rule scenario did not return schema_invalid.");
        }

        if (!result.Diagnostics.ValidationErrors.Any(error => error.StartsWith("business:", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Contract normalization smoke failed: business-rule scenario did not expose business validation failure.");
        }
    }

    private static async Task RunSummaryContractSuccessScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, _) =>
        {
            var payload = """{"summary":"Сессия: обсудили встречу и согласовали время созвона на завтра."}""";
            return Task.FromResult(BuildResponse(request, payload));
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(
            BuildRequest("Reasoning for summary success.", LlmContractKind.SessionSummaryV1),
            ct);

        if (result.Status != LlmContractNormalizationStatus.Success)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: summary family success scenario did not return success.");
        }
    }

    private static async Task RunSummaryContractSchemaInvalidScenarioAsync(CancellationToken ct)
    {
        var gateway = new StubGateway((request, _) =>
        {
            var payload = """{"summary":""}""";
            return Task.FromResult(BuildResponse(request, payload));
        });

        var normalizer = BuildNormalizer(gateway);
        var result = await normalizer.NormalizeAsync(
            BuildRequest("Reasoning for summary invalid.", LlmContractKind.SessionSummaryV1),
            ct);

        if (result.Status != LlmContractNormalizationStatus.SchemaInvalid)
        {
            throw new InvalidOperationException("Contract normalization smoke failed: summary family invalid scenario did not return schema_invalid.");
        }

        if (!result.Diagnostics.ValidationErrors.Any(error => string.Equals(error, "schema:summary_invalid", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Contract normalization smoke failed: summary family invalid scenario did not expose summary validation error.");
        }
    }

    private static OpenRouterContractNormalizer BuildNormalizer(ILlmGateway gateway)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddFilter(_ => false));
        return new OpenRouterContractNormalizer(
            gateway,
            new EditDiffContractSchemaProvider(),
            new EditDiffContractValidator(),
            loggerFactory.CreateLogger<OpenRouterContractNormalizer>());
    }

    private static LlmContractNormalizationRequest BuildRequest(string reasoning, LlmContractKind kind = LlmContractKind.EditDiffV1)
    {
        return new LlmContractNormalizationRequest
        {
            ContractKind = kind,
            RawReasoningPayload = reasoning,
            TaskKey = "contract_normalization_smoke",
            Trace = new LlmTraceContext
            {
                PathKey = "smoke:contract_normalization",
                RequestId = Guid.NewGuid().ToString("N")
            },
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 280,
                Temperature = 0f,
                TimeoutMs = 5000
            }
        };
    }

    private static LlmGatewayResponse BuildResponse(LlmGatewayRequest request, string payload)
    {
        return new LlmGatewayResponse
        {
            Provider = OpenRouterProviderClient.ProviderIdValue,
            Model = request.ModelHint ?? "openai/gpt-4.1-nano",
            RequestId = Guid.NewGuid().ToString("N"),
            LatencyMs = 12,
            Output = new LlmGatewayOutput
            {
                Text = payload,
                StructuredPayloadJson = payload
            }
        };
    }

    private sealed class StubGateway : ILlmGateway
    {
        private readonly Func<LlmGatewayRequest, int, Task<LlmGatewayResponse>> _handler;
        private int _attempt;

        public StubGateway(Func<LlmGatewayRequest, int, Task<LlmGatewayResponse>> handler)
        {
            _handler = handler;
        }

        public Task<LlmGatewayResponse> ExecuteAsync(LlmGatewayRequest request, CancellationToken ct = default)
        {
            _attempt++;
            return _handler(request, _attempt);
        }
    }
}

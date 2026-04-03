using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Launch;

public static class LlmGatewayFailureSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        await RunRetryableFallbackScenarioAsync(ct);
        await RunAuthFailFastScenarioAsync(ct);
        await RunSchemaMismatchScenarioAsync(ct);
    }

    private static async Task RunRetryableFallbackScenarioAsync(CancellationToken ct)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var codexHandler = new RecordingHttpMessageHandler((request, body) =>
        {
            AssertTextRequest(request, body, "failure-primary-model");
            return BuildErrorResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"codex degraded\"}", "codex-retryable-1");
        });
        var openRouterHandler = new RecordingHttpMessageHandler((request, body) =>
        {
            AssertTextRequest(request, body, "failure-fallback-model");
            return BuildJsonResponse(
                new
                {
                    id = "openrouter-fallback-1",
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = "gateway fallback ok"
                            }
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 14,
                        completion_tokens = 5,
                        total_tokens = 19,
                        cost = 0.0019m
                    }
                },
                "openrouter-fallback-1");
        });

        var gateway = BuildGateway(options, loggerFactory, codexHandler, openRouterHandler);
        var response = await gateway.ExecuteAsync(BuildTextRequest("fallback"), ct);

        if (!string.Equals(response.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
            || !string.Equals(response.Model, "failure-fallback-model", StringComparison.Ordinal)
            || !string.Equals(response.RequestId, "openrouter-fallback-1", StringComparison.Ordinal)
            || !string.Equals(response.Output.Text, "gateway fallback ok", StringComparison.Ordinal)
            || !response.FallbackApplied
            || !string.Equals(response.FallbackFromProvider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal)
            || response.Usage.TotalTokens != 19
            || string.IsNullOrWhiteSpace(response.RawProviderPayloadJson))
        {
            throw new InvalidOperationException("LLM gateway failure smoke failed: retryable fallback response did not preserve normalized fallback audit fields.");
        }

        if (codexHandler.RequestCount != 1 || openRouterHandler.RequestCount != 1)
        {
            throw new InvalidOperationException($"LLM gateway failure smoke failed: retryable fallback expected 1 request per provider, saw codex={codexHandler.RequestCount}, openrouter={openRouterHandler.RequestCount}.");
        }
    }

    private static async Task RunAuthFailFastScenarioAsync(CancellationToken ct)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var codexHandler = new RecordingHttpMessageHandler((request, body) =>
        {
            AssertTextRequest(request, body, "failure-primary-model");
            return BuildErrorResponse(HttpStatusCode.Unauthorized, "{\"error\":\"bad api key\"}", "codex-auth-1");
        });
        var openRouterHandler = new RecordingHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("LLM gateway failure smoke failed: fallback provider should not be invoked for non-retryable auth errors."));

        var gateway = BuildGateway(options, loggerFactory, codexHandler, openRouterHandler);

        try
        {
            await gateway.ExecuteAsync(BuildTextRequest("auth_fail_fast"), ct);
            throw new InvalidOperationException("LLM gateway failure smoke failed: auth error did not fail fast.");
        }
        catch (LlmGatewayException ex)
        {
            AssertException(
                ex,
                LlmGatewayErrorCategory.Auth,
                retryable: false,
                CodexLbChatProviderClient.ProviderIdValue,
                HttpStatusCode.Unauthorized,
                "401");
        }

        if (codexHandler.RequestCount != 1 || openRouterHandler.RequestCount != 0)
        {
            throw new InvalidOperationException($"LLM gateway failure smoke failed: auth fail-fast expected codex=1 openrouter=0, saw codex={codexHandler.RequestCount}, openrouter={openRouterHandler.RequestCount}.");
        }
    }

    private static async Task RunSchemaMismatchScenarioAsync(CancellationToken ct)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var codexHandler = new RecordingHttpMessageHandler((request, body) =>
        {
            AssertTextRequest(request, body, "failure-primary-model");
            return BuildErrorResponse(HttpStatusCode.BadRequest, "{\"error\":\"schema validation failed\"}", "codex-schema-1");
        });
        var openRouterHandler = new RecordingHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("LLM gateway failure smoke failed: fallback provider should not be invoked for schema mismatch errors."));

        var gateway = BuildGateway(options, loggerFactory, codexHandler, openRouterHandler);

        try
        {
            await gateway.ExecuteAsync(BuildTextRequest("schema_fail_fast"), ct);
            throw new InvalidOperationException("LLM gateway failure smoke failed: schema mismatch did not surface a normalized exception.");
        }
        catch (LlmGatewayException ex)
        {
            AssertException(
                ex,
                LlmGatewayErrorCategory.SchemaMismatch,
                retryable: false,
                CodexLbChatProviderClient.ProviderIdValue,
                HttpStatusCode.BadRequest,
                "schema");
        }

        if (codexHandler.RequestCount != 1 || openRouterHandler.RequestCount != 0)
        {
            throw new InvalidOperationException($"LLM gateway failure smoke failed: schema fail-fast expected codex=1 openrouter=0, saw codex={codexHandler.RequestCount}, openrouter={openRouterHandler.RequestCount}.");
        }
    }

    private static LlmGatewayService BuildGateway(
        IOptions<LlmGatewaySettings> options,
        ILoggerFactory loggerFactory,
        HttpMessageHandler codexHandler,
        HttpMessageHandler openRouterHandler)
    {
        return new LlmGatewayService(
            providers:
            [
                new CodexLbChatProviderClient(new HttpClient(codexHandler), options),
                new OpenRouterProviderClient(new HttpClient(openRouterHandler), options)
            ],
            routingPolicy: new DefaultLlmRoutingPolicy(options),
            settings: options,
            logger: loggerFactory.CreateLogger<LlmGatewayService>(),
            metrics: new LlmGatewayMetrics());
    }

    private static LlmGatewaySettings BuildSettings()
    {
        return new LlmGatewaySettings
        {
            Enabled = true,
            LogRawProviderPayloadJson = true,
            Routing = new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["text_chat"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = "failure-primary-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "failure-fallback-model"
                        }
                    ]
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://codex-lb.local",
                    ApiKey = "codex-failure-key",
                    DefaultModel = "codex-default-failure",
                    ChatCompletionsPath = "/v1/chat/completions"
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-failure-key",
                    DefaultModel = "openrouter-default-failure",
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings"
                }
            }
        };
    }

    private static LlmGatewayRequest BuildTextRequest(string scenario)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = $"gateway_failure_smoke_{scenario}",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = $"smoke:failure:{scenario}",
                RequestId = $"gateway-failure-{scenario}-request"
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, "You are a precise failure smoke test."),
                LlmGatewayMessage.FromText(LlmMessageRole.User, "Reply with a deterministic fallback outcome.")
            ]
        };
    }

    private static void AssertTextRequest(HttpRequestMessage request, string body, string expectedModel)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("LLM gateway failure smoke failed: provider request did not use POST.");
        }

        if (!string.Equals(request.RequestUri?.AbsolutePath, "/v1/chat/completions", StringComparison.Ordinal)
            && !string.Equals(request.RequestUri?.AbsolutePath, "/api/v1/chat/completions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway failure smoke failed: unexpected provider path '{request.RequestUri?.AbsolutePath}'.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertJsonString(root, "model", expectedModel, "failure model");
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() != 2)
        {
            throw new InvalidOperationException("LLM gateway failure smoke failed: text request messages were not serialized.");
        }
    }

    private static HttpResponseMessage BuildJsonResponse(object payload, string requestId)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        response.Headers.Add("x-request-id", requestId);
        return response;
    }

    private static HttpResponseMessage BuildErrorResponse(HttpStatusCode statusCode, string body, string requestId)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        response.Headers.Add("x-request-id", requestId);
        return response;
    }

    private static void AssertException(
        LlmGatewayException exception,
        LlmGatewayErrorCategory expectedCategory,
        bool retryable,
        string expectedProvider,
        HttpStatusCode expectedStatus,
        string rawReasonContains)
    {
        if (exception.Category != expectedCategory
            || exception.Retryable != retryable
            || !string.Equals(exception.Provider, expectedProvider, StringComparison.Ordinal)
            || exception.HttpStatus != expectedStatus
            || string.IsNullOrWhiteSpace(exception.RawReasonCode)
            || !exception.RawReasonCode.Contains(rawReasonContains, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"LLM gateway failure smoke failed: normalized exception mismatch. category={exception.Category} retryable={exception.Retryable} provider={exception.Provider} status={exception.HttpStatus} raw={exception.RawReasonCode}");
        }
    }

    private static void AssertJsonString(JsonElement element, string propertyName, string expected, string label)
    {
        if (!element.TryGetProperty(propertyName, out var node) || !string.Equals(node.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway failure smoke failed: unexpected {label}.");
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _handler(request, body);
        }
    }
}

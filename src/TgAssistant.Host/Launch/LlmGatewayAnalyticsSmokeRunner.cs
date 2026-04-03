using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Launch;

public static class LlmGatewayAnalyticsSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var codexHandler = new RecordingHttpMessageHandler(HandleCodexRequest);
        var openRouterHandler = new RecordingHttpMessageHandler(HandleOpenRouterRequest);
        var metrics = new LlmGatewayMetrics();

        using var collector = new GatewayMetricCollector();
        collector.Start();

        var gateway = new LlmGatewayService(
            providers:
            [
                new CodexLbChatProviderClient(new HttpClient(codexHandler), options),
                new OpenRouterProviderClient(new HttpClient(openRouterHandler), options)
            ],
            routingPolicy: new DefaultLlmRoutingPolicy(options),
            settings: options,
            logger: loggerFactory.CreateLogger<LlmGatewayService>(),
            metrics: metrics);

        var primaryResponse = await gateway.ExecuteAsync(BuildTextRequest("primary"), ct);
        if (!string.Equals(primaryResponse.Provider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway analytics smoke failed: expected primary text request to resolve to codex-lb.");
        }

        var fallbackResponse = await gateway.ExecuteAsync(BuildTextRequest("fallback"), ct);
        if (!string.Equals(fallbackResponse.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
            || !fallbackResponse.FallbackApplied)
        {
            throw new InvalidOperationException("LLM gateway analytics smoke failed: expected fallback text request to resolve to openrouter with fallback metadata.");
        }

        var samples = collector.GetSamples();
        AssertMetric(samples, "llm_gateway_requests_total", "provider", "model", "modality", "status");
        AssertMetric(samples, "llm_gateway_tokens_total", "provider", "model", "modality", "status");
        AssertMetric(samples, "llm_gateway_provider_latency_ms", "provider", "model", "modality", "status");
        AssertMetric(samples, "llm_gateway_end_to_end_latency_ms", "provider", "model", "modality", "status");
        AssertMetric(samples, "llm_gateway_tokens_per_second", "provider", "model", "modality", "status");
        AssertMetric(samples, "llm_gateway_spend_usd_total", "provider", "model", "modality", "status", "spend_source");
        AssertMetric(samples, "llm_gateway_fallback_total", "from_provider", "to_provider", "reason", "modality");

        if (!samples.Any(sample => sample.Name == "llm_gateway_requests_total"
                                   && sample.Tags.TryGetValue("route_kind", out var routeKind)
                                   && string.Equals(routeKind, "fallback", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("LLM gateway analytics smoke failed: request metrics did not expose fallback route dimension.");
        }

        if (!samples.Any(sample => sample.Name == "llm_gateway_spend_usd_total"
                                   && sample.Tags.TryGetValue("provider", out var provider)
                                   && string.Equals(provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
                                   && sample.Tags.TryGetValue("spend_source", out var spendSource)
                                   && string.Equals(spendSource, "derived", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("LLM gateway analytics smoke failed: openrouter spend metrics did not emit price-derived spend labels.");
        }
    }

    private static void AssertMetric(
        IReadOnlyList<MetricSample> samples,
        string metricName,
        params string[] requiredTags)
    {
        var matches = samples.Where(sample => string.Equals(sample.Name, metricName, StringComparison.Ordinal)).ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"LLM gateway analytics smoke failed: metric '{metricName}' was not emitted.");
        }

        if (requiredTags.Length == 0)
        {
            return;
        }

        if (!matches.Any(sample => requiredTags.All(tag => sample.Tags.ContainsKey(tag))))
        {
            throw new InvalidOperationException(
                $"LLM gateway analytics smoke failed: metric '{metricName}' is missing required tags [{string.Join(", ", requiredTags)}].");
        }
    }

    private static LlmGatewaySettings BuildSettings()
    {
        return new LlmGatewaySettings
        {
            Enabled = true,
            Routing = new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["text_chat"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = "analytics-primary-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "analytics-fallback-model"
                        }
                    ]
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://codex-lb.local",
                    ApiKey = "codex-analytics-key",
                    DefaultModel = "codex-default-analytics",
                    ChatCompletionsPath = "/v1/chat/completions"
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-analytics-key",
                    DefaultModel = "openrouter-default-analytics",
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings",
                    PromptCostUsdPer1kTokens = 0.12m,
                    CompletionCostUsdPer1kTokens = 0.24m
                }
            }
        };
    }

    private static LlmGatewayRequest BuildTextRequest(string scenario)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = $"gateway_analytics_smoke_{scenario}",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = $"smoke:analytics:{scenario}",
                RequestId = $"gateway-analytics-{scenario}-request"
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, "You are a precise analytics smoke test."),
                LlmGatewayMessage.FromText(LlmMessageRole.User, $"Reply with deterministic {scenario} output.")
            ]
        };
    }

    private static HttpResponseMessage HandleCodexRequest(HttpRequestMessage request, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("messages", out var messages)
            || messages.ValueKind != JsonValueKind.Array
            || messages.GetArrayLength() != 2)
        {
            throw new InvalidOperationException("LLM gateway analytics smoke failed: codex request was malformed.");
        }

        var userMessage = messages[1].GetProperty("content").GetString() ?? string.Empty;
        if (userMessage.Contains("fallback", StringComparison.Ordinal))
        {
            return BuildErrorResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"codex degraded\"}", "codex-analytics-retryable-1");
        }

        return BuildJsonResponse(
            new
            {
                id = "codex-analytics-success-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "analytics primary ok"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 11,
                    completion_tokens = 5,
                    total_tokens = 16,
                    cost = 0.0016m
                }
            },
            "codex-analytics-success-1");
    }

    private static HttpResponseMessage HandleOpenRouterRequest(HttpRequestMessage request, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("model", out _))
        {
            throw new InvalidOperationException("LLM gateway analytics smoke failed: openrouter request did not contain model.");
        }

        return BuildJsonResponse(
            new
            {
                id = "openrouter-analytics-fallback-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "analytics fallback ok"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 14,
                    completion_tokens = 6,
                    total_tokens = 20
                }
            },
            "openrouter-analytics-fallback-1");
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

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, string, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return _handler(request, body);
        }
    }

    private sealed class GatewayMetricCollector : IDisposable
    {
        private readonly List<MetricSample> _samples = [];
        private readonly object _sync = new();
        private MeterListener? _listener;

        public void Start()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (string.Equals(instrument.Meter.Name, LlmGatewayMetrics.MeterName, StringComparison.Ordinal))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                Capture(instrument.Name, measurement, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
                Capture(instrument.Name, measurement, tags));
            _listener.Start();
        }

        public IReadOnlyList<MetricSample> GetSamples()
        {
            _listener?.RecordObservableInstruments();
            lock (_sync)
            {
                return _samples.ToList();
            }
        }

        public void Dispose()
        {
            _listener?.Dispose();
        }

        private void Capture(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var capturedTags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                capturedTags[tag.Key] = tag.Value?.ToString() ?? string.Empty;
            }

            lock (_sync)
            {
                _samples.Add(new MetricSample(name, value, capturedTags));
            }
        }
    }

    private sealed record MetricSample(string Name, double Value, IReadOnlyDictionary<string, string> Tags);
}

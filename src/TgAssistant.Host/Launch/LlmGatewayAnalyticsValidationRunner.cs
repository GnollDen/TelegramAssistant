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

public static class LlmGatewayAnalyticsValidationRunner
{
    public static async Task<LlmGatewayAnalyticsValidationReport> RunAsync(string? outputPath = null, CancellationToken ct = default)
    {
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var codexHandler = new RecordingHttpMessageHandler(HandleCodexRequest);
        var openRouterHandler = new RecordingHttpMessageHandler(HandleOpenRouterRequest);
        var metrics = new LlmGatewayMetrics();
        var resolvedOutputPath = ResolveOutputPath(outputPath);

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

        await ExecuteScenarioAsync(gateway, BuildTextRequest("primary"), expectedProvider: CodexLbChatProviderClient.ProviderIdValue, expectFallback: false, ct);
        await ExecuteScenarioAsync(gateway, BuildTextRequest("fallback"), expectedProvider: OpenRouterProviderClient.ProviderIdValue, expectFallback: true, ct);
        await ExecuteScenarioAsync(gateway, BuildOpenRouterProviderSpendRequest(), expectedProvider: OpenRouterProviderClient.ProviderIdValue, expectFallback: false, ct);
        await ExecuteEmbeddingScenarioAsync(gateway, ct);
        await ExecuteHardFailureScenarioAsync(gateway, ct);

        var samples = collector.GetSamples();
        var metricCoverage = BuildMetricCoverage(samples);
        var spendSources = samples
            .Where(sample => string.Equals(sample.Name, "llm_gateway_spend_usd_total", StringComparison.Ordinal))
            .Where(sample => sample.Tags.TryGetValue("provider", out var provider)
                             && string.Equals(provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal))
            .Select(sample => sample.Tags.TryGetValue("spend_source", out var source) ? source : string.Empty)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(source => source, StringComparer.Ordinal)
            .ToList();

        var modalities = samples
            .Where(sample => string.Equals(sample.Name, "llm_gateway_requests_total", StringComparison.Ordinal))
            .Select(sample => sample.Tags.TryGetValue("modality", out var modality) ? modality : string.Empty)
            .Where(modality => !string.IsNullOrWhiteSpace(modality))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(modality => modality, StringComparer.Ordinal)
            .ToList();

        var statuses = samples
            .Where(sample => string.Equals(sample.Name, "llm_gateway_requests_total", StringComparison.Ordinal))
            .Select(sample => sample.Tags.TryGetValue("status", out var status) ? status : string.Empty)
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(status => status, StringComparer.Ordinal)
            .ToList();

        var routeKinds = samples
            .Where(sample => string.Equals(sample.Name, "llm_gateway_requests_total", StringComparison.Ordinal))
            .Select(sample => sample.Tags.TryGetValue("route_kind", out var routeKind) ? routeKind : string.Empty)
            .Where(routeKind => !string.IsNullOrWhiteSpace(routeKind))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(routeKind => routeKind, StringComparer.Ordinal)
            .ToList();

        var report = new LlmGatewayAnalyticsValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            Metrics = metricCoverage,
            OpenRouterSpendSources = spendSources,
            RequestModalities = modalities,
            RequestStatuses = statuses,
            RequestRouteKinds = routeKinds,
            GrafanaQueryHints =
            [
                "sum(rate(llm_gateway_requests_total{modality=\"textchat\"}[5m])) by (provider,model,status)",
                "sum(rate(llm_gateway_tokens_total[5m])) by (provider,model,modality,status)",
                "histogram_quantile(0.95, sum(rate(llm_gateway_provider_latency_ms_bucket[5m])) by (le,provider,model,modality,status))",
                "histogram_quantile(0.95, sum(rate(llm_gateway_end_to_end_latency_ms_bucket[5m])) by (le,provider,model,modality,status))",
                "sum(rate(llm_gateway_tokens_per_second_sum[5m])) / clamp_min(sum(rate(llm_gateway_tokens_per_second_count[5m])), 1)",
                "sum(increase(llm_gateway_spend_usd_total[1h])) by (provider,model,modality,status,spend_source)"
            ]
        };

        var hasPrimaryRetryableFailureAccounting = HasRequestSample(
            samples,
            provider: CodexLbChatProviderClient.ProviderIdValue,
            status: "error",
            routeKind: "primary");
        var hasPrimaryRetryableFailureCounter = HasFailureSample(
            samples,
            provider: CodexLbChatProviderClient.ProviderIdValue,
            routeKind: "primary");
        var hasFallbackSuccessAccounting = HasRequestSample(
            samples,
            provider: OpenRouterProviderClient.ProviderIdValue,
            status: "success",
            routeKind: "fallback");

        report.AllChecksPassed = report.Metrics.All(metric => metric.Emitted && metric.HasRequiredTags)
            && spendSources.Contains("provider", StringComparer.Ordinal)
            && spendSources.Contains("derived", StringComparer.Ordinal)
            && routeKinds.Contains("primary", StringComparer.Ordinal)
            && routeKinds.Contains("fallback", StringComparer.Ordinal)
            && statuses.Contains("success", StringComparer.Ordinal)
            && statuses.Contains("error", StringComparer.Ordinal)
            && hasPrimaryRetryableFailureAccounting
            && hasPrimaryRetryableFailureCounter
            && hasFallbackSuccessAccounting;
        report.Recommendation = report.AllChecksPassed
            ? "analytics_ready_for_rollout_ops_decisions"
            : "hold_rollout_until_analytics_coverage_is_complete";

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException("LLM gateway analytics validation failed: bounded analytics evidence is incomplete for rollout/ops decisions.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        return report;
    }

    private static bool HasRequestSample(
        IReadOnlyList<MetricSample> samples,
        string provider,
        string status,
        string routeKind)
    {
        return samples.Any(sample =>
            string.Equals(sample.Name, "llm_gateway_requests_total", StringComparison.Ordinal)
            && sample.Tags.TryGetValue("provider", out var taggedProvider)
            && string.Equals(taggedProvider, provider, StringComparison.Ordinal)
            && sample.Tags.TryGetValue("status", out var taggedStatus)
            && string.Equals(taggedStatus, status, StringComparison.Ordinal)
            && sample.Tags.TryGetValue("route_kind", out var taggedRouteKind)
            && string.Equals(taggedRouteKind, routeKind, StringComparison.Ordinal));
    }

    private static bool HasFailureSample(
        IReadOnlyList<MetricSample> samples,
        string provider,
        string routeKind)
    {
        return samples.Any(sample =>
            string.Equals(sample.Name, "llm_gateway_failures_total", StringComparison.Ordinal)
            && sample.Tags.TryGetValue("provider", out var taggedProvider)
            && string.Equals(taggedProvider, provider, StringComparison.Ordinal)
            && sample.Tags.TryGetValue("status", out var taggedStatus)
            && string.Equals(taggedStatus, "error", StringComparison.Ordinal)
            && sample.Tags.TryGetValue("route_kind", out var taggedRouteKind)
            && string.Equals(taggedRouteKind, routeKind, StringComparison.Ordinal));
    }

    private static async Task ExecuteScenarioAsync(
        LlmGatewayService gateway,
        LlmGatewayRequest request,
        string expectedProvider,
        bool expectFallback,
        CancellationToken ct)
    {
        var response = await gateway.ExecuteAsync(request, ct);
        if (!string.Equals(response.Provider, expectedProvider, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"LLM gateway analytics validation failed: expected provider '{expectedProvider}' but got '{response.Provider}'.");
        }

        if (response.FallbackApplied != expectFallback)
        {
            throw new InvalidOperationException(
                $"LLM gateway analytics validation failed: fallback expectation mismatch for task '{request.TaskKey}'.");
        }
    }

    private static async Task ExecuteEmbeddingScenarioAsync(LlmGatewayService gateway, CancellationToken ct)
    {
        var response = await gateway.ExecuteAsync(
            new LlmGatewayRequest
            {
                Modality = LlmModality.Embeddings,
                TaskKey = "gateway_analytics_validation_embeddings",
                ResponseMode = LlmResponseMode.EmbeddingVector,
                Limits = new LlmExecutionLimits
                {
                    TimeoutMs = 5000
                },
                Trace = new LlmTraceContext
                {
                    PathKey = "smoke:analytics:embeddings",
                    RequestId = "gateway-analytics-embeddings-request"
                },
                EmbeddingInputs = ["bounded embeddings validation input"]
            },
            ct);

        if (!string.Equals(response.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway analytics validation failed: embeddings request should stay on openrouter.");
        }
    }

    private static async Task ExecuteHardFailureScenarioAsync(LlmGatewayService gateway, CancellationToken ct)
    {
        try
        {
            await gateway.ExecuteAsync(BuildTextRequest("hard-failure"), ct);
            throw new InvalidOperationException("LLM gateway analytics validation failed: hard-failure scenario unexpectedly succeeded.");
        }
        catch (LlmGatewayException ex) when (ex.Category == LlmGatewayErrorCategory.Auth)
        {
        }
    }

    private static List<LlmGatewayMetricCoverage> BuildMetricCoverage(IReadOnlyList<MetricSample> samples)
    {
        var requirements = new[]
        {
            new MetricRequirement("llm_gateway_requests_total", "provider", "model", "modality", "status", "route_kind"),
            new MetricRequirement("llm_gateway_failures_total", "provider", "model", "modality", "status", "route_kind", "error_category"),
            new MetricRequirement("llm_gateway_tokens_total", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_prompt_tokens_total", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_completion_tokens_total", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_provider_latency_ms", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_end_to_end_latency_ms", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_tokens_per_second", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_fallback_total", "from_provider", "to_provider", "reason", "modality"),
            new MetricRequirement("llm_gateway_spend_usd_total", "provider", "model", "modality", "status", "spend_source"),
            new MetricRequirement("llm_gateway_request_spend_usd", "provider", "model", "modality", "status", "spend_source")
        };

        var result = new List<LlmGatewayMetricCoverage>(requirements.Length);
        foreach (var requirement in requirements)
        {
            var matches = samples
                .Where(sample => string.Equals(sample.Name, requirement.Name, StringComparison.Ordinal))
                .ToList();

            var missingTags = requirement.RequiredTags
                .Where(tag => !matches.Any(sample => sample.Tags.ContainsKey(tag)))
                .ToList();

            result.Add(new LlmGatewayMetricCoverage
            {
                Name = requirement.Name,
                Emitted = matches.Count > 0,
                HasRequiredTags = missingTags.Count == 0,
                MissingTags = missingTags
            });
        }

        return result;
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var candidate = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(repositoryRoot, "artifacts", "llm-gateway", "analytics_validation_report.json")
            : outputPath.Trim();
        return Path.GetFullPath(candidate);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        var cursor = new DirectoryInfo(startDirectory);
        while (cursor is not null)
        {
            var gitDirectory = Path.Combine(cursor.FullName, ".git");
            if (Directory.Exists(gitDirectory) || File.Exists(gitDirectory))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        return startDirectory;
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
                    PrimaryModel = "analytics-validation-primary-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "analytics-validation-fallback-model"
                        }
                    ]
                },
                ["embeddings"] = new()
                {
                    PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
                    PrimaryModel = "analytics-validation-embedding-model"
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
                    CompletionCostUsdPer1kTokens = 0.24m,
                    TotalCostUsdPer1kTokens = 0.10m
                }
            }
        };
    }

    private static LlmGatewayRequest BuildTextRequest(string scenario)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = $"gateway_analytics_validation_{scenario}",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = $"smoke:analytics:validate:{scenario}",
                RequestId = $"gateway-analytics-validation-{scenario}-request"
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, "You are a precise analytics validation test."),
                LlmGatewayMessage.FromText(LlmMessageRole.User, $"Reply with deterministic {scenario} output.")
            ]
        };
    }

    private static LlmGatewayRequest BuildOpenRouterProviderSpendRequest()
    {
        var request = BuildTextRequest("provider-spend");
        request.RouteOverride = new LlmGatewayRouteOverride
        {
            PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
            ProviderModelHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [OpenRouterProviderClient.ProviderIdValue] = "analytics-provider-spend-model"
            }
        };
        return request;
    }

    private static HttpResponseMessage HandleCodexRequest(HttpRequestMessage request, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var userMessage = root.TryGetProperty("messages", out var messages)
                          && messages.ValueKind == JsonValueKind.Array
                          && messages.GetArrayLength() > 1
            ? messages[1].GetProperty("content").GetString() ?? string.Empty
            : string.Empty;

        if (userMessage.Contains("hard-failure", StringComparison.Ordinal))
        {
            return BuildErrorResponse(HttpStatusCode.Unauthorized, "{\"error\":\"invalid key\"}", "codex-analytics-auth-1");
        }

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
                            content = "analytics validation primary ok"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 11,
                    completion_tokens = 5,
                    total_tokens = 16
                }
            },
            "codex-analytics-success-1");
    }

    private static HttpResponseMessage HandleOpenRouterRequest(HttpRequestMessage request, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("input", out _))
        {
            return BuildJsonResponse(
                new
                {
                    id = "openrouter-analytics-embedding-1",
                    data = new[]
                    {
                        new
                        {
                            embedding = new[] { 0.11f, 0.22f, 0.33f }
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 32,
                        total_tokens = 32,
                        cost = 0.0048m
                    }
                },
                "openrouter-analytics-embedding-1");
        }

        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? string.Empty
            : string.Empty;
        var isProviderSpendScenario = model.Contains("provider-spend", StringComparison.Ordinal);

        object usage = isProviderSpendScenario
            ? new
            {
                prompt_tokens = 15,
                completion_tokens = 9,
                total_tokens = 24,
                cost = 0.0036m
            }
            : new
            {
                prompt_tokens = 14,
                completion_tokens = 6,
                total_tokens = 20
            };

        return BuildJsonResponse(
            new
            {
                id = "openrouter-analytics-chat-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = isProviderSpendScenario
                                ? "analytics provider spend ok"
                                : "analytics derived spend ok"
                        }
                    }
                },
                usage
            },
            "openrouter-analytics-chat-1");
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

    private sealed record MetricRequirement(string Name, params string[] RequiredTags);
}

public sealed class LlmGatewayAnalyticsValidationReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public List<LlmGatewayMetricCoverage> Metrics { get; set; } = new();
    public List<string> OpenRouterSpendSources { get; set; } = new();
    public List<string> RequestModalities { get; set; } = new();
    public List<string> RequestStatuses { get; set; } = new();
    public List<string> RequestRouteKinds { get; set; } = new();
    public bool AllChecksPassed { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public List<string> GrafanaQueryHints { get; set; } = new();
}

public sealed class LlmGatewayMetricCoverage
{
    public string Name { get; set; } = string.Empty;
    public bool Emitted { get; set; }
    public bool HasRequiredTags { get; set; }
    public List<string> MissingTags { get; set; } = new();
}

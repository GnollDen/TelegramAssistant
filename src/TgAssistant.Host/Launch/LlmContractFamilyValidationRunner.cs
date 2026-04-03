using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Host.Launch;

public static class LlmContractFamilyValidationRunner
{
    private const string FamilyId = "session_summary_v1";

    public static async Task<LlmContractFamilyValidationReport> RunAsync(string? outputPath = null, CancellationToken ct = default)
    {
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var gateway = new LlmGatewayService(
            providers:
            [
                new OpenRouterProviderClient(new HttpClient(new RecordingHttpMessageHandler(HandleOpenRouterRequest)), options)
            ],
            routingPolicy: new DefaultLlmRoutingPolicy(options),
            settings: options,
            logger: loggerFactory.CreateLogger<LlmGatewayService>(),
            metrics: new LlmGatewayMetrics());

        var normalizer = new OpenRouterContractNormalizer(
            gateway,
            new EditDiffContractSchemaProvider(),
            new EditDiffContractValidator(),
            loggerFactory.CreateLogger<OpenRouterContractNormalizer>());

        var resolvedOutputPath = ResolveOutputPath(outputPath);

        using var collector = new GatewayMetricCollector();
        collector.Start();

        var parity = await ValidateParityAsync(normalizer, ct);
        var schema = await ValidateSchemaAsync(normalizer, ct);
        var fallback = await ValidateFallbackAsync(normalizer, ct);
        var providerError = await ValidateProviderErrorAsync(normalizer, ct);

        var metricCoverage = BuildMetricCoverage(collector.GetSamples());

        var report = new LlmContractFamilyValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            ContractFamily = FamilyId,
            Parity = parity,
            Schema = schema,
            Fallback = fallback,
            ProviderError = providerError,
            Metrics = metricCoverage,
            GrafanaQueryHints =
            [
                "sum(rate(llm_gateway_requests_total{provider=\"openrouter\",route_key=~\"validation:session_summary_v1:.*\"}[5m])) by (model,status)",
                "histogram_quantile(0.95, sum(rate(llm_gateway_provider_latency_ms_bucket{provider=\"openrouter\",route_key=~\"validation:session_summary_v1:.*\"}[5m])) by (le,model,status))",
                "sum(rate(llm_gateway_tokens_total{provider=\"openrouter\",route_key=~\"validation:session_summary_v1:.*\"}[5m])) by (model,status)",
                "sum(rate(llm_gateway_tokens_per_second_sum{provider=\"openrouter\",route_key=~\"validation:session_summary_v1:.*\"}[5m])) / clamp_min(sum(rate(llm_gateway_tokens_per_second_count{provider=\"openrouter\",route_key=~\"validation:session_summary_v1:.*\"}[5m])), 1)",
                "sum(increase(llm_gateway_spend_usd_total{provider=\"openrouter\",route_key=~\"validation:session_summary_v1:.*\"}[1h])) by (model,spend_source,status)"
            ]
        };

        report.AllChecksPassed = report.Parity.Passed
            && report.Schema.Passed
            && report.Fallback.Passed
            && report.ProviderError.Passed
            && report.Metrics.All(metric => metric.Emitted && metric.HasRequiredTags);

        report.Recommendation = report.AllChecksPassed
            ? "continue_bounded_reasoning_plus_shaping_for_next_single_family"
            : "hold_and_repair_before_next_family";

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resolvedOutputPath, json, ct);

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException("Contract family validation failed: bounded SessionSummary v1 evidence is incomplete.");
        }

        return report;
    }

    private static async Task<ParityCheckResult> ValidateParityAsync(OpenRouterContractNormalizer normalizer, CancellationToken ct)
    {
        const string expectedSummary = "Обсудили встречу и согласовали звонок на завтра.";
        var result = await normalizer.NormalizeAsync(
            BuildRequest("CASE:parity", maxTokens: 300),
            ct);

        var actualSummary = TryReadSummary(result.NormalizedPayloadJson);
        var parityMatch = string.Equals(actualSummary, expectedSummary, StringComparison.Ordinal);
        return new ParityCheckResult
        {
            Passed = result.Status == LlmContractNormalizationStatus.Success && parityMatch,
            ExpectedSummary = expectedSummary,
            ActualSummary = actualSummary,
            Status = result.Status.ToString()
        };
    }

    private static async Task<SchemaCheckResult> ValidateSchemaAsync(OpenRouterContractNormalizer normalizer, CancellationToken ct)
    {
        var result = await normalizer.NormalizeAsync(
            BuildRequest("CASE:schema_invalid", maxTokens: 300),
            ct);

        var validationErrors = result.Diagnostics.ValidationErrors;
        var hasExpectedError = validationErrors.Contains("schema:summary_invalid", StringComparer.Ordinal);

        return new SchemaCheckResult
        {
            Passed = result.Status == LlmContractNormalizationStatus.SchemaInvalid && hasExpectedError,
            Status = result.Status.ToString(),
            ValidationErrors = validationErrors
        };
    }

    private static async Task<FallbackCheckResult> ValidateFallbackAsync(OpenRouterContractNormalizer normalizer, CancellationToken ct)
    {
        var result = await normalizer.NormalizeAsync(
            BuildRequest("CASE:fallback_recovery", maxTokens: 300),
            ct);

        var fallbackAttempted = result.Diagnostics.Attempts.Count >= 2;
        var fallbackSucceeded = result.Status == LlmContractNormalizationStatus.Success
            && result.ProviderMetadata?.FallbackAttempt == true
            && string.Equals(result.ProviderMetadata.Model, "openai/gpt-4.1-mini", StringComparison.Ordinal);

        return new FallbackCheckResult
        {
            Passed = fallbackAttempted && fallbackSucceeded,
            Attempts = result.Diagnostics.Attempts.Count,
            Status = result.Status.ToString(),
            WinningModel = result.ProviderMetadata?.Model ?? string.Empty,
            FallbackAttempted = fallbackAttempted
        };
    }

    private static async Task<ProviderErrorCheckResult> ValidateProviderErrorAsync(OpenRouterContractNormalizer normalizer, CancellationToken ct)
    {
        var result = await normalizer.NormalizeAsync(
            BuildRequest("CASE:provider_error", maxTokens: 300),
            ct);

        var hasProviderErrors = result.Diagnostics.Attempts.All(attempt => attempt.GatewayErrorCategory == LlmGatewayErrorCategory.TransientUpstream);

        return new ProviderErrorCheckResult
        {
            Passed = result.Status == LlmContractNormalizationStatus.ProviderError && hasProviderErrors,
            Status = result.Status.ToString(),
            Attempts = result.Diagnostics.Attempts.Count
        };
    }

    private static LlmContractNormalizationRequest BuildRequest(string caseTag, int maxTokens)
    {
        return new LlmContractNormalizationRequest
        {
            ContractKind = LlmContractKind.SessionSummaryV1,
            RawReasoningPayload = $"{caseTag}\nСырой вывод рассуждения для bounded validation.",
            TaskKey = "stage5_summary_contract_family_validation",
            Trace = new LlmTraceContext
            {
                PathKey = $"validation:session_summary_v1:{caseTag.ToLowerInvariant()}",
                RequestId = $"session-summary-validation-{Guid.NewGuid():N}",
                ScopeTags = ["gateway", "contract_family", "session_summary_v1", "validation"]
            },
            Limits = new LlmExecutionLimits
            {
                MaxTokens = maxTokens,
                Temperature = 0f,
                TimeoutMs = 5000
            }
        };
    }

    private static List<ContractFamilyMetricCoverage> BuildMetricCoverage(IReadOnlyList<MetricSample> samples)
    {
        var requirements = new[]
        {
            new MetricRequirement("llm_gateway_requests_total", "provider", "model", "modality", "status", "route_kind", "route_key"),
            new MetricRequirement("llm_gateway_tokens_total", "provider", "model", "modality", "status", "route_key"),
            new MetricRequirement("llm_gateway_provider_latency_ms", "provider", "model", "modality", "status", "route_key"),
            new MetricRequirement("llm_gateway_end_to_end_latency_ms", "provider", "model", "modality", "status", "route_key"),
            new MetricRequirement("llm_gateway_tokens_per_second", "provider", "model", "modality", "status", "route_key"),
            new MetricRequirement("llm_gateway_spend_usd_total", "provider", "model", "modality", "status", "spend_source", "route_key")
        };

        var coverage = new List<ContractFamilyMetricCoverage>(requirements.Length);
        foreach (var requirement in requirements)
        {
            var matches = samples
                .Where(sample => string.Equals(sample.Name, requirement.Name, StringComparison.Ordinal))
                .Where(sample => sample.Tags.TryGetValue("provider", out var provider)
                                 && string.Equals(provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal))
                .ToList();

            var missingTags = requirement.RequiredTags
                .Where(tag => !matches.Any(sample => sample.Tags.ContainsKey(tag)))
                .ToList();

            coverage.Add(new ContractFamilyMetricCoverage
            {
                Name = requirement.Name,
                Emitted = matches.Count > 0,
                HasRequiredTags = missingTags.Count == 0,
                MissingTags = missingTags
            });
        }

        return coverage;
    }

    private static string? TryReadSummary(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("summary", out var summaryElement)
                || summaryElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return summaryElement.GetString();
        }
        catch (JsonException)
        {
            return null;
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
                    PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
                    PrimaryModel = "openai/gpt-4.1-nano"
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-family-validation-key",
                    DefaultModel = "openai/gpt-4.1-nano",
                    ChatCompletionsPath = "/chat/completions",
                    PromptCostUsdPer1kTokens = 0.12m,
                    CompletionCostUsdPer1kTokens = 0.24m
                }
            }
        };
    }

    private static HttpResponseMessage HandleOpenRouterRequest(HttpRequestMessage request, string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var model = root.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString() ?? "unknown-model"
            : "unknown-model";

        var userMessage = string.Empty;
        if (root.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array
            && messages.GetArrayLength() >= 2)
        {
            userMessage = messages[1].GetProperty("content").GetString() ?? string.Empty;
        }

        var scenario = ResolveScenario(userMessage);
        if (string.Equals(scenario, "provider_error", StringComparison.Ordinal))
        {
            return BuildErrorResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"transient upstream\"}", Guid.NewGuid().ToString("N"));
        }

        if (string.Equals(scenario, "fallback_recovery", StringComparison.Ordinal)
            && string.Equals(model, "openai/gpt-4.1-nano", StringComparison.Ordinal))
        {
            return BuildJsonResponse(
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = "not-json"
                            }
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 12,
                        completion_tokens = 4,
                        total_tokens = 16
                    }
                },
                Guid.NewGuid().ToString("N"));
        }

        var summaryPayload = scenario switch
        {
            "parity" => "Обсудили встречу и согласовали звонок на завтра.",
            "schema_invalid" => string.Empty,
            "fallback_recovery" => "Согласовали следующую сессию и время созвона.",
            _ => "Тестовый summary."
        };

        return BuildJsonResponse(
            new
            {
                id = Guid.NewGuid().ToString("N"),
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = string.IsNullOrWhiteSpace(summaryPayload)
                                ? "{\"summary\":\"\"}"
                                : $"{{\"summary\":\"{summaryPayload}\"}}"
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
            Guid.NewGuid().ToString("N"));
    }

    private static string ResolveScenario(string userMessage)
    {
        if (userMessage.Contains("CASE:parity", StringComparison.OrdinalIgnoreCase))
        {
            return "parity";
        }

        if (userMessage.Contains("CASE:schema_invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "schema_invalid";
        }

        if (userMessage.Contains("CASE:fallback_recovery", StringComparison.OrdinalIgnoreCase))
        {
            return "fallback_recovery";
        }

        if (userMessage.Contains("CASE:provider_error", StringComparison.OrdinalIgnoreCase))
        {
            return "provider_error";
        }

        return "default";
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

    private static string ResolveOutputPath(string? outputPath)
    {
        var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var candidate = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(repositoryRoot, "artifacts", "llm-gateway", "session_summary_family_validation_report.json")
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

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags));
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

        private void Capture<T>(string name, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
        {
            var tagMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag.Key))
                {
                    continue;
                }

                tagMap[tag.Key] = tag.Value?.ToString() ?? string.Empty;
            }

            lock (_sync)
            {
                _samples.Add(new MetricSample
                {
                    Name = name,
                    Value = Convert.ToDouble(value),
                    Tags = tagMap
                });
            }
        }

        public void Dispose()
        {
            _listener?.Dispose();
        }
    }

    private sealed class MetricSample
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public Dictionary<string, string> Tags { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record MetricRequirement(string Name, params string[] RequiredTags);

    public sealed class LlmContractFamilyValidationReport
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public string ContractFamily { get; set; } = FamilyId;
        public ParityCheckResult Parity { get; set; } = new();
        public SchemaCheckResult Schema { get; set; } = new();
        public FallbackCheckResult Fallback { get; set; } = new();
        public ProviderErrorCheckResult ProviderError { get; set; } = new();
        public List<ContractFamilyMetricCoverage> Metrics { get; set; } = new();
        public List<string> GrafanaQueryHints { get; set; } = new();
        public bool AllChecksPassed { get; set; }
        public string Recommendation { get; set; } = "hold_and_repair_before_next_family";
    }

    public sealed class ParityCheckResult
    {
        public bool Passed { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ExpectedSummary { get; set; } = string.Empty;
        public string? ActualSummary { get; set; }
    }

    public sealed class SchemaCheckResult
    {
        public bool Passed { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<string> ValidationErrors { get; set; } = new();
    }

    public sealed class FallbackCheckResult
    {
        public bool Passed { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Attempts { get; set; }
        public bool FallbackAttempted { get; set; }
        public string WinningModel { get; set; } = string.Empty;
    }

    public sealed class ProviderErrorCheckResult
    {
        public bool Passed { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Attempts { get; set; }
    }

    public sealed class ContractFamilyMetricCoverage
    {
        public string Name { get; set; } = string.Empty;
        public bool Emitted { get; set; }
        public bool HasRequiredTags { get; set; }
        public List<string> MissingTags { get; set; } = new();
    }
}

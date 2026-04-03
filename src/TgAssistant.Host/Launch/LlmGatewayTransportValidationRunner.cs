using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Launch;

public static class LlmGatewayTransportValidationRunner
{
    private const string CodexApiKey = "codex-transport-key";
    private const string OpenRouterApiKey = "openrouter-transport-key";
    private const string PrimaryModel = "transport-primary-model";
    private const string FallbackModel = "transport-fallback-model";
    private const string OverrideModel = "transport-override-model";

    public static async Task<LlmGatewayTransportValidationReport> RunAsync(string? outputPath = null, CancellationToken ct = default)
    {
        var resolvedOutputPath = ResolveOutputPath(outputPath);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        using var loopbackServer = new LoopbackGatewayServer();
        await loopbackServer.StartAsync(ct);

        var settings = BuildSettings(loopbackServer.BaseUrl);
        var options = Options.Create(settings);
        var metrics = new LlmGatewayMetrics();
        using var metricCollector = new GatewayMetricCollector();
        metricCollector.Start();

        LlmGatewaySettingsValidator.ValidateOrThrow(settings);

        using var codexClient = new HttpClient();
        using var openRouterClient = new HttpClient();
        var gateway = new LlmGatewayService(
            providers:
            [
                new CodexLbChatProviderClient(codexClient, options),
                new OpenRouterProviderClient(openRouterClient, options)
            ],
            routingPolicy: new DefaultLlmRoutingPolicy(options),
            settings: options,
            logger: loggerFactory.CreateLogger<LlmGatewayService>(),
            metrics: metrics);

        var primarySuccess = await RunPrimarySuccessScenarioAsync(gateway, loopbackServer, ct);
        var retryableFallback = await RunRetryableFallbackScenarioAsync(gateway, loopbackServer, ct);
        var boundedRouteOverride = await RunBoundedRouteOverrideScenarioAsync(gateway, loopbackServer, ct);
        var governance = await RunRejectedRouteOverrideScenarioAsync(gateway, loopbackServer, ct);
        governance.BoundedRouteOverridePassed = boundedRouteOverride.Passed;
        governance.AllChecksPassed = governance.BoundedRouteOverridePassed && governance.OutOfBoundsRouteOverrideBlocked && governance.UnexpectedNetworkCallsDuringRejectedOverride == 0;

        var readiness = ValidateReadiness(settings);
        var observability = BuildObservability(metricCollector.GetSamples());

        var report = new LlmGatewayTransportValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            OutputPath = resolvedOutputPath,
            LoopbackBaseUrl = loopbackServer.BaseUrl,
            PrimarySuccess = primarySuccess,
            RetryableFallback = retryableFallback,
            BoundedRouteOverride = boundedRouteOverride,
            Governance = governance,
            Readiness = readiness,
            Observability = observability,
            TransportRequests = loopbackServer.GetRequests()
        };

        report.AllChecksPassed = primarySuccess.Passed
            && retryableFallback.Passed
            && boundedRouteOverride.Passed
            && governance.AllChecksPassed
            && readiness.AllChecksPassed
            && observability.AllChecksPassed;
        report.Recommendation = report.AllChecksPassed
            ? "continue_bounded_selective_rollout_under_existing_provider_policy_boundaries"
            : "hold_selective_rollout_until_transport_validation_gaps_are_closed";
        report.ContinueHoldRecommendationText = report.AllChecksPassed
            ? "Continue bounded selective rollout under the existing provider policy boundaries only; do not open broad rollout."
            : "Hold further selective rollout until the transport-backed validation gaps are closed; do not open broad rollout.";

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resolvedOutputPath, json, ct);

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException("LLM gateway transport validation failed: bounded transport-backed evidence is incomplete.");
        }

        return report;
    }

    private static async Task<LlmGatewayTransportScenarioResult> RunPrimarySuccessScenarioAsync(
        LlmGatewayService gateway,
        LoopbackGatewayServer loopbackServer,
        CancellationToken ct)
    {
        var beforeCount = loopbackServer.RequestCount;
        var response = await gateway.ExecuteAsync(BuildTextRequest("primary"), ct);
        var observed = loopbackServer.GetRequestsSince(beforeCount);

        var passed = string.Equals(response.Provider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal)
            && string.Equals(response.Model, PrimaryModel, StringComparison.Ordinal)
            && string.Equals(response.RequestId, "codex-transport-primary-1", StringComparison.Ordinal)
            && string.Equals(response.Output.Text, "transport primary ok", StringComparison.Ordinal)
            && !response.FallbackApplied
            && observed.Count == 1
            && observed.All(request => string.Equals(request.Provider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal))
            && observed.All(request => request.AuthorizationHeaderPresent)
            && observed.All(request => string.Equals(request.Model, PrimaryModel, StringComparison.Ordinal));

        return new LlmGatewayTransportScenarioResult
        {
            Scenario = "primary_success",
            Passed = passed,
            Provider = response.Provider,
            Model = response.Model,
            RequestId = response.RequestId,
            FallbackApplied = response.FallbackApplied,
            FallbackFromProvider = response.FallbackFromProvider,
            ObservedRequestCount = observed.Count,
            ObservedProviders = observed.Select(request => request.Provider).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList(),
            ObservedPaths = observed.Select(request => request.Path).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList()
        };
    }

    private static async Task<LlmGatewayTransportScenarioResult> RunRetryableFallbackScenarioAsync(
        LlmGatewayService gateway,
        LoopbackGatewayServer loopbackServer,
        CancellationToken ct)
    {
        var beforeCount = loopbackServer.RequestCount;
        var response = await gateway.ExecuteAsync(BuildTextRequest("fallback"), ct);
        var observed = loopbackServer.GetRequestsSince(beforeCount);

        var passed = string.Equals(response.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
            && string.Equals(response.Model, FallbackModel, StringComparison.Ordinal)
            && string.Equals(response.RequestId, "openrouter-transport-fallback-1", StringComparison.Ordinal)
            && string.Equals(response.Output.Text, "transport fallback ok", StringComparison.Ordinal)
            && response.FallbackApplied
            && string.Equals(response.FallbackFromProvider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal)
            && observed.Count == 2
            && observed.Any(request => string.Equals(request.Provider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal) && request.StatusCode == (int)HttpStatusCode.ServiceUnavailable)
            && observed.Any(request => string.Equals(request.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal) && request.StatusCode == (int)HttpStatusCode.OK)
            && observed.All(request => request.AuthorizationHeaderPresent);

        return new LlmGatewayTransportScenarioResult
        {
            Scenario = "retryable_fallback",
            Passed = passed,
            Provider = response.Provider,
            Model = response.Model,
            RequestId = response.RequestId,
            FallbackApplied = response.FallbackApplied,
            FallbackFromProvider = response.FallbackFromProvider,
            ObservedRequestCount = observed.Count,
            ObservedProviders = observed.Select(request => request.Provider).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList(),
            ObservedPaths = observed.Select(request => request.Path).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList()
        };
    }

    private static async Task<LlmGatewayTransportScenarioResult> RunBoundedRouteOverrideScenarioAsync(
        LlmGatewayService gateway,
        LoopbackGatewayServer loopbackServer,
        CancellationToken ct)
    {
        var request = BuildTextRequest("bounded_override");
        request.RouteOverride = new LlmGatewayRouteOverride
        {
            PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
            ProviderModelHints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [OpenRouterProviderClient.ProviderIdValue] = OverrideModel
            }
        };

        var beforeCount = loopbackServer.RequestCount;
        var response = await gateway.ExecuteAsync(request, ct);
        var observed = loopbackServer.GetRequestsSince(beforeCount);

        var passed = string.Equals(response.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
            && string.Equals(response.Model, OverrideModel, StringComparison.Ordinal)
            && string.Equals(response.RequestId, "openrouter-transport-override-1", StringComparison.Ordinal)
            && string.Equals(response.Output.Text, "transport override ok", StringComparison.Ordinal)
            && !response.FallbackApplied
            && observed.Count == 1
            && observed.All(observation => string.Equals(observation.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal))
            && observed.All(observation => observation.AuthorizationHeaderPresent)
            && observed.All(observation => string.Equals(observation.Model, OverrideModel, StringComparison.Ordinal));

        return new LlmGatewayTransportScenarioResult
        {
            Scenario = "bounded_route_override",
            Passed = passed,
            Provider = response.Provider,
            Model = response.Model,
            RequestId = response.RequestId,
            FallbackApplied = response.FallbackApplied,
            FallbackFromProvider = response.FallbackFromProvider,
            ObservedRequestCount = observed.Count,
            ObservedProviders = observed.Select(requestObservation => requestObservation.Provider).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList(),
            ObservedPaths = observed.Select(requestObservation => requestObservation.Path).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList()
        };
    }

    private static async Task<LlmGatewayTransportGovernanceResult> RunRejectedRouteOverrideScenarioAsync(
        LlmGatewayService gateway,
        LoopbackGatewayServer loopbackServer,
        CancellationToken ct)
    {
        var request = BuildTextRequest("out_of_bounds_override");
        request.RouteOverride = new LlmGatewayRouteOverride
        {
            PrimaryProvider = "spare-provider"
        };

        var beforeCount = loopbackServer.RequestCount;
        string validationMessage;
        try
        {
            await gateway.ExecuteAsync(request, ct);
            throw new InvalidOperationException("LLM gateway transport validation failed: out-of-bounds route override unexpectedly succeeded.");
        }
        catch (LlmGatewayException ex) when (ex.Category == LlmGatewayErrorCategory.Validation)
        {
            validationMessage = ex.Message;
        }

        var rejectedNetworkCalls = loopbackServer.RequestCount - beforeCount;
        return new LlmGatewayTransportGovernanceResult
        {
            OutOfBoundsRouteOverrideBlocked = validationMessage.Contains("outside configured provider bounds", StringComparison.OrdinalIgnoreCase),
            UnexpectedNetworkCallsDuringRejectedOverride = Math.Max(0, rejectedNetworkCalls),
            ValidationMessage = validationMessage
        };
    }

    private static LlmGatewayTransportReadinessResult ValidateReadiness(LlmGatewaySettings validSettings)
    {
        var validConfigurationAccepted = true;
        try
        {
            LlmGatewaySettingsValidator.ValidateOrThrow(validSettings);
        }
        catch
        {
            validConfigurationAccepted = false;
        }

        var invalid = new LlmGatewaySettings
        {
            Enabled = true,
            Routing = new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["text_chat"] = new()
                {
                    PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
                    TimeoutBudgetClass = string.Empty,
                    RetryPolicyClass = "transport_validation"
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://127.0.0.1:1/openrouter",
                    ApiKey = string.Empty,
                    TimeoutSeconds = 0,
                    ChatCompletionsPath = string.Empty
                }
            }
        };

        var invalidConfigurationRejected = false;
        var diagnostics = new List<string>();
        try
        {
            LlmGatewaySettingsValidator.ValidateOrThrow(invalid);
        }
        catch (InvalidOperationException ex)
        {
            invalidConfigurationRejected = true;
            diagnostics = ex.Message
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(message => message.Contains("TimeoutBudgetClass", StringComparison.Ordinal)
                    || message.Contains("ApiKey", StringComparison.Ordinal)
                    || message.Contains("TimeoutSeconds", StringComparison.Ordinal)
                    || message.Contains("ChatCompletionsPath", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(message => message, StringComparer.Ordinal)
                .ToList();
        }

        return new LlmGatewayTransportReadinessResult
        {
            ValidConfigurationAccepted = validConfigurationAccepted,
            InvalidConfigurationRejected = invalidConfigurationRejected,
            InvalidDiagnostics = diagnostics,
            AllChecksPassed = validConfigurationAccepted
                && invalidConfigurationRejected
                && diagnostics.Any(message => message.Contains("TimeoutBudgetClass", StringComparison.Ordinal))
                && diagnostics.Any(message => message.Contains("ApiKey", StringComparison.Ordinal))
                && diagnostics.Any(message => message.Contains("TimeoutSeconds", StringComparison.Ordinal))
                && diagnostics.Any(message => message.Contains("ChatCompletionsPath", StringComparison.Ordinal))
        };
    }

    private static LlmGatewayTransportObservabilityResult BuildObservability(IReadOnlyList<MetricSample> samples)
    {
        var metrics = BuildMetricCoverage(samples);
        var requestStatuses = samples
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

        var result = new LlmGatewayTransportObservabilityResult
        {
            Metrics = metrics,
            RequestStatuses = requestStatuses,
            RequestRouteKinds = routeKinds,
            PrimaryRetryableFailureVisible = HasRequestSample(
                samples,
                provider: CodexLbChatProviderClient.ProviderIdValue,
                status: "error",
                routeKind: "primary"),
            FallbackSuccessVisible = HasRequestSample(
                samples,
                provider: OpenRouterProviderClient.ProviderIdValue,
                status: "success",
                routeKind: "fallback"),
            FallbackTransitionVisible = HasFallbackSample(
                samples,
                fromProvider: CodexLbChatProviderClient.ProviderIdValue,
                toProvider: OpenRouterProviderClient.ProviderIdValue,
                reason: LlmGatewayErrorCategory.TransientUpstream.ToString())
        };

        result.AllChecksPassed = result.Metrics.All(metric => metric.Emitted && metric.HasRequiredTags)
            && result.RequestStatuses.Contains("success", StringComparer.Ordinal)
            && result.RequestStatuses.Contains("error", StringComparer.Ordinal)
            && result.RequestRouteKinds.Contains("primary", StringComparer.Ordinal)
            && result.RequestRouteKinds.Contains("fallback", StringComparer.Ordinal)
            && result.PrimaryRetryableFailureVisible
            && result.FallbackSuccessVisible
            && result.FallbackTransitionVisible;
        return result;
    }

    private static List<LlmGatewayMetricCoverage> BuildMetricCoverage(IReadOnlyList<MetricSample> samples)
    {
        var requirements = new[]
        {
            new MetricRequirement("llm_gateway_requests_total", "provider", "model", "modality", "status", "route_kind"),
            new MetricRequirement("llm_gateway_fallback_total", "from_provider", "to_provider", "reason", "modality"),
            new MetricRequirement("llm_gateway_provider_latency_ms", "provider", "model", "modality", "status"),
            new MetricRequirement("llm_gateway_end_to_end_latency_ms", "provider", "model", "modality", "status")
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

    private static bool HasFallbackSample(
        IReadOnlyList<MetricSample> samples,
        string fromProvider,
        string toProvider,
        string reason)
    {
        return samples.Any(sample =>
            string.Equals(sample.Name, "llm_gateway_fallback_total", StringComparison.Ordinal)
            && sample.Tags.TryGetValue("from_provider", out var taggedFromProvider)
            && string.Equals(taggedFromProvider, fromProvider, StringComparison.Ordinal)
            && sample.Tags.TryGetValue("to_provider", out var taggedToProvider)
            && string.Equals(taggedToProvider, toProvider, StringComparison.Ordinal)
            && sample.Tags.TryGetValue("reason", out var taggedReason)
            && string.Equals(taggedReason, reason, StringComparison.Ordinal));
    }

    private static LlmGatewayRequest BuildTextRequest(string scenario)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = $"gateway_transport_validation_{scenario}",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = $"validation:transport:{scenario}",
                RequestId = $"gateway-transport-{scenario}-request"
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, "You are a precise transport validation test."),
                LlmGatewayMessage.FromText(LlmMessageRole.User, $"Reply with deterministic {scenario} output.")
            ]
        };
    }

    private static LlmGatewaySettings BuildSettings(string baseUrl)
    {
        return new LlmGatewaySettings
        {
            Enabled = true,
            Routing = new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["text_chat"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = PrimaryModel,
                    RetryPolicyClass = "transport_validation",
                    TimeoutBudgetClass = "transport_validation",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = FallbackModel
                        }
                    ]
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = $"{baseUrl}codex",
                    ApiKey = CodexApiKey,
                    DefaultModel = PrimaryModel,
                    ChatCompletionsPath = "/v1/chat/completions",
                    TimeoutSeconds = 5
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = $"{baseUrl}openrouter/api/v1",
                    ApiKey = OpenRouterApiKey,
                    DefaultModel = FallbackModel,
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings",
                    TimeoutSeconds = 5
                }
            }
        };
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var candidate = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(repositoryRoot, "artifacts", "llm-gateway", "transport_validation_report.json")
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

    private sealed class LoopbackGatewayServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly ConcurrentQueue<LlmGatewayTransportRequestRecord> _requests = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly List<Task> _connectionTasks = new();
        private Task? _acceptLoopTask;
        private int _requestSequence;

        public string BaseUrl { get; private set; } = string.Empty;

        public int RequestCount => _requests.Count;

        public async Task StartAsync(CancellationToken ct)
        {
            _listener.Start();
            BaseUrl = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _acceptLoopTask = Task.Run(() => AcceptLoopAsync(CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token).Token), CancellationToken.None);
            await Task.Yield();
        }

        public List<LlmGatewayTransportRequestRecord> GetRequests()
        {
            return _requests.OrderBy(record => record.Sequence).ToList();
        }

        public List<LlmGatewayTransportRequestRecord> GetRequestsSince(int count)
        {
            return _requests
                .OrderBy(record => record.Sequence)
                .Skip(Math.Max(0, count))
                .ToList();
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();

            try
            {
                _acceptLoopTask?.GetAwaiter().GetResult();
            }
            catch
            {
            }

            foreach (var task in _connectionTasks.ToArray())
            {
                try
                {
                    task.GetAwaiter().GetResult();
                }
                catch
                {
                }
            }

            _cts.Dispose();
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }

                var task = HandleClientAsync(client, ct);
                lock (_connectionTasks)
                {
                    _connectionTasks.Add(task);
                }

                _ = task.ContinueWith(
                    completed =>
                    {
                        lock (_connectionTasks)
                        {
                            _connectionTasks.Remove(completed);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                using var stream = client.GetStream();
                try
                {
                    var request = await ReadRequestAsync(stream, ct);
                    var response = BuildResponse(request);
                    _requests.Enqueue(response.Record);
                    await WriteResponseAsync(stream, response, ct);
                }
                catch (Exception ex)
                {
                    var failure = new LoopbackHttpResponse
                    {
                        StatusCode = HttpStatusCode.InternalServerError,
                        Body = JsonSerializer.Serialize(new { error = ex.Message }),
                        Record = new LlmGatewayTransportRequestRecord
                        {
                            Sequence = Interlocked.Increment(ref _requestSequence),
                            Provider = "loopback_server",
                            Path = "unparsed",
                            StatusCode = (int)HttpStatusCode.InternalServerError,
                            Error = ex.Message
                        }
                    };
                    _requests.Enqueue(failure.Record);
                    await WriteResponseAsync(stream, failure, ct);
                }
            }
        }

        private LoopbackHttpResponse BuildResponse(LoopbackHttpRequest request)
        {
            if (string.Equals(request.Path, "/codex/v1/chat/completions", StringComparison.Ordinal))
            {
                return BuildCodexResponse(request);
            }

            if (string.Equals(request.Path, "/openrouter/api/v1/chat/completions", StringComparison.Ordinal))
            {
                return BuildOpenRouterChatResponse(request);
            }

            return new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.NotFound,
                Body = "{\"error\":\"unknown path\"}",
                Record = BuildRecord(
                    provider: "unknown",
                    path: request.Path,
                    model: string.Empty,
                    authorizationHeaderPresent: request.Headers.ContainsKey("authorization"),
                    userMessage: string.Empty,
                    statusCode: HttpStatusCode.NotFound)
            };
        }

        private LoopbackHttpResponse BuildCodexResponse(LoopbackHttpRequest request)
        {
            using var document = JsonDocument.Parse(request.Body);
            var root = document.RootElement;
            var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? string.Empty : string.Empty;
            var userMessage = ReadUserMessage(root);
            var authHeader = request.Headers.TryGetValue("authorization", out var authorization) ? authorization : string.Empty;
            if (!string.Equals(authHeader, $"Bearer {CodexApiKey}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected codex authorization header. actual={authHeader}");
            }

            if (!string.Equals(model, PrimaryModel, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected primary model '{PrimaryModel}', got '{model}'.");
            }

            var statusCode = userMessage.Contains("fallback", StringComparison.Ordinal)
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK;
            var body = statusCode == HttpStatusCode.OK
                ? JsonSerializer.Serialize(new
                {
                    id = "codex-transport-primary-1",
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = "transport primary ok"
                            }
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 12,
                        completion_tokens = 6,
                        total_tokens = 18
                    }
                })
                : "{\"error\":\"codex degraded\"}";

            return new LoopbackHttpResponse
            {
                StatusCode = statusCode,
                Body = body,
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-request-id"] = statusCode == HttpStatusCode.OK
                        ? "codex-transport-primary-1"
                        : "codex-transport-fallback-1"
                },
                Record = BuildRecord(
                    provider: CodexLbChatProviderClient.ProviderIdValue,
                    path: request.Path,
                    model: model,
                    authorizationHeaderPresent: true,
                    userMessage: userMessage,
                    statusCode: statusCode)
            };
        }

        private LoopbackHttpResponse BuildOpenRouterChatResponse(LoopbackHttpRequest request)
        {
            using var document = JsonDocument.Parse(request.Body);
            var root = document.RootElement;
            var model = root.TryGetProperty("model", out var modelElement) ? modelElement.GetString() ?? string.Empty : string.Empty;
            var userMessage = ReadUserMessage(root);
            var authHeader = request.Headers.TryGetValue("authorization", out var authorization) ? authorization : string.Empty;
            if (!string.Equals(authHeader, $"Bearer {OpenRouterApiKey}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected openrouter authorization header. actual={authHeader}");
            }

            var responseText = string.Equals(model, OverrideModel, StringComparison.Ordinal)
                ? "transport override ok"
                : "transport fallback ok";
            var requestId = string.Equals(model, OverrideModel, StringComparison.Ordinal)
                ? "openrouter-transport-override-1"
                : "openrouter-transport-fallback-1";

            return new LoopbackHttpResponse
            {
                StatusCode = HttpStatusCode.OK,
                Body = JsonSerializer.Serialize(new
                {
                    id = requestId,
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = responseText
                            }
                        }
                    },
                    usage = new
                    {
                        prompt_tokens = 14,
                        completion_tokens = 7,
                        total_tokens = 21,
                        cost = 0.0021m
                    }
                }),
                Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x-request-id"] = requestId
                },
                Record = BuildRecord(
                    provider: OpenRouterProviderClient.ProviderIdValue,
                    path: request.Path,
                    model: model,
                    authorizationHeaderPresent: true,
                    userMessage: userMessage,
                    statusCode: HttpStatusCode.OK)
            };
        }

        private LlmGatewayTransportRequestRecord BuildRecord(
            string provider,
            string path,
            string model,
            bool authorizationHeaderPresent,
            string userMessage,
            HttpStatusCode statusCode)
        {
            return new LlmGatewayTransportRequestRecord
            {
                Sequence = Interlocked.Increment(ref _requestSequence),
                Provider = provider,
                Path = path,
                Model = model,
                AuthorizationHeaderPresent = authorizationHeaderPresent,
                UserMessage = userMessage,
                StatusCode = (int)statusCode
            };
        }

        private static string ReadUserMessage(JsonElement root)
        {
            if (!root.TryGetProperty("messages", out var messages)
                || messages.ValueKind != JsonValueKind.Array
                || messages.GetArrayLength() < 2)
            {
                throw new InvalidOperationException("Expected serialized text messages.");
            }

            var content = messages[1].TryGetProperty("content", out var contentElement)
                ? contentElement
                : default;
            return content.ValueKind switch
            {
                JsonValueKind.String => content.GetString() ?? string.Empty,
                _ => content.ToString()
            };
        }

        private static async Task<LoopbackHttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            var buffer = new byte[4096];
            using var headerBuffer = new MemoryStream();
            var headerEnd = -1;

            while (headerEnd < 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0)
                {
                    throw new InvalidOperationException("Loopback server received an empty request.");
                }

                headerBuffer.Write(buffer, 0, read);
                headerEnd = FindHeaderTerminator(headerBuffer.GetBuffer(), (int)headerBuffer.Length);
            }

            var combined = headerBuffer.ToArray();
            var headerText = Encoding.ASCII.GetString(combined, 0, headerEnd);
            var lines = headerText.Split("\r\n", StringSplitOptions.None);
            if (lines.Length == 0)
            {
                throw new InvalidOperationException("Loopback server received a malformed request line.");
            }

            var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLine.Length < 2)
            {
                throw new InvalidOperationException($"Loopback server received malformed request line '{lines[0]}'.");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                headers[key] = value;
            }

            headers.TryGetValue("Content-Length", out var contentLengthValue);
            var contentLength = int.TryParse(contentLengthValue, out var parsedContentLength) ? parsedContentLength : 0;
            var bodyStart = headerEnd + 4;
            var bodyBytes = new byte[contentLength];
            var initialBodyLength = Math.Min(Math.Max(0, combined.Length - bodyStart), contentLength);
            if (initialBodyLength > 0)
            {
                Array.Copy(combined, bodyStart, bodyBytes, 0, initialBodyLength);
            }

            var remaining = contentLength - initialBodyLength;
            var offset = initialBodyLength;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(bodyBytes.AsMemory(offset, remaining), ct);
                if (read == 0)
                {
                    throw new InvalidOperationException("Loopback server request body terminated early.");
                }

                offset += read;
                remaining -= read;
            }

            return new LoopbackHttpRequest
            {
                Method = requestLine[0],
                Path = requestLine[1],
                Headers = headers,
                Body = Encoding.UTF8.GetString(bodyBytes)
            };
        }

        private static async Task WriteResponseAsync(NetworkStream stream, LoopbackHttpResponse response, CancellationToken ct)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
            var builder = new StringBuilder();
            builder.Append("HTTP/1.1 ")
                .Append((int)response.StatusCode)
                .Append(' ')
                .Append(GetReasonPhrase(response.StatusCode))
                .Append("\r\n")
                .Append("Content-Type: application/json\r\n")
                .Append("Content-Length: ")
                .Append(bodyBytes.Length)
                .Append("\r\n")
                .Append("Connection: close\r\n");

            foreach (var header in response.Headers)
            {
                builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
            }

            builder.Append("\r\n");
            var headerBytes = Encoding.ASCII.GetBytes(builder.ToString());
            await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), ct);
            await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), ct);
            await stream.FlushAsync(ct);
        }

        private static int FindHeaderTerminator(byte[] buffer, int length)
        {
            for (var index = 0; index <= length - 4; index++)
            {
                if (buffer[index] == '\r'
                    && buffer[index + 1] == '\n'
                    && buffer[index + 2] == '\r'
                    && buffer[index + 3] == '\n')
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetReasonPhrase(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.OK => "OK",
                HttpStatusCode.NotFound => "Not Found",
                HttpStatusCode.ServiceUnavailable => "Service Unavailable",
                HttpStatusCode.InternalServerError => "Internal Server Error",
                _ => "HTTP Response"
            };
        }
    }

    private sealed class LoopbackHttpRequest
    {
        public string Method { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body { get; init; } = string.Empty;
    }

    private sealed class LoopbackHttpResponse
    {
        public HttpStatusCode StatusCode { get; init; }
        public string Body { get; init; } = string.Empty;
        public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public LlmGatewayTransportRequestRecord Record { get; init; } = new();
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

public sealed class LlmGatewayTransportValidationReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string LoopbackBaseUrl { get; set; } = string.Empty;
    public LlmGatewayTransportScenarioResult PrimarySuccess { get; set; } = new();
    public LlmGatewayTransportScenarioResult RetryableFallback { get; set; } = new();
    public LlmGatewayTransportScenarioResult BoundedRouteOverride { get; set; } = new();
    public LlmGatewayTransportGovernanceResult Governance { get; set; } = new();
    public LlmGatewayTransportReadinessResult Readiness { get; set; } = new();
    public LlmGatewayTransportObservabilityResult Observability { get; set; } = new();
    public List<LlmGatewayTransportRequestRecord> TransportRequests { get; set; } = new();
    public bool AllChecksPassed { get; set; }
    public string Recommendation { get; set; } = string.Empty;
    public string ContinueHoldRecommendationText { get; set; } = string.Empty;
}

public sealed class LlmGatewayTransportScenarioResult
{
    public string Scenario { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public bool FallbackApplied { get; set; }
    public string? FallbackFromProvider { get; set; }
    public int ObservedRequestCount { get; set; }
    public List<string> ObservedProviders { get; set; } = new();
    public List<string> ObservedPaths { get; set; } = new();
}

public sealed class LlmGatewayTransportGovernanceResult
{
    public bool BoundedRouteOverridePassed { get; set; }
    public bool OutOfBoundsRouteOverrideBlocked { get; set; }
    public int UnexpectedNetworkCallsDuringRejectedOverride { get; set; }
    public string ValidationMessage { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
}

public sealed class LlmGatewayTransportReadinessResult
{
    public bool ValidConfigurationAccepted { get; set; }
    public bool InvalidConfigurationRejected { get; set; }
    public List<string> InvalidDiagnostics { get; set; } = new();
    public bool AllChecksPassed { get; set; }
}

public sealed class LlmGatewayTransportObservabilityResult
{
    public List<LlmGatewayMetricCoverage> Metrics { get; set; } = new();
    public List<string> RequestStatuses { get; set; } = new();
    public List<string> RequestRouteKinds { get; set; } = new();
    public bool PrimaryRetryableFailureVisible { get; set; }
    public bool FallbackSuccessVisible { get; set; }
    public bool FallbackTransitionVisible { get; set; }
    public bool AllChecksPassed { get; set; }
}

public sealed class LlmGatewayTransportRequestRecord
{
    public int Sequence { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool AuthorizationHeaderPresent { get; set; }
    public string UserMessage { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? Error { get; set; }
}

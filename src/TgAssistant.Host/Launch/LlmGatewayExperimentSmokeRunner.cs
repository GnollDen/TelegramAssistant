using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Launch;

public static class LlmGatewayExperimentSmokeRunner
{
    private const string ExperimentLabel = "gateway_text_replay_ab";

    public static async Task RunAsync(CancellationToken ct = default)
    {
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var routingPolicy = new DefaultLlmRoutingPolicy(options);
        var codexHandler = new RecordingHttpMessageHandler((request, body) => HandleCodexRequest(request, body, "baseline-text-model"));
        var openRouterHandler = new RecordingHttpMessageHandler((request, body) => HandleOpenRouterRequest(request, body, "candidate-text-model"));
        var gateway = new LlmGatewayService(
            providers:
            [
                new CodexLbChatProviderClient(new HttpClient(codexHandler), options),
                new OpenRouterProviderClient(new HttpClient(openRouterHandler), options)
            ],
            routingPolicy,
            options,
            loggerFactory.CreateLogger<LlmGatewayService>());

        var baselineKey = FindStickyKeyForBranch(routingPolicy, "baseline");
        var candidateKey = FindStickyKeyForBranch(routingPolicy, "candidate");

        var baselineFirst = await gateway.ExecuteAsync(BuildTextRequest(baselineKey, "experiment-baseline-1"), ct);
        var baselineSecond = await gateway.ExecuteAsync(BuildTextRequest(baselineKey, "experiment-baseline-2"), ct);
        var candidate = await gateway.ExecuteAsync(BuildTextRequest(candidateKey, "experiment-candidate-1"), ct);

        AssertExperimentResponse(
            baselineFirst,
            expectedBranch: "baseline",
            expectedStickyKey: baselineKey,
            expectedProvider: CodexLbChatProviderClient.ProviderIdValue,
            expectedModel: "baseline-text-model");
        AssertExperimentResponse(
            baselineSecond,
            expectedBranch: "baseline",
            expectedStickyKey: baselineKey,
            expectedProvider: CodexLbChatProviderClient.ProviderIdValue,
            expectedModel: "baseline-text-model");
        AssertExperimentResponse(
            candidate,
            expectedBranch: "candidate",
            expectedStickyKey: candidateKey,
            expectedProvider: OpenRouterProviderClient.ProviderIdValue,
            expectedModel: "candidate-text-model");

        if (codexHandler.RequestCount != 2)
        {
            throw new InvalidOperationException($"LLM gateway experiment smoke failed: expected 2 codex-lb requests, saw {codexHandler.RequestCount}.");
        }

        if (openRouterHandler.RequestCount != 1)
        {
            throw new InvalidOperationException($"LLM gateway experiment smoke failed: expected 1 openrouter request, saw {openRouterHandler.RequestCount}.");
        }
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
                    PrimaryModel = "default-text-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "default-fallback-model"
                        }
                    ]
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://codex-lb.local",
                    ApiKey = "codex-experiment-key",
                    DefaultModel = "codex-default-experiment",
                    ChatCompletionsPath = "/v1/chat/completions"
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-experiment-key",
                    DefaultModel = "openrouter-default-experiment",
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings"
                }
            },
            Experiments = new Dictionary<string, LlmGatewayExperimentSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [ExperimentLabel] = new()
                {
                    Enabled = true,
                    Branches =
                    [
                        new LlmGatewayExperimentBranchSettings
                        {
                            Branch = "baseline",
                            WeightPercent = 50,
                            Provider = CodexLbChatProviderClient.ProviderIdValue,
                            Model = "baseline-text-model",
                            FallbackProviders =
                            [
                                new LlmGatewayProviderTargetSettings
                                {
                                    Provider = OpenRouterProviderClient.ProviderIdValue,
                                    Model = "baseline-fallback-model"
                                }
                            ]
                        },
                        new LlmGatewayExperimentBranchSettings
                        {
                            Branch = "candidate",
                            WeightPercent = 50,
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "candidate-text-model"
                        }
                    ]
                }
            }
        };
    }

    private static LlmGatewayRequest BuildTextRequest(string stickyKey, string requestId)
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = "gateway_experiment_smoke_text",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "smoke:experiment:text",
                RequestId = requestId
            },
            Experiment = new LlmGatewayExperimentContext
            {
                Label = ExperimentLabel,
                StickyRoutingKey = stickyKey
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, "You are a precise experiment smoke test."),
                LlmGatewayMessage.FromText(LlmMessageRole.User, "Reply with deterministic experiment output.")
            ]
        };
    }

    private static string FindStickyKeyForBranch(DefaultLlmRoutingPolicy routingPolicy, string expectedBranch)
    {
        for (var index = 0; index < 512; index++)
        {
            var stickyKey = $"experiment-sticky-{index}";
            var decision = routingPolicy.Resolve(BuildTextRequest(stickyKey, $"route-scan-{index}"));
            if (string.Equals(decision.Experiment?.Branch, expectedBranch, StringComparison.Ordinal))
            {
                return stickyKey;
            }
        }

        throw new InvalidOperationException($"LLM gateway experiment smoke failed: unable to find sticky routing key for branch '{expectedBranch}'.");
    }

    private static void AssertExperimentResponse(
        LlmGatewayResponse response,
        string expectedBranch,
        string expectedStickyKey,
        string expectedProvider,
        string expectedModel)
    {
        if (!string.Equals(response.Provider, expectedProvider, StringComparison.Ordinal)
            || !string.Equals(response.Model, expectedModel, StringComparison.Ordinal)
            || response.Experiment is null
            || !string.Equals(response.Experiment.Label, ExperimentLabel, StringComparison.Ordinal)
            || !string.Equals(response.Experiment.Branch, expectedBranch, StringComparison.Ordinal)
            || !string.Equals(response.Experiment.StickyRoutingKey, expectedStickyKey, StringComparison.Ordinal)
            || !string.Equals(response.Experiment.SelectedProvider, expectedProvider, StringComparison.Ordinal)
            || !string.Equals(response.Experiment.SelectedModel, expectedModel, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(response.RequestId)
            || response.Usage.TotalTokens is null
            || response.FallbackApplied
            || string.IsNullOrWhiteSpace(response.RawProviderPayloadJson))
        {
            throw new InvalidOperationException("LLM gateway experiment smoke failed: response is missing normalized experiment audit metadata.");
        }
    }

    private static HttpResponseMessage HandleCodexRequest(HttpRequestMessage request, string body, string expectedModel)
    {
        AssertTextRequest(
            request,
            body,
            expectedPath: "/v1/chat/completions",
            expectedApiKey: "codex-experiment-key",
            expectedModel);

        return BuildJsonResponse(
            new
            {
                id = "codex-experiment-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "baseline branch ok"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 11,
                    completion_tokens = 4,
                    total_tokens = 15,
                    cost = 0.0015m
                }
            },
            "codex-experiment-1");
    }

    private static HttpResponseMessage HandleOpenRouterRequest(HttpRequestMessage request, string body, string expectedModel)
    {
        AssertTextRequest(
            request,
            body,
            expectedPath: "/api/v1/chat/completions",
            expectedApiKey: "openrouter-experiment-key",
            expectedModel);

        return BuildJsonResponse(
            new
            {
                id = "openrouter-experiment-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "candidate branch ok"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 13,
                    completion_tokens = 5,
                    total_tokens = 18,
                    cost = 0.0018m
                }
            },
            "openrouter-experiment-1");
    }

    private static void AssertTextRequest(
        HttpRequestMessage request,
        string body,
        string expectedPath,
        string expectedApiKey,
        string expectedModel)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("LLM gateway experiment smoke failed: provider request did not use POST.");
        }

        if (!string.Equals(request.RequestUri?.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway experiment smoke failed: unexpected provider path '{request.RequestUri?.AbsolutePath}'.");
        }

        if (!string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal)
            || !string.Equals(request.Headers.Authorization?.Parameter, expectedApiKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway experiment smoke failed: provider authorization header missing.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertJsonString(root, "model", expectedModel, "experiment model");
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() != 2)
        {
            throw new InvalidOperationException("LLM gateway experiment smoke failed: text request messages were not serialized.");
        }
    }

    private static void AssertJsonString(JsonElement element, string propertyName, string expected, string label)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !string.Equals(property.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway experiment smoke failed: expected {label}='{expected}'.");
        }
    }

    private static HttpResponseMessage BuildJsonResponse(object payload, string requestId)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json),
            Headers =
            {
                { "x-request-id", requestId }
            }
        };
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
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            return _handler(request, body);
        }
    }
}

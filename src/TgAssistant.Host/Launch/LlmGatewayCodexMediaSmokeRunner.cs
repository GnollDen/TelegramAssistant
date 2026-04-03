using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.LlmGateway;

namespace TgAssistant.Host.Launch;

public static class LlmGatewayCodexMediaSmokeRunner
{
    public static async Task RunAsync(CancellationToken ct = default)
    {
        var settings = BuildSettings();
        var options = Options.Create(settings);
        var loggerFactory = LoggerFactory.Create(_ => { });
        var codexHandler = new RecordingHttpMessageHandler(HandleCodexRequest);
        var openRouterHandler = new RecordingHttpMessageHandler(HandleOpenRouterRequest);

        var gateway = new LlmGatewayService(
            providers:
            [
                new CodexLbChatProviderClient(new HttpClient(codexHandler), options),
                new OpenRouterProviderClient(new HttpClient(openRouterHandler), options)
            ],
            routingPolicy: new DefaultLlmRoutingPolicy(options),
            settings: options,
            logger: loggerFactory.CreateLogger<LlmGatewayService>(),
            metrics: new LlmGatewayMetrics());

        var vision = await gateway.ExecuteAsync(BuildVisionRequest(), ct);
        var audio = await gateway.ExecuteAsync(BuildAudioTranscriptionRequest(), ct);
        var paraling = await gateway.ExecuteAsync(BuildAudioParalinguisticsRequest(), ct);

        AssertProvider(vision, "vision");
        AssertProvider(audio, "audio transcription");
        AssertProvider(paraling, "audio paralinguistics");

        if (codexHandler.RequestCount != 3)
        {
            throw new InvalidOperationException($"Codex media smoke failed: expected 3 codex requests, saw {codexHandler.RequestCount}.");
        }

        if (openRouterHandler.RequestCount != 0)
        {
            throw new InvalidOperationException($"Codex media smoke failed: expected 0 openrouter requests, saw {openRouterHandler.RequestCount}.");
        }
    }

    private static LlmGatewaySettings BuildSettings()
    {
        return new LlmGatewaySettings
        {
            Enabled = true,
            Routing = new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase)
            {
                ["vision"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = "smoke-codex-media-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "smoke-openrouter-vision-model"
                        }
                    ]
                },
                ["audio_transcription"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = "smoke-codex-media-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "smoke-openrouter-audio-model"
                        }
                    ]
                },
                ["audio_paralinguistics"] = new()
                {
                    PrimaryProvider = CodexLbChatProviderClient.ProviderIdValue,
                    PrimaryModel = "smoke-codex-media-model",
                    FallbackProviders =
                    [
                        new LlmGatewayProviderTargetSettings
                        {
                            Provider = OpenRouterProviderClient.ProviderIdValue,
                            Model = "smoke-openrouter-audio-model"
                        }
                    ]
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://codex-lb.local",
                    ApiKey = "codex-smoke-key",
                    DefaultModel = "smoke-codex-media-model",
                    ChatCompletionsPath = "/v1/chat/completions"
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-smoke-key",
                    DefaultModel = "smoke-openrouter-default",
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings"
                }
            }
        };
    }

    private static LlmGatewayRequest BuildVisionRequest()
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.Vision,
            TaskKey = "codex_media_smoke_vision",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                TimeoutMs = 5000
            },
            Messages =
            [
                new LlmGatewayMessage
                {
                    Role = LlmMessageRole.User,
                    ContentParts =
                    [
                        LlmMessageContentPart.FromText("Describe this image."),
                        new LlmMessageContentPart
                        {
                            Type = LlmContentPartType.InlineData,
                            MimeType = "image/png",
                            InlineDataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-image"))
                        }
                    ]
                }
            ]
        };
    }

    private static LlmGatewayRequest BuildAudioTranscriptionRequest()
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.AudioTranscription,
            TaskKey = "codex_media_smoke_audio_transcription",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                TimeoutMs = 5000
            },
            Messages =
            [
                new LlmGatewayMessage
                {
                    Role = LlmMessageRole.User,
                    ContentParts =
                    [
                        LlmMessageContentPart.FromText("Transcribe this audio."),
                        new LlmMessageContentPart
                        {
                            Type = LlmContentPartType.InlineData,
                            MimeType = "audio/wav",
                            InlineDataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-audio"))
                        }
                    ]
                }
            ]
        };
    }

    private static LlmGatewayRequest BuildAudioParalinguisticsRequest()
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.AudioParalinguistics,
            TaskKey = "codex_media_smoke_audio_paralinguistics",
            ResponseMode = LlmResponseMode.StructuredAudio,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 128,
                TimeoutMs = 5000
            },
            Messages =
            [
                new LlmGatewayMessage
                {
                    Role = LlmMessageRole.User,
                    ContentParts =
                    [
                        LlmMessageContentPart.FromText("Return strict JSON for audio emotion profile."),
                        new LlmMessageContentPart
                        {
                            Type = LlmContentPartType.InlineData,
                            MimeType = "audio/wav",
                            InlineDataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("synthetic-audio"))
                        }
                    ]
                }
            ]
        };
    }

    private static void AssertProvider(LlmGatewayResponse response, string label)
    {
        if (!string.Equals(response.Provider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Codex media smoke failed: {label} routed to '{response.Provider}' instead of codex-lb.");
        }
    }

    private static HttpResponseMessage HandleCodexRequest(HttpRequestMessage request, string body)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("Codex media smoke failed: codex request did not use POST.");
        }

        if (!string.Equals(request.RequestUri?.AbsolutePath, "/v1/chat/completions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Codex media smoke failed: unexpected codex path '{request.RequestUri?.AbsolutePath}'.");
        }

        if (!string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal)
            || !string.Equals(request.Headers.Authorization?.Parameter, "codex-smoke-key", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Codex media smoke failed: codex authorization header missing.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("model", out var modelNode)
            || !string.Equals(modelNode.GetString(), "smoke-codex-media-model", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Codex media smoke failed: codex media model was not applied.");
        }

        return BuildJsonResponse(
            new
            {
                id = "codex-media-smoke-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "{\"ok\":true}"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 10,
                    completion_tokens = 5,
                    total_tokens = 15
                }
            },
            "codex-media-smoke-1");
    }

    private static HttpResponseMessage HandleOpenRouterRequest(HttpRequestMessage _, string __)
    {
        throw new InvalidOperationException("Codex media smoke failed: openrouter request was not expected.");
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

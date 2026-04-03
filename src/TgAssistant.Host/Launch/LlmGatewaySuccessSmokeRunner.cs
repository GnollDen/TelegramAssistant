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

public static class LlmGatewaySuccessSmokeRunner
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

        var textResponse = await gateway.ExecuteAsync(BuildTextRequest(), ct);
        AssertTextResponse(textResponse);

        var embeddingResponse = await gateway.ExecuteAsync(BuildEmbeddingRequest(), ct);
        AssertEmbeddingResponse(embeddingResponse);

        var audioResponse = await gateway.ExecuteAsync(BuildAudioRequest(), ct);
        AssertAudioResponse(audioResponse);

        if (codexHandler.RequestCount != 1)
        {
            throw new InvalidOperationException($"LLM gateway success smoke failed: expected 1 codex-lb request, saw {codexHandler.RequestCount}.");
        }

        if (openRouterHandler.RequestCount != 2)
        {
            throw new InvalidOperationException($"LLM gateway success smoke failed: expected 2 openrouter requests, saw {openRouterHandler.RequestCount}.");
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
                    PrimaryModel = "smoke-text-model"
                },
                ["embeddings"] = new()
                {
                    PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
                    PrimaryModel = "smoke-embedding-model"
                },
                ["audio_transcription"] = new()
                {
                    PrimaryProvider = OpenRouterProviderClient.ProviderIdValue,
                    PrimaryModel = "smoke-audio-model"
                }
            },
            Providers = new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase)
            {
                [CodexLbChatProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "http://codex-lb.local",
                    ApiKey = "codex-smoke-key",
                    DefaultModel = "codex-default-smoke",
                    ChatCompletionsPath = "/v1/chat/completions"
                },
                [OpenRouterProviderClient.ProviderIdValue] = new()
                {
                    BaseUrl = "https://openrouter.local/api/v1",
                    ApiKey = "openrouter-smoke-key",
                    DefaultModel = "openrouter-default-smoke",
                    ChatCompletionsPath = "/chat/completions",
                    EmbeddingsPath = "/embeddings"
                }
            }
        };
    }

    private static LlmGatewayRequest BuildTextRequest()
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = "gateway_smoke_text",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 64,
                Temperature = 0.1f,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "smoke:text",
                RequestId = "gateway-smoke-text-request"
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, "You are a precise smoke test."),
                LlmGatewayMessage.FromText(LlmMessageRole.User, "Reply with gateway smoke ok.")
            ]
        };
    }

    private static LlmGatewayRequest BuildEmbeddingRequest()
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.Embeddings,
            TaskKey = "gateway_smoke_embeddings",
            ResponseMode = LlmResponseMode.EmbeddingVector,
            Limits = new LlmExecutionLimits
            {
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "smoke:embeddings",
                RequestId = "gateway-smoke-embedding-request"
            },
            EmbeddingInputs = ["gateway smoke embedding input"]
        };
    }

    private static LlmGatewayRequest BuildAudioRequest()
    {
        return new LlmGatewayRequest
        {
            Modality = LlmModality.AudioTranscription,
            TaskKey = "gateway_smoke_audio_transcription",
            ResponseMode = LlmResponseMode.StructuredAudio,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = 128,
                TimeoutMs = 5000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "smoke:audio_transcription",
                RequestId = "gateway-smoke-audio-request"
            },
            Messages =
            [
                new LlmGatewayMessage
                {
                    Role = LlmMessageRole.User,
                    ContentParts =
                    [
                        LlmMessageContentPart.FromText("Transcribe the audio as JSON."),
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

    private static HttpResponseMessage HandleCodexRequest(HttpRequestMessage request, string body)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: codex-lb request did not use POST.");
        }

        if (!string.Equals(request.RequestUri?.AbsolutePath, "/v1/chat/completions", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway success smoke failed: unexpected codex-lb path '{request.RequestUri?.AbsolutePath}'.");
        }

        if (!string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal)
            || !string.Equals(request.Headers.Authorization?.Parameter, "codex-smoke-key", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: codex-lb authorization header missing.");
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertJsonString(root, "model", "smoke-text-model", "codex-lb model");
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() != 2)
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: codex-lb text request did not serialize two messages.");
        }

        var userMessage = messages[1];
        AssertJsonString(userMessage, "role", "user", "codex-lb user role");
        AssertJsonString(userMessage, "content", "Reply with gateway smoke ok.", "codex-lb user content");

        return BuildJsonResponse(
            new
            {
                id = "codex-request-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "gateway smoke ok"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 12,
                    completion_tokens = 4,
                    total_tokens = 16,
                    cost = 0.0012m
                }
            },
            "codex-request-1");
    }

    private static HttpResponseMessage HandleOpenRouterRequest(HttpRequestMessage request, string body)
    {
        if (request.Method != HttpMethod.Post)
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: openrouter request did not use POST.");
        }

        if (!string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.Ordinal)
            || !string.Equals(request.Headers.Authorization?.Parameter, "openrouter-smoke-key", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: openrouter authorization header missing.");
        }

        return request.RequestUri?.AbsolutePath switch
        {
            "/api/v1/embeddings" => HandleEmbeddingRequest(body),
            "/api/v1/chat/completions" => HandleAudioRequest(body),
            _ => throw new InvalidOperationException($"LLM gateway success smoke failed: unexpected openrouter path '{request.RequestUri?.AbsolutePath}'.")
        };
    }

    private static HttpResponseMessage HandleEmbeddingRequest(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertJsonString(root, "model", "smoke-embedding-model", "embedding model");
        AssertJsonString(root, "input", "gateway smoke embedding input", "embedding input");

        return BuildJsonResponse(
            new
            {
                id = "openrouter-embedding-1",
                data = new[]
                {
                    new
                    {
                        embedding = new[] { 0.11f, 0.22f, 0.33f }
                    }
                },
                usage = new
                {
                    prompt_tokens = 3,
                    total_tokens = 3,
                    cost = 0.0003m
                }
            },
            "openrouter-embedding-1");
    }

    private static HttpResponseMessage HandleAudioRequest(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        AssertJsonString(root, "model", "smoke-audio-model", "audio model");
        if (!root.TryGetProperty("response_format", out var responseFormat)
            || responseFormat.ValueKind != JsonValueKind.Object
            || !responseFormat.TryGetProperty("type", out var typeNode)
            || !string.Equals(typeNode.GetString(), "json_object", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: audio request did not enforce json_object response format.");
        }

        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array || messages.GetArrayLength() != 1)
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: audio request messages were not serialized.");
        }

        var content = messages[0].GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() != 2)
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: audio request content parts were not preserved.");
        }

        var audioPart = content[1];
        AssertJsonString(audioPart, "type", "input_audio", "audio content type");
        if (!audioPart.TryGetProperty("input_audio", out var inputAudio)
            || !inputAudio.TryGetProperty("data", out var dataNode)
            || string.IsNullOrWhiteSpace(dataNode.GetString())
            || !inputAudio.TryGetProperty("format", out var formatNode)
            || !string.Equals(formatNode.GetString(), "wav", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: audio inline payload did not serialize to input_audio.");
        }

        return BuildJsonResponse(
            new
            {
                id = "openrouter-audio-1",
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "{\"transcript\":\"synthetic audio transcript\"}"
                        }
                    }
                },
                usage = new
                {
                    prompt_tokens = 21,
                    completion_tokens = 9,
                    total_tokens = 30,
                    cost = 0.0021m
                }
            },
            "openrouter-audio-1");
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

    private static void AssertTextResponse(LlmGatewayResponse response)
    {
        if (!string.Equals(response.Provider, CodexLbChatProviderClient.ProviderIdValue, StringComparison.Ordinal)
            || !string.Equals(response.Model, "smoke-text-model", StringComparison.Ordinal)
            || !string.Equals(response.RequestId, "codex-request-1", StringComparison.Ordinal)
            || !string.Equals(response.Output.Text, "gateway smoke ok", StringComparison.Ordinal)
            || response.FallbackApplied
            || response.Usage.TotalTokens != 16
            || string.IsNullOrWhiteSpace(response.RawProviderPayloadJson))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: codex-lb text response did not normalize expected audit fields.");
        }
    }

    private static void AssertEmbeddingResponse(LlmGatewayResponse response)
    {
        if (!string.Equals(response.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
            || !string.Equals(response.Model, "smoke-embedding-model", StringComparison.Ordinal)
            || !string.Equals(response.RequestId, "openrouter-embedding-1", StringComparison.Ordinal)
            || response.Output.Embeddings.Count != 1
            || response.Output.Embeddings[0].Length != 3
            || response.Usage.TotalTokens != 3
            || string.IsNullOrWhiteSpace(response.RawProviderPayloadJson))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: openrouter embedding response did not normalize expected audit fields.");
        }
    }

    private static void AssertAudioResponse(LlmGatewayResponse response)
    {
        if (!string.Equals(response.Provider, OpenRouterProviderClient.ProviderIdValue, StringComparison.Ordinal)
            || !string.Equals(response.Model, "smoke-audio-model", StringComparison.Ordinal)
            || !string.Equals(response.RequestId, "openrouter-audio-1", StringComparison.Ordinal)
            || !string.Equals(response.Output.StructuredPayloadJson, "{\"transcript\":\"synthetic audio transcript\"}", StringComparison.Ordinal)
            || response.Usage.TotalTokens != 30
            || string.IsNullOrWhiteSpace(response.RawProviderPayloadJson))
        {
            throw new InvalidOperationException("LLM gateway success smoke failed: openrouter audio response did not normalize expected audit fields.");
        }
    }

    private static void AssertJsonString(JsonElement element, string propertyName, string expected, string label)
    {
        if (!element.TryGetProperty(propertyName, out var node) || !string.Equals(node.GetString(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"LLM gateway success smoke failed: unexpected {label}.");
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

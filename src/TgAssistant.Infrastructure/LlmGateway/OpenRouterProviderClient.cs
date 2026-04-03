using System.Text.Json;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public class OpenRouterProviderClient : LlmGatewayProviderClientBase, ILlmProviderClient
{
    public const string ProviderIdValue = "openrouter";

    public OpenRouterProviderClient(HttpClient httpClient, IOptions<LlmGatewaySettings> settings)
        : base(httpClient, settings)
    {
    }

    public string ProviderId => ProviderIdValue;

    public bool Supports(LlmModality modality)
    {
        return modality is LlmModality.TextChat
            or LlmModality.Tools
            or LlmModality.Embeddings
            or LlmModality.Vision
            or LlmModality.AudioTranscription
            or LlmModality.AudioParalinguistics;
    }

    public Task<LlmProviderResult> ExecuteAsync(LlmProviderRequest request, CancellationToken ct = default)
    {
        if (!Supports(request.Request.Modality))
        {
            throw new LlmGatewayException($"Provider '{ProviderId}' does not support modality '{request.Request.Modality}'.")
            {
                Category = LlmGatewayErrorCategory.UnsupportedModality,
                Provider = ProviderId,
                Modality = request.Request.Modality,
                Retryable = false
            };
        }

        return request.Request.Modality == LlmModality.Embeddings
            ? ExecuteEmbeddingsAsync(request, ct)
            : ExecuteChatAsync(request, ct);
    }

    private Task<LlmProviderResult> ExecuteEmbeddingsAsync(LlmProviderRequest request, CancellationToken ct)
    {
        var providerSettings = GetRequiredProviderSettings(ProviderId);
        object inputPayload = request.Request.EmbeddingInputs.Count == 1
            ? request.Request.EmbeddingInputs[0]
            : request.Request.EmbeddingInputs.ToArray();
        var payload = new
        {
            model = request.Model,
            input = inputPayload
        };

        return SendJsonAsync(
            ProviderId,
            providerSettings.EmbeddingsPath,
            payload,
            request,
            (responseBody, response) => new LlmProviderResult
            {
                ProviderId = ProviderId,
                Model = request.Model,
                Output = new LlmGatewayOutput
                {
                    Embeddings = ReadEmbeddings(responseBody)
                },
                Usage = ReadUsage(responseBody),
                RequestId = ExtractRequestId(response, responseBody),
                RawProviderPayloadJson = responseBody
            },
            ct);
    }

    private Task<LlmProviderResult> ExecuteChatAsync(LlmProviderRequest request, CancellationToken ct)
    {
        var providerSettings = GetRequiredProviderSettings(ProviderId);
        var payload = new
        {
            model = request.Model,
            messages = request.Request.Messages.Select(message => new
            {
                role = message.Role switch
                {
                    LlmMessageRole.System => "system",
                    LlmMessageRole.User => "user",
                    LlmMessageRole.Assistant => "assistant",
                    LlmMessageRole.Tool => "tool",
                    _ => "user"
                },
                content = BuildOpenAiCompatibleContent(message, allowBinaryInlineData: true),
                name = message.Name,
                tool_call_id = message.ToolCallId,
                tool_calls = message.ToolCalls.Count > 0 ? BuildToolCalls(message.ToolCalls) : null
            }).ToArray(),
            tools = request.Request.ToolDefinitions.Count > 0 ? BuildToolDefinitions(request.Request.ToolDefinitions) : null,
            tool_choice = request.Request.ToolDefinitions.Count > 0 ? "auto" : null,
            response_format = request.Request.ResponseMode is LlmResponseMode.JsonObject or LlmResponseMode.StructuredAudio
                ? new { type = "json_object" }
                : null,
            max_tokens = request.Request.Limits.MaxTokens,
            temperature = request.Request.Limits.Temperature
        };

        return SendJsonAsync(
            ProviderId,
            providerSettings.ChatCompletionsPath,
            payload,
            request,
            (responseBody, response) => new LlmProviderResult
            {
                ProviderId = ProviderId,
                Model = request.Model,
                Output = ReadChatOutput(responseBody, request.Request.ResponseMode),
                Usage = ReadUsage(responseBody),
                RequestId = ExtractRequestId(response, responseBody),
                RawProviderPayloadJson = responseBody
            },
            ct);
    }

    private static List<float[]> ReadEmbeddings(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return new List<float[]>();
        }

        var embeddings = new List<float[]>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("embedding", out var vector) || vector.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            embeddings.Add(vector.EnumerateArray().Select(value => value.GetSingle()).ToArray());
        }

        return embeddings;
    }
}

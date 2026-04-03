using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public class CodexLbChatProviderClient : LlmGatewayProviderClientBase, ILlmProviderClient
{
    public const string ProviderIdValue = "codex-lb";

    public CodexLbChatProviderClient(HttpClient httpClient, IOptions<LlmGatewaySettings> settings)
        : base(httpClient, settings)
    {
    }

    public string ProviderId => ProviderIdValue;

    public bool Supports(LlmModality modality)
    {
        return modality is LlmModality.TextChat or LlmModality.Tools;
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
                content = BuildTextOnlyContent(message),
                name = message.Name,
                tool_call_id = message.ToolCallId,
                tool_calls = message.ToolCalls.Count > 0 ? BuildToolCalls(message.ToolCalls) : null
            }).ToArray(),
            tools = request.Request.ToolDefinitions.Count > 0 ? BuildToolDefinitions(request.Request.ToolDefinitions) : null,
            tool_choice = request.Request.ToolDefinitions.Count > 0 ? "auto" : null,
            response_format = request.Request.ResponseMode == LlmResponseMode.JsonObject ? new { type = "json_object" } : null,
            max_tokens = request.Request.Limits.MaxTokens,
            temperature = request.Request.Limits.Temperature
        };

        return SendJsonAsync(
            ProviderId,
            GetRequiredProviderSettings(ProviderId).ChatCompletionsPath,
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
}

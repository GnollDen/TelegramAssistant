using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public abstract class LlmGatewayProviderClientBase
{
    private const string RequestIdHeader = "x-request-id";

    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly LlmGatewaySettings _settings;

    protected LlmGatewayProviderClientBase(HttpClient httpClient, IOptions<LlmGatewaySettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    protected Task<LlmProviderResult> SendJsonAsync(
        string providerId,
        string relativePath,
        object payload,
        LlmProviderRequest request,
        Func<string, HttpResponseMessage, LlmProviderResult> onSuccess,
        CancellationToken ct)
    {
        return SendJsonAsync(
            providerId,
            relativePath,
            JsonSerializer.Serialize(payload, JsonOptions),
            request,
            onSuccess,
            ct);
    }

    protected async Task<LlmProviderResult> SendJsonAsync(
        string providerId,
        string relativePath,
        string jsonPayload,
        LlmProviderRequest request,
        Func<string, HttpResponseMessage, LlmProviderResult> onSuccess,
        CancellationToken ct)
    {
        var providerSettings = GetRequiredProviderSettings(providerId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeoutMs = request.Request.Limits.TimeoutMs ?? Math.Max(1, providerSettings.TimeoutSeconds) * 1000;
        linkedCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(providerSettings.BaseUrl, relativePath))
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        ApplyAuthorization(providerSettings, httpRequest);

        var startedAt = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            startedAt.Stop();

            if (!response.IsSuccessStatusCode)
            {
                throw BuildHttpFailure(providerId, request.Request.Modality, response.StatusCode, responseBody);
            }

            var result = onSuccess(responseBody, response);
            result.LatencyMs = Math.Max(0, (int)startedAt.ElapsedMilliseconds);
            result.StatusCode = response.StatusCode;
            result.RawProviderPayloadJson ??= responseBody;
            result.RequestId ??= ExtractRequestId(response, responseBody);
            return result;
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw BuildTimeout(providerId, request.Request.Modality, timeoutMs, ex);
        }
        catch (HttpRequestException ex)
        {
            throw BuildTransportFailure(providerId, request.Request.Modality, ex);
        }
    }

    protected LlmGatewayProviderSettings GetRequiredProviderSettings(string providerId)
    {
        var providerSettings = _settings.GetProvider(providerId);
        if (providerSettings is null || !providerSettings.Enabled)
        {
            throw new LlmGatewayException($"Gateway provider '{providerId}' is not configured or disabled.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Provider = providerId,
                Retryable = false
            };
        }

        if (string.IsNullOrWhiteSpace(providerSettings.BaseUrl))
        {
            throw new LlmGatewayException($"Gateway provider '{providerId}' is missing BaseUrl.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Provider = providerId,
                Retryable = false
            };
        }

        return providerSettings;
    }

    protected static object BuildOpenAiCompatibleContent(LlmGatewayMessage message, bool allowBinaryInlineData)
    {
        if (message.ContentParts.Count == 0)
        {
            return string.Empty;
        }

        if (message.ContentParts.Count == 1 && message.ContentParts[0].Type == LlmContentPartType.Text)
        {
            return message.ContentParts[0].Text ?? string.Empty;
        }

        return message.ContentParts
            .Select(part => BuildOpenAiCompatibleContentPart(part, allowBinaryInlineData))
            .ToArray();
    }

    protected static object BuildTextOnlyContent(LlmGatewayMessage message)
    {
        if (message.ContentParts.Any(part => part.Type != LlmContentPartType.Text))
        {
            throw new LlmGatewayException("The selected provider only supports text content parts for this modality.")
            {
                Category = LlmGatewayErrorCategory.UnsupportedModality,
                Modality = InferMessageModality(message),
                Retryable = false
            };
        }

        return string.Join('\n', message.ContentParts.Select(part => part.Text).Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    protected static object[] BuildToolDefinitions(IEnumerable<LlmToolDefinition> toolDefinitions)
    {
        return toolDefinitions
            .Select(tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = ParseJson(tool.ParametersJsonSchema)
                }
            })
            .ToArray();
    }

    protected static object[] BuildToolCalls(IEnumerable<LlmToolCall> toolCalls)
    {
        return toolCalls
            .Select(toolCall => new
            {
                id = toolCall.Id,
                type = "function",
                function = new
                {
                    name = toolCall.Name,
                    arguments = toolCall.ArgumentsJson
                }
            })
            .ToArray();
    }

    protected static LlmGatewayOutput ReadChatOutput(string responseBody, LlmResponseMode responseMode)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var message = root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var messageElement)
            ? messageElement
            : default;

        var output = new LlmGatewayOutput();
        if (message.ValueKind == JsonValueKind.Object)
        {
            if (message.TryGetProperty("content", out var content))
            {
                output.Text = NormalizeResponseContent(content);
            }

            if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            {
                output.ToolCalls = ReadToolCalls(toolCalls);
            }
        }

        if ((responseMode == LlmResponseMode.JsonObject || responseMode == LlmResponseMode.StructuredAudio)
            && !string.IsNullOrWhiteSpace(output.Text))
        {
            output.StructuredPayloadJson = output.Text;
        }

        return output;
    }

    protected static LlmUsageInfo ReadUsage(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return new LlmUsageInfo();
        }

        return new LlmUsageInfo
        {
            PromptTokens = TryReadInt(usage, "prompt_tokens"),
            CompletionTokens = TryReadInt(usage, "completion_tokens"),
            TotalTokens = TryReadInt(usage, "total_tokens"),
            CostUsd = TryReadDecimal(usage, "cost")
        };
    }

    protected static string? ExtractRequestId(HttpResponseMessage response, string responseBody)
    {
        if (response.Headers.TryGetValues(RequestIdHeader, out var requestIds))
        {
            return requestIds.FirstOrDefault();
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    protected static string NormalizeResponseContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                '\n',
                content.EnumerateArray()
                    .Select(item => item.ValueKind == JsonValueKind.Object
                        && item.TryGetProperty("text", out var textNode)
                        && textNode.ValueKind == JsonValueKind.String
                            ? textNode.GetString()
                            : null)
                    .Where(text => !string.IsNullOrWhiteSpace(text))),
            JsonValueKind.Object when content.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String
                => textNode.GetString() ?? string.Empty,
            _ => content.ToString()
        };
    }

    private static Uri BuildUri(string baseUrl, string relativePath)
    {
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        return new Uri(baseUri, relativePath.TrimStart('/'));
    }

    private static void ApplyAuthorization(LlmGatewayProviderSettings providerSettings, HttpRequestMessage httpRequest)
    {
        if (providerSettings.UseAuthorizationHeader && !string.IsNullOrWhiteSpace(providerSettings.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", providerSettings.ApiKey);
        }
    }

    private static object BuildOpenAiCompatibleContentPart(LlmMessageContentPart part, bool allowBinaryInlineData)
    {
        return part.Type switch
        {
            LlmContentPartType.Text => new
            {
                type = "text",
                text = part.Text ?? string.Empty
            },
            LlmContentPartType.ImageUri => new
            {
                type = "image_url",
                image_url = new
                {
                    url = part.MediaUri ?? string.Empty
                }
            },
            LlmContentPartType.AudioUri => BuildAudioUriPart(part),
            LlmContentPartType.InlineData => BuildInlineDataPart(part, allowBinaryInlineData),
            _ => throw new LlmGatewayException($"Content part type '{part.Type}' is not supported by the selected provider.")
            {
                Category = LlmGatewayErrorCategory.UnsupportedModality,
                Retryable = false
            }
        };
    }

    private static object BuildInlineDataPart(LlmMessageContentPart part, bool allowBinaryInlineData)
    {
        if (!allowBinaryInlineData)
        {
            throw new LlmGatewayException("The selected provider client does not support binary inline content for this modality.")
            {
                Category = LlmGatewayErrorCategory.UnsupportedModality,
                Retryable = false
            };
        }

        if (string.IsNullOrWhiteSpace(part.MimeType) || string.IsNullOrWhiteSpace(part.InlineDataBase64))
        {
            throw new LlmGatewayException("Inline data parts require both mime_type and inline_data_base64.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Retryable = false
            };
        }

        if (part.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{part.MimeType};base64,{part.InlineDataBase64}"
                }
            };
        }

        if (part.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                type = "input_audio",
                input_audio = new
                {
                    data = part.InlineDataBase64,
                    format = part.MimeType["audio/".Length..]
                }
            };
        }

        throw new LlmGatewayException($"Inline data mime type '{part.MimeType}' is not supported by the selected provider.")
        {
            Category = LlmGatewayErrorCategory.UnsupportedModality,
            Retryable = false
        };
    }

    private static object BuildAudioUriPart(LlmMessageContentPart part)
    {
        if (string.IsNullOrWhiteSpace(part.MediaUri))
        {
            throw new LlmGatewayException("Audio URI content parts require media_uri.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Retryable = false
            };
        }

        if (!part.MediaUri.StartsWith("data:audio/", StringComparison.OrdinalIgnoreCase))
        {
            throw new LlmGatewayException("Audio URI content parts must use a data URI for the current provider adapters.")
            {
                Category = LlmGatewayErrorCategory.UnsupportedModality,
                Retryable = false
            };
        }

        var prefixEnd = part.MediaUri.IndexOf(";base64,", StringComparison.OrdinalIgnoreCase);
        if (prefixEnd <= "data:audio/".Length)
        {
            throw new LlmGatewayException("Audio data URI is not in the expected base64 form.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Retryable = false
            };
        }

        var format = part.MediaUri["data:audio/".Length..prefixEnd];
        var data = part.MediaUri[(prefixEnd + ";base64,".Length)..];
        return new
        {
            type = "input_audio",
            input_audio = new
            {
                data,
                format
            }
        };
    }

    private static LlmGatewayException BuildHttpFailure(
        string providerId,
        LlmModality modality,
        HttpStatusCode statusCode,
        string responseBody)
    {
        var category = ClassifyCategory(statusCode, responseBody);
        return new LlmGatewayException($"Provider '{providerId}' returned HTTP {(int)statusCode}.")
        {
            Category = category,
            Provider = providerId,
            Modality = modality,
            HttpStatus = statusCode,
            Retryable = IsRetryable(category),
            RawReasonCode = BuildRawReasonCode(statusCode, responseBody)
        };
    }

    private static LlmGatewayException BuildTimeout(
        string providerId,
        LlmModality modality,
        int timeoutMs,
        Exception innerException)
    {
        return new LlmGatewayException($"Provider '{providerId}' timed out after {timeoutMs} ms.", innerException)
        {
            Category = LlmGatewayErrorCategory.Timeout,
            Provider = providerId,
            Modality = modality,
            Retryable = true,
            RawReasonCode = "timeout"
        };
    }

    private static LlmGatewayException BuildTransportFailure(
        string providerId,
        LlmModality modality,
        Exception innerException)
    {
        return new LlmGatewayException($"Provider '{providerId}' failed before receiving a successful response.", innerException)
        {
            Category = LlmGatewayErrorCategory.TransientUpstream,
            Provider = providerId,
            Modality = modality,
            Retryable = true,
            RawReasonCode = "transport_failure"
        };
    }

    private static LlmGatewayErrorCategory ClassifyCategory(HttpStatusCode statusCode, string responseBody)
    {
        if (BudgetErrorClassifier.IsQuotaLike(statusCode, responseBody))
        {
            return LlmGatewayErrorCategory.Quota;
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => LlmGatewayErrorCategory.Auth,
            HttpStatusCode.Forbidden => LlmGatewayErrorCategory.Auth,
            HttpStatusCode.TooManyRequests => LlmGatewayErrorCategory.RateLimit,
            HttpStatusCode.RequestTimeout => LlmGatewayErrorCategory.Timeout,
            HttpStatusCode.BadGateway => LlmGatewayErrorCategory.TransientUpstream,
            HttpStatusCode.ServiceUnavailable => LlmGatewayErrorCategory.TransientUpstream,
            HttpStatusCode.GatewayTimeout => LlmGatewayErrorCategory.Timeout,
            HttpStatusCode.BadRequest when responseBody.Contains("schema", StringComparison.OrdinalIgnoreCase)
                || responseBody.Contains("json", StringComparison.OrdinalIgnoreCase)
                => LlmGatewayErrorCategory.SchemaMismatch,
            HttpStatusCode.BadRequest => LlmGatewayErrorCategory.Validation,
            _ when (int)statusCode >= 500 => LlmGatewayErrorCategory.TransientUpstream,
            _ => LlmGatewayErrorCategory.Unknown
        };
    }

    private static bool IsRetryable(LlmGatewayErrorCategory category)
    {
        return category is LlmGatewayErrorCategory.Timeout
            or LlmGatewayErrorCategory.TransientUpstream
            or LlmGatewayErrorCategory.RateLimit;
    }

    private static string BuildRawReasonCode(HttpStatusCode statusCode, string responseBody)
    {
        var snippet = responseBody.Length > 120 ? responseBody[..120] : responseBody;
        return $"{(int)statusCode}:{snippet}";
    }

    private static JsonElement ParseJson(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(json) ? "{}" : json, JsonOptions);
    }

    private static int? TryReadInt(JsonElement usage, string propertyName)
    {
        return usage.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
    }

    private static decimal? TryReadDecimal(JsonElement usage, string propertyName)
    {
        return usage.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;
    }

    private static List<LlmToolCall> ReadToolCalls(JsonElement toolCalls)
    {
        var result = new List<LlmToolCall>();
        foreach (var toolCall in toolCalls.EnumerateArray())
        {
            if (!toolCall.TryGetProperty("function", out var function))
            {
                continue;
            }

            result.Add(new LlmToolCall
            {
                Id = toolCall.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : string.Empty,
                Name = function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : string.Empty,
                ArgumentsJson = function.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String
                    ? arguments.GetString() ?? "{}"
                    : "{}"
            });
        }

        return result;
    }

    private static LlmModality InferMessageModality(LlmGatewayMessage message)
    {
        if (message.ContentParts.Any(part => part.Type is LlmContentPartType.AudioUri or LlmContentPartType.InlineData))
        {
            return LlmModality.AudioTranscription;
        }

        if (message.ContentParts.Any(part => part.Type == LlmContentPartType.ImageUri))
        {
            return LlmModality.Vision;
        }

        return LlmModality.TextChat;
    }
}

using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.OpenRouter;

namespace TgAssistant.Intelligence.Stage5;

public class OpenRouterAnalysisService
{
    private readonly HttpClient _http;
    private readonly AnalysisSettings _analysis;
    private readonly ExtractionSchemaValidator _schemaValidator;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly ILogger<OpenRouterAnalysisService> _logger;

    public OpenRouterAnalysisService(
        HttpClient http,
        IOptions<AnalysisSettings> analysis,
        ExtractionSchemaValidator schemaValidator,
        IAnalysisUsageRepository usageRepository,
        ILogger<OpenRouterAnalysisService> logger)
    {
        _http = http;
        _analysis = analysis.Value;
        _schemaValidator = schemaValidator;
        _usageRepository = usageRepository;
        _logger = logger;
    }

    public async Task<ExtractionBatchResult> ExtractCheapAsync(string model, string systemPrompt, List<AnalysisInputMessage> batch, CancellationToken ct)
    {
        var user = MessageContentBuilder.BuildCheapBatchPrompt(batch);
        var req = BuildRequest(
            model,
            systemPrompt,
            user,
            NormalizeMaxTokens(_analysis.CheapMaxTokens, 300, 8000),
            0.0f,
            phase: "cheap");
        var json = await SendAndExtractJsonAsync(req, "cheap", ct);
        return ParseBatch(json);
    }

    public async Task<bool> ProbeCheapAvailabilityAsync(CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(_analysis.CheapModel)
            ? "openai/gpt-4o-mini"
            : _analysis.CheapModel.Trim();
        var req = BuildRequest(
            model,
            "Return only valid JSON object: {\"items\":[]}",
            "ping",
            32,
            0.0f,
            phase: "cheap");

        try
        {
            var json = await SendAndExtractJsonAsync(req, "cheap_probe", ct);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch (OpenRouterBalanceException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<ExtractionItem?> ResolveExpensiveAsync(
        string model,
        string systemPrompt,
        ExtractionItem candidate,
        List<string> currentFacts,
        string messageText,
        AnalysisMessageContext? context,
        CancellationToken ct)
    {
        var user = JsonSerializer.Serialize(
            new
            {
                message_text = messageText,
                context = new
                {
                    local_burst = context?.LocalBurst ?? [],
                    session_start = context?.SessionStart ?? [],
                    historical = context?.HistoricalSummaries ?? []
                },
                candidate,
                current_facts = currentFacts
            },
            JsonOptions);
        var req = BuildRequest(
            model,
            systemPrompt,
            user,
            NormalizeMaxTokens(_analysis.ExpensiveMaxTokens, 500, 12000),
            0.0f,
            phase: "expensive");
        var json = await SendAndExtractJsonAsync(req, "expensive", ct);
        var parsed = ParseBatch(json);
        return parsed.Items.FirstOrDefault();
    }

    public async Task<string> SummarizeDialogAsync(
        string model,
        string systemPrompt,
        long chatId,
        string scope,
        DateTime periodStart,
        DateTime periodEnd,
        List<Message> messages,
        IReadOnlyCollection<SummaryHistoricalHint>? historicalHints,
        IReadOnlyDictionary<long, string>? cheapJsonByMessageId,
        CancellationToken ct)
    {
        var user = MessageContentBuilder.BuildSummaryPrompt(
            chatId,
            scope,
            periodStart,
            periodEnd,
            messages,
            historicalHints,
            cheapJsonByMessageId);
        var req = BuildRequest(
            model,
            systemPrompt,
            user,
            NormalizeMaxTokens(_analysis.SummaryMaxTokens, 256, 8000),
            0.0f,
            phase: "summary");
        return await SendAndExtractJsonAsync(req, "summary", ct);
    }

    public async Task<string> CompleteTextAsync(
        string model,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        CancellationToken ct)
    {
        var req = new OpenRouterRequest
        {
            Model = model,
            Messages =
            [
                new OpenRouterMessage { Role = "system", Content = systemPrompt },
                new OpenRouterMessage { Role = "user", Content = userPrompt }
            ],
            MaxTokens = NormalizeMaxTokens(maxTokens, 64, 4000),
            Temperature = 0.2f
        };

        return await SendAndExtractTextAsync(req, "chat", ct);
    }

    public async Task<OpenRouterResponseMessage> CompleteChatWithToolsAsync(
        string model,
        List<OpenRouterMessage> messages,
        List<OpenRouterTool>? tools,
        int maxTokens,
        CancellationToken ct)
    {
        var req = new OpenRouterRequest
        {
            Model = model,
            Messages = messages,
            Tools = tools?.Count > 0 ? tools : null,
            ToolChoice = tools?.Count > 0 ? "auto" : null,
            MaxTokens = NormalizeMaxTokens(maxTokens, 64, 4000),
            Temperature = 0.2f
        };

        var response = await SendAsync(req, "chat", ct);
        return response?.Choices?.FirstOrDefault()?.Message ?? new OpenRouterResponseMessage();
    }

    private OpenRouterRequest BuildRequest(string model, string systemPrompt, string userPrompt, int maxTokens, float temperature, string phase)
    {
        return new OpenRouterRequest
        {
            Model = model,
            Messages =
            [
                new OpenRouterMessage { Role = "system", Content = systemPrompt },
                new OpenRouterMessage { Role = "user", Content = userPrompt }
            ],
            ResponseFormat = new OpenRouterResponseFormat { Type = "json_object" },
            Provider = BuildProviderPreferences(phase),
            MaxTokens = maxTokens,
            Temperature = temperature
        };
    }

    private OpenRouterProviderPreferences? BuildProviderPreferences(string phase)
    {
        if (!string.Equals(phase, "cheap", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(_analysis.CheapProviderOrder))
        {
            return null;
        }

        var order = _analysis.CheapProviderOrder
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (order.Count == 0)
        {
            return null;
        }

        return new OpenRouterProviderPreferences
        {
            Order = order,
            AllowFallbacks = _analysis.CheapProviderAllowFallbacks
        };
    }

    private async Task<string> SendAndExtractJsonAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var parsed = await SendAsync(request, phase, ct);
        var content = ExtractMessageContent(parsed?.Choices?.FirstOrDefault()?.Message?.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            return phase == "summary" ? "{\"summary\":\"\"}" : "{\"items\":[]}";
        }

        _logger.LogDebug("Analysis model response ({Model}) len={Len}", request.Model, content.Length);
        return content;
    }

    private async Task<string> SendAndExtractTextAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var parsed = await SendAsync(request, phase, ct);
        var content = ExtractMessageContent(parsed?.Choices?.FirstOrDefault()?.Message?.Content);
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        _logger.LogDebug("Text completion response ({Model}) len={Len}", request.Model, content.Length);
        return content.Trim();
    }

    private async Task<OpenRouterResponse?> SendAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync("/api/v1/chat/completions", request, JsonOptions, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            if (IsBalanceOrQuotaIssue(res.StatusCode, body))
            {
                throw new OpenRouterBalanceException($"OpenRouter balance/quota issue {res.StatusCode}: {body}", res.StatusCode);
            }

            throw new HttpRequestException($"OpenRouter error {res.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenRouterResponse>(body, JsonOptions);
        await LogUsageAsync(phase, request.Model, parsed?.Usage, ct);
        return parsed;
    }

    private static string ExtractMessageContent(object? content)
    {
        if (content == null)
        {
            return string.Empty;
        }

        if (content is string text)
        {
            return text;
        }

        if (content is JsonElement element)
        {
            return ExtractMessageContentFromJsonElement(element);
        }

        return content.ToString() ?? string.Empty;
    }

    private static string ExtractMessageContentFromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = element.EnumerateArray()
                .Select(part =>
                {
                    if (part.ValueKind == JsonValueKind.String)
                    {
                        return part.GetString() ?? string.Empty;
                    }

                    if (part.ValueKind == JsonValueKind.Object &&
                        part.TryGetProperty("text", out var textNode) &&
                        textNode.ValueKind == JsonValueKind.String)
                    {
                        return textNode.GetString() ?? string.Empty;
                    }

                    return string.Empty;
                })
                .Where(part => !string.IsNullOrWhiteSpace(part));

            return string.Join('\n', parts);
        }

        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty("text", out var textProperty) &&
            textProperty.ValueKind == JsonValueKind.String)
        {
            return textProperty.GetString() ?? string.Empty;
        }

        return element.ToString();
    }

    private static bool IsBalanceOrQuotaIssue(HttpStatusCode statusCode, string body)
    {
        if (statusCode == HttpStatusCode.PaymentRequired)
        {
            return true;
        }

        if (statusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest))
        {
            return false;
        }

        var text = body?.ToLowerInvariant() ?? string.Empty;
        return text.Contains("insufficient", StringComparison.Ordinal)
               || text.Contains("credit", StringComparison.Ordinal)
               || text.Contains("quota", StringComparison.Ordinal)
               || text.Contains("balance", StringComparison.Ordinal)
               || text.Contains("billing", StringComparison.Ordinal)
               || text.Contains("payment", StringComparison.Ordinal)
               || text.Contains("funds", StringComparison.Ordinal)
               || text.Contains("exhausted", StringComparison.Ordinal)
               || text.Contains("limit reached", StringComparison.Ordinal);
    }

    private async Task LogUsageAsync(string phase, string model, OpenRouterUsage? usage, CancellationToken ct)
    {
        if (usage == null)
        {
            return;
        }

        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = phase,
            Model = model,
            PromptTokens = usage.PromptTokens ?? 0,
            CompletionTokens = usage.CompletionTokens ?? 0,
            TotalTokens = usage.TotalTokens ?? 0,
            CostUsd = usage.Cost ?? 0m,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private ExtractionBatchResult ParseBatch(string json)
    {
        if (!_schemaValidator.TryParseBatch(json, out var parsed, out var error))
        {
            _logger.LogWarning("Stage5 schema validation failed: {Reason}", error ?? "invalid_schema");
            return new ExtractionBatchResult();
        }

        return parsed;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static int NormalizeMaxTokens(int requested, int min, int max)
    {
        return Math.Max(min, Math.Min(max, requested));
    }
}

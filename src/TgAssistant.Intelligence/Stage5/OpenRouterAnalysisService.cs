using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Intelligence.Stage5;

public class OpenRouterAnalysisService
{
    private readonly HttpClient _http;
    private readonly ClaudeSettings _claude;
    private readonly AnalysisSettings _analysis;
    private readonly ExtractionSchemaValidator _schemaValidator;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly ILogger<OpenRouterAnalysisService> _logger;

    public OpenRouterAnalysisService(
        HttpClient http,
        IOptions<ClaudeSettings> claude,
        IOptions<AnalysisSettings> analysis,
        ExtractionSchemaValidator schemaValidator,
        IAnalysisUsageRepository usageRepository,
        ILogger<OpenRouterAnalysisService> logger)
    {
        _http = http;
        _claude = claude.Value;
        _analysis = analysis.Value;
        _schemaValidator = schemaValidator;
        _usageRepository = usageRepository;
        _logger = logger;
        _http.BaseAddress = new Uri(_claude.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(30, _analysis.HttpTimeoutSeconds));
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_claude.ApiKey}");
    }

    public async Task<ExtractionBatchResult> ExtractCheapAsync(string model, string systemPrompt, List<AnalysisInputMessage> batch, CancellationToken ct)
    {
        var user = BuildCheapBatchPrompt(batch);
        var req = BuildRequest(model, systemPrompt, user, NormalizeMaxTokens(_analysis.CheapMaxTokens, 300, 8000), 0.0f);
        var json = await SendAndExtractJsonAsync(req, "cheap", ct);
        return ParseBatch(json);
    }

    public async Task<ExtractionItem?> ResolveExpensiveAsync(string model, string systemPrompt, ExtractionItem candidate, List<string> currentFacts, string messageText, CancellationToken ct)
    {
        var user = JsonSerializer.Serialize(new { message_text = messageText, candidate, current_facts = currentFacts }, JsonOptions);
        var req = BuildRequest(model, systemPrompt, user, NormalizeMaxTokens(_analysis.ExpensiveMaxTokens, 500, 12000), 0.0f);
        var json = await SendAndExtractJsonAsync(req, "expensive", ct);
        var parsed = ParseBatch(json);
        return parsed.Items.FirstOrDefault();
    }

    private static string BuildCheapBatchPrompt(List<AnalysisInputMessage> batch)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analyze these chat messages and return one extraction item per message.");
        foreach (var msg in batch)
        {
            sb.AppendLine($"<message id=\"{msg.MessageId}\" sender_name=\"{msg.SenderName}\" ts=\"{msg.Timestamp:O}\">");
            sb.AppendLine(msg.Text);
            sb.AppendLine("</message>");
        }

        return sb.ToString();
    }

    private OpenRouterRequest BuildRequest(string model, string systemPrompt, string userPrompt, int maxTokens, float temperature)
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
            MaxTokens = maxTokens,
            Temperature = temperature
        };
    }

    private async Task<string> SendAndExtractJsonAsync(OpenRouterRequest request, string phase, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync("/api/v1/chat/completions", request, JsonOptions, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenRouter error {res.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenRouterResponse>(body, JsonOptions);
        await LogUsageAsync(phase, request.Model, parsed?.Usage, ct);
        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return "{\"items\":[]}";
        }

        _logger.LogDebug("Analysis model response ({Model}) len={Len}", request.Model, content.Length);
        return content;
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

internal class OpenRouterRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterMessage> Messages { get; set; } = new();
    public OpenRouterResponseFormat? ResponseFormat { get; set; }
    public int? MaxTokens { get; set; }
    public float Temperature { get; set; }
}

internal class OpenRouterMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

internal class OpenRouterResponseFormat
{
    public string Type { get; set; } = string.Empty;
}

internal class OpenRouterResponse
{
    public List<OpenRouterChoice>? Choices { get; set; }
    public OpenRouterUsage? Usage { get; set; }
}

internal class OpenRouterChoice
{
    public OpenRouterResponseMessage? Message { get; set; }
}

internal class OpenRouterResponseMessage
{
    public string Content { get; set; } = string.Empty;
}

internal class OpenRouterUsage
{
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? Cost { get; set; }
}

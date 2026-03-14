using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
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

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Intelligence.Stage5;

public class OpenRouterAnalysisService
{
    private readonly HttpClient _http;
    private readonly ClaudeSettings _claude;
    private readonly ILogger<OpenRouterAnalysisService> _logger;

    public OpenRouterAnalysisService(HttpClient http, IOptions<ClaudeSettings> claude, ILogger<OpenRouterAnalysisService> logger)
    {
        _http = http;
        _claude = claude.Value;
        _logger = logger;
        _http.BaseAddress = new Uri(_claude.BaseUrl);
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_claude.ApiKey}");
    }

    public async Task<ExtractionBatchResult> ExtractCheapAsync(string model, string systemPrompt, List<AnalysisInputMessage> batch, CancellationToken ct)
    {
        var user = JsonSerializer.Serialize(new { messages = batch }, JsonOptions);
        var req = BuildRequest(model, systemPrompt, user, 4000);
        var json = await SendAndExtractJsonAsync(req, ct);
        return ParseBatch(json);
    }

    public async Task<ExtractionItem?> ResolveExpensiveAsync(string model, string systemPrompt, ExtractionItem candidate, List<string> currentFacts, CancellationToken ct)
    {
        var user = JsonSerializer.Serialize(new { candidate, current_facts = currentFacts }, JsonOptions);
        var req = BuildRequest(model, systemPrompt, user, 3000);
        var json = await SendAndExtractJsonAsync(req, ct);
        var parsed = ParseBatch(json);
        return parsed.Items.FirstOrDefault();
    }

    private OpenRouterRequest BuildRequest(string model, string systemPrompt, string userPrompt, int maxTokens)
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
            MaxTokens = maxTokens
        };
    }

    private async Task<string> SendAndExtractJsonAsync(OpenRouterRequest request, CancellationToken ct)
    {
        var res = await _http.PostAsJsonAsync("/api/v1/chat/completions", request, JsonOptions, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenRouter error {res.StatusCode}: {body}");
        }

        var parsed = JsonSerializer.Deserialize<OpenRouterResponse>(body, JsonOptions);
        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            return "{\"items\":[]}";
        }

        _logger.LogDebug("Analysis model response ({Model}) len={Len}", request.Model, content.Length);
        return content;
    }

    private static ExtractionBatchResult ParseBatch(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ExtractionBatchResult>(json, JsonOptions);
            return parsed ?? new ExtractionBatchResult();
        }
        catch
        {
            return new ExtractionBatchResult();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

internal class OpenRouterRequest
{
    public string Model { get; set; } = string.Empty;
    public List<OpenRouterMessage> Messages { get; set; } = new();
    public OpenRouterResponseFormat? ResponseFormat { get; set; }
    public int? MaxTokens { get; set; }
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
}

internal class OpenRouterChoice
{
    public OpenRouterResponseMessage? Message { get; set; }
}

internal class OpenRouterResponseMessage
{
    public string Content { get; set; } = string.Empty;
}

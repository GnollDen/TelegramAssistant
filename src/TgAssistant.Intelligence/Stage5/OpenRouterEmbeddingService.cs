using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Intelligence.Stage5;

public class OpenRouterEmbeddingService : ITextEmbeddingGenerator
{
    private readonly HttpClient _http;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly ILogger<OpenRouterEmbeddingService> _logger;

    public OpenRouterEmbeddingService(
        HttpClient http,
        IAnalysisUsageRepository usageRepository,
        ILogger<OpenRouterEmbeddingService> logger)
    {
        _http = http;
        _usageRepository = usageRepository;
        _logger = logger;
    }

    public async Task<float[]> GenerateAsync(string model, string input, CancellationToken ct = default)
    {
        var req = new EmbeddingRequest
        {
            Model = model,
            Input = input
        };

        var res = await _http.PostAsJsonAsync("/api/v1/embeddings", req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenRouter embedding error {res.StatusCode}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            _logger.LogDebug("OpenRouter embedding response missing data for model={Model}", model);
            return Array.Empty<float>();
        }

        var first = data[0];
        if (!first.TryGetProperty("embedding", out var emb) || emb.ValueKind != JsonValueKind.Array)
        {
            _logger.LogDebug("OpenRouter embedding response missing vector for model={Model}", model);
            return Array.Empty<float>();
        }

        var vector = new float[emb.GetArrayLength()];
        var i = 0;
        foreach (var x in emb.EnumerateArray())
        {
            vector[i++] = x.GetSingle();
        }

        await TryLogUsageAsync(root, model, ct);
        return vector;
    }

    private async Task TryLogUsageAsync(JsonElement root, string model, CancellationToken ct)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var promptTokens = usage.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0;
        var totalTokens = usage.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : promptTokens;
        decimal cost = 0m;
        if (usage.TryGetProperty("cost", out var c) && c.ValueKind is JsonValueKind.Number)
        {
            cost = c.GetDecimal();
        }

        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = "embedding",
            Model = model,
            PromptTokens = promptTokens,
            CompletionTokens = 0,
            TotalTokens = totalTokens,
            CostUsd = cost,
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogDebug("Embedding usage logged model={Model} tokens={Tokens}", model, totalTokens);
    }

    private sealed class EmbeddingRequest
    {
        public string Model { get; set; } = string.Empty;
        public string Input { get; set; } = string.Empty;
    }
}

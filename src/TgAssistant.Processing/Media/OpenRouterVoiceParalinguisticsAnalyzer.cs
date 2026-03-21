using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Media;

public class OpenRouterVoiceParalinguisticsAnalyzer : IVoiceParalinguisticsAnalyzer
{
    private readonly HttpClient _http;
    private readonly GeminiSettings _gemini;
    private readonly VoiceParalinguisticsSettings _settings;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<OpenRouterVoiceParalinguisticsAnalyzer> _logger;

    public OpenRouterVoiceParalinguisticsAnalyzer(
        HttpClient http,
        IOptions<GeminiSettings> gemini,
        IOptions<VoiceParalinguisticsSettings> settings,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<OpenRouterVoiceParalinguisticsAnalyzer> logger)
    {
        _http = http;
        _gemini = gemini.Value;
        _settings = settings.Value;
        _usageRepository = usageRepository;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
        _http.BaseAddress = new Uri(_gemini.BaseUrl);
        if (!_http.DefaultRequestHeaders.Contains("Authorization"))
        {
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_gemini.ApiKey}");
        }
    }

    public async Task<string> AnalyzeAsync(string filePath, CancellationToken ct = default)
    {
        var wavPath = filePath;
        if (!filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            wavPath = Path.ChangeExtension(filePath, ".paraling.wav");
            await ConvertToWavAsync(filePath, wavPath, ct);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(wavPath, ct);
            var base64 = Convert.ToBase64String(bytes);
            var request = new VoiceOpenRouterRequest
            {
                Model = _settings.Model,
                MaxTokens = _settings.MaxTokens,
                Messages = new[]
                {
                    new VoiceOpenRouterMessage
                    {
                        Role = "user",
                        Content = new object[]
                        {
                            new { type = "input_audio", input_audio = new { data = base64, format = "wav" } },
                            new
                            {
                                type = "text",
                                text =
                                    "Return strict JSON only: {\"primary_emotion\":\"...\",\"secondary_emotion\":\"...\",\"valence\":-1..1,\"arousal\":0..1,\"dominance\":0..1,\"sarcasm_probability\":0..1,\"confidence\":0..1,\"evidence\":[\"...\"]}. Analyze tone/prosody/tempo/pauses/intensity from audio signal, not lexical meaning."
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(request, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("/api/v1/chat/completions", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                if (BudgetErrorClassifier.IsQuotaLike(response.StatusCode, body))
                {
                    await _budgetGuardrailService.RegisterQuotaBlockedAsync(
                        pathKey: "audio_paralinguistics",
                        modality: BudgetModalities.Audio,
                        reason: "quota_like_provider_failure",
                        isImportScope: false,
                        isOptionalPath: true,
                        ct: ct);
                }

                throw new HttpRequestException($"OpenRouter error {(int)response.StatusCode}: {body}");
            }

            var parsed = JsonSerializer.Deserialize<VoiceOpenRouterResponse>(body, JsonOptions);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Empty voice paralinguistics response");
            }

            await TryLogUsageAsync(parsed?.Usage, ct);

            var sanitized = TryExtractJson(text);
            using var doc = JsonDocument.Parse(sanitized);
            var normalized = NormalizePayload(doc.RootElement);
            return JsonSerializer.Serialize(normalized, JsonOptions);
        }
        finally
        {
            if (wavPath != filePath && File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

    private static object NormalizePayload(JsonElement root)
    {
        var primary = NormalizeEmotion(TryGetString(root, "primary_emotion"));
        var secondary = NormalizeEmotion(TryGetString(root, "secondary_emotion"));
        var valence = Clamp(TryGetFloat(root, "valence"), -1f, 1f);
        var arousal = Clamp(TryGetFloat(root, "arousal"), 0f, 1f);
        var dominance = Clamp(TryGetFloat(root, "dominance"), 0f, 1f);
        var sarcasm = Clamp(TryGetFloat(root, "sarcasm_probability"), 0f, 1f);
        var confidence = Clamp(TryGetFloat(root, "confidence"), 0f, 1f);

        var evidence = new List<string>();
        if (root.TryGetProperty("evidence", out var evidenceElement) && evidenceElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in evidenceElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        evidence.Add(value.Length > 240 ? value[..240] : value);
                    }
                }
            }
        }

        return new
        {
            primary_emotion = primary,
            secondary_emotion = secondary,
            valence,
            arousal,
            dominance,
            sarcasm_probability = sarcasm,
            confidence,
            evidence
        };
    }

    private static string NormalizeEmotion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "neutral";
        }

        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "neutral" => "neutral",
            "joy" or "happy" or "happiness" => "joy",
            "sad" or "sadness" => "sadness",
            "anger" or "angry" => "anger",
            "fear" or "anxiety" => "fear",
            "surprise" => "surprise",
            "disgust" => "disgust",
            "calm" => "calm",
            "irritation" => "irritation",
            "excitement" => "excitement",
            "sarcasm" => "sarcasm",
            _ => "neutral"
        };
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static float TryGetFloat(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return 0f;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && float.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0f;
    }

    private static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));

    private static string TryExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return raw[start..(end + 1)];
        }

        return raw;
    }

    private static async Task ConvertToWavAsync(string inputPath, string outputPath, CancellationToken ct)
    {
        await RunFfmpegAsync($"-i \"{inputPath}\" -ar 16000 -ac 1 -f wav \"{outputPath}\" -y", ct);
    }

    private static async Task RunFfmpegAsync(string arguments, CancellationToken ct)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync(ct);
        if (process.ExitCode == 0)
        {
            return;
        }

        var error = await process.StandardError.ReadToEndAsync(ct);
        throw new InvalidOperationException($"ffmpeg failed: {error}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private async Task TryLogUsageAsync(VoiceOpenRouterUsage? usage, CancellationToken ct)
    {
        if (usage == null)
        {
            return;
        }

        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = "audio_paralinguistics",
            Model = _settings.Model,
            PromptTokens = usage.PromptTokens ?? 0,
            CompletionTokens = usage.CompletionTokens ?? 0,
            TotalTokens = usage.TotalTokens ?? 0,
            CostUsd = usage.Cost ?? 0m,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private sealed class VoiceOpenRouterRequest
    {
        public string Model { get; set; } = string.Empty;
        public VoiceOpenRouterMessage[] Messages { get; set; } = Array.Empty<VoiceOpenRouterMessage>();
        public int? MaxTokens { get; set; }
    }

    private sealed class VoiceOpenRouterMessage
    {
        public string Role { get; set; } = string.Empty;
        public object Content { get; set; } = string.Empty;
    }

    private sealed class VoiceOpenRouterResponse
    {
        public List<VoiceOpenRouterChoice>? Choices { get; set; }
        public VoiceOpenRouterUsage? Usage { get; set; }
    }

    private sealed class VoiceOpenRouterChoice
    {
        public VoiceOpenRouterResponseMessage? Message { get; set; }
    }

    private sealed class VoiceOpenRouterResponseMessage
    {
        public string? Content { get; set; }
    }

    private sealed class VoiceOpenRouterUsage
    {
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
        public int? TotalTokens { get; set; }
        public decimal? Cost { get; set; }
    }
}

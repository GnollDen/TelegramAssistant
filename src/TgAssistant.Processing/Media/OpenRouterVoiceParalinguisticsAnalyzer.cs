using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Processing.Media;

public class OpenRouterVoiceParalinguisticsAnalyzer : IVoiceParalinguisticsAnalyzer
{
    private readonly HttpClient _http;
    private readonly GeminiSettings _gemini;
    private readonly VoiceParalinguisticsSettings _settings;
    private readonly ILogger<OpenRouterVoiceParalinguisticsAnalyzer> _logger;

    public OpenRouterVoiceParalinguisticsAnalyzer(
        HttpClient http,
        IOptions<GeminiSettings> gemini,
        IOptions<VoiceParalinguisticsSettings> settings,
        ILogger<OpenRouterVoiceParalinguisticsAnalyzer> logger)
    {
        _http = http;
        _gemini = gemini.Value;
        _settings = settings.Value;
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
                throw new HttpRequestException($"OpenRouter error {(int)response.StatusCode}: {body}");
            }

            var parsed = JsonSerializer.Deserialize<VoiceOpenRouterResponse>(body, JsonOptions);
            var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Empty voice paralinguistics response");
            }

            var sanitized = TryExtractJson(text);
            _ = JsonDocument.Parse(sanitized);
            return sanitized;
        }
        finally
        {
            if (wavPath != filePath && File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

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
    }

    private sealed class VoiceOpenRouterChoice
    {
        public VoiceOpenRouterResponseMessage? Message { get; set; }
    }

    private sealed class VoiceOpenRouterResponseMessage
    {
        public string? Content { get; set; }
    }
}

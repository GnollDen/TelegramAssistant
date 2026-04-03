using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
    private readonly ILlmGateway _gateway;
    private readonly VoiceParalinguisticsSettings _settings;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<OpenRouterVoiceParalinguisticsAnalyzer> _logger;

    public OpenRouterVoiceParalinguisticsAnalyzer(
        HttpClient http,
        IOptions<GeminiSettings> gemini,
        ILlmGateway gateway,
        IOptions<VoiceParalinguisticsSettings> settings,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<OpenRouterVoiceParalinguisticsAnalyzer> logger)
    {
        _http = http;
        _gemini = gemini.Value;
        _gateway = gateway;
        _settings = settings.Value;
        _usageRepository = usageRepository;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
        _ = _http;
        _ = _gemini;
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
            var response = await SendCompletionRequestAsync(base64, includeResponseFormat: true, ct);
            var text = response.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidOperationException("Empty voice paralinguistics response");
            }

            await TryLogUsageAsync(response.Usage, ct);
            if (TryNormalizePayload(text, out var payloadJson))
            {
                return payloadJson;
            }

            _logger.LogInformation(
                "Voice paralinguistics response was non-JSON for model={Model}; using conservative fallback payload.",
                _settings.Model);
            return BuildFallbackPayload(text);
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

    private static bool TryNormalizePayload(string rawResponse, out string payloadJson)
    {
        payloadJson = string.Empty;
        var sanitized = TryExtractJson(rawResponse);
        if (!sanitized.Contains('{'))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(sanitized);
            var normalized = NormalizePayload(doc.RootElement);
            payloadJson = JsonSerializer.Serialize(normalized, JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildFallbackPayload(string rawResponse)
    {
        var compact = CollapseWhitespace(rawResponse);
        var primary = TryInferEmotionFromText(compact);
        var confidence = 0.2f;
        var evidence = string.IsNullOrWhiteSpace(compact)
            ? new List<string>()
            : new List<string> { compact.Length > 220 ? compact[..220] : compact };
        var fallback = new
        {
            primary_emotion = primary,
            secondary_emotion = "neutral",
            valence = primary is "anger" or "sadness" or "fear" ? -0.15f : 0f,
            arousal = primary is "anger" or "excitement" ? 0.45f : 0.3f,
            dominance = 0.3f,
            sarcasm_probability = TryParseFloatToken(compact, "sarcasm_probability"),
            confidence,
            evidence
        };

        return JsonSerializer.Serialize(fallback, JsonOptions);
    }

    private static string TryInferEmotionFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "neutral";
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains("anger") || lower.Contains("angry") || lower.Contains("зл"))
        {
            return "anger";
        }

        if (lower.Contains("sad") || lower.Contains("sadness") || lower.Contains("груст"))
        {
            return "sadness";
        }

        if (lower.Contains("fear") || lower.Contains("anxiety") || lower.Contains("трев"))
        {
            return "fear";
        }

        if (lower.Contains("joy") || lower.Contains("happy") || lower.Contains("радост"))
        {
            return "joy";
        }

        if (lower.Contains("sarcas") || lower.Contains("сарказ"))
        {
            return "sarcasm";
        }

        return "neutral";
    }

    private static float TryParseFloatToken(string text, string tokenName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0f;
        }

        var pattern = $@"{Regex.Escape(tokenName)}\s*[:=]\s*(?<num>-?\d+(?:[.,]\d+)?)";
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return 0f;
        }

        var raw = match.Groups["num"].Value.Replace(',', '.');
        return float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Clamp(value, 0f, 1f)
            : 0f;
    }

    private static string CollapseWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    private static string NormalizeMessageContent(object? content)
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
                element.TryGetProperty("text", out var objectTextNode) &&
                objectTextNode.ValueKind == JsonValueKind.String)
            {
                return objectTextNode.GetString() ?? string.Empty;
            }
        }

        return content.ToString() ?? string.Empty;
    }

    private async Task<(string Text, LlmUsageInfo Usage)> SendCompletionRequestAsync(
        string base64Audio,
        bool includeResponseFormat,
        CancellationToken ct)
    {
        var request = new LlmGatewayRequest
        {
            Modality = LlmModality.AudioParalinguistics,
            TaskKey = "audio_paralinguistics",
            ResponseMode = includeResponseFormat ? LlmResponseMode.StructuredAudio : LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = _settings.MaxTokens,
                Temperature = 0f,
                TimeoutMs = 30000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "audio_paralinguistics",
                IsImportScope = false,
                IsOptionalPath = true,
                ScopeTags = ["media", "audio_paralinguistics"]
            },
            Messages =
            [
                new LlmGatewayMessage
                {
                    Role = LlmMessageRole.User,
                    ContentParts =
                    [
                        new LlmMessageContentPart
                        {
                            Type = LlmContentPartType.InlineData,
                            MimeType = "audio/wav",
                            InlineDataBase64 = base64Audio
                        },
                        LlmMessageContentPart.FromText(
                            "Return strict JSON only: {\"primary_emotion\":\"...\",\"secondary_emotion\":\"...\",\"valence\":-1..1,\"arousal\":0..1,\"dominance\":0..1,\"sarcasm_probability\":0..1,\"confidence\":0..1,\"evidence\":[\"...\"]}. Analyze tone/prosody/tempo/pauses/intensity from audio signal, not lexical meaning.")
                    ]
                }
            ]
        };

        try
        {
            var response = await _gateway.ExecuteAsync(request, ct);
            var text = NormalizeMessageContent(response.Output.StructuredPayloadJson ?? response.Output.Text).Trim();
            return (text, response.Usage);
        }
        catch (LlmGatewayException ex)
        {
            if (includeResponseFormat && IsUnsupportedResponseFormatError(ex))
            {
                _logger.LogWarning(
                    "Voice model {Model} does not support structured/json response mode, retrying without structured response mode.",
                    _settings.Model);
                return await SendCompletionRequestAsync(base64Audio, includeResponseFormat: false, ct);
            }

            if (ex.Category == LlmGatewayErrorCategory.Quota)
            {
                await _budgetGuardrailService.RegisterQuotaBlockedAsync(
                    pathKey: "audio_paralinguistics",
                    modality: BudgetModalities.Audio,
                    reason: "quota_like_provider_failure",
                    isImportScope: false,
                    isOptionalPath: true,
                    ct: ct);
            }

            throw new HttpRequestException(
                $"Gateway paralinguistics error provider={ex.Provider ?? "unknown"} category={ex.Category} status={ex.HttpStatus}; {ex.Message}",
                ex,
                ex.HttpStatus);
        }
    }

    private static bool IsUnsupportedResponseFormatError(LlmGatewayException ex)
    {
        if (ex.HttpStatus != HttpStatusCode.BadRequest)
        {
            return false;
        }

        var payload = ex.RawReasonCode ?? ex.Message;
        return payload.Contains("response_format", StringComparison.OrdinalIgnoreCase)
               && payload.Contains("not supported", StringComparison.OrdinalIgnoreCase);
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

    private async Task TryLogUsageAsync(LlmUsageInfo? usage, CancellationToken ct)
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
            CostUsd = usage.CostUsd ?? 0m,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

}

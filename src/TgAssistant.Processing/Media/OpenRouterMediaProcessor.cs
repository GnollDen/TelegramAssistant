using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Media;

public class OpenRouterMediaProcessor : IMediaProcessor
{
    private readonly HttpClient _http;
    private readonly GeminiSettings _settings;
    private readonly MediaSettings _mediaSettings;
    private readonly ArchiveImportSettings _archiveSettings;
    private readonly IStickerCacheRepository _stickerCacheRepository;
    private readonly ILogger<OpenRouterMediaProcessor> _logger;

    public OpenRouterMediaProcessor(
        HttpClient http,
        IOptions<GeminiSettings> settings,
        IOptions<MediaSettings> mediaSettings,
        IOptions<ArchiveImportSettings> archiveSettings,
        IStickerCacheRepository stickerCacheRepository,
        ILoggerFactory loggerFactory)
    {
        _http = http;
        _settings = settings.Value;
        _mediaSettings = mediaSettings.Value;
        _archiveSettings = archiveSettings.Value;
        _stickerCacheRepository = stickerCacheRepository;
        _logger = loggerFactory.CreateLogger<OpenRouterMediaProcessor>();
        _http.BaseAddress = new Uri(_settings.BaseUrl);
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");
    }

    public async Task<MediaProcessingResult> ProcessAsync(
        string filePath, MediaType mediaType, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return new MediaProcessingResult
                {
                    Success = false,
                    FailureReason = "File not found"
                };
            }

            var fileSizeMb = new FileInfo(filePath).Length / (1024d * 1024d);
            if (fileSizeMb > _mediaSettings.MaxProcessFileSizeMb)
            {
                return new MediaProcessingResult
                {
                    Success = false,
                    FailureReason = $"File too large: {fileSizeMb:F1}MB > {_mediaSettings.MaxProcessFileSizeMb}MB"
                };
            }

            return mediaType switch
            {
                MediaType.Photo => await ProcessImageAsync(filePath, BuildPhotoPrompt(filePath), ct),
                MediaType.Sticker => await ProcessStickerAsync(filePath, ct),
                MediaType.Animation => await ProcessImageAsync(filePath, "Describe this GIF in 1 short sentence: action + emotion.", ct),
                MediaType.Voice => await ProcessAudioAsync(filePath, ct),
                MediaType.VideoNote => await ProcessAudioAsync(filePath, ct),
                MediaType.Video => await ProcessAudioAsync(filePath, ct),
                _ => new MediaProcessingResult
                {
                    Success = false,
                    FailureReason = $"Unsupported media type: {mediaType}"
                }
            };
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Timeout processing {Type} at {Path}", mediaType, filePath);
            return new MediaProcessingResult
            {
                Success = false,
                FailureReason = "Request timeout (30s)"
            };
        }
        catch (Exception ex)
        {
            var reason = ClassifyFailureReason(ex, filePath, mediaType);
            _logger.LogError(ex, "Failed to process {Type} at {Path}", mediaType, filePath);
            _logger.LogWarning("Media processing failure reason={Reason} type={Type} path={Path}", reason, mediaType, filePath);
            return new MediaProcessingResult
            {
                Success = false,
                FailureReason = $"{reason}: {ex.GetType().Name}: {ex.Message}"
            };
        }
    }

    private async Task<MediaProcessingResult> ProcessStickerAsync(string filePath, CancellationToken ct)
    {
        if (filePath.EndsWith(".tgs", StringComparison.OrdinalIgnoreCase))
        {
            // Telegram animated stickers (Lottie .tgs) are not directly decodable by ffmpeg.
            _logger.LogInformation("Skipping animated sticker decode (reason=unsupported_animated_sticker) path={Path}", filePath);
            return new MediaProcessingResult
            {
                Success = true,
                Description = "Animated sticker (TGS), visual preview unavailable.",
                Confidence = 0.3f
            };
        }

        var contentHash = await ComputeContentHashAsync(filePath, ct);
        var cached = await _stickerCacheRepository.GetByHashAsync(contentHash, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Sticker cache hit for hash {Hash}", contentHash);
            return new MediaProcessingResult
            {
                Success = true,
                Description = cached.Description,
                Confidence = 0.99f
            };
        }

        var result = await ProcessImageAsync(filePath, "Describe this sticker in 1 short sentence: object + emotion.", ct);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Description))
        {
            await _stickerCacheRepository.UpsertAsync(contentHash, result.Description, ResolveVisionModel(filePath), ct);
        }

        return result;
    }

    private async Task<MediaProcessingResult> ProcessImageAsync(
        string filePath, string prompt, CancellationToken ct)
    {
        var optimizedPath = filePath;
        try
        {
            optimizedPath = await OptimizeImageForVisionAsync(filePath, ct);

            var bytes = await File.ReadAllBytesAsync(optimizedPath, ct);
            var base64 = Convert.ToBase64String(bytes);
            var mimeType = DetectMimeType(optimizedPath);

            var isArchive = IsArchiveMediaPath(filePath);
            var primaryModel = isArchive ? _mediaSettings.ArchiveVisionModel : _mediaSettings.VisionModel;
            var fallbackModel = isArchive ? _mediaSettings.VisionModel : null;
            var maxTokens = isArchive ? _mediaSettings.ArchiveVisionMaxTokens : _mediaSettings.VisionMaxTokens;

            var response = await SendImageRequestWithFallbackAsync(primaryModel, fallbackModel, maxTokens, mimeType, base64, prompt, ct);

            return new MediaProcessingResult
            {
                Success = true,
                Description = response,
                Confidence = 0.9f
            };
        }
        finally
        {
            if (!string.Equals(optimizedPath, filePath, StringComparison.OrdinalIgnoreCase) && File.Exists(optimizedPath))
            {
                File.Delete(optimizedPath);
            }
        }
    }

    private async Task<string> SendImageRequestWithFallbackAsync(
        string primaryModel,
        string? fallbackModel,
        int maxTokens,
        string mimeType,
        string base64,
        string prompt,
        CancellationToken ct)
    {
        try
        {
            return await SendRequestAsync(BuildImageRequest(primaryModel, maxTokens, mimeType, base64, prompt), ct);
        }
        catch (Exception ex) when (!string.IsNullOrWhiteSpace(fallbackModel) && !string.Equals(primaryModel, fallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex,
                "Primary vision model failed ({PrimaryModel}), switching to fallback ({FallbackModel})",
                primaryModel,
                fallbackModel);
            return await SendRequestAsync(BuildImageRequest(fallbackModel!, maxTokens, mimeType, base64, prompt), ct);
        }
    }

    private OpenRouterRequest BuildImageRequest(string model, int maxTokens, string mimeType, string base64, string prompt)
    {
        return new OpenRouterRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenRouterMessage
                {
                    Role = "user",
                    Content = new object[]
                    {
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64}" } },
                        new { type = "text", text = prompt }
                    }
                }
            },
            MaxTokens = maxTokens
        };
    }

    private async Task<MediaProcessingResult> ProcessAudioAsync(
        string filePath, CancellationToken ct)
    {
        // Convert to wav if needed (GPT Audio Mini expects wav)
        var wavPath = filePath;
        var tempCreated = false;
        if (!filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            wavPath = Path.Combine(Path.GetTempPath(), $"tgassistant_{Guid.NewGuid():N}.proc.wav");
            tempCreated = true;
            await ConvertToWavAsync(filePath, wavPath, ct);
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(wavPath, ct);
            var base64 = Convert.ToBase64String(bytes);

            var request = new OpenRouterRequest
            {
                Model = _mediaSettings.AudioModel,
                Messages = new[]
                {
                    new OpenRouterMessage
                    {
                        Role = "user",
                        Content = new object[]
                        {
                            new { type = "input_audio", input_audio = new { data = base64, format = "wav" } },
                            new { type = "text", text = "Transcribe this audio message exactly. If you cannot understand it, describe what you hear (noise, music, etc). Respond ONLY with the transcription, no commentary." }
                        }
                    }
                },
                MaxTokens = 1000
            };

            var response = await SendRequestAsync(request, ct);

            return new MediaProcessingResult
            {
                Success = true,
                Transcription = response,
                Confidence = 0.9f
            };
        }
        finally
        {
            if (tempCreated && File.Exists(wavPath))
            {
                File.Delete(wavPath);
            }
        }
    }

    private async Task<string> OptimizeImageForVisionAsync(string inputPath, CancellationToken ct)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"tgassistant_{Guid.NewGuid():N}.jpg");
        var maxSide = Math.Max(320, _mediaSettings.MaxImageLongSide);

        // Convert any image-like input to JPEG and cap long side to control token usage.
        var filter = $"scale='if(gt(iw,ih),min(iw,{maxSide}),-2)':'if(gt(iw,ih),-2,min(ih,{maxSide}))'";
        var qScale = JpegQualityToQScale(_mediaSettings.JpegQuality);
        var args = $"-i \"{inputPath}\" -vf {filter} -frames:v 1 -q:v {qScale} \"{outputPath}\" -y";

        await RunFfmpegAsync(args, ct);
        return outputPath;
    }

    private async Task<string> SendRequestAsync(OpenRouterRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var response = await _http.PostAsync("/api/v1/chat/completions", content, cts.Token);
        var responseJson = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenRouter API error {Code}: {Body}", response.StatusCode, responseJson);
            throw new HttpRequestException($"OpenRouter API error {response.StatusCode}: {responseJson}");
        }

        var result = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson, JsonOptions);
        var text = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "";

        _logger.LogDebug("OpenRouter response ({Model}): {Text}",
            request.Model, text.Length > 100 ? text[..100] + "..." : text);

        return text;
    }

    private string BuildPhotoPrompt(string filePath)
    {
        if (IsArchiveMediaPath(filePath))
        {
            return "Give a very short description (max 20 words): key objects and context only. If visible text exists, include only critical text.";
        }

        return "Describe this image in detail. If there is text on the image, transcribe it. If it's a meme, describe the meme and its meaning. Respond in the same language as any text found.";
    }

    private bool IsArchiveMediaPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(filePath);
        var archiveRoot = string.IsNullOrWhiteSpace(_archiveSettings.MediaBasePath)
            ? "/data/archive"
            : _archiveSettings.MediaBasePath;

        return fullPath.StartsWith(Path.GetFullPath(archiveRoot), StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveVisionModel(string filePath)
    {
        return IsArchiveMediaPath(filePath) ? _mediaSettings.ArchiveVisionModel : _mediaSettings.VisionModel;
    }

    private static async Task<string> ComputeContentHashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hashBytes);
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

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"ffmpeg failed: {error}");
        }
    }

    private static int JpegQualityToQScale(int quality)
    {
        var q = Math.Clamp(quality, 1, 100);
        return Math.Clamp(2 + (100 - q) / 6, 2, 31);
    }

    private static string DetectMimeType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string ClassifyFailureReason(Exception ex, string filePath, MediaType mediaType)
    {
        if (ex is IOException or UnauthorizedAccessException)
        {
            return "audio_temp_io";
        }

        var message = ex.Message;
        if (message.Contains("Read-only file system", StringComparison.OrdinalIgnoreCase))
        {
            return "audio_temp_io";
        }

        if (mediaType == MediaType.Sticker && filePath.EndsWith(".tgs", StringComparison.OrdinalIgnoreCase))
        {
            return "unsupported_animated_sticker";
        }

        if (message.Contains("ffmpeg failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase))
        {
            return "ffmpeg_decode";
        }

        return "media_processing_error";
    }
}

// === DTOs for OpenRouter API ===

internal class OpenRouterRequest
{
    public string Model { get; set; } = "";
    public OpenRouterMessage[] Messages { get; set; } = Array.Empty<OpenRouterMessage>();
    public int? MaxTokens { get; set; }
}

internal class OpenRouterMessage
{
    public string Role { get; set; } = "";
    public object Content { get; set; } = "";
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
    public string Content { get; set; } = "";
}

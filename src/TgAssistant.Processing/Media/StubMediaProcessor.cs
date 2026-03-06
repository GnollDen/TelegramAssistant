using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Media;

/// <summary>
/// Stub: marks all media as pending review. Gemini integration in Stage 3.
/// </summary>
public class StubMediaProcessor : IMediaProcessor
{
    public Task<MediaProcessingResult> ProcessAsync(
        string filePath, MediaType mediaType, CancellationToken ct = default)
    {
        return Task.FromResult(new MediaProcessingResult
        {
            Success = false,
            FailureReason = $"Stub processor: {mediaType} at {filePath}. Gemini not configured yet.",
            Confidence = 0f
        });
    }
}
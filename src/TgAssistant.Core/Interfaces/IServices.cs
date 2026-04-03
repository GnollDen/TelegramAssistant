using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IMessageQueue
{
    Task EnqueueAsync(RawTelegramMessage message, CancellationToken ct = default);
    Task<List<RawTelegramMessage>> DequeueAsync(int maxCount, TimeSpan timeout, CancellationToken ct = default);
    Task AcknowledgeAsync(IEnumerable<string> messageIds, CancellationToken ct = default);
}

public interface IMediaProcessor
{
    Task<MediaProcessingResult> ProcessAsync(string filePath, MediaType mediaType, CancellationToken ct = default);
}

public interface IVoiceParalinguisticsAnalyzer
{
    Task<string> AnalyzeAsync(string filePath, CancellationToken ct = default);
}

public interface ITextEmbeddingGenerator
{
    Task<float[]> GenerateAsync(string model, string input, CancellationToken ct = default);
}

public interface IStickerCacheRepository
{
    Task<StickerCacheItem?> GetByHashAsync(string contentHash, CancellationToken ct = default);
    Task UpsertAsync(string contentHash, string description, string model, CancellationToken ct = default);
}

public class RawTelegramMessage
{
    public string StreamId { get; set; } = string.Empty;
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Text { get; set; }
    public MediaType MediaType { get; set; }
    public string? MediaPath { get; set; }
    public long? ReplyToMessageId { get; set; }
    public DateTime? EditTimestamp { get; set; }
    public string? ReactionsJson { get; set; }
    public string? ForwardJson { get; set; }
}

public class MediaProcessingResult
{
    public bool Success { get; set; }
    public string? Transcription { get; set; }
    public string? Description { get; set; }
    public string? Sentiment { get; set; }
    public float Confidence { get; set; }
    public string? FailureReason { get; set; }
}

public class StickerCacheItem
{
    public string ContentHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

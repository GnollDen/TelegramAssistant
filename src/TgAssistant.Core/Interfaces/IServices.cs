using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

/// <summary>
/// Message queue for buffering between listener and batch worker.
/// </summary>
public interface IMessageQueue
{
    Task EnqueueAsync(RawTelegramMessage message, CancellationToken ct = default);
    Task<List<RawTelegramMessage>> DequeueAsync(int maxCount, TimeSpan timeout, CancellationToken ct = default);
    Task AcknowledgeAsync(IEnumerable<string> messageIds, CancellationToken ct = default);
}

/// <summary>
/// Media processing via Gemini Flash Lite.
/// </summary>
public interface IMediaProcessor
{
    Task<MediaProcessingResult> ProcessAsync(string filePath, MediaType mediaType, CancellationToken ct = default);
}

/// <summary>
/// Intelligence layer - Claude API for dossier and chat.
/// </summary>
public interface IIntelligenceService
{
    Task<DossierDiff> UpdateDossierAsync(Guid entityId, List<Message> newMessages, CancellationToken ct = default);
    Task<string> ChatAsync(string userMessage, Guid? contextEntityId = null, CancellationToken ct = default);
    Task<List<string>> GenerateInlineRepliesAsync(Guid entityId, List<Message> recentMessages, CancellationToken ct = default);
}

// ===== DTOs =====

/// <summary>
/// Raw message from Telegram listener, before batch processing.
/// </summary>
public class RawTelegramMessage
{
    public string StreamId { get; set; } = string.Empty; // Redis stream ID for ACK
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

public class DossierDiff
{
    public List<Entity> NewEntities { get; set; } = new();
    public List<Relationship> NewRelationships { get; set; } = new();
    public List<Fact> NewFacts { get; set; } = new();
    public List<Fact> UpdatedFacts { get; set; } = new();
    public List<Guid> SupersededFactIds { get; set; } = new();
}

namespace TgAssistant.Core.Models;

/// <summary>
/// Raw message from Telegram, stored after batch processing.
/// </summary>
public class Message
{
    public long Id { get; set; }
    public long TelegramMessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ForwardJson { get; set; }
    
    // Content
    public string? Text { get; set; }
    public MediaType MediaType { get; set; } = MediaType.None;
    public string? MediaPath { get; set; }
    public string? MediaTranscription { get; set; }
    public string? MediaDescription { get; set; }
    
    // Context
    public long? ReplyToMessageId { get; set; }
    public DateTime? EditTimestamp { get; set; }
    public string? ReactionsJson { get; set; }
    
    // Processing
    public MessageSource Source { get; set; } = MessageSource.Realtime;
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum MediaType
{
    None,
    Photo,
    Voice,
    VideoNote,  // circles
    Video,
    Document,
    Sticker,
    Animation   // GIFs
}

public enum MessageSource
{
    Realtime,
    Archive
}

public enum ProcessingStatus
{
    Pending,
    Processed,
    Failed,
    PendingReview  // unrecognized media, needs manual context
}

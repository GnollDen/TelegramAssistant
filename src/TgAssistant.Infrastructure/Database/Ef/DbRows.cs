namespace TgAssistant.Infrastructure.Database.Ef;

public class DbMessage
{
    public long Id { get; set; }
    public long TelegramMessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? Text { get; set; }
    public short MediaType { get; set; }
    public string? MediaPath { get; set; }
    public string? MediaDescription { get; set; }
    public string? MediaTranscription { get; set; }
    public long? ReplyToMessageId { get; set; }
    public DateTime? EditTimestamp { get; set; }
    public string? ReactionsJson { get; set; }
    public string? ForwardJson { get; set; }
    public short ProcessingStatus { get; set; }
    public short Source { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbArchiveImportRun
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public short Status { get; set; }
    public int LastMessageIndex { get; set; }
    public long ImportedMessages { get; set; }
    public long QueuedMedia { get; set; }
    public long TotalMessages { get; set; }
    public long TotalMedia { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntity
{
    public Guid Id { get; set; }
    public short Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public string Metadata { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbFact
{
    public Guid Id { get; set; }
    public Guid EntityId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public short Status { get; set; }
    public float Confidence { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbRelationship
{
    public Guid Id { get; set; }
    public Guid FromEntityId { get; set; }
    public Guid ToEntityId { get; set; }
    public string Type { get; set; } = string.Empty;
    public short Status { get; set; }
    public float Confidence { get; set; }
    public string? ContextText { get; set; }
    public long? SourceMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbDailySummary
{
    public Guid Id { get; set; }
    public long ChatId { get; set; }
    public Guid? EntityId { get; set; }
    public DateTime Date { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int MessageCount { get; set; }
    public int MediaCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbPromptTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbAnalysisState
{
    public string Key { get; set; } = string.Empty;
    public long Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbMessageExtraction
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public string CheapJson { get; set; } = "{}";
    public string? ExpensiveJson { get; set; }
    public bool NeedsExpensive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbStickerCache
{
    public string ContentHash { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public long HitCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
}

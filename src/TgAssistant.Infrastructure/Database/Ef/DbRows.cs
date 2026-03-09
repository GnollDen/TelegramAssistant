using System.Text.Json;

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
    public bool NeedsReanalysis { get; set; }
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
    public string? ActorKey { get; set; }
    public long? TelegramUserId { get; set; }
    public string? TelegramUsername { get; set; }
    public JsonDocument Metadata { get; set; } = JsonDocument.Parse("{}");
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntityAlias
{
    public long Id { get; set; }
    public Guid EntityId { get; set; }
    public string Alias { get; set; } = string.Empty;
    public string AliasNorm { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntityMergeCandidate
{
    public long Id { get; set; }
    public Guid EntityLowId { get; set; }
    public Guid EntityHighId { get; set; }
    public string AliasNorm { get; set; } = string.Empty;
    public int EvidenceCount { get; set; }
    public float Score { get; set; }
    public short ReviewPriority { get; set; }
    public short Status { get; set; }
    public string? DecisionNote { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbEntityMergeDecision
{
    public long Id { get; set; }
    public long? CandidateId { get; set; }
    public Guid EntityLowId { get; set; }
    public Guid EntityHighId { get; set; }
    public string AliasNorm { get; set; } = string.Empty;
    public short Decision { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbEntityMergeCommand
{
    public long Id { get; set; }
    public long CandidateId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public short Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class DbFactReviewCommand
{
    public long Id { get; set; }
    public Guid FactId { get; set; }
    public string Command { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public short Status { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
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
    public int ExpensiveRetryCount { get; set; }
    public DateTime? ExpensiveNextRetryAt { get; set; }
    public string? ExpensiveLastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DbExtractionError
{
    public long Id { get; set; }
    public string Stage { get; set; } = string.Empty;
    public long? MessageId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class DbStage5MetricsSnapshot
{
    public long Id { get; set; }
    public DateTime CapturedAt { get; set; }
    public long ProcessedMessages { get; set; }
    public long ExtractionsTotal { get; set; }
    public long ExpensiveBacklog { get; set; }
    public long MergeCandidatesPending { get; set; }
    public long FactReviewsPending { get; set; }
    public long ExtractionErrors1h { get; set; }
    public long AnalysisRequests1h { get; set; }
    public long AnalysisTokens1h { get; set; }
    public decimal AnalysisCostUsd1h { get; set; }
}

public class DbAnalysisUsageEvent
{
    public long Id { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAt { get; set; }
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

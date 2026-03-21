namespace TgAssistant.Core.Models;

public class OfflineEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long CaseId { get; set; }
    public long? ChatId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UserSummary { get; set; } = string.Empty;
    public string? AutoSummary { get; set; }
    public DateTime TimestampStart { get; set; }
    public DateTime? TimestampEnd { get; set; }
    public Guid? PeriodId { get; set; }
    public string ReviewStatus { get; set; } = "pending";
    public string? ImpactSummary { get; set; }
    public string SourceType { get; set; } = "user";
    public string SourceId { get; set; } = string.Empty;
    public long? SourceMessageId { get; set; }
    public Guid? SourceSessionId { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AudioAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OfflineEventId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int? DurationSeconds { get; set; }
    public string TranscriptStatus { get; set; } = "pending";
    public string? TranscriptText { get; set; }
    public string SpeakerReviewStatus { get; set; } = "pending";
    public string ProcessingStatus { get; set; } = "pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class AudioSegment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioAssetId { get; set; }
    public int SegmentIndex { get; set; }
    public decimal StartSeconds { get; set; }
    public decimal EndSeconds { get; set; }
    public string? SpeakerLabel { get; set; }
    public string TranscriptText { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AudioSnippet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AudioAssetId { get; set; }
    public Guid? AudioSegmentId { get; set; }
    public string SnippetType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string EvidenceRefsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

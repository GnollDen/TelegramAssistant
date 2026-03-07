namespace TgAssistant.Core.Models;

public class ArchiveImportRun
{
    public Guid Id { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public ArchiveImportRunStatus Status { get; set; } = ArchiveImportRunStatus.Running;
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

public enum ArchiveImportRunStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    AwaitingConfirmation = 3
}

public class ArchiveCostEstimate
{
    public long TotalMessages { get; set; }
    public long MediaMessages { get; set; }
    public long ImageLikeMedia { get; set; }
    public long AudioLikeMedia { get; set; }
    public long VideoLikeMedia { get; set; }
    public decimal EstimatedCostUsd { get; set; }
}

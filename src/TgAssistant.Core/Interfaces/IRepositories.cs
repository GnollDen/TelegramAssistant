using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IMessageRepository
{
    Task<long> SaveBatchAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    Task<List<Message>> GetUnprocessedAsync(int limit = 100, CancellationToken ct = default);
    Task<List<Message>> GetByContactSinceAsync(long chatId, DateTime since, CancellationToken ct = default);
    Task<List<Message>> GetProcessedAfterIdAsync(long afterId, int limit, CancellationToken ct = default);
    Task MarkProcessedAsync(IEnumerable<long> messageIds, CancellationToken ct = default);
    Task<List<Message>> GetPendingArchiveMediaAsync(int limit, CancellationToken ct = default);
    Task UpdateMediaProcessingResultAsync(long messageId, MediaProcessingResult result, ProcessingStatus status, CancellationToken ct = default);
}

public interface IArchiveImportRepository
{
    Task<ArchiveImportRun?> GetRunningRunAsync(string sourcePath, CancellationToken ct = default);
    Task<ArchiveImportRun?> GetLatestRunAsync(string sourcePath, CancellationToken ct = default);
    Task<ArchiveImportRun> CreateRunAsync(ArchiveImportRun run, CancellationToken ct = default);
    Task UpsertEstimateAsync(string sourcePath, ArchiveCostEstimate estimate, ArchiveImportRunStatus status, CancellationToken ct = default);
    Task UpdateProgressAsync(Guid runId, int lastMessageIndex, long importedMessages, long queuedMedia, CancellationToken ct = default);
    Task CompleteRunAsync(Guid runId, ArchiveImportRunStatus status, string? error, CancellationToken ct = default);
}

public interface IEntityRepository
{
    Task<Entity> UpsertAsync(Entity entity, CancellationToken ct = default);
    Task<Entity?> FindByTelegramIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<Entity?> FindByNameOrAliasAsync(string name, CancellationToken ct = default);
    Task<List<Entity>> GetAllAsync(CancellationToken ct = default);
}

public interface IRelationshipRepository
{
    Task<Relationship> UpsertAsync(Relationship relationship, CancellationToken ct = default);
    Task<List<Relationship>> GetByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default);
}

public interface IFactRepository
{
    Task<Fact> UpsertAsync(Fact fact, CancellationToken ct = default);
    Task<List<Fact>> GetCurrentByEntityAsync(Guid entityId, CancellationToken ct = default);
    Task SupersedeFactAsync(Guid oldFactId, Fact newFact, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, ConfidenceStatus status, CancellationToken ct = default);
}

public interface ISummaryRepository
{
    Task SaveAsync(DailySummary summary, CancellationToken ct = default);
    Task<List<DailySummary>> GetByEntityAsync(Guid entityId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

public interface IPromptTemplateRepository
{
    Task<PromptTemplate?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<PromptTemplate> UpsertAsync(PromptTemplate template, CancellationToken ct = default);
}

public interface IAnalysisStateRepository
{
    Task<long> GetWatermarkAsync(string key, CancellationToken ct = default);
    Task SetWatermarkAsync(string key, long value, CancellationToken ct = default);
}

public interface IMessageExtractionRepository
{
    Task UpsertCheapAsync(long messageId, string cheapJson, bool needsExpensive, CancellationToken ct = default);
    Task<List<MessageExtractionRecord>> GetExpensiveBacklogAsync(int limit, CancellationToken ct = default);
    Task ResolveExpensiveAsync(long extractionId, string expensiveJson, CancellationToken ct = default);
}

public class MessageExtractionRecord
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public string CheapJson { get; set; } = "{}";
    public string? ExpensiveJson { get; set; }
    public bool NeedsExpensive { get; set; }
}

using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IMessageRepository
{
    Task<long> SaveBatchAsync(IEnumerable<Message> messages, CancellationToken ct = default);
    Task<List<Message>> GetUnprocessedAsync(int limit = 100, CancellationToken ct = default);
    Task<List<Message>> GetByContactSinceAsync(long chatId, DateTime since, CancellationToken ct = default);
    Task MarkProcessedAsync(IEnumerable<long> messageIds, CancellationToken ct = default);
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

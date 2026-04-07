using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public interface IResolutionCaseReintegrationTransactionalService
{
    Task<ResolutionCaseReintegrationLedgerEntry> RecordWithinDbContextAsync(
        TgAssistantDbContext db,
        ResolutionCaseReintegrationRecordRequest request,
        CancellationToken ct = default);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class FoundationDomainVerificationService
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IAnalysisStateRepository _analysisStateRepository;
    private readonly IStage5MetricsRepository _stage5MetricsRepository;
    private readonly ILogger<FoundationDomainVerificationService> _logger;

    public FoundationDomainVerificationService(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IAnalysisStateRepository analysisStateRepository,
        IStage5MetricsRepository stage5MetricsRepository,
        ILogger<FoundationDomainVerificationService> logger)
    {
        _dbFactory = dbFactory;
        _analysisStateRepository = analysisStateRepository;
        _stage5MetricsRepository = stage5MetricsRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!await db.Database.CanConnectAsync(ct))
        {
            throw new InvalidOperationException("Foundation verification failed: database connectivity check failed.");
        }

        // Validate that core active baseline tables are reachable.
        var totalMessages = await db.Messages.AsNoTracking().LongCountAsync(ct);
        var totalExtractions = await db.MessageExtractions.AsNoTracking().LongCountAsync(ct);
        var totalRuntimeControlRows = await db.RuntimeControlStates.AsNoTracking().LongCountAsync(ct);
        var totalRecomputeQueueRows = await db.Stage8RecomputeQueueItems.AsNoTracking().LongCountAsync(ct);

        // Validate active write/read path through analysis_state.
        var watermarkKey = $"foundation:smoke:active:{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var watermarkValue = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await _analysisStateRepository.SetWatermarkAsync(watermarkKey, watermarkValue, ct);
        var persistedWatermark = await _analysisStateRepository.GetWatermarkAsync(watermarkKey, ct);
        if (persistedWatermark < watermarkValue)
        {
            throw new InvalidOperationException("Foundation verification failed: active analysis_state write/read check did not pass.");
        }

        // Validate active observability path remains healthy.
        var snapshot = await _stage5MetricsRepository.CaptureAsync(ct);
        if (snapshot.CapturedAt == default)
        {
            throw new InvalidOperationException("Foundation verification failed: Stage5 metrics capture did not produce a valid snapshot.");
        }

        _logger.LogInformation(
            "Foundation verification passed. messages={Messages}, extractions={Extractions}, runtime_control_rows={RuntimeControlRows}, recompute_queue_rows={RecomputeQueueRows}, watermark_key={WatermarkKey}",
            totalMessages,
            totalExtractions,
            totalRuntimeControlRows,
            totalRecomputeQueueRows,
            watermarkKey);
    }
}

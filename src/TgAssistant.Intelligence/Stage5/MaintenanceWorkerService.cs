using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Intelligence.Stage5;

public class MaintenanceWorkerService : BackgroundService
{
    private readonly MaintenanceSettings _settings;
    private readonly IMaintenanceRepository _maintenanceRepository;
    private readonly ILogger<MaintenanceWorkerService> _logger;

    public MaintenanceWorkerService(
        IOptions<MaintenanceSettings> settings,
        IMaintenanceRepository maintenanceRepository,
        ILogger<MaintenanceWorkerService> logger)
    {
        _settings = settings.Value;
        _maintenanceRepository = maintenanceRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Maintenance worker is disabled");
            return;
        }

        _logger.LogInformation(
            "Maintenance worker started. poll={Poll}m err_retention={Err}d metrics_retention={Metrics}d merge_retention={Merge}d fact_review_retention={FactReview}d fact_review_pending_timeout={FactReviewPendingTimeout}d",
            _settings.PollIntervalMinutes,
            _settings.ExtractionErrorsRetentionDays,
            _settings.Stage5MetricsRetentionDays,
            _settings.MergeDecisionsRetentionDays,
            _settings.FactReviewCommandsRetentionDays,
            _settings.FactReviewPendingTimeoutDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await _maintenanceRepository.CleanupAsync(new MaintenanceCleanupRequest
                {
                    ExtractionErrorsRetentionDays = _settings.ExtractionErrorsRetentionDays,
                    Stage5MetricsRetentionDays = _settings.Stage5MetricsRetentionDays,
                    MergeDecisionsRetentionDays = _settings.MergeDecisionsRetentionDays,
                    FactReviewCommandsRetentionDays = _settings.FactReviewCommandsRetentionDays,
                    FactReviewPendingTimeoutDays = _settings.FactReviewPendingTimeoutDays
                }, stoppingToken);

                if (result.ExtractionErrorsDeleted > 0
                    || result.Stage5MetricsDeleted > 0
                    || result.MergeDecisionsDeleted > 0
                    || result.FactReviewCommandsDeleted > 0
                    || result.FactReviewCommandsTimedOut > 0)
                {
                    _logger.LogInformation(
                        "Maintenance cleanup done: extraction_errors={Err}, metrics={Metrics}, merge_decisions={Merge}, fact_review_commands_deleted={FactReviewDeleted}, fact_review_commands_timed_out={FactReviewTimedOut}",
                        result.ExtractionErrorsDeleted,
                        result.Stage5MetricsDeleted,
                        result.MergeDecisionsDeleted,
                        result.FactReviewCommandsDeleted,
                        result.FactReviewCommandsTimedOut);
                }
                else
                {
                    _logger.LogDebug("Maintenance cleanup done: no rows removed");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Maintenance cleanup failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(5, _settings.PollIntervalMinutes)), stoppingToken);
        }
    }
}

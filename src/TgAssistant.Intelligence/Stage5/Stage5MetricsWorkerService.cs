using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Intelligence.Stage5;

public class Stage5MetricsWorkerService : BackgroundService
{
    private readonly MonitoringSettings _settings;
    private readonly IStage5MetricsRepository _metricsRepository;
    private readonly ILogger<Stage5MetricsWorkerService> _logger;

    public Stage5MetricsWorkerService(
        IOptions<MonitoringSettings> settings,
        IStage5MetricsRepository metricsRepository,
        ILogger<Stage5MetricsWorkerService> logger)
    {
        _settings = settings.Value;
        _metricsRepository = metricsRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Stage5 metrics worker is disabled");
            return;
        }

        _logger.LogInformation("Stage5 metrics worker started. poll={Poll}s", _settings.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _metricsRepository.CaptureAsync(stoppingToken);
                await _metricsRepository.SaveSnapshotAsync(snapshot, stoppingToken);
                _logger.LogInformation(
                    "Stage5 metrics snapshot: processed={Processed} extracted={Extracted} expensive_backlog={Backlog} merge_pending={MergePending} errors_1h={Errors}",
                    snapshot.ProcessedMessages,
                    snapshot.ExtractionsTotal,
                    snapshot.ExpensiveBacklog,
                    snapshot.MergeCandidatesPending,
                    snapshot.ExtractionErrors1h);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage5 metrics snapshot failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _settings.PollIntervalSeconds)), stoppingToken);
        }
    }
}

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
    private Stage5MetricsSnapshot? _previousSnapshot;

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
                    "Stage5 metrics snapshot: processed={Processed} extracted={Extracted} expensive_backlog={Backlog} merge_pending={MergePending} errors_1h={Errors} pending_sessions_queue={PendingSessionsQueue} reanalysis_backlog={ReanalysisBacklog} quarantine_total={QuarantineTotal} quarantine_stuck={QuarantineStuck}",
                    snapshot.ProcessedMessages,
                    snapshot.ExtractionsTotal,
                    snapshot.ExpensiveBacklog,
                    snapshot.MergeCandidatesPending,
                    snapshot.ExtractionErrors1h,
                    snapshot.PendingSessionsQueue,
                    snapshot.ReanalysisBacklog,
                    snapshot.QuarantineTotal,
                    snapshot.QuarantineStuck);
                if (_previousSnapshot != null)
                {
                    _logger.LogInformation(
                        "Stage5 metrics delta_since_last_poll (rolling-window gauges may be negative): processed_delta={ProcessedDelta} extracted_delta={ExtractedDelta} backlog_delta={BacklogDelta} errors_1h_delta={ErrorsDelta} requests_1h_delta={RequestsDelta} tokens_1h_delta={TokensDelta} cost_1h_delta={CostDelta} pending_sessions_queue_delta={PendingSessionsQueueDelta} reanalysis_backlog_delta={ReanalysisBacklogDelta} quarantine_total_delta={QuarantineTotalDelta} quarantine_stuck_delta={QuarantineStuckDelta}",
                        snapshot.ProcessedMessages - _previousSnapshot.ProcessedMessages,
                        snapshot.ExtractionsTotal - _previousSnapshot.ExtractionsTotal,
                        snapshot.ExpensiveBacklog - _previousSnapshot.ExpensiveBacklog,
                        snapshot.ExtractionErrors1h - _previousSnapshot.ExtractionErrors1h,
                        snapshot.AnalysisRequests1h - _previousSnapshot.AnalysisRequests1h,
                        snapshot.AnalysisTokens1h - _previousSnapshot.AnalysisTokens1h,
                        snapshot.AnalysisCostUsd1h - _previousSnapshot.AnalysisCostUsd1h,
                        snapshot.PendingSessionsQueue - _previousSnapshot.PendingSessionsQueue,
                        snapshot.ReanalysisBacklog - _previousSnapshot.ReanalysisBacklog,
                        snapshot.QuarantineTotal - _previousSnapshot.QuarantineTotal,
                        snapshot.QuarantineStuck - _previousSnapshot.QuarantineStuck);
                }

                _previousSnapshot = snapshot;
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

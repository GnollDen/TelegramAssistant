using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage8Recompute;

public class Stage8RecomputeWorkerService : BackgroundService
{
    private static readonly TimeSpan BusyDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(3);

    private readonly IStage8RecomputeQueueService _queueService;
    private readonly ILogger<Stage8RecomputeWorkerService> _logger;

    public Stage8RecomputeWorkerService(
        IStage8RecomputeQueueService queueService,
        ILogger<Stage8RecomputeWorkerService> logger)
    {
        _queueService = queueService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Stage8 recompute worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var execution = await _queueService.ExecuteNextAsync(stoppingToken);
                if (!execution.Executed
                    || string.Equals(execution.ExecutionStatus, Stage8RecomputeExecutionStatuses.NoWorkAvailable, StringComparison.Ordinal))
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                await Task.Delay(BusyDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage8 recompute worker loop failed");
                await Task.Delay(ErrorDelay, stoppingToken);
            }
        }
    }
}

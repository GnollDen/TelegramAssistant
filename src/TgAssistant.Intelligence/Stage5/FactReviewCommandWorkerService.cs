using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class FactReviewCommandWorkerService : BackgroundService
{
    private readonly AnalysisSettings _analysisSettings;
    private readonly IFactReviewCommandRepository _commandRepository;
    private readonly IFactRepository _factRepository;
    private readonly ILogger<FactReviewCommandWorkerService> _logger;

    public FactReviewCommandWorkerService(
        IOptions<AnalysisSettings> analysisSettings,
        IFactReviewCommandRepository commandRepository,
        IFactRepository factRepository,
        ILogger<FactReviewCommandWorkerService> logger)
    {
        _analysisSettings = analysisSettings.Value;
        _commandRepository = commandRepository;
        _factRepository = factRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_analysisSettings.Enabled)
        {
            _logger.LogInformation("Fact review command worker is disabled because analysis is disabled");
            return;
        }

        _logger.LogInformation("Fact review command worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var batchSize = Math.Max(1, _analysisSettings.FactReviewBatchSize);
            var pending = await _commandRepository.GetPendingAsync(batchSize, stoppingToken);
            var processed = 0;
            foreach (var cmd in pending)
            {
                try
                {
                    await ExecuteCommandAsync(cmd, stoppingToken);
                    await _commandRepository.MarkDoneAsync(cmd.Id, stoppingToken);
                    processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fact review command failed. command_id={CommandId}", cmd.Id);
                    await _commandRepository.MarkFailedAsync(cmd.Id, ex.Message, stoppingToken);
                }
            }

            if (processed > 0)
            {
                _logger.LogInformation("Fact review command pass done: processed={Processed}, batch_size={BatchSize}", processed, batchSize);
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _analysisSettings.PollIntervalSeconds)), stoppingToken);
        }
    }

    private async Task ExecuteCommandAsync(FactReviewCommand cmd, CancellationToken ct)
    {
        var fact = await _factRepository.GetByIdAsync(cmd.FactId, ct);
        if (fact == null)
        {
            throw new InvalidOperationException($"fact_not_found:{cmd.FactId}");
        }

        var action = (cmd.Command ?? string.Empty).Trim().ToLowerInvariant();
        if (action is "approve" or "confirm")
        {
            if (fact.Status == ConfidenceStatus.Confirmed)
            {
                _logger.LogDebug("Fact already confirmed: fact_id={FactId}", cmd.FactId);
                return;
            }

            await _factRepository.UpdateStatusAsync(cmd.FactId, ConfidenceStatus.Confirmed, ct);
            _logger.LogInformation("Fact confirmed manually: fact_id={FactId}", cmd.FactId);
            return;
        }

        if (action is "reject" or "decline")
        {
            if (fact.Status == ConfidenceStatus.Rejected)
            {
                _logger.LogDebug("Fact already rejected: fact_id={FactId}", cmd.FactId);
                return;
            }

            await _factRepository.UpdateStatusAsync(cmd.FactId, ConfidenceStatus.Rejected, ct);
            _logger.LogInformation("Fact rejected manually: fact_id={FactId}", cmd.FactId);
            return;
        }

        throw new InvalidOperationException($"unsupported_fact_command:{cmd.Command}");
    }
}

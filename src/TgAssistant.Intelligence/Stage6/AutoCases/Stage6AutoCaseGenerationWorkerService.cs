using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Intelligence.Stage6.AutoCases;

public class Stage6AutoCaseGenerationWorkerService : BackgroundService
{
    private readonly Stage6AutoCaseGenerationSettings _settings;
    private readonly Stage6AutoCaseGenerationService _service;
    private readonly ILogger<Stage6AutoCaseGenerationWorkerService> _logger;

    public Stage6AutoCaseGenerationWorkerService(
        IOptions<Stage6AutoCaseGenerationSettings> settings,
        Stage6AutoCaseGenerationService service,
        ILogger<Stage6AutoCaseGenerationWorkerService> logger)
    {
        _settings = settings.Value;
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Stage6 auto-case generation worker is disabled.");
            return;
        }

        _logger.LogInformation(
            "Stage6 auto-case generation worker started. poll_interval_s={PollIntervalSeconds}, lookback_h={LookbackHours}",
            Math.Max(10, _settings.PollIntervalSeconds),
            Math.Max(1, _settings.ScopeLookbackHours));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _ = await _service.RunOnceAsync(force: false, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage6 auto-case generation loop failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(10, _settings.PollIntervalSeconds)), stoppingToken);
        }
    }
}

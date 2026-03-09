using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Processing.Workers;

public class VoiceParalinguisticsWorkerService : BackgroundService
{
    private readonly VoiceParalinguisticsSettings _settings;
    private readonly ArchiveImportSettings _archiveImportSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IVoiceParalinguisticsAnalyzer _analyzer;
    private readonly IExtractionErrorRepository _errorRepository;
    private readonly ILogger<VoiceParalinguisticsWorkerService> _logger;

    public VoiceParalinguisticsWorkerService(
        IOptions<VoiceParalinguisticsSettings> settings,
        IOptions<ArchiveImportSettings> archiveImportSettings,
        IMessageRepository messageRepository,
        IVoiceParalinguisticsAnalyzer analyzer,
        IExtractionErrorRepository errorRepository,
        ILogger<VoiceParalinguisticsWorkerService> logger)
    {
        _settings = settings.Value;
        _archiveImportSettings = archiveImportSettings.Value;
        _messageRepository = messageRepository;
        _analyzer = analyzer;
        _errorRepository = errorRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Voice paralinguistics worker is disabled");
            return;
        }

        _logger.LogInformation(
            "Voice paralinguistics worker started. model={Model}, batch={BatchSize}, parallel={Parallel}",
            _settings.Model,
            _settings.BatchSize,
            _settings.MaxParallel);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batch = await _messageRepository.GetPendingVoiceParalinguisticsAsync(_settings.BatchSize, stoppingToken);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                await Parallel.ForEachAsync(
                    batch,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, _settings.MaxParallel),
                        CancellationToken = stoppingToken
                    },
                    async (message, ct) => await ProcessMessageAsync(message.Id, message.MediaPath, ct));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Voice paralinguistics loop failed");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.PollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private async Task ProcessMessageAsync(long messageId, string? mediaPath, CancellationToken ct)
    {
        var resolvedPath = ResolveAccessiblePath(mediaPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            _logger.LogDebug("Voice paralinguistics skipped missing file for message_id={MessageId}", messageId);
            return;
        }

        try
        {
            var payload = await AnalyzeWithRetryAsync(messageId, resolvedPath, ct);
            await _messageRepository.UpdateMediaParalinguisticsAsync(messageId, payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Voice paralinguistics failed for message_id={MessageId}", messageId);
            await _errorRepository.LogAsync(
                stage: "voice_paralinguistics",
                reason: ex.Message,
                messageId: messageId,
                payload: $"path={resolvedPath};exception={ex.GetType().Name}",
                ct: ct);
        }
    }

    private async Task<string> AnalyzeWithRetryAsync(long messageId, string path, CancellationToken ct)
    {
        var attempts = Math.Max(1, _settings.RetryCount + 1);
        Exception? last = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await _analyzer.AnalyzeAsync(path, ct);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt >= attempts)
                {
                    break;
                }

                var delaySeconds = Math.Max(1, _settings.RetryBaseDelaySeconds) * attempt;
                _logger.LogDebug(
                    "Voice paralinguistics retry message_id={MessageId}, attempt={Attempt}/{Max}, delay={Delay}s",
                    messageId,
                    attempt,
                    attempts,
                    delaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
        }

        throw last ?? new InvalidOperationException("voice_paralinguistics_retry_failed");
    }

    private string? ResolveAccessiblePath(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return null;
        }

        var trimmed = mediaPath.Trim();
        if (File.Exists(trimmed))
        {
            return trimmed;
        }

        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_archiveImportSettings.MediaBasePath, normalized));
    }
}

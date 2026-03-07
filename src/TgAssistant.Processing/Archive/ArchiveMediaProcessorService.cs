using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive;

public class ArchiveMediaProcessorService : BackgroundService
{
    private readonly ArchiveImportSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly ILogger<ArchiveMediaProcessorService> _logger;
    private readonly SlidingWindowRateLimiter _rateLimiter;

    public ArchiveMediaProcessorService(
        IOptions<ArchiveImportSettings> settings,
        IMessageRepository messageRepository,
        IMediaProcessor mediaProcessor,
        ILogger<ArchiveMediaProcessorService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _mediaProcessor = mediaProcessor;
        _logger = logger;
        _rateLimiter = new SlidingWindowRateLimiter(_settings.RequestsPerMinute, TimeSpan.FromMinutes(1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Archive media processor is disabled");
            return;
        }

        _logger.LogInformation("Archive media processor started with max_parallel={Parallel}, rpm={Rpm}",
            _settings.MaxParallelMedia,
            _settings.RequestsPerMinute);

        while (!stoppingToken.IsCancellationRequested)
        {
            var items = await _messageRepository.GetPendingArchiveMediaAsync(_settings.MaxParallelMedia * 4, stoppingToken);
            if (items.Count == 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
                continue;
            }

            await Parallel.ForEachAsync(
                items,
                new ParallelOptions { MaxDegreeOfParallelism = _settings.MaxParallelMedia, CancellationToken = stoppingToken },
                async (message, ct) => await ProcessMessageAsync(message, ct));
        }
    }

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.MediaPath) || !File.Exists(message.MediaPath))
        {
            await _messageRepository.UpdateMediaProcessingResultAsync(
                message.Id,
                new MediaProcessingResult { Success = false, FailureReason = "Media file not found" },
                ProcessingStatus.PendingReview,
                ct);
            return;
        }

        var fileSizeMb = new FileInfo(message.MediaPath).Length / (1024d * 1024d);
        if (fileSizeMb > _settings.MaxMediaFileSizeMb)
        {
            await _messageRepository.UpdateMediaProcessingResultAsync(
                message.Id,
                new MediaProcessingResult { Success = false, FailureReason = $"Skipped by policy: file too large ({fileSizeMb:F1}MB)" },
                ProcessingStatus.PendingReview,
                ct);
            return;
        }

        await _rateLimiter.WaitAsync(ct);

        try
        {
            var result = await _mediaProcessor.ProcessAsync(message.MediaPath, message.MediaType, ct);
            var status = result.Success ? ProcessingStatus.Processed : ProcessingStatus.PendingReview;
            await _messageRepository.UpdateMediaProcessingResultAsync(message.Id, result, status, ct);

            _logger.LogInformation("Archive media processed message_id={MessageId} status={Status}", message.Id, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Archive media processing failed for message_id={MessageId}", message.Id);
            await _messageRepository.UpdateMediaProcessingResultAsync(
                message.Id,
                new MediaProcessingResult { Success = false, FailureReason = ex.Message },
                ProcessingStatus.PendingReview,
                ct);
        }
    }
}

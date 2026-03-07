using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive;

public class ArchiveMediaProcessorService : BackgroundService
{
    private const string LegacyArchiveRootPrefix = "/opt/tgassistant/TelegramAssistant/archive/";

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
        if (!_settings.MediaProcessingEnabled)
        {
            _logger.LogInformation("Archive media processor is disabled by config");
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
        var resolvedPath = ResolveAccessiblePath(message.MediaPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            _logger.LogWarning(
                "Archive media missing for message_id={MessageId}. media_path={MediaPath}, resolved_path={ResolvedPath}, media_base={MediaBasePath}",
                message.Id,
                message.MediaPath,
                resolvedPath,
                _settings.MediaBasePath);

            await _messageRepository.UpdateMediaProcessingResultAsync(
                message.Id,
                new MediaProcessingResult { Success = false, FailureReason = "Media file not found" },
                ProcessingStatus.PendingReview,
                ct);
            return;
        }

        var fileSizeMb = new FileInfo(resolvedPath).Length / (1024d * 1024d);
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
            var result = await _mediaProcessor.ProcessAsync(resolvedPath, message.MediaType, ct);
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

        foreach (var candidate in BuildFallbackCandidates(trimmed))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_settings.MediaBasePath, normalized));
    }

    private IEnumerable<string> BuildFallbackCandidates(string rootedPath)
    {
        if (!Path.IsPathRooted(rootedPath))
        {
            yield break;
        }

        var normalizedRooted = rootedPath.Replace('\\', '/');
        if (normalizedRooted.StartsWith(LegacyArchiveRootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var fromArchiveRoot = normalizedRooted[LegacyArchiveRootPrefix.Length..].TrimStart('/');
            yield return Path.GetFullPath(Path.Combine(_settings.MediaBasePath, fromArchiveRoot));
        }

        var exportMarker = "/ChatExport_";
        var exportIdx = normalizedRooted.IndexOf(exportMarker, StringComparison.OrdinalIgnoreCase);
        if (exportIdx >= 0)
        {
            var afterExport = normalizedRooted[(exportIdx + 1)..];
            var slashIdx = afterExport.IndexOf('/');
            if (slashIdx > 0 && slashIdx + 1 < afterExport.Length)
            {
                var insideExport = afterExport[(slashIdx + 1)..];
                yield return Path.GetFullPath(Path.Combine(_settings.MediaBasePath, insideExport));
            }
        }
    }
}

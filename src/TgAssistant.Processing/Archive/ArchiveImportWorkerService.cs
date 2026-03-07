using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive;

public class ArchiveImportWorkerService : BackgroundService
{
    private readonly ArchiveImportSettings _settings;
    private readonly MediaSettings _mediaSettings;
    private readonly TelegramDesktopArchiveParser _parser;
    private readonly IMessageRepository _messageRepository;
    private readonly IArchiveImportRepository _archiveImportRepository;
    private readonly ILogger<ArchiveImportWorkerService> _logger;

    public ArchiveImportWorkerService(
        IOptions<ArchiveImportSettings> settings,
        IOptions<MediaSettings> mediaSettings,
        TelegramDesktopArchiveParser parser,
        IMessageRepository messageRepository,
        IArchiveImportRepository archiveImportRepository,
        ILogger<ArchiveImportWorkerService> logger)
    {
        _settings = settings.Value;
        _mediaSettings = mediaSettings.Value;
        _parser = parser;
        _messageRepository = messageRepository;
        _archiveImportRepository = archiveImportRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Archive import is disabled");
            return;
        }

        if (!File.Exists(_settings.SourcePath))
        {
            _logger.LogWarning("Archive file not found: {Path}", _settings.SourcePath);
            return;
        }

        ArchiveImportRun? run = null;
        try
        {
            var parseResult = await _parser.ParseAsync(_settings.SourcePath, stoppingToken);
            var burstSkipIndices = BuildPhotoBurstSkipIndices(parseResult.Messages);
            if (burstSkipIndices.Count > 0)
            {
                _logger.LogInformation(
                    "Archive photo burst policy: skipping {Count} photo items from LLM processing",
                    burstSkipIndices.Count);
            }

            _logger.LogInformation(
                "Archive cost estimate: messages={Messages}, media={Media}, estimated_usd={Cost}",
                parseResult.CostEstimate.TotalMessages,
                parseResult.CostEstimate.MediaMessages,
                parseResult.CostEstimate.EstimatedCostUsd);

            run = await _archiveImportRepository.GetRunningRunAsync(_settings.SourcePath, stoppingToken)
                ?? await _archiveImportRepository.CreateRunAsync(new ArchiveImportRun
                {
                    SourcePath = _settings.SourcePath,
                    Status = ArchiveImportRunStatus.Running,
                    LastMessageIndex = -1,
                    ImportedMessages = 0,
                    QueuedMedia = 0,
                    TotalMessages = parseResult.CostEstimate.TotalMessages,
                    TotalMedia = parseResult.CostEstimate.MediaMessages,
                    EstimatedCostUsd = parseResult.CostEstimate.EstimatedCostUsd
                }, stoppingToken);

            var startIndex = run.LastMessageIndex + 1;
            if (startIndex >= parseResult.Messages.Count)
            {
                await _archiveImportRepository.CompleteRunAsync(run.Id, ArchiveImportRunStatus.Completed, null, stoppingToken);
                _logger.LogInformation("Archive import already complete for {Path}", _settings.SourcePath);
                return;
            }

            _logger.LogInformation("Starting archive import from index {StartIndex}", startIndex);

            var buffer = new List<Message>(_settings.BatchSize);
            var importedMessages = run.ImportedMessages;
            var queuedMedia = run.QueuedMedia;
            var lastIndex = run.LastMessageIndex;

            for (var i = startIndex; i < parseResult.Messages.Count; i++)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var item = parseResult.Messages[i];
                var mediaPath = ResolveMediaPath(item.RelativeMediaPath);
                var hasMedia = item.MediaType != MediaType.None && !string.IsNullOrWhiteSpace(mediaPath);
                var isProcessableMedia = hasMedia && IsProcessableMedia(item.MediaType);
                var isBurstSkippedPhoto = burstSkipIndices.Contains(item.Index);

                buffer.Add(new Message
                {
                    TelegramMessageId = item.MessageId,
                    ChatId = item.ChatId,
                    SenderId = item.SenderId,
                    SenderName = item.SenderName,
                    Timestamp = item.Timestamp,
                    Text = item.Text,
                    MediaType = hasMedia ? item.MediaType : MediaType.None,
                    MediaPath = hasMedia ? mediaPath : null,
                    MediaDescription = BuildArchiveMediaDescription(item.MediaType, hasMedia, isProcessableMedia, isBurstSkippedPhoto),
                    ReplyToMessageId = item.ReplyToMessageId,
                    ForwardJson = item.ForwardJson,
                    Source = MessageSource.Archive,
                    ProcessingStatus = ResolveProcessingStatus(hasMedia, isProcessableMedia, isBurstSkippedPhoto),
                    ProcessedAt = !hasMedia || !isProcessableMedia || isBurstSkippedPhoto ? DateTime.UtcNow : null
                });

                lastIndex = item.Index;

                if (buffer.Count >= _settings.BatchSize)
                {
                    await FlushAsync(buffer, stoppingToken);
                    importedMessages += buffer.Count;
                    queuedMedia += buffer.Count(x => ShouldQueueForMediaProcessing(x));
                    await _archiveImportRepository.UpdateProgressAsync(run.Id, lastIndex, importedMessages, queuedMedia, stoppingToken);
                    _logger.LogInformation("Archive import progress: {Imported}/{Total}", importedMessages, parseResult.Messages.Count);
                    buffer.Clear();
                }
            }

            if (buffer.Count > 0)
            {
                await FlushAsync(buffer, stoppingToken);
                importedMessages += buffer.Count;
                queuedMedia += buffer.Count(x => ShouldQueueForMediaProcessing(x));
                await _archiveImportRepository.UpdateProgressAsync(run.Id, lastIndex, importedMessages, queuedMedia, stoppingToken);
            }

            await _archiveImportRepository.CompleteRunAsync(run.Id, ArchiveImportRunStatus.Completed, null, stoppingToken);
            _logger.LogInformation("Archive import completed: {Imported} messages, {Media} media queued", importedMessages, queuedMedia);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Archive import cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive import failed");
            if (run != null)
            {
                await _archiveImportRepository.CompleteRunAsync(run.Id, ArchiveImportRunStatus.Failed, ex.Message, CancellationToken.None);
            }
        }
    }

    private async Task FlushAsync(List<Message> buffer, CancellationToken ct)
    {
        await _messageRepository.SaveBatchAsync(buffer, ct);
    }

    private string? ResolveMediaPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? normalized
            : Path.GetFullPath(Path.Combine(_settings.MediaBasePath, normalized));
    }

    private static bool IsProcessableMedia(MediaType type)
    {
        return type is MediaType.Photo or MediaType.Sticker or MediaType.Animation or MediaType.Voice or MediaType.Video or MediaType.VideoNote;
    }

    private static ProcessingStatus ResolveProcessingStatus(bool hasMedia, bool isProcessableMedia, bool isBurstSkippedPhoto)
    {
        if (!hasMedia)
        {
            return ProcessingStatus.Processed;
        }

        if (isBurstSkippedPhoto)
        {
            return ProcessingStatus.Processed;
        }

        if (isProcessableMedia)
        {
            return ProcessingStatus.Pending;
        }

        return ProcessingStatus.PendingReview;
    }

    private static string? BuildArchiveMediaDescription(MediaType mediaType, bool hasMedia, bool isProcessableMedia, bool isBurstSkippedPhoto)
    {
        if (!hasMedia)
        {
            return null;
        }

        if (isBurstSkippedPhoto)
        {
            return "Skipped by burst policy: high-volume photo forwarding";
        }

        if (!isProcessableMedia)
        {
            return $"Skipped by policy: unsupported archive media type {mediaType}";
        }

        return null;
    }

    private bool ShouldQueueForMediaProcessing(Message message)
    {
        return message.ProcessingStatus == ProcessingStatus.Pending
               && message.MediaType != MediaType.None
               && !string.IsNullOrWhiteSpace(message.MediaPath);
    }

    private HashSet<int> BuildPhotoBurstSkipIndices(IReadOnlyList<ArchiveMessageRecord> items)
    {
        var skip = new HashSet<int>();
        if (!_mediaSettings.EnablePhotoBurstGuard)
        {
            return skip;
        }

        var threshold = Math.Max(1, _mediaSettings.PhotoBurstThreshold);
        var keepCount = Math.Max(0, _mediaSettings.PhotoBurstKeepCount);
        var window = TimeSpan.FromSeconds(Math.Max(1, _mediaSettings.PhotoBurstWindowSeconds));

        var byChat = new Dictionary<long, Queue<ArchiveMessageRecord>>();

        foreach (var item in items)
        {
            if (item.MediaType != MediaType.Photo || string.IsNullOrWhiteSpace(item.RelativeMediaPath))
            {
                continue;
            }

            if (!byChat.TryGetValue(item.ChatId, out var queue))
            {
                queue = new Queue<ArchiveMessageRecord>();
                byChat[item.ChatId] = queue;
            }

            while (queue.Count > 0 && item.Timestamp - queue.Peek().Timestamp > window)
            {
                queue.Dequeue();
            }

            queue.Enqueue(item);

            if (queue.Count >= threshold)
            {
                foreach (var toSkip in queue.Skip(keepCount))
                {
                    skip.Add(toSkip.Index);
                }
            }
        }

        return skip;
    }
}

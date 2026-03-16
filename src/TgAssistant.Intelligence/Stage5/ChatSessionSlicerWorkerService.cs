using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class ChatSessionSlicerWorkerService : BackgroundService
{
    private const string WatermarkKey = "stage5:slicer_watermark";

    private readonly AnalysisSettings _settings;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly IAnalysisStateRepository _stateRepository;
    private readonly IExtractionErrorRepository _extractionErrorRepository;
    private readonly ILogger<ChatSessionSlicerWorkerService> _logger;

    public ChatSessionSlicerWorkerService(
        IOptions<AnalysisSettings> settings,
        IMessageRepository messageRepository,
        IChatSessionRepository chatSessionRepository,
        IAnalysisStateRepository stateRepository,
        IExtractionErrorRepository extractionErrorRepository,
        ILogger<ChatSessionSlicerWorkerService> logger)
    {
        _settings = settings.Value;
        _messageRepository = messageRepository;
        _chatSessionRepository = chatSessionRepository;
        _stateRepository = stateRepository;
        _extractionErrorRepository = extractionErrorRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.SessionSlicerEnabled)
        {
            _logger.LogInformation("Stage5 session slicer is disabled");
            return;
        }

        _logger.LogInformation(
            "Stage5 session slicer started. batch_size={BatchSize}, gap_minutes={GapMinutes}, max_sessions_per_chat={MaxSessions}",
            Math.Max(1, _settings.SessionSlicerBatchSize),
            Math.Max(1, _settings.EpisodicSessionGapMinutes),
            Math.Max(1, _settings.TestModeMaxSessionsPerChat));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var watermark = await _stateRepository.GetWatermarkAsync(WatermarkKey, stoppingToken);
                var batch = await _messageRepository.GetProcessedAfterIdAsync(
                    watermark,
                    Math.Max(1, _settings.SessionSlicerBatchSize),
                    stoppingToken);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SessionSlicerPollIntervalSeconds)), stoppingToken);
                    continue;
                }

                var sessionsByChat = await _chatSessionRepository.GetByChatsAsync(
                    batch.Select(x => x.ChatId).Distinct().ToArray(),
                    stoppingToken);
                var upserts = 0;

                foreach (var message in batch.OrderBy(x => x.Id))
                {
                    var sessions = sessionsByChat.GetValueOrDefault(message.ChatId);
                    if (sessions == null)
                    {
                        sessions = [];
                        sessionsByChat[message.ChatId] = sessions;
                    }

                    var current = ResolveCurrentSession(sessions, message.Timestamp);
                    if (current == null)
                    {
                        var nextIndex = sessions.Count == 0 ? 0 : sessions.Max(x => x.SessionIndex) + 1;
                        if (nextIndex >= Math.Max(1, _settings.TestModeMaxSessionsPerChat))
                        {
                            continue;
                        }

                        current = new ChatSession
                        {
                            ChatId = message.ChatId,
                            SessionIndex = nextIndex,
                            StartDate = message.Timestamp,
                            EndDate = message.Timestamp,
                            Summary = string.Empty
                        };
                        sessions.Add(current);
                    }
                    else
                    {
                        current.StartDate = current.StartDate > message.Timestamp ? message.Timestamp : current.StartDate;
                        current.EndDate = current.EndDate < message.Timestamp ? message.Timestamp : current.EndDate;
                    }

                    await _chatSessionRepository.UpsertAsync(current, stoppingToken);
                    upserts++;
                }

                var nextWatermark = batch.Max(x => x.Id);
                await _stateRepository.SetWatermarkAsync(WatermarkKey, nextWatermark, stoppingToken);
                _logger.LogInformation(
                    "Stage5 session slicer pass done: processed={Count}, upserts={Upserts}, watermark={Watermark}",
                    batch.Count,
                    upserts,
                    nextWatermark);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage5 session slicer loop failed");
                await _extractionErrorRepository.LogAsync(
                    stage: "stage5_session_slicer_loop",
                    reason: ex.Message,
                    payload: ex.GetType().Name,
                    ct: stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, _settings.SessionSlicerPollIntervalSeconds)), stoppingToken);
            }
        }
    }

    private ChatSession? ResolveCurrentSession(List<ChatSession> sessions, DateTime messageTimestamp)
    {
        var matching = sessions.FirstOrDefault(session =>
            session.StartDate <= messageTimestamp && messageTimestamp <= session.EndDate);
        if (matching != null)
        {
            return matching;
        }

        var latest = sessions.OrderByDescending(x => x.SessionIndex).FirstOrDefault();
        if (latest == null)
        {
            return null;
        }

        var gap = TimeSpan.FromMinutes(Math.Max(1, _settings.EpisodicSessionGapMinutes));
        return messageTimestamp - latest.EndDate > gap ? null : latest;
    }
}

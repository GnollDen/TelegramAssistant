using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TL;
using WTelegram;
using TlMessage = TL.Message;

namespace TgAssistant.Processing.Archive;

/// <summary>
/// One-shot backfill service that imports chat history from Telegram into the realtime queue.
/// </summary>
public class HistoryBackfillService : BackgroundService
{
    private const int HistoryBatchSize = 100;
    private const int HandoverFreshnessTailSampleSize = 40;
    private const int HandoverFreshnessMaxPasses = 3;

    private readonly BackfillSettings _settings;
    private readonly TelegramSettings _telegramSettings;
    private readonly MediaSettings _mediaSettings;
    private readonly ChatCoordinationSettings _coordinationSettings;
    private readonly IMessageQueue _messageQueue;
    private readonly IMessageRepository _messageRepository;
    private readonly IChatCoordinationService _chatCoordinationService;
    private readonly ILogger<HistoryBackfillService> _logger;
    private readonly string _phaseOwnerId = $"history_backfill:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public HistoryBackfillService(
        IOptions<BackfillSettings> settings,
        IOptions<TelegramSettings> telegramSettings,
        IOptions<MediaSettings> mediaSettings,
        IOptions<ChatCoordinationSettings> coordinationSettings,
        IMessageQueue messageQueue,
        IMessageRepository messageRepository,
        IChatCoordinationService chatCoordinationService,
        ILogger<HistoryBackfillService> logger)
    {
        _settings = settings.Value;
        _telegramSettings = telegramSettings.Value;
        _mediaSettings = mediaSettings.Value;
        _coordinationSettings = coordinationSettings.Value;
        _messageQueue = messageQueue;
        _messageRepository = messageRepository;
        _chatCoordinationService = chatCoordinationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recoveryMode = false;

        if (!TryParseSinceDate(_settings.SinceDate, out var sinceUtc))
        {
            _logger.LogWarning(
                "Backfill since date '{SinceDate}' is invalid. Falling back to 2026-03-07",
                _settings.SinceDate);
            sinceUtc = new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc);
        }

        IReadOnlyList<long> targetChatIds;
        // Bound backfill to recent window for chats that do not yet have persisted messages.
        // This prevents accidental full-history pulls in routine runtime startups.
        var startupUpperBoundSinceUtc = DateTime.UtcNow.AddHours(-24);
        if (_settings.Enabled)
        {
            targetChatIds = ResolveTargetChatIds();
        }
        else if (_coordinationSettings.Enabled && _coordinationSettings.AutoRecoveryCatchupEnabled)
        {
            targetChatIds = await ResolveDowntimeRecoveryTargetsAsync(stoppingToken);
            recoveryMode = targetChatIds.Count > 0;
            if (!recoveryMode)
            {
                _logger.LogInformation("History backfill is disabled and no downtime-recovery catch-up is required");
                return;
            }
        }
        else
        {
            _logger.LogInformation("History backfill is disabled");
            return;
        }

        if (targetChatIds.Count == 0)
        {
            _logger.LogWarning(
                "History backfill {Mode} but no target chats resolved",
                recoveryMode ? "downtime-recovery mode is enabled" : "is enabled");
            return;
        }

        if (_coordinationSettings.Enabled)
        {
            await _chatCoordinationService.EnsureStatesAsync(
                _telegramSettings.MonitoredChatIds,
                targetChatIds,
                backfillEnabled: true,
                _coordinationSettings.HandoverPendingExtractionThreshold,
                stoppingToken);
        }

        _logger.LogInformation(
            "Starting history backfill ({Mode}) from {SinceDate} for {ChatCount} chats: {ChatIds}",
            recoveryMode ? "downtime_recovery" : "normal",
            sinceUtc,
            targetChatIds.Count,
            string.Join(", ", targetChatIds));

        using var client = new Client(ConfigProvider);
        var user = await client.LoginUserIfNeeded();
        _logger.LogInformation("Backfill client logged in as {Name} (ID: {Id})", user.first_name, user.id);

        var peerMap = await ResolvePeerMapAsync(client, targetChatIds, stoppingToken);

        foreach (var chatId in targetChatIds)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!peerMap.TryGetValue(chatId, out var inputPeer))
            {
                _logger.LogWarning("Backfill skipped chat {ChatId}: cannot resolve InputPeer", chatId);
                continue;
            }

            try
            {
                if (_coordinationSettings.Enabled && _coordinationSettings.PhaseGuardsEnabled)
                {
                    var phaseDecision = await _chatCoordinationService.TryAcquirePhaseAsync(
                        chatId,
                        ChatRuntimePhases.BackfillIngest,
                        _phaseOwnerId,
                        reason: "history_backfill_start",
                        ct: stoppingToken);
                    if (!phaseDecision.Allowed)
                    {
                        _logger.LogWarning(
                            "Backfill skipped by phase guard: chat_id={ChatId}, requested_phase={RequestedPhase}, current_phase={CurrentPhase}, deny_code={DenyCode}, deny_reason={DenyReason}",
                            chatId,
                            phaseDecision.RequestedPhase,
                            phaseDecision.CurrentPhase,
                            phaseDecision.DenyCode,
                            phaseDecision.DenyReason);
                        continue;
                    }
                }

                var leaseHeartbeat = StartPhaseLeaseHeartbeat(
                    chatId,
                    ChatRuntimePhases.BackfillIngest,
                    "history_backfill_lease_heartbeat",
                    stoppingToken);
                var releaseMismatch = false;
                var leaseLost = false;
                using var leaseScope = CreateLeaseLinkedTokenSource(leaseHeartbeat, stoppingToken);
                var leaseCt = leaseScope.Token;
                if (_coordinationSettings.Enabled)
                {
                    await _chatCoordinationService.MarkBackfillStartedAsync(chatId, leaseCt);
                }

                try
                {
                    var effectiveSinceUtc = await ResolveEffectiveSinceUtcAsync(
                        chatId,
                        sinceUtc,
                        startupUpperBoundSinceUtc,
                        leaseCt);
                    await BackfillChatAsync(client, chatId, inputPeer, effectiveSinceUtc, leaseCt);
                    await EnsureFreshTailBeforeHandoverAsync(client, chatId, inputPeer, leaseCt);

                    if (_coordinationSettings.Enabled)
                    {
                        await _chatCoordinationService.MarkBackfillCompletedAsync(
                            chatId,
                            _coordinationSettings.HandoverPendingExtractionThreshold,
                            leaseCt);
                    }
                }
                catch (OperationCanceledException) when (leaseHeartbeat.LeaseLost && !stoppingToken.IsCancellationRequested)
                {
                    leaseLost = true;
                    if (_coordinationSettings.Enabled)
                    {
                        await _chatCoordinationService.MarkBackfillDegradedAsync(
                            chatId,
                            "backfill_phase_lease_lost",
                            CancellationToken.None);
                    }

                    _logger.LogWarning(
                        "Backfill workload stopped after lease loss: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}",
                        chatId,
                        ChatRuntimePhases.BackfillIngest,
                        _phaseOwnerId);
                }
                finally
                {
                    await StopPhaseLeaseHeartbeatAsync(leaseHeartbeat);
                    if (_coordinationSettings.Enabled && _coordinationSettings.PhaseGuardsEnabled)
                    {
                        releaseMismatch = !await ReleaseBackfillPhaseAsync(chatId, "history_backfill_end", stoppingToken);
                    }
                }

                if (leaseLost)
                {
                    continue;
                }

                if (releaseMismatch)
                {
                    if (_coordinationSettings.Enabled)
                    {
                        await _chatCoordinationService.MarkBackfillDegradedAsync(
                            chatId,
                            "backfill_phase_release_mismatch",
                            CancellationToken.None);
                    }

                    _logger.LogError(
                        "Backfill chat marked degraded due to phase release mismatch escalation: chat_id={ChatId}",
                        chatId);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (_coordinationSettings.Enabled)
                {
                    await _chatCoordinationService.MarkBackfillDegradedAsync(
                        chatId,
                        "backfill_interrupted_before_handover",
                        CancellationToken.None);
                }

                _logger.LogInformation("History backfill cancelled while processing chat {ChatId}", chatId);
                break;
            }
            catch (Exception ex)
            {
                if (_coordinationSettings.Enabled)
                {
                    await _chatCoordinationService.MarkBackfillDegradedAsync(
                        chatId,
                        $"backfill_failed:{ex.GetType().Name}",
                        CancellationToken.None);
                }

                _logger.LogError(ex, "History backfill failed for chat {ChatId}", chatId);
            }
        }

        _logger.LogInformation(
            "History backfill complete. Mode={Mode}. Set Backfill__Enabled=false to disable one-shot backfill on next start",
            recoveryMode ? "downtime_recovery" : "normal");
    }

    private PhaseLeaseHeartbeatHandle StartPhaseLeaseHeartbeat(
        long chatId,
        string phase,
        string reason,
        CancellationToken ct)
    {
        if (!_coordinationSettings.Enabled || !_coordinationSettings.PhaseGuardsEnabled || chatId <= 0)
        {
            return PhaseLeaseHeartbeatHandle.None;
        }

        var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var leaseLostCts = new CancellationTokenSource();
        var interval = ResolvePhaseLeaseRenewInterval();
        var task = Task.Run(
            () => RunPhaseLeaseHeartbeatLoopAsync(chatId, phase, reason, interval, heartbeatCts.Token, leaseLostCts),
            CancellationToken.None);
        return new PhaseLeaseHeartbeatHandle(heartbeatCts, leaseLostCts, task);
    }

    private async Task StopPhaseLeaseHeartbeatAsync(PhaseLeaseHeartbeatHandle heartbeat)
    {
        if (!heartbeat.IsActive)
        {
            return;
        }

        heartbeat.HeartbeatTokenSource!.Cancel();
        try
        {
            await heartbeat.RunTask!;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            heartbeat.HeartbeatTokenSource.Dispose();
            heartbeat.LeaseLostTokenSource?.Dispose();
        }
    }

    private static CancellationTokenSource CreateLeaseLinkedTokenSource(PhaseLeaseHeartbeatHandle heartbeat, CancellationToken ct)
    {
        return heartbeat.LeaseLostTokenSource is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, heartbeat.LeaseLostTokenSource.Token);
    }

    private async Task<bool> ReleaseBackfillPhaseAsync(long chatId, string reason, CancellationToken ct)
    {
        var result = await _chatCoordinationService.ReleasePhaseAsync(
            chatId,
            ChatRuntimePhases.BackfillIngest,
            _phaseOwnerId,
            reason: reason,
            ct: ct.IsCancellationRequested ? CancellationToken.None : ct);
        if (result.Released)
        {
            return true;
        }

        _logger.LogError(
            "Backfill phase release escalation: chat_id={ChatId}, requested_phase={RequestedPhase}, owner_id={OwnerId}, ownership_mismatch={OwnershipMismatch}, current_phase={CurrentPhase}, current_owner={CurrentOwner}, current_lease_expires_at_utc={LeaseExpiresAtUtc}",
            chatId,
            ChatRuntimePhases.BackfillIngest,
            _phaseOwnerId,
            result.OwnershipMismatch,
            result.CurrentPhase,
            result.CurrentOwnerId,
            result.CurrentLeaseExpiresAtUtc);
        return false;
    }

    private async Task RunPhaseLeaseHeartbeatLoopAsync(
        long chatId,
        string phase,
        string reason,
        TimeSpan interval,
        CancellationToken ct,
        CancellationTokenSource leaseLostCts)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var renewDecision = await _chatCoordinationService.TryRenewPhaseLeaseAsync(
                    chatId,
                    phase,
                    _phaseOwnerId,
                    reason,
                    ct);
                if (renewDecision.Renewed)
                {
                    _logger.LogDebug(
                        "Backfill lease renewed: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}, lease_expires_at_utc={LeaseExpiresAtUtc}",
                        chatId,
                        phase,
                        _phaseOwnerId,
                        renewDecision.CurrentLeaseExpiresAtUtc);
                    continue;
                }

                _logger.LogWarning(
                    "Backfill lease renewal stopped: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}, deny_code={DenyCode}, deny_reason={DenyReason}",
                    chatId,
                    phase,
                    _phaseOwnerId,
                    renewDecision.DenyCode,
                    renewDecision.DenyReason);
                if (!leaseLostCts.IsCancellationRequested)
                {
                    leaseLostCts.Cancel();
                }
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Backfill lease heartbeat iteration failed: chat_id={ChatId}, phase={Phase}, owner_id={OwnerId}",
                    chatId,
                    phase,
                    _phaseOwnerId);
            }
        }
    }

    private TimeSpan ResolvePhaseLeaseRenewInterval()
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _coordinationSettings.PhaseGuardLeaseTtlMinutes));
        var seconds = Math.Max(5, ttl.TotalSeconds / 3d);
        return TimeSpan.FromSeconds(seconds);
    }

    private async Task<IReadOnlyList<long>> ResolveDowntimeRecoveryTargetsAsync(CancellationToken ct)
    {
        var states = await _chatCoordinationService.EnsureStatesAsync(
            _telegramSettings.MonitoredChatIds,
            _settings.ChatIds,
            backfillEnabled: false,
            _coordinationSettings.HandoverPendingExtractionThreshold,
            ct);

        return states.Values
            .Where(x => !string.Equals(x.State, ChatCoordinationStates.RealtimeActive, StringComparison.Ordinal))
            .Select(x => x.ChatId)
            .Distinct()
            .ToList();
    }

    private async Task<Dictionary<long, InputPeer>> ResolvePeerMapAsync(
        Client client,
        IReadOnlyCollection<long> targetChatIds,
        CancellationToken ct)
    {
        var resolved = new Dictionary<long, InputPeer>();
        var targetSet = targetChatIds.ToHashSet();
        var dialogs = await client.Messages_GetAllDialogs();

        foreach (var dialog in dialogs.Dialogs)
        {
            ct.ThrowIfCancellationRequested();

            var chatId = GetPeerId(dialog.Peer);
            if (chatId <= 0 || !targetSet.Contains(chatId) || resolved.ContainsKey(chatId))
            {
                continue;
            }

            var userOrChat = dialogs.UserOrChat(dialog.Peer);
            if (!TryToInputPeer(userOrChat, out var inputPeer))
            {
                _logger.LogWarning("Cannot convert dialog peer to InputPeer for chat {ChatId}", chatId);
                continue;
            }

            resolved[chatId] = inputPeer;
        }

        return resolved;
    }

    private async Task BackfillChatAsync(
        Client client,
        long chatId,
        InputPeer inputPeer,
        DateTime sinceUtc,
        CancellationToken ct)
    {
        var offsetId = 0;
        var fetched = 0;
        var enqueued = 0;
        var skippedExisting = 0;
        var skippedOld = 0;
        var seenInRun = new HashSet<long>();

        _logger.LogInformation("Backfill started for chat {ChatId}", chatId);

        while (!ct.IsCancellationRequested)
        {
            var history = await client.Messages_GetHistory(
                inputPeer,
                offsetId,
                DateTime.UtcNow,
                0,
                HistoryBatchSize,
                0,
                0,
                0);

            var pageMessages = history.Messages
                .OfType<TlMessage>()
                .Where(x => x.id > 0)
                .ToList();

            if (pageMessages.Count == 0)
            {
                break;
            }

            fetched += pageMessages.Count;
            var oldestDateInPage = pageMessages.Min(x => EnsureUtc(x.date));
            var inRange = pageMessages
                .Where(x => EnsureUtc(x.date) >= sinceUtc)
                .ToList();

            skippedOld += pageMessages.Count - inRange.Count;

            if (inRange.Count > 0)
            {
                var candidateIds = inRange
                    .Select(x => (long)x.id)
                    .Where(id => !seenInRun.Contains(id))
                    .Distinct()
                    .ToList();

                var existing = await _messageRepository.GetByTelegramMessageIdsAsync(
                    chatId,
                    MessageSource.Realtime,
                    candidateIds,
                    ct);

                foreach (var message in inRange
                             .OrderBy(x => EnsureUtc(x.date))
                             .ThenBy(x => x.id))
                {
                    var messageId = (long)message.id;
                    if (!seenInRun.Add(messageId))
                    {
                        continue;
                    }

                    if (existing.ContainsKey(messageId))
                    {
                        skippedExisting++;
                        continue;
                    }

                    var raw = await BuildRawMessageAsync(client, chatId, history, message, ct);
                    await _messageQueue.EnqueueAsync(raw, ct);
                    enqueued++;
                }
            }

            var minMessageId = pageMessages.Min(x => x.id);
            if (minMessageId <= 1)
            {
                break;
            }

            offsetId = minMessageId - 1;

            if (oldestDateInPage < sinceUtc)
            {
                break;
            }
        }

        _logger.LogInformation(
            "Backfill finished for chat {ChatId}: fetched={Fetched}, enqueued={Enqueued}, skipped_existing={SkippedExisting}, skipped_old={SkippedOld}",
            chatId,
            fetched,
            enqueued,
            skippedExisting,
            skippedOld);
    }

    private async Task<DateTime> ResolveEffectiveSinceUtcAsync(
        long chatId,
        DateTime configuredSinceUtc,
        DateTime? startupUpperBoundSinceUtc,
        CancellationToken ct)
    {
        var latestProcessed = await _messageRepository.GetProcessedByChatAsync(chatId, limit: 1, ct);
        if (latestProcessed.Count > 0)
        {
            var latestTimestamp = EnsureUtc(latestProcessed[^1].Timestamp);
            var gapLookback = TimeSpan.FromMinutes(Math.Max(5, _coordinationSettings.DowntimeCatchupThresholdMinutes));
            var gapSinceUtc = latestTimestamp.Subtract(gapLookback);
            var effectiveFromLatest = MaxUtc(configuredSinceUtc, gapSinceUtc);
            _logger.LogInformation(
                "Backfill scope resolved from latest processed message: chat_id={ChatId}, configured_since_utc={ConfiguredSinceUtc:O}, latest_message_utc={LatestMessageUtc:O}, gap_since_utc={GapSinceUtc:O}, effective_since_utc={EffectiveSinceUtc:O}",
                chatId,
                configuredSinceUtc,
                latestTimestamp,
                gapSinceUtc,
                effectiveFromLatest);
            return effectiveFromLatest;
        }

        var startupBoundedSinceUtc = startupUpperBoundSinceUtc.HasValue
            ? MaxUtc(configuredSinceUtc, startupUpperBoundSinceUtc.Value)
            : configuredSinceUtc;
        _logger.LogInformation(
            "Backfill scope resolved without existing processed messages: chat_id={ChatId}, configured_since_utc={ConfiguredSinceUtc:O}, startup_bound_since_utc={StartupBoundSinceUtc:O}, effective_since_utc={EffectiveSinceUtc:O}",
            chatId,
            configuredSinceUtc,
            startupUpperBoundSinceUtc ?? configuredSinceUtc,
            startupBoundedSinceUtc);
        return startupBoundedSinceUtc;
    }

    private async Task EnsureFreshTailBeforeHandoverAsync(
        Client client,
        long chatId,
        InputPeer inputPeer,
        CancellationToken ct)
    {
        var totalQueued = 0;
        var enqueuedLogicalKeys = new HashSet<string>(StringComparer.Ordinal);
        for (var pass = 1; pass <= HandoverFreshnessMaxPasses; pass++)
        {
            ct.ThrowIfCancellationRequested();

            var history = await client.Messages_GetHistory(
                inputPeer,
                offset_id: 0,
                offset_date: DateTime.UtcNow,
                add_offset: 0,
                limit: HandoverFreshnessTailSampleSize,
                max_id: 0,
                min_id: 0,
                hash: 0);

            var tailMessages = history.Messages
                .OfType<TlMessage>()
                .Where(x => x.id > 0)
                .OrderBy(x => EnsureUtc(x.date))
                .ThenBy(x => x.id)
                .ToList();
            if (tailMessages.Count == 0)
            {
                return;
            }

            var candidateIds = tailMessages
                .Select(x => (long)x.id)
                .Distinct()
                .ToList();
            var existing = await _messageRepository.GetByTelegramMessageIdsAsync(
                chatId,
                MessageSource.Realtime,
                candidateIds,
                ct);

            var missing = tailMessages
                .Where(x => !existing.ContainsKey(x.id))
                .ToList();
            if (missing.Count == 0)
            {
                if (totalQueued > 0)
                {
                    _logger.LogInformation(
                        "Backfill handoff freshness guard converged: chat_id={ChatId}, queued_tail_messages={QueuedTailMessages}, passes={Passes}",
                        chatId,
                        totalQueued,
                        pass);
                }

                return;
            }

            var missingToQueue = missing
                .Where(x => enqueuedLogicalKeys.Add(BuildLogicalMessageKey(chatId, x.id)))
                .ToList();
            var dedupeSkipped = missing.Count - missingToQueue.Count;
            if (dedupeSkipped > 0)
            {
                _logger.LogInformation(
                    "Backfill handoff freshness guard suppressed duplicate tail re-enqueue within same handover run: chat_id={ChatId}, pass={Pass}, skipped_count={SkippedCount}",
                    chatId,
                    pass,
                    dedupeSkipped);
            }

            if (missingToQueue.Count == 0)
            {
                _logger.LogInformation(
                    "Backfill handoff freshness guard detected only already-queued tail gaps; continuing handover without duplicate re-enqueue: chat_id={ChatId}, pass={Pass}, missing_count={MissingCount}",
                    chatId,
                    pass,
                    missing.Count);
                return;
            }

            foreach (var message in missingToQueue)
            {
                var raw = await BuildRawMessageAsync(client, chatId, history, message, ct);
                await _messageQueue.EnqueueAsync(raw, ct);
            }

            totalQueued += missingToQueue.Count;
            _logger.LogWarning(
                "Backfill handoff freshness guard queued missing tail messages before realtime handoff: chat_id={ChatId}, pass={Pass}, missing_count={MissingCount}, enqueued_count={EnqueuedCount}, newest_tail_message_id={NewestTailMessageId}",
                chatId,
                pass,
                missing.Count,
                missingToQueue.Count,
                tailMessages[^1].id);

            if (pass < HandoverFreshnessMaxPasses)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            }
        }

        _logger.LogWarning(
            "Backfill handoff freshness guard reached max passes with active tail churn; proceeding after queueing latest known tail messages: chat_id={ChatId}, queued_tail_messages={QueuedTailMessages}, max_passes={MaxPasses}",
            chatId,
            totalQueued,
            HandoverFreshnessMaxPasses);
    }

    private static string BuildLogicalMessageKey(long chatId, long messageId)
        => $"{chatId}:{messageId}";

    private async Task<RawTelegramMessage> BuildRawMessageAsync(
        Client client,
        long chatId,
        Messages_MessagesBase history,
        TlMessage message,
        CancellationToken ct)
    {
        var senderId = ResolveSenderId(message);
        var senderName = ResolveSenderName(history, message.from_id);
        var mediaType = DetectMediaType(message);
        var mediaPath = mediaType == MediaType.None
            ? null
            : await DownloadMediaAsync(client, chatId, message, mediaType, ct);

        return new RawTelegramMessage
        {
            MessageId = message.id,
            ChatId = chatId,
            SenderId = senderId,
            SenderName = senderName,
            Timestamp = EnsureUtc(message.date),
            Text = message.message,
            MediaType = mediaType,
            MediaPath = mediaPath,
            ReplyToMessageId = message.reply_to is MessageReplyHeader reply ? reply.reply_to_msg_id : null,
            EditTimestamp = message.edit_date,
            ReactionsJson = SerializeReactions(message),
            ForwardJson = SerializeForward(message)
        };
    }

    private async Task<string?> DownloadMediaAsync(
        Client client,
        long chatId,
        TlMessage message,
        MediaType mediaType,
        CancellationToken ct)
    {
        try
        {
            var date = EnsureUtc(message.date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var dir = Path.Combine(_mediaSettings.StoragePath, chatId.ToString(CultureInfo.InvariantCulture), date);
            Directory.CreateDirectory(dir);

            var ext = ResolveMediaExtension(mediaType);
            var filePath = Path.Combine(dir, $"{message.id}{ext}");

            if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
            {
                return filePath;
            }

            await using var fs = File.Create(filePath);
            if (message.media is MessageMediaPhoto { photo: Photo photo })
            {
                await client.DownloadFileAsync(photo, fs);
            }
            else if (message.media is MessageMediaDocument { document: Document doc })
            {
                await client.DownloadFileAsync(doc, fs);
            }
            else
            {
                return null;
            }

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backfill media download failed for chat {ChatId}, message {MessageId}", chatId, message.id);
            return null;
        }
    }

    private static string ResolveMediaExtension(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Voice => ".ogg",
            MediaType.Photo => ".jpg",
            MediaType.Video => ".mp4",
            MediaType.VideoNote => ".mp4",
            MediaType.Sticker => ".webp",
            MediaType.Animation => ".gif",
            _ => ".bin"
        };
    }

    private static MediaType DetectMediaType(TlMessage message)
    {
        return message.media switch
        {
            MessageMediaPhoto => MediaType.Photo,
            MessageMediaDocument { document: Document doc } => DetectDocumentType(doc),
            _ => MediaType.None
        };
    }

    private static MediaType DetectDocumentType(Document doc)
    {
        foreach (var attr in doc.attributes)
        {
            switch (attr)
            {
                case DocumentAttributeSticker:
                    return MediaType.Sticker;
                case DocumentAttributeAnimated:
                    return MediaType.Animation;
                case DocumentAttributeVideo dav when dav.flags.HasFlag(DocumentAttributeVideo.Flags.round_message):
                    return MediaType.VideoNote;
                case DocumentAttributeVideo:
                    return MediaType.Video;
                case DocumentAttributeAudio daa when daa.flags.HasFlag(DocumentAttributeAudio.Flags.voice):
                    return MediaType.Voice;
                case DocumentAttributeAudio:
                    return MediaType.Voice;
            }
        }

        return doc.mime_type switch
        {
            var m when m?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true => MediaType.Voice,
            var m when m?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true => MediaType.Video,
            var m when m?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true => MediaType.Photo,
            _ => MediaType.Document
        };
    }

    private static long ResolveSenderId(TlMessage message)
    {
        return message.from_id switch
        {
            PeerUser peerUser => peerUser.user_id,
            PeerChat peerChat => peerChat.chat_id,
            PeerChannel peerChannel => peerChannel.channel_id,
            _ => 0
        };
    }

    private static string ResolveSenderName(Messages_MessagesBase history, Peer? fromPeer)
    {
        if (fromPeer == null)
        {
            return string.Empty;
        }

        var userOrChat = history.UserOrChat(fromPeer);
        return userOrChat switch
        {
            User user => $"{user.first_name} {user.last_name}".Trim(),
            ChatBase chat => chat.Title ?? string.Empty,
            _ => string.Empty
        };
    }

    private static string? SerializeReactions(TlMessage message)
    {
        if (message.reactions?.results == null)
        {
            return null;
        }

        var reactions = message.reactions.results
            .Select(r => new { emoji = r.reaction is ReactionEmoji re ? re.emoticon : "custom", count = r.count })
            .ToList();

        return reactions.Count == 0 ? null : JsonSerializer.Serialize(reactions);
    }

    private static string? SerializeForward(TlMessage message)
    {
        if (message.fwd_from == null)
        {
            return null;
        }

        var payload = new
        {
            from_id = message.fwd_from.from_id switch
            {
                PeerUser pu => pu.user_id,
                PeerChannel pc => pc.channel_id,
                PeerChat ch => ch.chat_id,
                _ => 0L
            },
            from_name = message.fwd_from.from_name,
            date = message.fwd_from.date
        };

        return JsonSerializer.Serialize(payload);
    }

    private static long GetPeerId(Peer peer)
    {
        return peer switch
        {
            PeerUser u => u.user_id,
            PeerChat c => c.chat_id,
            PeerChannel c => c.channel_id,
            _ => 0
        };
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static bool TryToInputPeer(object? userOrChat, out InputPeer inputPeer)
    {
        switch (userOrChat)
        {
            case User user when user.flags.HasFlag(User.Flags.self):
                inputPeer = new InputPeerSelf();
                return true;
            case User user when user.flags.HasFlag(User.Flags.has_access_hash):
                inputPeer = new InputPeerUser(user.id, user.access_hash);
                return true;
            case Chat chat:
                inputPeer = new InputPeerChat(chat.id);
                return true;
            case ChatForbidden chatForbidden:
                inputPeer = new InputPeerChat(chatForbidden.id);
                return true;
            case Channel channel when channel.flags.HasFlag(Channel.Flags.has_access_hash):
                inputPeer = new InputPeerChannel(channel.id, channel.access_hash);
                return true;
            case ChannelForbidden channelForbidden:
                inputPeer = new InputPeerChannel(channelForbidden.id, channelForbidden.access_hash);
                return true;
            default:
                inputPeer = null!;
                return false;
        }
    }

    private IReadOnlyList<long> ResolveTargetChatIds()
    {
        var fromMonitored = _telegramSettings.MonitoredChatIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();
        var fromBackfill = _settings.ChatIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        if (fromMonitored.Count == 0)
        {
            return fromBackfill;
        }

        if (fromBackfill.Count == 0)
        {
            return fromMonitored;
        }

        return fromMonitored
            .Concat(fromBackfill)
            .Distinct()
            .ToList();
    }

    private static DateTime MaxUtc(DateTime a, DateTime b)
    {
        var ua = EnsureUtc(a);
        var ub = EnsureUtc(b);
        return ua >= ub ? ua : ub;
    }

    private static bool TryParseSinceDate(string raw, out DateTime sinceUtc)
    {
        if (DateTime.TryParseExact(
                raw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            sinceUtc = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsed))
        {
            sinceUtc = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
            return true;
        }

        sinceUtc = default;
        return false;
    }

    private string? ConfigProvider(string what)
    {
        return what switch
        {
            "api_id" => _telegramSettings.ApiId.ToString(CultureInfo.InvariantCulture),
            "api_hash" => _telegramSettings.ApiHash,
            "phone_number" => _telegramSettings.PhoneNumber,
            "verification_code" => GetVerificationCode(),
            "session_pathname" => "data/telegram.session",
            _ => null
        };
    }

    private static string GetVerificationCode()
    {
        Console.Write("Enter Telegram verification code: ");
        return Console.ReadLine() ?? string.Empty;
    }

    private sealed class PhaseLeaseHeartbeatHandle
    {
        public static PhaseLeaseHeartbeatHandle None { get; } = new();

        public PhaseLeaseHeartbeatHandle()
        {
        }

        public PhaseLeaseHeartbeatHandle(
            CancellationTokenSource heartbeatTokenSource,
            CancellationTokenSource leaseLostTokenSource,
            Task runTask)
        {
            HeartbeatTokenSource = heartbeatTokenSource;
            LeaseLostTokenSource = leaseLostTokenSource;
            RunTask = runTask;
        }

        public CancellationTokenSource? HeartbeatTokenSource { get; }
        public CancellationTokenSource? LeaseLostTokenSource { get; }
        public Task? RunTask { get; }
        public bool IsActive => HeartbeatTokenSource is not null && RunTask is not null;
        public bool LeaseLost => LeaseLostTokenSource?.IsCancellationRequested == true;
    }
}

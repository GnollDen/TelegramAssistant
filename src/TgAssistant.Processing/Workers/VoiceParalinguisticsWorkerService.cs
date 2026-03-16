using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Workers;

public class VoiceParalinguisticsWorkerService : BackgroundService
{
    private readonly VoiceParalinguisticsSettings _settings;
    private readonly ArchiveImportSettings _archiveImportSettings;
    private readonly MediaSettings _mediaSettings;
    private readonly IMessageRepository _messageRepository;
    private readonly IMediaProcessor _mediaProcessor;
    private readonly IVoiceParalinguisticsAnalyzer _analyzer;
    private readonly IExtractionErrorRepository _errorRepository;
    private readonly ILogger<VoiceParalinguisticsWorkerService> _logger;
    private readonly ConcurrentDictionary<long, DateTimeOffset> _blockedUntilByMessageId = new();
    private readonly ConcurrentDictionary<long, int> _failureStreakByMessageId = new();

    public VoiceParalinguisticsWorkerService(
        IOptions<VoiceParalinguisticsSettings> settings,
        IOptions<ArchiveImportSettings> archiveImportSettings,
        IOptions<MediaSettings> mediaSettings,
        IMessageRepository messageRepository,
        IMediaProcessor mediaProcessor,
        IVoiceParalinguisticsAnalyzer analyzer,
        IExtractionErrorRepository errorRepository,
        ILogger<VoiceParalinguisticsWorkerService> logger)
    {
        _settings = settings.Value;
        _archiveImportSettings = archiveImportSettings.Value;
        _mediaSettings = mediaSettings.Value;
        _messageRepository = messageRepository;
        _mediaProcessor = mediaProcessor;
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
                var batch = (await _messageRepository.GetPendingVoiceParalinguisticsAsync(_settings.BatchSize, stoppingToken))
                    .Where(message => !IsMessageBackedOff(message.Id))
                    .ToList();
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
                    async (message, ct) => await ProcessMessageAsync(message, ct));
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

    private async Task ProcessMessageAsync(Message message, CancellationToken ct)
    {
        var resolvedPath = ResolveAccessiblePath(message.MediaPath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            await PersistTerminalFailureAsync(
                message,
                null,
                "media_file_missing",
                clearMediaPath: true,
                ct);
            return;
        }

        var shouldDeleteSourceAudio = false;
        try
        {
            var result = await ProcessVoiceMessageAsync(message, resolvedPath, ct);
            switch (result.Status)
            {
                case VoiceProcessingStatus.Success:
                    ClearBackoff(message.Id);
                    await _messageRepository.UpdateVoiceProcessingResultAsync(
                        message.Id,
                        result.Transcription,
                        result.ParalinguisticsJson,
                        needsReanalysis: true,
                        clearMediaPath: ShouldDeleteSourceAudio(message, resolvedPath),
                        ct: ct);
                    shouldDeleteSourceAudio = ShouldDeleteSourceAudio(message, resolvedPath);
                    break;

                case VoiceProcessingStatus.TransientFailure:
                    RegisterTransientFailure(message.Id);
                    _logger.LogWarning(
                        "Voice processing deferred for message_id={MessageId}: {Reason}",
                        message.Id,
                        result.FailureReason);
                    break;

                case VoiceProcessingStatus.PermanentFailure:
                    ClearBackoff(message.Id);
                    await PersistTerminalFailureAsync(
                        message,
                        result.Transcription,
                        result.FailureReason ?? "voice_processing_failed",
                        clearMediaPath: ShouldDeleteSourceAudio(message, resolvedPath),
                        ct);
                    shouldDeleteSourceAudio = ShouldDeleteSourceAudio(message, resolvedPath);
                    break;
            }
        }
        catch (Exception ex)
        {
            RegisterTransientFailure(message.Id);
            _logger.LogWarning(ex, "Voice paralinguistics failed for message_id={MessageId}", message.Id);
            await _errorRepository.LogAsync(
                stage: "voice_paralinguistics",
                reason: ex.Message,
                messageId: message.Id,
                payload: $"path={resolvedPath};exception={ex.GetType().Name}",
                ct: ct);
        }
        finally
        {
            if (shouldDeleteSourceAudio)
            {
                TryDeleteFile(resolvedPath);
            }
        }
    }

    private async Task<VoiceProcessingAttemptResult> ProcessVoiceMessageAsync(Message message, string path, CancellationToken ct)
    {
        string? transcription = null;
        var transcriptionChanged = false;

        if (_settings.TranscriptionEnabled && string.IsNullOrWhiteSpace(message.MediaTranscription))
        {
            var transcriptionResult = await TranscribeWithRetryAsync(message.Id, path, message.MediaType, ct);
            if (transcriptionResult.Status != VoiceProcessingStatus.Success)
            {
                return transcriptionResult;
            }

            transcription = transcriptionResult.Transcription;
            transcriptionChanged = !string.IsNullOrWhiteSpace(transcription);
        }

        var paralinguisticsResult = await AnalyzeWithRetryAsync(message.Id, path, ct);
        if (paralinguisticsResult.Status == VoiceProcessingStatus.Success)
        {
            return paralinguisticsResult with
            {
                Transcription = transcriptionChanged ? transcription : null
            };
        }

        return paralinguisticsResult with
        {
            Transcription = transcriptionChanged ? transcription : null
        };
    }

    private async Task<VoiceProcessingAttemptResult> TranscribeWithRetryAsync(long messageId, string path, MediaType mediaType, CancellationToken ct)
    {
        var attempts = Math.Max(1, _settings.RetryCount + 1);
        string? lastReason = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var result = await _mediaProcessor.ProcessAsync(path, mediaType, ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Transcription))
            {
                return new VoiceProcessingAttemptResult(
                    VoiceProcessingStatus.Success,
                    result.Transcription.Trim(),
                    null,
                    null);
            }

            lastReason = string.IsNullOrWhiteSpace(result.FailureReason)
                ? "voice_transcription_failed"
                : result.FailureReason.Trim();

            if (!IsTransientFailure(lastReason) || attempt >= attempts)
            {
                return new VoiceProcessingAttemptResult(
                    IsTransientFailure(lastReason) ? VoiceProcessingStatus.TransientFailure : VoiceProcessingStatus.PermanentFailure,
                    null,
                    null,
                    lastReason);
            }

            await DelayBeforeRetryAsync(messageId, attempt, "transcription", ct);
        }

        return new VoiceProcessingAttemptResult(VoiceProcessingStatus.TransientFailure, null, null, lastReason ?? "voice_transcription_retry_failed");
    }

    private async Task<VoiceProcessingAttemptResult> AnalyzeWithRetryAsync(long messageId, string path, CancellationToken ct)
    {
        var attempts = Math.Max(1, _settings.RetryCount + 1);
        string? lastReason = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                var payload = await _analyzer.AnalyzeAsync(path, ct);
                return new VoiceProcessingAttemptResult(VoiceProcessingStatus.Success, null, payload, null);
            }
            catch (Exception ex)
            {
                lastReason = ex.Message;
                if (!IsTransientFailure(ex) || attempt >= attempts)
                {
                    return new VoiceProcessingAttemptResult(
                        IsTransientFailure(ex) ? VoiceProcessingStatus.TransientFailure : VoiceProcessingStatus.PermanentFailure,
                        null,
                        null,
                        ex.Message);
                }

                await DelayBeforeRetryAsync(messageId, attempt, "paralinguistics", ct);
            }
        }

        return new VoiceProcessingAttemptResult(VoiceProcessingStatus.TransientFailure, null, null, lastReason ?? "voice_paralinguistics_retry_failed");
    }

    private async Task DelayBeforeRetryAsync(long messageId, int attempt, string phase, CancellationToken ct)
    {
        var delaySeconds = Math.Max(1, _settings.RetryBaseDelaySeconds) * attempt;
        _logger.LogDebug(
            "Voice processing retry message_id={MessageId}, phase={Phase}, attempt={Attempt}, delay={Delay}s",
            messageId,
            phase,
            attempt,
            delaySeconds);
        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
    }

    private async Task PersistTerminalFailureAsync(
        Message message,
        string? transcription,
        string reason,
        bool clearMediaPath,
        CancellationToken ct)
    {
        var failurePayload = JsonSerializer.Serialize(new
        {
            status = "failed",
            reason,
            failed_at = DateTime.UtcNow
        });
        var needsReanalysis = !string.IsNullOrWhiteSpace(transcription);

        await _messageRepository.UpdateVoiceProcessingResultAsync(
            message.Id,
            transcription,
            failurePayload,
            needsReanalysis,
            clearMediaPath,
            ct);

        await _errorRepository.LogAsync(
            stage: "voice_paralinguistics_terminal",
            reason: reason,
            messageId: message.Id,
            payload: $"source={message.Source};media_type={message.MediaType};clear_media_path={clearMediaPath}",
            ct: ct);
    }

    private bool IsMessageBackedOff(long messageId)
    {
        if (!_blockedUntilByMessageId.TryGetValue(messageId, out var blockedUntil))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow >= blockedUntil)
        {
            _blockedUntilByMessageId.TryRemove(messageId, out _);
            return false;
        }

        return true;
    }

    private void RegisterTransientFailure(long messageId)
    {
        var streak = _failureStreakByMessageId.TryGetValue(messageId, out var current)
            ? current + 1
            : 1;
        _failureStreakByMessageId[messageId] = streak;

        var rawSeconds = Math.Max(1, _settings.TransientBackoffBaseSeconds) * (1 << Math.Min(8, streak - 1));
        var boundedSeconds = Math.Min(Math.Max(rawSeconds, _settings.TransientBackoffBaseSeconds), Math.Max(_settings.TransientBackoffBaseSeconds, _settings.TransientBackoffMaxSeconds));
        _blockedUntilByMessageId[messageId] = DateTimeOffset.UtcNow.AddSeconds(boundedSeconds);
    }

    private void ClearBackoff(long messageId)
    {
        _failureStreakByMessageId.TryRemove(messageId, out _);
        _blockedUntilByMessageId.TryRemove(messageId, out _);
    }

    private static bool IsTransientFailure(Exception ex)
    {
        if (ex is TaskCanceledException or TimeoutException)
        {
            return true;
        }

        return ex is HttpRequestException hre && IsTransientFailure(hre.Message);
    }

    private static bool IsTransientFailure(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return false;
        }

        return reason.Contains("timeout", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || reason.Contains(" 429", StringComparison.OrdinalIgnoreCase)
               || reason.Contains(" 500", StringComparison.OrdinalIgnoreCase)
               || reason.Contains(" 502", StringComparison.OrdinalIgnoreCase)
               || reason.Contains(" 503", StringComparison.OrdinalIgnoreCase)
               || reason.Contains(" 504", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("tempor", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("connection", StringComparison.OrdinalIgnoreCase)
               || reason.Contains("upstream", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldDeleteSourceAudio(Message message, string resolvedPath)
    {
        if (!_settings.DeleteSourceAudioAfterProcessing || message.Source != MessageSource.Realtime)
        {
            return false;
        }

        var storageRoot = Path.GetFullPath(_mediaSettings.StoragePath);
        var mediaFullPath = Path.GetFullPath(resolvedPath);
        return mediaFullPath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase);
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete processed voice media file: {Path}", path);
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

        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        var normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_archiveImportSettings.MediaBasePath, normalized));
    }

    private enum VoiceProcessingStatus
    {
        Success,
        TransientFailure,
        PermanentFailure
    }

    private sealed record VoiceProcessingAttemptResult(
        VoiceProcessingStatus Status,
        string? Transcription,
        string? ParalinguisticsJson,
        string? FailureReason);
}

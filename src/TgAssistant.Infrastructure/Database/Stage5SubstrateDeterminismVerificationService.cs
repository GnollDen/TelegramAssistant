using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage5SubstrateDeterminismVerificationService
{
    private const long RealtimeTextTelegramMessageId = 11_001;
    private const long RealtimeVoiceTelegramMessageId = 11_002;
    private const long ArchivePhotoTelegramMessageId = 21_001;
    private const long ArchiveVoiceTelegramMessageId = 21_002;

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IMessageRepository _messageRepository;
    private readonly IRealtimeMessageSubstrateRepository _realtimeMessageSubstrateRepository;
    private readonly IArchiveMessageSubstrateRepository _archiveMessageSubstrateRepository;
    private readonly IArchiveImportRepository _archiveImportRepository;
    private readonly IChatSessionRepository _chatSessionRepository;
    private readonly ILogger<Stage5SubstrateDeterminismVerificationService> _logger;

    public Stage5SubstrateDeterminismVerificationService(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IMessageRepository messageRepository,
        IRealtimeMessageSubstrateRepository realtimeMessageSubstrateRepository,
        IArchiveMessageSubstrateRepository archiveMessageSubstrateRepository,
        IArchiveImportRepository archiveImportRepository,
        IChatSessionRepository chatSessionRepository,
        ILogger<Stage5SubstrateDeterminismVerificationService> logger)
    {
        _dbFactory = dbFactory;
        _messageRepository = messageRepository;
        _realtimeMessageSubstrateRepository = realtimeMessageSubstrateRepository;
        _archiveMessageSubstrateRepository = archiveMessageSubstrateRepository;
        _archiveImportRepository = archiveImportRepository;
        _chatSessionRepository = chatSessionRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var entropy = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L;
        var chatId = ScopeVisibilityPolicy.SyntheticSmokeChatIdMin + entropy;
        var senderId = 5_001L;
        var scopeKey = BuildScopeKey(chatId);
        var sourcePath = $"/synthetic/stage5-substrate-determinism/{chatId}.json";
        var archiveRun = await _archiveImportRepository.CreateRunAsync(new ArchiveImportRun
        {
            Id = Guid.NewGuid(),
            SourcePath = sourcePath,
            Status = ArchiveImportRunStatus.Running,
            LastMessageIndex = -1,
            ImportedMessages = 0,
            QueuedMedia = 0,
            TotalMessages = 2,
            TotalMedia = 2,
            EstimatedCostUsd = 0
        }, ct);

        try
        {
            await EnsureSyntheticPersonAsync(chatId, senderId, ct);

            var firstSnapshot = await ExecuteReplayAsync(chatId, senderId, archiveRun.Id, sourcePath, ct);
            AssertExpectedCounts(firstSnapshot);

            var secondSnapshot = await ExecuteReplayAsync(chatId, senderId, archiveRun.Id, sourcePath, ct);
            AssertExpectedCounts(secondSnapshot);
            AssertSnapshotsEqual(firstSnapshot, secondSnapshot);

            if (secondSnapshot.ModelPassRunCount != 0
                || secondSnapshot.NormalizationRunCount != 0
                || secondSnapshot.DurableObjectMetadataCount != 0)
            {
                throw new InvalidOperationException(
                    $"Stage5 substrate determinism smoke failed: Stage5 wrote non-substrate rows for scope '{scopeKey}'.");
            }

            await _archiveImportRepository.CompleteRunAsync(archiveRun.Id, ArchiveImportRunStatus.Completed, null, ct);

            _logger.LogInformation(
                "Stage5 substrate determinism smoke passed. chat_id={ChatId}, messages={MessageCount}, source_objects={SourceCount}, evidence_items={EvidenceCount}, evidence_person_links={LinkCount}, sessions={SessionCount}",
                chatId,
                secondSnapshot.MessageIdsByTelegramMessageId.Count,
                secondSnapshot.SourceObjectIdsByKey.Count,
                secondSnapshot.EvidenceIdsByKey.Count,
                secondSnapshot.PersonLinkIdsByKey.Count,
                secondSnapshot.SessionIdsByIndex.Count);
        }
        catch (Exception ex)
        {
            await TryMarkArchiveRunFailedAsync(archiveRun.Id, ex.Message, ct);
            throw;
        }
    }

    private async Task<ReplaySnapshot> ExecuteReplayAsync(
        long chatId,
        long senderId,
        Guid archiveImportRunId,
        string sourcePath,
        CancellationToken ct)
    {
        var realtimeMessages = BuildRealtimeMessages(chatId, senderId);
        await _messageRepository.SaveBatchAsync(realtimeMessages, ct);

        var persistedRealtime = await _messageRepository.GetByTelegramMessageIdsAsync(
            chatId,
            MessageSource.Realtime,
            realtimeMessages.Select(x => x.TelegramMessageId).ToArray(),
            ct);
        EnsurePersistedMessages("realtime", persistedRealtime, realtimeMessages.Count);
        await _realtimeMessageSubstrateRepository.UpsertRealtimeBatchAsync(persistedRealtime.Values.ToList(), ct);

        var archiveMessages = BuildArchiveMessages(chatId, senderId);
        await _messageRepository.SaveBatchAsync(archiveMessages, ct);

        var persistedArchive = await _messageRepository.GetByTelegramMessageIdsAsync(
            chatId,
            MessageSource.Archive,
            archiveMessages.Select(x => x.TelegramMessageId).ToArray(),
            ct);
        EnsurePersistedMessages("archive", persistedArchive, archiveMessages.Count);
        await _archiveMessageSubstrateRepository.UpsertArchiveBatchAsync(
            persistedArchive.Values.ToList(),
            archiveImportRunId,
            sourcePath,
            ct);

        await _chatSessionRepository.UpsertAsync(BuildSession(chatId), ct);

        await _messageRepository.UpdateMediaProcessingResultAsync(
            persistedArchive[ArchivePhotoTelegramMessageId].Id,
            new MediaProcessingResult
            {
                Success = true,
                Description = "Synthetic archive photo enrichment",
                Confidence = 0.91f
            },
            ProcessingStatus.Processed,
            ct);
        await _messageRepository.UpdateMediaParalinguisticsAsync(
            persistedRealtime[RealtimeVoiceTelegramMessageId].Id,
            "{\"tempo\":\"steady\",\"energy\":\"calm\"}",
            ct);
        await _messageRepository.UpdateVoiceProcessingResultAsync(
            persistedArchive[ArchiveVoiceTelegramMessageId].Id,
            "Synthetic archive voice transcript",
            "{\"tone\":\"calm\",\"pace\":\"steady\"}",
            needsReanalysis: true,
            clearMediaPath: false,
            ct);

        return await CaptureSnapshotAsync(chatId, ct);
    }

    private async Task EnsureSyntheticPersonAsync(long chatId, long senderId, CancellationToken ct)
    {
        var scopeKey = BuildScopeKey(chatId);
        var actorKey = BuildActorKey(chatId, senderId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var existingPerson = await db.Persons.FirstOrDefaultAsync(
            x => x.ScopeKey == scopeKey && x.PrimaryActorKey == actorKey,
            ct);
        if (existingPerson != null)
        {
            return;
        }

        db.Persons.Add(new DbPerson
        {
            Id = Guid.NewGuid(),
            ScopeKey = scopeKey,
            PersonType = "tracked_person",
            DisplayName = "Stage5 Smoke Sender",
            CanonicalName = "stage5 smoke sender",
            Status = "active",
            PrimaryActorKey = actorKey,
            PrimaryTelegramUserId = senderId,
            PrimaryTelegramUsername = "stage5_smoke_sender",
            MetadataJson = "{\"source\":\"stage5_substrate_smoke\"}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }

    private async Task<ReplaySnapshot> CaptureSnapshotAsync(long chatId, CancellationToken ct)
    {
        var scopeKey = BuildScopeKey(chatId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var messages = await db.Messages
            .AsNoTracking()
            .Where(x => x.ChatId == chatId)
            .OrderBy(x => x.TelegramMessageId)
            .ToListAsync(ct);
        var sourceObjects = await db.SourceObjects
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey)
            .OrderBy(x => x.SourceKind)
            .ThenBy(x => x.SourceRef)
            .ToListAsync(ct);
        var evidenceRows = await (
            from evidence in db.EvidenceItems.AsNoTracking()
            join source in db.SourceObjects.AsNoTracking() on evidence.SourceObjectId equals source.Id
            where source.ScopeKey == scopeKey
            orderby source.SourceKind, source.SourceRef, evidence.EvidenceKind, evidence.TruthLayer
            select new
            {
                evidence.Id,
                source.SourceKind,
                source.SourceRef,
                evidence.EvidenceKind,
                evidence.TruthLayer
            })
            .ToListAsync(ct);
        var personLinkRows = await (
            from link in db.EvidenceItemPersonLinks.AsNoTracking()
            join evidence in db.EvidenceItems.AsNoTracking() on link.EvidenceItemId equals evidence.Id
            join source in db.SourceObjects.AsNoTracking() on evidence.SourceObjectId equals source.Id
            where source.ScopeKey == scopeKey
            orderby source.SourceKind, source.SourceRef, evidence.EvidenceKind, link.PersonId, link.LinkRole
            select new
            {
                link.Id,
                source.SourceKind,
                source.SourceRef,
                evidence.EvidenceKind,
                link.PersonId,
                link.LinkRole
            })
            .ToListAsync(ct);
        var sessions = await db.ChatSessions
            .AsNoTracking()
            .Where(x => x.ChatId == chatId)
            .OrderBy(x => x.SessionIndex)
            .ToListAsync(ct);

        return new ReplaySnapshot(
            messages.ToDictionary(x => x.TelegramMessageId, x => x.Id),
            sourceObjects.ToDictionary(x => BuildSourceKey(x.SourceKind, x.SourceRef), x => x.Id),
            evidenceRows.ToDictionary(
                x => BuildEvidenceKey(x.SourceKind, x.SourceRef, x.EvidenceKind, x.TruthLayer),
                x => x.Id),
            personLinkRows.ToDictionary(
                x => BuildPersonLinkKey(x.SourceKind, x.SourceRef, x.EvidenceKind, x.PersonId, x.LinkRole),
                x => x.Id),
            sessions.ToDictionary(x => x.SessionIndex, x => x.Id),
            await db.ModelPassRuns.AsNoTracking().CountAsync(x => x.ScopeKey == scopeKey, ct),
            await db.NormalizationRuns.AsNoTracking().CountAsync(x => x.ScopeKey == scopeKey, ct),
            await db.DurableObjectMetadata.AsNoTracking().CountAsync(x => x.ScopeKey == scopeKey, ct));
    }

    private static void EnsurePersistedMessages(string pathName, Dictionary<long, Message> persistedMessages, int expectedCount)
    {
        if (persistedMessages.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"Stage5 substrate determinism smoke failed: {pathName} replay resolved {persistedMessages.Count} canonical messages, expected {expectedCount}.");
        }
    }

    private static void AssertExpectedCounts(ReplaySnapshot snapshot)
    {
        if (snapshot.MessageIdsByTelegramMessageId.Count != 4
            || snapshot.SourceObjectIdsByKey.Count != 5
            || snapshot.EvidenceIdsByKey.Count != 8
            || snapshot.PersonLinkIdsByKey.Count != 7
            || snapshot.SessionIdsByIndex.Count != 1)
        {
            throw new InvalidOperationException(
                "Stage5 substrate determinism smoke failed: seeded replay did not produce the expected substrate shape.");
        }
    }

    private static void AssertSnapshotsEqual(ReplaySnapshot first, ReplaySnapshot second)
    {
        AssertDictionaryEqual("messages", first.MessageIdsByTelegramMessageId, second.MessageIdsByTelegramMessageId);
        AssertDictionaryEqual("source_objects", first.SourceObjectIdsByKey, second.SourceObjectIdsByKey);
        AssertDictionaryEqual("evidence_items", first.EvidenceIdsByKey, second.EvidenceIdsByKey);
        AssertDictionaryEqual("evidence_item_person_links", first.PersonLinkIdsByKey, second.PersonLinkIdsByKey);
        AssertDictionaryEqual("chat_sessions", first.SessionIdsByIndex, second.SessionIdsByIndex);

        if (first.ModelPassRunCount != second.ModelPassRunCount
            || first.NormalizationRunCount != second.NormalizationRunCount
            || first.DurableObjectMetadataCount != second.DurableObjectMetadataCount)
        {
            throw new InvalidOperationException(
                "Stage5 substrate determinism smoke failed: non-substrate table counts changed between replays.");
        }
    }

    private static void AssertDictionaryEqual<TKey, TValue>(
        string label,
        IReadOnlyDictionary<TKey, TValue> first,
        IReadOnlyDictionary<TKey, TValue> second)
        where TKey : notnull
        where TValue : notnull
    {
        if (first.Count != second.Count)
        {
            throw new InvalidOperationException(
                $"Stage5 substrate determinism smoke failed: '{label}' count changed between replays.");
        }

        foreach (var (key, value) in first)
        {
            if (!second.TryGetValue(key, out var secondValue)
                || !EqualityComparer<TValue>.Default.Equals(value, secondValue))
            {
                throw new InvalidOperationException(
                    $"Stage5 substrate determinism smoke failed: '{label}' identity drift detected for key '{key}'.");
            }
        }
    }

    private async Task TryMarkArchiveRunFailedAsync(Guid runId, string error, CancellationToken ct)
    {
        try
        {
            await _archiveImportRepository.CompleteRunAsync(runId, ArchiveImportRunStatus.Failed, error, ct);
        }
        catch (Exception completionEx)
        {
            _logger.LogWarning(
                completionEx,
                "Failed to mark synthetic archive run as failed after substrate determinism verification error. run_id={RunId}",
                runId);
        }
    }

    private static List<Message> BuildRealtimeMessages(long chatId, long senderId)
    {
        var now = DateTime.UtcNow;
        return
        [
            new Message
            {
                TelegramMessageId = RealtimeTextTelegramMessageId,
                ChatId = chatId,
                SenderId = senderId,
                SenderName = "Stage5 Smoke Sender",
                Timestamp = now.AddMinutes(-20),
                Text = "Realtime deterministic replay smoke message.",
                Source = MessageSource.Realtime,
                ProcessingStatus = ProcessingStatus.Pending
            },
            new Message
            {
                TelegramMessageId = RealtimeVoiceTelegramMessageId,
                ChatId = chatId,
                SenderId = senderId,
                SenderName = "Stage5 Smoke Sender",
                Timestamp = now.AddMinutes(-18),
                MediaType = MediaType.Voice,
                MediaPath = "/tmp/stage5-smoke-realtime-voice.ogg",
                Source = MessageSource.Realtime,
                ProcessingStatus = ProcessingStatus.Pending
            }
        ];
    }

    private static List<Message> BuildArchiveMessages(long chatId, long senderId)
    {
        var now = DateTime.UtcNow;
        return
        [
            new Message
            {
                TelegramMessageId = ArchivePhotoTelegramMessageId,
                ChatId = chatId,
                SenderId = senderId,
                SenderName = "Stage5 Smoke Sender",
                Timestamp = now.AddHours(-3),
                Text = "Archive photo replay smoke message.",
                MediaType = MediaType.Photo,
                MediaPath = "/tmp/stage5-smoke-archive-photo.jpg",
                Source = MessageSource.Archive,
                ProcessingStatus = ProcessingStatus.Pending
            },
            new Message
            {
                TelegramMessageId = ArchiveVoiceTelegramMessageId,
                ChatId = chatId,
                SenderId = senderId,
                SenderName = "Stage5 Smoke Sender",
                Timestamp = now.AddHours(-2),
                MediaType = MediaType.Voice,
                MediaPath = "/tmp/stage5-smoke-archive-voice.ogg",
                Source = MessageSource.Archive,
                ProcessingStatus = ProcessingStatus.Pending
            }
        ];
    }

    private static ChatSession BuildSession(long chatId)
    {
        var now = DateTime.UtcNow;
        return new ChatSession
        {
            Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ChatId = chatId,
            SessionIndex = 1,
            StartDate = now.AddHours(-4),
            EndDate = now.AddHours(-1),
            LastMessageAt = now.AddHours(-1),
            Summary = "Synthetic deterministic replay session",
            IsFinalized = true,
            IsAnalyzed = false
        };
    }

    private static string BuildScopeKey(long chatId) => $"chat:{chatId}";

    private static string BuildActorKey(long chatId, long senderId) => $"{chatId}:{senderId}";

    private static string BuildSourceKey(string sourceKind, string sourceRef) => $"{sourceKind}|{sourceRef}";

    private static string BuildEvidenceKey(string sourceKind, string sourceRef, string evidenceKind, string truthLayer)
        => $"{sourceKind}|{sourceRef}|{evidenceKind}|{truthLayer}";

    private static string BuildPersonLinkKey(string sourceKind, string sourceRef, string evidenceKind, Guid personId, string linkRole)
        => $"{sourceKind}|{sourceRef}|{evidenceKind}|{personId}|{linkRole}";

    private sealed record ReplaySnapshot(
        IReadOnlyDictionary<long, long> MessageIdsByTelegramMessageId,
        IReadOnlyDictionary<string, Guid> SourceObjectIdsByKey,
        IReadOnlyDictionary<string, Guid> EvidenceIdsByKey,
        IReadOnlyDictionary<string, long> PersonLinkIdsByKey,
        IReadOnlyDictionary<int, Guid> SessionIdsByIndex,
        int ModelPassRunCount,
        int NormalizationRunCount,
        int DurableObjectMetadataCount);
}

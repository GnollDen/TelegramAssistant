using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ArchiveMessageSubstrateRepository : IArchiveMessageSubstrateRepository
{
    private const string SourceKind = "telegram_archive_message";
    private const string ProvenanceKind = "telegram_desktop_archive";
    private const string EvidenceKind = "telegram_message";
    private const string TruthLayer = "canonical_truth";
    private const string SenderLinkRole = "sender";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<ArchiveMessageSubstrateRepository> _logger;

    public ArchiveMessageSubstrateRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<ArchiveMessageSubstrateRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task UpsertArchiveBatchAsync(
        IReadOnlyCollection<Message> messages,
        Guid archiveImportRunId,
        string sourcePath,
        CancellationToken ct = default)
    {
        var items = messages
            .Where(x => x.Id > 0 && x.ChatId > 0)
            .OrderBy(x => x.Id)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var messageIds = items.Select(x => x.Id).Distinct().ToArray();
        var scopeKeys = items.Select(x => BuildScopeKey(x.ChatId)).Distinct().ToArray();
        var senderIds = items.Where(x => x.SenderId > 0).Select(x => x.SenderId).Distinct().ToArray();
        var actorKeys = items
            .Where(x => x.SenderId > 0)
            .Select(x => BuildActorKey(x.ChatId, x.SenderId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var existingSources = await db.SourceObjects
            .Where(x => x.SourceKind == SourceKind
                        && x.SourceMessageId != null
                        && messageIds.Contains(x.SourceMessageId.Value))
            .ToListAsync(ct);
        var sourceByMessageId = existingSources
            .GroupBy(x => x.SourceMessageId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.UpdatedAt).First());

        var matchedPersons = await db.Persons
            .AsNoTracking()
            .Where(x => scopeKeys.Contains(x.ScopeKey)
                        && ((x.PrimaryActorKey != null && actorKeys.Contains(x.PrimaryActorKey))
                            || (x.PrimaryTelegramUserId != null && senderIds.Contains(x.PrimaryTelegramUserId.Value))))
            .ToListAsync(ct);
        var personsByActorKey = matchedPersons
            .Where(x => !string.IsNullOrWhiteSpace(x.PrimaryActorKey))
            .GroupBy(x => (x.ScopeKey, x.PrimaryActorKey!))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).First().Id);
        var personsByTelegramUserId = matchedPersons
            .Where(x => x.PrimaryTelegramUserId != null)
            .GroupBy(x => (x.ScopeKey, x.PrimaryTelegramUserId!.Value))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).First().Id);

        foreach (var message in items)
        {
            var scopeKey = BuildScopeKey(message.ChatId);
            if (!sourceByMessageId.TryGetValue(message.Id, out var source))
            {
                source = new DbSourceObject
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = now
                };
                db.SourceObjects.Add(source);
                sourceByMessageId[message.Id] = source;
            }

            source.ScopeKey = scopeKey;
            source.SourceKind = SourceKind;
            source.SourceRef = BuildSourceRef(message.ChatId, message.TelegramMessageId);
            source.ProvenanceKind = ProvenanceKind;
            source.ProvenanceRef = BuildProvenanceRef(message.ChatId, message.TelegramMessageId);
            source.ProvenanceNormalized = source.ProvenanceRef;
            source.Status = "active";
            source.DisplayLabel = BuildDisplayLabel(message);
            source.ChatId = message.ChatId;
            source.SourceMessageId = message.Id;
            source.SourceSessionId = null;
            source.ArchiveImportRunId = archiveImportRunId;
            source.OccurredAt = message.Timestamp;
            source.PayloadJson = JsonSerializer.Serialize(BuildSourcePayload(message));
            source.MetadataJson = JsonSerializer.Serialize(BuildSourceMetadata(message, archiveImportRunId, sourcePath));
            source.UpdatedAt = now;
        }

        var sourceIds = sourceByMessageId.Values.Select(x => x.Id).ToArray();
        var existingEvidence = await db.EvidenceItems
            .Where(x => sourceIds.Contains(x.SourceObjectId)
                        && x.EvidenceKind == EvidenceKind
                        && x.TruthLayer == TruthLayer)
            .ToListAsync(ct);
        var evidenceBySourceObjectId = existingEvidence
            .GroupBy(x => x.SourceObjectId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.UpdatedAt).First());

        foreach (var message in items)
        {
            var source = sourceByMessageId[message.Id];
            if (!evidenceBySourceObjectId.TryGetValue(source.Id, out var evidence))
            {
                evidence = new DbEvidenceItem
                {
                    Id = Guid.NewGuid(),
                    CreatedAt = now
                };
                db.EvidenceItems.Add(evidence);
                evidenceBySourceObjectId[source.Id] = evidence;
            }

            evidence.ScopeKey = source.ScopeKey;
            evidence.SourceObjectId = source.Id;
            evidence.EvidenceKind = EvidenceKind;
            evidence.Status = "active";
            evidence.TruthLayer = TruthLayer;
            evidence.SummaryText = BuildEvidenceSummary(message);
            evidence.StructuredPayloadJson = JsonSerializer.Serialize(BuildEvidencePayload(message));
            evidence.ProvenanceJson = JsonSerializer.Serialize(BuildEvidenceProvenance(source, message, archiveImportRunId, sourcePath));
            evidence.Confidence = 1.0f;
            evidence.ObservedAt = message.Timestamp;
            evidence.UpdatedAt = now;
        }

        var evidenceIds = evidenceBySourceObjectId.Values.Select(x => x.Id).ToArray();
        var existingSenderLinks = await db.EvidenceItemPersonLinks
            .Where(x => evidenceIds.Contains(x.EvidenceItemId) && x.LinkRole == SenderLinkRole)
            .ToListAsync(ct);
        var senderLinkedEvidenceIds = existingSenderLinks
            .Select(x => x.EvidenceItemId)
            .ToHashSet();

        foreach (var message in items)
        {
            var evidence = evidenceBySourceObjectId[sourceByMessageId[message.Id].Id];
            if (senderLinkedEvidenceIds.Contains(evidence.Id))
            {
                continue;
            }

            var personId = ResolveSenderPersonId(message, personsByActorKey, personsByTelegramUserId);
            if (personId == null)
            {
                continue;
            }

            db.EvidenceItemPersonLinks.Add(new DbEvidenceItemPersonLink
            {
                EvidenceItemId = evidence.Id,
                PersonId = personId.Value,
                ScopeKey = BuildScopeKey(message.ChatId),
                LinkRole = SenderLinkRole,
                IsPrimary = true,
                CreatedAt = now
            });
            senderLinkedEvidenceIds.Add(evidence.Id);
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Archive substrate upserted: messages={MessageCount}, source_objects={SourceCount}, evidence_items={EvidenceCount}, archive_import_run_id={RunId}",
            items.Count,
            sourceByMessageId.Count,
            evidenceBySourceObjectId.Count,
            archiveImportRunId);
    }

    private static Guid? ResolveSenderPersonId(
        Message message,
        IReadOnlyDictionary<(string ScopeKey, string ActorKey), Guid> personsByActorKey,
        IReadOnlyDictionary<(string ScopeKey, long TelegramUserId), Guid> personsByTelegramUserId)
    {
        if (message.SenderId <= 0)
        {
            return null;
        }

        var scopeKey = BuildScopeKey(message.ChatId);
        var actorKey = BuildActorKey(message.ChatId, message.SenderId);
        if (personsByActorKey.TryGetValue((scopeKey, actorKey), out var actorMatch))
        {
            return actorMatch;
        }

        return personsByTelegramUserId.TryGetValue((scopeKey, message.SenderId), out var telegramMatch)
            ? telegramMatch
            : null;
    }

    private static string BuildScopeKey(long chatId) => $"chat:{chatId}";

    private static string BuildActorKey(long chatId, long senderId) => $"{chatId}:{senderId}";

    private static string BuildSourceRef(long chatId, long telegramMessageId) => $"{chatId}:{telegramMessageId}";

    private static string BuildProvenanceRef(long chatId, long telegramMessageId) => $"{chatId}:{telegramMessageId}";

    private static string BuildDisplayLabel(Message message)
        => $"Archive message {message.TelegramMessageId} in chat {message.ChatId}";

    private static object BuildSourcePayload(Message message)
    {
        return new
        {
            message_id = message.Id,
            telegram_message_id = message.TelegramMessageId,
            chat_id = message.ChatId,
            sender_id = message.SenderId,
            sender_name = message.SenderName,
            text = message.Text,
            media_type = message.MediaType.ToString(),
            media_path = message.MediaPath,
            media_description = message.MediaDescription,
            media_transcription = message.MediaTranscription,
            media_paralinguistics_json = message.MediaParalinguisticsJson,
            reply_to_message_id = message.ReplyToMessageId,
            edit_timestamp_utc = message.EditTimestamp?.ToUniversalTime().ToString("O"),
            reactions_json = message.ReactionsJson,
            forward_json = message.ForwardJson
        };
    }

    private static object BuildSourceMetadata(Message message, Guid archiveImportRunId, string sourcePath)
    {
        return new
        {
            ingest_path = "archive_import",
            archive_import_run_id = archiveImportRunId,
            archive_source_path = sourcePath,
            message_source = message.Source.ToString(),
            processing_status = message.ProcessingStatus.ToString(),
            processed_at_utc = message.ProcessedAt?.ToUniversalTime().ToString("O"),
            needs_reanalysis = message.NeedsReanalysis
        };
    }

    private static string BuildEvidenceSummary(Message message)
    {
        if (!string.IsNullOrWhiteSpace(message.Text))
        {
            var text = message.Text.Trim();
            return text.Length <= 300 ? text : text[..300].TrimEnd() + "...";
        }

        return $"[{message.MediaType}]";
    }

    private static object BuildEvidencePayload(Message message)
    {
        return new
        {
            message_id = message.Id,
            telegram_message_id = message.TelegramMessageId,
            chat_id = message.ChatId,
            sender = new
            {
                id = message.SenderId,
                name = message.SenderName
            },
            content = new
            {
                text = message.Text,
                media_type = message.MediaType.ToString(),
                media_path = message.MediaPath,
                media_description = message.MediaDescription,
                media_transcription = message.MediaTranscription,
                media_paralinguistics_json = message.MediaParalinguisticsJson
            },
            reply_to_message_id = message.ReplyToMessageId,
            edit_timestamp_utc = message.EditTimestamp?.ToUniversalTime().ToString("O"),
            reactions_json = message.ReactionsJson,
            forward_json = message.ForwardJson
        };
    }

    private static object BuildEvidenceProvenance(
        DbSourceObject source,
        Message message,
        Guid archiveImportRunId,
        string sourcePath)
    {
        return new
        {
            ingest_path = "archive_import",
            archive_import_run_id = archiveImportRunId,
            archive_source_path = sourcePath,
            provenance_kind = source.ProvenanceKind,
            provenance_ref = source.ProvenanceRef,
            source_kind = source.SourceKind,
            source_ref = source.SourceRef,
            source_message_id = message.Id,
            observed_at_utc = message.Timestamp.ToUniversalTime().ToString("O")
        };
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Processing.Archive.ExternalIngestion;

public class ExternalArchiveIngestionService : IExternalArchiveIngestionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IExternalArchivePreparationService _preparationService;
    private readonly IExternalArchiveIngestionRepository _repository;
    private readonly IDomainReviewEventRepository _reviewEvents;
    private readonly ILogger<ExternalArchiveIngestionService> _logger;

    public ExternalArchiveIngestionService(
        IExternalArchivePreparationService preparationService,
        IExternalArchiveIngestionRepository repository,
        IDomainReviewEventRepository reviewEvents,
        ILogger<ExternalArchiveIngestionService> logger)
    {
        _preparationService = preparationService;
        _repository = repository;
        _reviewEvents = reviewEvents;
        _logger = logger;
    }

    public async Task<ExternalArchiveIngestionResult> IngestAsync(ExternalArchiveImportRequest request, CancellationToken ct = default)
    {
        var preparation = await _preparationService.PrepareAsync(request, ct);
        if (!preparation.Validation.IsValid)
        {
            throw new InvalidOperationException($"External archive contract is invalid: {string.Join(" | ", preparation.Validation.Errors)}");
        }

        var requestPayloadHash = ComputeEnvelopeHash(request);
        var existingBatch = await _repository.GetBatchByDedupKeyAsync(
            request.CaseId,
            request.SourceClass,
            request.SourceRef,
            requestPayloadHash,
            ct);

        if (existingBatch is not null)
        {
            var replayRecords = await _repository.GetRecordsByRunIdAsync(existingBatch.RunId, ct);
            var replayLinks = await _repository.GetLinkageArtifactsByRunIdAsync(existingBatch.RunId, ct);
            await AddReviewEventAsync(existingBatch, "external_archive_replayed", "idempotent replay detected", request.Actor, ct);

            _logger.LogInformation(
                "External archive replay detected: run_id={RunId}, case_id={CaseId}, records={RecordCount}, links={LinkCount}",
                existingBatch.RunId,
                existingBatch.CaseId,
                replayRecords.Count,
                replayLinks.Count);

            return new ExternalArchiveIngestionResult
            {
                Batch = existingBatch,
                PersistedRecordCount = replayRecords.Count,
                PersistedLinkageCount = replayLinks.Count,
                IsReplay = true
            };
        }

        var importBatchId = string.IsNullOrWhiteSpace(request.ImportBatchId)
            ? ExternalArchiveHashing.ComputeSha256($"{request.CaseId}:{request.SourceClass}:{request.SourceRef}:{request.ImportedAtUtc:O}")[..16]
            : request.ImportBatchId!;

        var batch = await _repository.CreateBatchAsync(new ExternalArchiveImportBatch
        {
            RunId = Guid.NewGuid(),
            CaseId = request.CaseId,
            SourceClass = request.SourceClass,
            SourceRef = request.SourceRef,
            ImportBatchId = importBatchId,
            RequestPayloadHash = requestPayloadHash,
            ImportedAtUtc = request.ImportedAtUtc == default ? DateTime.UtcNow : request.ImportedAtUtc,
            Actor = string.IsNullOrWhiteSpace(request.Actor) ? "system" : request.Actor,
            RecordCount = request.Records.Count,
            AcceptedCount = 0,
            ReplayedCount = 0,
            RejectedCount = 0,
            Status = ExternalArchiveIngestionStatuses.Prepared
        }, ct);

        var acceptedCount = 0;
        var replayedCount = 0;
        var rejectedCount = 0;
        var persistedLinkages = 0;
        var rejections = new List<string>();

        foreach (var prepared in preparation.PreparedRecords)
        {
            ct.ThrowIfCancellationRequested();

            var existingRecord = await _repository.GetRecordByNaturalKeyAsync(
                request.CaseId,
                request.SourceClass,
                request.SourceRef,
                prepared.Record.RecordId,
                ct);

            if (existingRecord is not null)
            {
                if (!string.Equals(existingRecord.PayloadHash, prepared.Provenance.PayloadHash, StringComparison.Ordinal))
                {
                    rejectedCount++;
                    var reason = $"record_id={prepared.Record.RecordId} rejected: conflicting payload hash for duplicate replay.";
                    rejections.Add(reason);
                    continue;
                }

                replayedCount++;
                continue;
            }

            var persistedRecord = await _repository.CreateRecordAsync(new ExternalArchivePersistedRecord
            {
                Id = Guid.NewGuid(),
                RunId = batch.RunId,
                CaseId = request.CaseId,
                SourceClass = prepared.Provenance.SourceClass,
                SourceRef = prepared.Provenance.SourceRef,
                ImportBatchId = prepared.Provenance.ImportBatchId,
                RecordId = prepared.Record.RecordId,
                OccurredAtUtc = prepared.Record.OccurredAtUtc,
                RecordType = prepared.Record.RecordType,
                Text = prepared.Record.Text,
                SubjectActorKey = prepared.Record.SubjectActorKey,
                TargetActorKey = prepared.Record.TargetActorKey,
                ChatId = prepared.Record.ChatId,
                SourceMessageId = prepared.Record.SourceMessageId,
                SourceSessionId = prepared.Record.SourceSessionId,
                Confidence = prepared.Record.Confidence,
                RawPayloadJson = prepared.Record.RawPayloadJson,
                EvidenceRefsJson = SerializeJson(prepared.Record.EvidenceRefs),
                TruthLayer = prepared.Provenance.TruthLayer,
                PayloadHash = prepared.Provenance.PayloadHash,
                BaseWeight = prepared.Weighting.BaseWeight,
                ConfidenceMultiplier = prepared.Weighting.ConfidenceMultiplier,
                CorroborationMultiplier = prepared.Weighting.CorroborationMultiplier,
                FinalWeight = prepared.Weighting.FinalWeight,
                NeedsClarification = prepared.Weighting.NeedsClarification,
                WeightingReason = prepared.Weighting.WeightingReason,
                Status = ExternalArchiveIngestionStatuses.Persisted,
                CreatedAt = DateTime.UtcNow
            }, ct);

            acceptedCount++;

            foreach (var linkage in prepared.Linkages)
            {
                _ = await _repository.CreateLinkageArtifactAsync(new ExternalArchiveLinkageArtifact
                {
                    Id = Guid.NewGuid(),
                    RunId = batch.RunId,
                    RecordRowId = persistedRecord.Id,
                    CaseId = request.CaseId,
                    LinkType = linkage.LinkType,
                    TargetType = linkage.TargetType,
                    TargetId = linkage.TargetId,
                    LinkConfidence = linkage.LinkConfidence,
                    Reason = linkage.Reason,
                    ReviewStatus = ExternalArchiveIngestionStatuses.Prepared,
                    AutoApplyAllowed = false,
                    CreatedAt = DateTime.UtcNow
                }, ct);
                persistedLinkages++;
            }
        }

        var finalStatus = rejectedCount > 0
            ? ExternalArchiveIngestionStatuses.PartialRejected
            : ExternalArchiveIngestionStatuses.Persisted;

        await _repository.UpdateBatchStatusAsync(
            batch.RunId,
            acceptedCount,
            replayedCount,
            rejectedCount,
            finalStatus,
            ct);

        var persistedBatch = new ExternalArchiveImportBatch
        {
            RunId = batch.RunId,
            CaseId = batch.CaseId,
            SourceClass = batch.SourceClass,
            SourceRef = batch.SourceRef,
            ImportBatchId = batch.ImportBatchId,
            RequestPayloadHash = batch.RequestPayloadHash,
            ImportedAtUtc = batch.ImportedAtUtc,
            Actor = batch.Actor,
            RecordCount = batch.RecordCount,
            AcceptedCount = acceptedCount,
            ReplayedCount = replayedCount,
            RejectedCount = rejectedCount,
            Status = finalStatus,
            CreatedAt = batch.CreatedAt,
            UpdatedAt = DateTime.UtcNow
        };

        await AddReviewEventAsync(
            persistedBatch,
            "external_archive_ingested",
            $"accepted={acceptedCount}, replayed={replayedCount}, rejected={rejectedCount}, links={persistedLinkages}",
            request.Actor,
            ct);

        _logger.LogInformation(
            "External archive ingested: run_id={RunId}, case_id={CaseId}, source_class={SourceClass}, accepted={Accepted}, replayed={Replayed}, rejected={Rejected}, linkages={Linkages}",
            persistedBatch.RunId,
            persistedBatch.CaseId,
            persistedBatch.SourceClass,
            acceptedCount,
            replayedCount,
            rejectedCount,
            persistedLinkages);

        return new ExternalArchiveIngestionResult
        {
            Batch = persistedBatch,
            Rejections = rejections,
            PersistedRecordCount = acceptedCount,
            PersistedLinkageCount = persistedLinkages,
            IsReplay = false
        };
    }

    private async Task AddReviewEventAsync(
        ExternalArchiveImportBatch batch,
        string action,
        string reason,
        string actor,
        CancellationToken ct)
    {
        _ = await _reviewEvents.AddAsync(new DomainReviewEvent
        {
            ObjectType = "external_archive_batch",
            ObjectId = batch.RunId.ToString(),
            Action = action,
            NewValueRef = $"status={batch.Status}",
            Reason = reason,
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private static string SerializeJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string ComputeEnvelopeHash(ExternalArchiveImportRequest request)
    {
        var canonicalRecords = request.Records
            .OrderBy(x => x.RecordId, StringComparer.Ordinal)
            .Select(x => new
            {
                x.RecordId,
                x.RecordType,
                x.OccurredAtUtc,
                x.ChatId,
                x.SourceMessageId,
                x.SourceSessionId,
                x.Confidence,
                x.RawPayloadJson
            })
            .ToList();

        var canonical = new
        {
            request.CaseId,
            request.SourceClass,
            request.SourceRef,
            request.ImportBatchId,
            request.ImportedAtUtc,
            Records = canonicalRecords
        };

        return ExternalArchiveHashing.ComputeSha256(JsonSerializer.Serialize(canonical, JsonOptions));
    }
}

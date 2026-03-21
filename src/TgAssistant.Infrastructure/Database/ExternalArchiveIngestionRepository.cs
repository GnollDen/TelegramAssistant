using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class ExternalArchiveIngestionRepository : IExternalArchiveIngestionRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public ExternalArchiveIngestionRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ExternalArchiveImportBatch?> GetBatchByDedupKeyAsync(
        long caseId,
        string sourceClass,
        string sourceRef,
        string requestPayloadHash,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.ExternalArchiveImportBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.CaseId == caseId
                    && x.SourceClass == sourceClass
                    && x.SourceRef == sourceRef
                    && x.RequestPayloadHash == requestPayloadHash,
                    ct);

            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<ExternalArchiveImportBatch> CreateBatchAsync(ExternalArchiveImportBatch batch, CancellationToken ct = default)
    {
        var row = new DbExternalArchiveImportBatch
        {
            RunId = batch.RunId == Guid.Empty ? Guid.NewGuid() : batch.RunId,
            CaseId = batch.CaseId,
            SourceClass = batch.SourceClass,
            SourceRef = batch.SourceRef,
            ImportBatchId = batch.ImportBatchId,
            RequestPayloadHash = batch.RequestPayloadHash,
            ImportedAtUtc = batch.ImportedAtUtc,
            Actor = batch.Actor,
            RecordCount = batch.RecordCount,
            AcceptedCount = batch.AcceptedCount,
            ReplayedCount = batch.ReplayedCount,
            RejectedCount = batch.RejectedCount,
            Status = batch.Status,
            CreatedAt = batch.CreatedAt == default ? DateTime.UtcNow : batch.CreatedAt,
            UpdatedAt = batch.UpdatedAt == default ? DateTime.UtcNow : batch.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.ExternalArchiveImportBatches.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task UpdateBatchStatusAsync(
        Guid runId,
        int acceptedCount,
        int replayedCount,
        int rejectedCount,
        string status,
        CancellationToken ct = default)
    {
        await WithDbContextAsync(async db =>
        {
            var row = await db.ExternalArchiveImportBatches.FirstOrDefaultAsync(x => x.RunId == runId, ct)
                ?? throw new InvalidOperationException($"external archive batch '{runId}' was not found.");

            row.AcceptedCount = acceptedCount;
            row.ReplayedCount = replayedCount;
            row.RejectedCount = rejectedCount;
            row.Status = status;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    public async Task<ExternalArchivePersistedRecord?> GetRecordByNaturalKeyAsync(
        long caseId,
        string sourceClass,
        string sourceRef,
        string recordId,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.ExternalArchiveImportRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.CaseId == caseId
                    && x.SourceClass == sourceClass
                    && x.SourceRef == sourceRef
                    && x.RecordId == recordId,
                    ct);

            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<ExternalArchivePersistedRecord> CreateRecordAsync(ExternalArchivePersistedRecord record, CancellationToken ct = default)
    {
        var row = new DbExternalArchiveImportRecord
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            RunId = record.RunId,
            CaseId = record.CaseId,
            SourceClass = record.SourceClass,
            SourceRef = record.SourceRef,
            ImportBatchId = record.ImportBatchId,
            RecordId = record.RecordId,
            OccurredAtUtc = record.OccurredAtUtc,
            RecordType = record.RecordType,
            Text = record.Text,
            SubjectActorKey = record.SubjectActorKey,
            TargetActorKey = record.TargetActorKey,
            ChatId = record.ChatId,
            SourceMessageId = record.SourceMessageId,
            SourceSessionId = record.SourceSessionId,
            Confidence = record.Confidence,
            RawPayloadJson = record.RawPayloadJson,
            EvidenceRefsJson = record.EvidenceRefsJson,
            TruthLayer = record.TruthLayer,
            PayloadHash = record.PayloadHash,
            BaseWeight = record.BaseWeight,
            ConfidenceMultiplier = record.ConfidenceMultiplier,
            CorroborationMultiplier = record.CorroborationMultiplier,
            FinalWeight = record.FinalWeight,
            NeedsClarification = record.NeedsClarification,
            WeightingReason = record.WeightingReason,
            Status = record.Status,
            CreatedAt = record.CreatedAt == default ? DateTime.UtcNow : record.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.ExternalArchiveImportRecords.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<ExternalArchiveLinkageArtifact> CreateLinkageArtifactAsync(ExternalArchiveLinkageArtifact artifact, CancellationToken ct = default)
    {
        var row = new DbExternalArchiveLinkageArtifact
        {
            Id = artifact.Id == Guid.Empty ? Guid.NewGuid() : artifact.Id,
            RunId = artifact.RunId,
            RecordRowId = artifact.RecordRowId,
            CaseId = artifact.CaseId,
            LinkType = artifact.LinkType,
            TargetType = artifact.TargetType,
            TargetId = artifact.TargetId,
            LinkConfidence = artifact.LinkConfidence,
            Reason = artifact.Reason,
            ReviewStatus = artifact.ReviewStatus,
            AutoApplyAllowed = artifact.AutoApplyAllowed,
            CreatedAt = artifact.CreatedAt == default ? DateTime.UtcNow : artifact.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.ExternalArchiveLinkageArtifacts.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<ExternalArchivePersistedRecord>> GetRecordsByRunIdAsync(Guid runId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.ExternalArchiveImportRecords
                .AsNoTracking()
                .Where(x => x.RunId == runId)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<List<ExternalArchivePersistedRecord>> GetRecentRecordsByCaseSourceAsync(
        long caseId,
        string sourceClass,
        long? chatId,
        DateTime asOfUtc,
        int limit = 200,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var normalizedLimit = Math.Clamp(limit, 1, 2000);

            var query = db.ExternalArchiveImportRecords
                .AsNoTracking()
                .Where(x =>
                    x.CaseId == caseId
                    && x.SourceClass == sourceClass
                    && x.OccurredAtUtc <= asOfUtc);

            if (chatId.HasValue)
            {
                query = query.Where(x => x.ChatId == null || x.ChatId == chatId.Value);
            }

            var rows = await query
                .OrderByDescending(x => x.OccurredAtUtc)
                .ThenByDescending(x => x.CreatedAt)
                .Take(normalizedLimit)
                .ToListAsync(ct);

            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<List<ExternalArchiveLinkageArtifact>> GetLinkageArtifactsByRunIdAsync(Guid runId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.ExternalArchiveLinkageArtifacts
                .AsNoTracking()
                .Where(x => x.RunId == runId)
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static ExternalArchiveImportBatch ToDomain(DbExternalArchiveImportBatch row) => new()
    {
        RunId = row.RunId,
        CaseId = row.CaseId,
        SourceClass = row.SourceClass,
        SourceRef = row.SourceRef,
        ImportBatchId = row.ImportBatchId,
        RequestPayloadHash = row.RequestPayloadHash,
        ImportedAtUtc = row.ImportedAtUtc,
        Actor = row.Actor,
        RecordCount = row.RecordCount,
        AcceptedCount = row.AcceptedCount,
        ReplayedCount = row.ReplayedCount,
        RejectedCount = row.RejectedCount,
        Status = row.Status,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static ExternalArchivePersistedRecord ToDomain(DbExternalArchiveImportRecord row) => new()
    {
        Id = row.Id,
        RunId = row.RunId,
        CaseId = row.CaseId,
        SourceClass = row.SourceClass,
        SourceRef = row.SourceRef,
        ImportBatchId = row.ImportBatchId,
        RecordId = row.RecordId,
        OccurredAtUtc = row.OccurredAtUtc,
        RecordType = row.RecordType,
        Text = row.Text,
        SubjectActorKey = row.SubjectActorKey,
        TargetActorKey = row.TargetActorKey,
        ChatId = row.ChatId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        Confidence = row.Confidence,
        RawPayloadJson = row.RawPayloadJson,
        EvidenceRefsJson = row.EvidenceRefsJson,
        TruthLayer = row.TruthLayer,
        PayloadHash = row.PayloadHash,
        BaseWeight = row.BaseWeight,
        ConfidenceMultiplier = row.ConfidenceMultiplier,
        CorroborationMultiplier = row.CorroborationMultiplier,
        FinalWeight = row.FinalWeight,
        NeedsClarification = row.NeedsClarification,
        WeightingReason = row.WeightingReason,
        Status = row.Status,
        CreatedAt = row.CreatedAt
    };

    private static ExternalArchiveLinkageArtifact ToDomain(DbExternalArchiveLinkageArtifact row) => new()
    {
        Id = row.Id,
        RunId = row.RunId,
        RecordRowId = row.RecordRowId,
        CaseId = row.CaseId,
        LinkType = row.LinkType,
        TargetType = row.TargetType,
        TargetId = row.TargetId,
        LinkConfidence = row.LinkConfidence,
        Reason = row.Reason,
        ReviewStatus = row.ReviewStatus,
        AutoApplyAllowed = row.AutoApplyAllowed,
        CreatedAt = row.CreatedAt
    };

    private async Task<TResult> WithDbContextAsync<TResult>(Func<TgAssistantDbContext, Task<TResult>> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            return await action(ambientDb);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await action(db);
    }

    private async Task WithDbContextAsync(Func<TgAssistantDbContext, Task> action, CancellationToken ct)
    {
        var ambientDb = AmbientDbContextScope.Current;
        if (ambientDb is not null)
        {
            await action(ambientDb);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await action(db);
    }
}

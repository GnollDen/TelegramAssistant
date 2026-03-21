using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class OfflineEventRepository : IOfflineEventRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public OfflineEventRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<OfflineEvent> CreateOfflineEventAsync(OfflineEvent evt, CancellationToken ct = default)
    {
        var row = new DbOfflineEvent
        {
            Id = evt.Id == Guid.Empty ? Guid.NewGuid() : evt.Id,
            CaseId = evt.CaseId,
            ChatId = evt.ChatId,
            EventType = evt.EventType,
            Title = evt.Title,
            UserSummary = evt.UserSummary,
            AutoSummary = evt.AutoSummary,
            TimestampStart = evt.TimestampStart,
            TimestampEnd = evt.TimestampEnd,
            PeriodId = evt.PeriodId,
            ReviewStatus = evt.ReviewStatus,
            ImpactSummary = evt.ImpactSummary,
            SourceType = evt.SourceType,
            SourceId = evt.SourceId,
            SourceMessageId = evt.SourceMessageId,
            SourceSessionId = evt.SourceSessionId,
            EvidenceRefsJson = evt.EvidenceRefsJson,
            CreatedAt = evt.CreatedAt == default ? DateTime.UtcNow : evt.CreatedAt,
            UpdatedAt = evt.UpdatedAt == default ? DateTime.UtcNow : evt.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.OfflineEvents.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<OfflineEvent?> GetOfflineEventByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.OfflineEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<OfflineEvent>> GetOfflineEventsByCaseAsync(long caseId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.OfflineEvents
                .AsNoTracking()
                .Where(x => x.CaseId == caseId)
                .OrderByDescending(x => x.TimestampStart)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<int> AssignPeriodByTimeRangeAsync(
        long caseId,
        long? chatId,
        Guid periodId,
        DateTime startAt,
        DateTime? endAt,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var safeEnd = endAt ?? DateTime.MaxValue;
            var query = db.OfflineEvents.Where(x => x.CaseId == caseId && x.TimestampStart >= startAt && x.TimestampStart <= safeEnd);
            if (chatId.HasValue)
            {
                query = query.Where(x => x.ChatId == null || x.ChatId == chatId.Value);
            }

            var rows = await query.ToListAsync(ct);
            if (rows.Count == 0)
            {
                return 0;
            }

            var now = DateTime.UtcNow;
            foreach (var row in rows)
            {
                row.PeriodId = periodId;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync(ct);
            return rows.Count;
        }, ct);
    }

    public async Task<AudioAsset> CreateAudioAssetAsync(AudioAsset asset, CancellationToken ct = default)
    {
        var row = new DbAudioAsset
        {
            Id = asset.Id == Guid.Empty ? Guid.NewGuid() : asset.Id,
            OfflineEventId = asset.OfflineEventId,
            FilePath = asset.FilePath,
            DurationSeconds = asset.DurationSeconds,
            TranscriptStatus = asset.TranscriptStatus,
            TranscriptText = asset.TranscriptText,
            SpeakerReviewStatus = asset.SpeakerReviewStatus,
            ProcessingStatus = asset.ProcessingStatus,
            CreatedAt = asset.CreatedAt == default ? DateTime.UtcNow : asset.CreatedAt,
            UpdatedAt = asset.UpdatedAt == default ? DateTime.UtcNow : asset.UpdatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.AudioAssets.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<AudioAsset?> GetAudioAssetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.AudioAssets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<AudioAsset>> GetAudioAssetsByOfflineEventIdAsync(Guid offlineEventId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.AudioAssets.AsNoTracking().Where(x => x.OfflineEventId == offlineEventId).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<AudioSegment> CreateAudioSegmentAsync(AudioSegment segment, CancellationToken ct = default)
    {
        var row = new DbAudioSegment
        {
            Id = segment.Id == Guid.Empty ? Guid.NewGuid() : segment.Id,
            AudioAssetId = segment.AudioAssetId,
            SegmentIndex = segment.SegmentIndex,
            StartSeconds = segment.StartSeconds,
            EndSeconds = segment.EndSeconds,
            SpeakerLabel = segment.SpeakerLabel,
            TranscriptText = segment.TranscriptText,
            Confidence = segment.Confidence,
            CreatedAt = segment.CreatedAt == default ? DateTime.UtcNow : segment.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.AudioSegments.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<AudioSegment>> GetAudioSegmentsByAssetIdAsync(Guid audioAssetId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.AudioSegments
                .AsNoTracking()
                .Where(x => x.AudioAssetId == audioAssetId)
                .OrderBy(x => x.SegmentIndex)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<AudioSnippet> CreateAudioSnippetAsync(AudioSnippet snippet, CancellationToken ct = default)
    {
        var row = new DbAudioSnippet
        {
            Id = snippet.Id == Guid.Empty ? Guid.NewGuid() : snippet.Id,
            AudioAssetId = snippet.AudioAssetId,
            AudioSegmentId = snippet.AudioSegmentId,
            SnippetType = snippet.SnippetType,
            Text = snippet.Text,
            Confidence = snippet.Confidence,
            EvidenceRefsJson = snippet.EvidenceRefsJson,
            CreatedAt = snippet.CreatedAt == default ? DateTime.UtcNow : snippet.CreatedAt
        };

        await WithDbContextAsync(async db =>
        {
            db.AudioSnippets.Add(row);
            await db.SaveChangesAsync(ct);
        }, ct);

        return ToDomain(row);
    }

    public async Task<List<AudioSnippet>> GetAudioSnippetsByAssetIdAsync(Guid audioAssetId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.AudioSnippets
                .AsNoTracking()
                .Where(x => x.AudioAssetId == audioAssetId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static OfflineEvent ToDomain(DbOfflineEvent row) => new()
    {
        Id = row.Id,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        EventType = row.EventType,
        Title = row.Title,
        UserSummary = row.UserSummary,
        AutoSummary = row.AutoSummary,
        TimestampStart = row.TimestampStart,
        TimestampEnd = row.TimestampEnd,
        PeriodId = row.PeriodId,
        ReviewStatus = row.ReviewStatus,
        ImpactSummary = row.ImpactSummary,
        SourceType = row.SourceType,
        SourceId = row.SourceId,
        SourceMessageId = row.SourceMessageId,
        SourceSessionId = row.SourceSessionId,
        EvidenceRefsJson = row.EvidenceRefsJson,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static AudioAsset ToDomain(DbAudioAsset row) => new()
    {
        Id = row.Id,
        OfflineEventId = row.OfflineEventId,
        FilePath = row.FilePath,
        DurationSeconds = row.DurationSeconds,
        TranscriptStatus = row.TranscriptStatus,
        TranscriptText = row.TranscriptText,
        SpeakerReviewStatus = row.SpeakerReviewStatus,
        ProcessingStatus = row.ProcessingStatus,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt
    };

    private static AudioSegment ToDomain(DbAudioSegment row) => new()
    {
        Id = row.Id,
        AudioAssetId = row.AudioAssetId,
        SegmentIndex = row.SegmentIndex,
        StartSeconds = row.StartSeconds,
        EndSeconds = row.EndSeconds,
        SpeakerLabel = row.SpeakerLabel,
        TranscriptText = row.TranscriptText,
        Confidence = row.Confidence,
        CreatedAt = row.CreatedAt
    };

    private static AudioSnippet ToDomain(DbAudioSnippet row) => new()
    {
        Id = row.Id,
        AudioAssetId = row.AudioAssetId,
        AudioSegmentId = row.AudioSegmentId,
        SnippetType = row.SnippetType,
        Text = row.Text,
        Confidence = row.Confidence,
        EvidenceRefsJson = row.EvidenceRefsJson,
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

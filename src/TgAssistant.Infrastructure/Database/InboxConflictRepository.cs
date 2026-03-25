using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class InboxConflictRepository : IInboxConflictRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly IStage6CaseRepository _stage6CaseRepository;

    public InboxConflictRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        IStage6CaseRepository stage6CaseRepository)
    {
        _dbFactory = dbFactory;
        _stage6CaseRepository = stage6CaseRepository;
    }

    public async Task<InboxItem> CreateInboxItemAsync(InboxItem item, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = new DbInboxItem
        {
            Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id,
            ItemType = item.ItemType,
            SourceObjectType = item.SourceObjectType,
            SourceObjectId = item.SourceObjectId,
            Priority = item.Priority,
            IsBlocking = item.IsBlocking,
            Title = item.Title,
            Summary = item.Summary,
            PeriodId = item.PeriodId,
            CaseId = item.CaseId,
            ChatId = item.ChatId,
            Status = item.Status,
            LastActor = item.LastActor,
            LastReason = item.LastReason,
            CreatedAt = item.CreatedAt == default ? now : item.CreatedAt,
            UpdatedAt = item.UpdatedAt == default ? now : item.UpdatedAt
        };

        return await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO domain_inbox_items (
                    id,
                    item_type,
                    source_object_type,
                    source_object_id,
                    priority,
                    is_blocking,
                    title,
                    summary,
                    period_id,
                    case_id,
                    chat_id,
                    status,
                    last_actor,
                    last_reason,
                    created_at,
                    updated_at
                )
                VALUES (
                    {row.Id},
                    {row.ItemType},
                    {row.SourceObjectType},
                    {row.SourceObjectId},
                    {row.Priority},
                    {row.IsBlocking},
                    {row.Title},
                    {row.Summary},
                    {row.PeriodId},
                    {row.CaseId},
                    {row.ChatId},
                    {row.Status},
                    {row.LastActor},
                    {row.LastReason},
                    {row.CreatedAt},
                    {row.UpdatedAt}
                )
                ON CONFLICT (case_id, item_type, source_object_type, source_object_id)
                DO UPDATE
                SET priority = EXCLUDED.priority,
                    is_blocking = EXCLUDED.is_blocking,
                    title = EXCLUDED.title,
                    summary = EXCLUDED.summary,
                    period_id = EXCLUDED.period_id,
                    chat_id = COALESCE(EXCLUDED.chat_id, domain_inbox_items.chat_id),
                    status = EXCLUDED.status,
                    last_actor = EXCLUDED.last_actor,
                    last_reason = EXCLUDED.last_reason,
                    updated_at = EXCLUDED.updated_at;
                """, ct);

            var persisted = await db.InboxItems
                .AsNoTracking()
                .Where(x => x.CaseId == row.CaseId
                            && x.ItemType == row.ItemType
                            && x.SourceObjectType == row.SourceObjectType
                            && x.SourceObjectId == row.SourceObjectId)
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstAsync(ct);

            _ = await UpsertCaseFromInboxAsync(persisted, ct);
            return ToDomain(persisted);
        }, ct);
    }

    public async Task<InboxItem?> GetInboxItemByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.InboxItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<InboxItem>> GetInboxItemsAsync(long caseId, string? status = null, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var query = db.InboxItems.AsNoTracking().Where(x => x.CaseId == caseId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            var rows = await query.OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<ConflictRecord> CreateConflictRecordAsync(ConflictRecord record, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = new DbConflictRecord
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            ConflictType = record.ConflictType,
            ObjectAType = record.ObjectAType,
            ObjectAId = record.ObjectAId,
            ObjectBType = record.ObjectBType,
            ObjectBId = record.ObjectBId,
            Summary = record.Summary,
            Severity = record.Severity,
            Status = record.Status,
            PeriodId = record.PeriodId,
            CaseId = record.CaseId,
            ChatId = record.ChatId,
            LastActor = record.LastActor,
            LastReason = record.LastReason,
            CreatedAt = record.CreatedAt == default ? now : record.CreatedAt,
            UpdatedAt = record.UpdatedAt == default ? now : record.UpdatedAt
        };

        return await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO domain_conflict_records (
                    id,
                    conflict_type,
                    object_a_type,
                    object_a_id,
                    object_b_type,
                    object_b_id,
                    summary,
                    severity,
                    status,
                    period_id,
                    case_id,
                    chat_id,
                    last_actor,
                    last_reason,
                    created_at,
                    updated_at
                )
                VALUES (
                    {row.Id},
                    {row.ConflictType},
                    {row.ObjectAType},
                    {row.ObjectAId},
                    {row.ObjectBType},
                    {row.ObjectBId},
                    {row.Summary},
                    {row.Severity},
                    {row.Status},
                    {row.PeriodId},
                    {row.CaseId},
                    {row.ChatId},
                    {row.LastActor},
                    {row.LastReason},
                    {row.CreatedAt},
                    {row.UpdatedAt}
                )
                ON CONFLICT (
                    case_id,
                    conflict_type,
                    (LEAST(object_a_type || ':' || object_a_id, object_b_type || ':' || object_b_id)),
                    (GREATEST(object_a_type || ':' || object_a_id, object_b_type || ':' || object_b_id))
                )
                DO UPDATE
                SET object_a_type = EXCLUDED.object_a_type,
                    object_a_id = EXCLUDED.object_a_id,
                    object_b_type = EXCLUDED.object_b_type,
                    object_b_id = EXCLUDED.object_b_id,
                    summary = EXCLUDED.summary,
                    severity = EXCLUDED.severity,
                    status = EXCLUDED.status,
                    period_id = EXCLUDED.period_id,
                    chat_id = COALESCE(EXCLUDED.chat_id, domain_conflict_records.chat_id),
                    last_actor = EXCLUDED.last_actor,
                    last_reason = EXCLUDED.last_reason,
                    updated_at = EXCLUDED.updated_at;
                """, ct);

            var persisted = await db.ConflictRecords
                .AsNoTracking()
                .Where(x => x.CaseId == row.CaseId
                            && x.ConflictType == row.ConflictType
                            && ((x.ObjectAType == row.ObjectAType
                                 && x.ObjectAId == row.ObjectAId
                                 && x.ObjectBType == row.ObjectBType
                                 && x.ObjectBId == row.ObjectBId)
                                || (x.ObjectAType == row.ObjectBType
                                    && x.ObjectAId == row.ObjectBId
                                    && x.ObjectBType == row.ObjectAType
                                    && x.ObjectBId == row.ObjectAId)))
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstAsync(ct);

            _ = await UpsertCaseFromConflictAsync(persisted, ct);
            return ToDomain(persisted);
        }, ct);
    }

    public async Task<ConflictRecord?> GetConflictRecordByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.ConflictRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<ConflictRecord>> GetConflictRecordsAsync(long caseId, string? status = null, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var query = db.ConflictRecords.AsNoTracking().Where(x => x.CaseId == caseId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(x => x.Status == status);
            }

            var rows = await query.OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<bool> UpdateInboxItemStatusAsync(Guid id, string status, string actor, string? reason = null, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            var row = await db.InboxItems.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null)
            {
                return false;
            }

            var oldRef = JsonSerializer.Serialize(new { row.Status });
            row.Status = status;
            row.LastActor = actor;
            row.LastReason = reason;
            row.UpdatedAt = DateTime.UtcNow;

            db.DomainReviewEvents.Add(new DbDomainReviewEvent
            {
                Id = Guid.NewGuid(),
                ObjectType = "inbox_item",
                ObjectId = row.Id.ToString(),
                Action = "update_status",
                OldValueRef = oldRef,
                NewValueRef = JsonSerializer.Serialize(new { row.Status }),
                Reason = reason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            _ = await UpsertCaseFromInboxAsync(row, ct);
            return true;
        }, ct);
    }

    public async Task<bool> UpdateConflictStatusAsync(Guid id, string status, string actor, string? reason = null, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            using var scope = AmbientDbContextScope.Enter(db);
            var row = await db.ConflictRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null)
            {
                return false;
            }

            var oldRef = JsonSerializer.Serialize(new { row.Status });
            row.Status = status;
            row.LastActor = actor;
            row.LastReason = reason;
            row.UpdatedAt = DateTime.UtcNow;

            db.DomainReviewEvents.Add(new DbDomainReviewEvent
            {
                Id = Guid.NewGuid(),
                ObjectType = "conflict_record",
                ObjectId = row.Id.ToString(),
                Action = "update_status",
                OldValueRef = oldRef,
                NewValueRef = JsonSerializer.Serialize(new { row.Status }),
                Reason = reason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            _ = await UpsertCaseFromConflictAsync(row, ct);
            return true;
        }, ct);
    }

    private async Task<Stage6CaseRecord> UpsertCaseFromInboxAsync(DbInboxItem row, CancellationToken ct)
    {
        var stage6Case = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = row.CaseId,
            ChatId = row.ChatId,
            ScopeType = "chat",
            CaseType = ResolveInboxCaseType(row.ItemType),
            CaseSubtype = row.ItemType,
            Status = ResolveCaseStatusFromInboxStatus(row.Status),
            Priority = NormalizePriority(row.Priority),
            Confidence = null,
            ReasonSummary = string.IsNullOrWhiteSpace(row.Summary) ? row.Title : row.Summary,
            EvidenceRefsJson = "[]",
            SubjectRefsJson = "[]",
            TargetArtifactTypesJson = "[]",
            ReopenTriggerRulesJson = """["new_signal","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                row.ItemType,
                row.Title,
                row.LastActor,
                row.LastReason
            }),
            SourceObjectType = "inbox_item",
            SourceObjectId = row.Id.ToString(),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        }, ct);

        await _stage6CaseRepository.UpsertLinkAsync(new Stage6CaseLink
        {
            Stage6CaseId = stage6Case.Id,
            LinkedObjectType = "inbox_item",
            LinkedObjectId = row.Id.ToString(),
            LinkRole = Stage6CaseLinkRoles.Source,
            MetadataJson = "{}",
            CreatedAt = row.CreatedAt
        }, ct);

        return stage6Case;
    }

    private async Task<Stage6CaseRecord> UpsertCaseFromConflictAsync(DbConflictRecord row, CancellationToken ct)
    {
        var stage6Case = await _stage6CaseRepository.UpsertAsync(new Stage6CaseRecord
        {
            ScopeCaseId = row.CaseId,
            ChatId = row.ChatId,
            ScopeType = "chat",
            CaseType = Stage6CaseTypes.Risk,
            CaseSubtype = row.ConflictType,
            Status = ResolveCaseStatusFromConflictStatus(row.Status),
            Priority = ResolvePriorityFromSeverity(row.Severity),
            Confidence = ResolveConfidenceFromSeverity(row.Severity),
            ReasonSummary = row.Summary,
            EvidenceRefsJson = "[]",
            SubjectRefsJson = "[]",
            TargetArtifactTypesJson = "[]",
            ReopenTriggerRulesJson = """["new_conflict_evidence","operator_reopen"]""",
            ProvenanceJson = JsonSerializer.Serialize(new
            {
                row.ConflictType,
                row.ObjectAType,
                row.ObjectAId,
                row.ObjectBType,
                row.ObjectBId,
                row.LastActor,
                row.LastReason
            }),
            SourceObjectType = "conflict_record",
            SourceObjectId = row.Id.ToString(),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        }, ct);

        await _stage6CaseRepository.UpsertLinkAsync(new Stage6CaseLink
        {
            Stage6CaseId = stage6Case.Id,
            LinkedObjectType = "conflict_record",
            LinkedObjectId = row.Id.ToString(),
            LinkRole = Stage6CaseLinkRoles.Source,
            MetadataJson = "{}",
            CreatedAt = row.CreatedAt
        }, ct);

        return stage6Case;
    }

    private static string ResolveInboxCaseType(string itemType)
    {
        var normalized = (itemType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Contains("refresh", StringComparison.OrdinalIgnoreCase))
        {
            return Stage6CaseTypes.StateRefreshNeeded;
        }

        if (normalized.Contains("dossier", StringComparison.OrdinalIgnoreCase))
        {
            return Stage6CaseTypes.DossierCandidate;
        }

        if (normalized.Contains("draft", StringComparison.OrdinalIgnoreCase))
        {
            return Stage6CaseTypes.DraftCandidate;
        }

        return Stage6CaseTypes.NeedsReview;
    }

    private static string ResolveCaseStatusFromInboxStatus(string status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "open" or "pending" or "in_progress" => Stage6CaseStatuses.Ready,
            "resolved" => Stage6CaseStatuses.Resolved,
            "rejected" => Stage6CaseStatuses.Rejected,
            "stale" => Stage6CaseStatuses.Stale,
            _ => Stage6CaseStatuses.New
        };
    }

    private static string ResolveCaseStatusFromConflictStatus(string status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "open" or "in_progress" => Stage6CaseStatuses.Ready,
            "resolved" => Stage6CaseStatuses.Resolved,
            "rejected" => Stage6CaseStatuses.Rejected,
            "stale" => Stage6CaseStatuses.Stale,
            _ => Stage6CaseStatuses.New
        };
    }

    private static string ResolvePriorityFromSeverity(string severity)
    {
        var normalized = (severity ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "high" => "blocking",
            "low" => "optional",
            _ => "important"
        };
    }

    private static float ResolveConfidenceFromSeverity(string severity)
    {
        var normalized = (severity ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "high" => 0.9f,
            "low" => 0.35f,
            _ => 0.65f
        };
    }

    private static string NormalizePriority(string priority)
    {
        var normalized = (priority ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "blocking" or "important" or "optional" => normalized,
            _ => "important"
        };
    }

    private static InboxItem ToDomain(DbInboxItem row) => new()
    {
        Id = row.Id,
        ItemType = row.ItemType,
        SourceObjectType = row.SourceObjectType,
        SourceObjectId = row.SourceObjectId,
        Priority = row.Priority,
        IsBlocking = row.IsBlocking,
        Title = row.Title,
        Summary = row.Summary,
        PeriodId = row.PeriodId,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        Status = row.Status,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        LastActor = row.LastActor,
        LastReason = row.LastReason
    };

    private static ConflictRecord ToDomain(DbConflictRecord row) => new()
    {
        Id = row.Id,
        ConflictType = row.ConflictType,
        ObjectAType = row.ObjectAType,
        ObjectAId = row.ObjectAId,
        ObjectBType = row.ObjectBType,
        ObjectBId = row.ObjectBId,
        Summary = row.Summary,
        Severity = row.Severity,
        Status = row.Status,
        PeriodId = row.PeriodId,
        CaseId = row.CaseId,
        ChatId = row.ChatId,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        LastActor = row.LastActor,
        LastReason = row.LastReason
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

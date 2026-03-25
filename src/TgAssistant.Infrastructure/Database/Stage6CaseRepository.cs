using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage6CaseRepository : IStage6CaseRepository
{
    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage6CaseRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage6CaseRecord> UpsertAsync(Stage6CaseRecord record, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var status = NormalizeStatus(record.Status);
        var row = new DbStage6Case
        {
            Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id,
            ScopeCaseId = record.ScopeCaseId,
            ChatId = record.ChatId,
            ScopeType = string.IsNullOrWhiteSpace(record.ScopeType) ? "chat" : record.ScopeType.Trim(),
            CaseType = NormalizeCaseType(record.CaseType),
            CaseSubtype = string.IsNullOrWhiteSpace(record.CaseSubtype) ? null : record.CaseSubtype.Trim(),
            Status = status,
            Priority = NormalizePriority(record.Priority),
            Confidence = record.Confidence.HasValue ? Math.Clamp(record.Confidence.Value, 0f, 1f) : null,
            ReasonSummary = record.ReasonSummary ?? string.Empty,
            ClarificationKind = string.IsNullOrWhiteSpace(record.ClarificationKind) ? null : record.ClarificationKind.Trim(),
            QuestionText = string.IsNullOrWhiteSpace(record.QuestionText) ? null : record.QuestionText.Trim(),
            ResponseMode = string.IsNullOrWhiteSpace(record.ResponseMode) ? null : record.ResponseMode.Trim(),
            ResponseChannelHint = string.IsNullOrWhiteSpace(record.ResponseChannelHint) ? null : record.ResponseChannelHint.Trim(),
            EvidenceRefsJson = NormalizeJson(record.EvidenceRefsJson, "[]"),
            SubjectRefsJson = NormalizeJson(record.SubjectRefsJson, "[]"),
            TargetArtifactTypesJson = NormalizeJson(record.TargetArtifactTypesJson, "[]"),
            ReopenTriggerRulesJson = NormalizeJson(record.ReopenTriggerRulesJson, "[]"),
            ProvenanceJson = NormalizeJson(record.ProvenanceJson, "{}"),
            SourceObjectType = record.SourceObjectType.Trim(),
            SourceObjectId = record.SourceObjectId.Trim(),
            CreatedAt = record.CreatedAt == default ? now : record.CreatedAt,
            UpdatedAt = record.UpdatedAt == default ? now : record.UpdatedAt,
            ReadyAt = record.ReadyAt,
            ResolvedAt = record.ResolvedAt,
            RejectedAt = record.RejectedAt,
            StaleAt = record.StaleAt
        };

        StampStatusTimestamps(row, now);

        return await WithDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO stage6_cases (
                    id,
                    scope_case_id,
                    chat_id,
                    scope_type,
                    case_type,
                    case_subtype,
                    status,
                    priority,
                    confidence,
                    reason_summary,
                    clarification_kind,
                    question_text,
                    response_mode,
                    response_channel_hint,
                    evidence_refs_json,
                    subject_refs_json,
                    target_artifact_types_json,
                    reopen_trigger_rules_json,
                    provenance_json,
                    source_object_type,
                    source_object_id,
                    created_at,
                    updated_at,
                    ready_at,
                    resolved_at,
                    rejected_at,
                    stale_at
                )
                VALUES (
                    {row.Id},
                    {row.ScopeCaseId},
                    {row.ChatId},
                    {row.ScopeType},
                    {row.CaseType},
                    {row.CaseSubtype},
                    {row.Status},
                    {row.Priority},
                    {row.Confidence},
                    {row.ReasonSummary},
                    {row.ClarificationKind},
                    {row.QuestionText},
                    {row.ResponseMode},
                    {row.ResponseChannelHint},
                    {row.EvidenceRefsJson}::jsonb,
                    {row.SubjectRefsJson}::jsonb,
                    {row.TargetArtifactTypesJson}::jsonb,
                    {row.ReopenTriggerRulesJson}::jsonb,
                    {row.ProvenanceJson}::jsonb,
                    {row.SourceObjectType},
                    {row.SourceObjectId},
                    {row.CreatedAt},
                    {row.UpdatedAt},
                    {row.ReadyAt},
                    {row.ResolvedAt},
                    {row.RejectedAt},
                    {row.StaleAt}
                )
                ON CONFLICT (scope_case_id, case_type, source_object_type, source_object_id)
                DO UPDATE
                SET chat_id = EXCLUDED.chat_id,
                    scope_type = EXCLUDED.scope_type,
                    case_subtype = EXCLUDED.case_subtype,
                    status = EXCLUDED.status,
                    priority = EXCLUDED.priority,
                    confidence = EXCLUDED.confidence,
                    reason_summary = EXCLUDED.reason_summary,
                    clarification_kind = EXCLUDED.clarification_kind,
                    question_text = EXCLUDED.question_text,
                    response_mode = EXCLUDED.response_mode,
                    response_channel_hint = EXCLUDED.response_channel_hint,
                    evidence_refs_json = EXCLUDED.evidence_refs_json,
                    subject_refs_json = EXCLUDED.subject_refs_json,
                    target_artifact_types_json = EXCLUDED.target_artifact_types_json,
                    reopen_trigger_rules_json = EXCLUDED.reopen_trigger_rules_json,
                    provenance_json = EXCLUDED.provenance_json,
                    updated_at = EXCLUDED.updated_at,
                    ready_at = EXCLUDED.ready_at,
                    resolved_at = EXCLUDED.resolved_at,
                    rejected_at = EXCLUDED.rejected_at,
                    stale_at = EXCLUDED.stale_at;
                """, ct);

            var persisted = await db.Stage6Cases
                .AsNoTracking()
                .Where(x => x.ScopeCaseId == row.ScopeCaseId
                            && x.CaseType == row.CaseType
                            && x.SourceObjectType == row.SourceObjectType
                            && x.SourceObjectId == row.SourceObjectId)
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstAsync(ct);

            return ToDomain(persisted);
        }, ct);
    }

    public async Task<Stage6CaseRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Stage6Cases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<Stage6CaseRecord?> GetBySourceAsync(
        long scopeCaseId,
        string caseType,
        string sourceObjectType,
        string sourceObjectId,
        CancellationToken ct = default)
    {
        var normalizedCaseType = NormalizeCaseType(caseType);
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Stage6Cases
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ScopeCaseId == scopeCaseId
                                          && x.CaseType == normalizedCaseType
                                          && x.SourceObjectType == sourceObjectType
                                          && x.SourceObjectId == sourceObjectId, ct);
            return row == null ? null : ToDomain(row);
        }, ct);
    }

    public async Task<List<Stage6CaseRecord>> GetCasesAsync(
        long scopeCaseId,
        string? status = null,
        string? caseType = null,
        CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var query = db.Stage6Cases.AsNoTracking().Where(x => x.ScopeCaseId == scopeCaseId);
            if (!string.IsNullOrWhiteSpace(status))
            {
                var normalizedStatus = NormalizeStatus(status);
                query = query.Where(x => x.Status == normalizedStatus);
            }

            if (!string.IsNullOrWhiteSpace(caseType))
            {
                var normalizedCaseType = NormalizeCaseType(caseType);
                query = query.Where(x => x.CaseType == normalizedCaseType);
            }

            var rows = await query.OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    public async Task<bool> UpdateStatusAsync(Guid id, string status, string actor, string? reason = null, CancellationToken ct = default)
    {
        var normalizedStatus = NormalizeStatus(status);
        return await WithDbContextAsync(async db =>
        {
            var row = await db.Stage6Cases.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (row == null)
            {
                return false;
            }

            var oldRef = JsonSerializer.Serialize(new { row.Status, row.ReadyAt, row.ResolvedAt, row.RejectedAt, row.StaleAt });
            row.Status = normalizedStatus;
            row.UpdatedAt = DateTime.UtcNow;
            StampStatusTimestamps(row, row.UpdatedAt);

            db.DomainReviewEvents.Add(new DbDomainReviewEvent
            {
                Id = Guid.NewGuid(),
                ObjectType = "stage6_case",
                ObjectId = row.Id.ToString(),
                Action = "update_status",
                OldValueRef = oldRef,
                NewValueRef = JsonSerializer.Serialize(new { row.Status, row.ReadyAt, row.ResolvedAt, row.RejectedAt, row.StaleAt }),
                Reason = reason,
                Actor = actor,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
    }

    public async Task<Stage6CaseLink> UpsertLinkAsync(Stage6CaseLink link, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = new DbStage6CaseLink
        {
            Id = link.Id == Guid.Empty ? Guid.NewGuid() : link.Id,
            Stage6CaseId = link.Stage6CaseId,
            LinkedObjectType = link.LinkedObjectType.Trim(),
            LinkedObjectId = link.LinkedObjectId.Trim(),
            LinkRole = link.LinkRole.Trim(),
            MetadataJson = NormalizeJson(link.MetadataJson, "{}"),
            CreatedAt = link.CreatedAt == default ? now : link.CreatedAt
        };

        return await WithDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO stage6_case_links (
                    id,
                    stage6_case_id,
                    linked_object_type,
                    linked_object_id,
                    link_role,
                    metadata_json,
                    created_at
                )
                VALUES (
                    {row.Id},
                    {row.Stage6CaseId},
                    {row.LinkedObjectType},
                    {row.LinkedObjectId},
                    {row.LinkRole},
                    {row.MetadataJson},
                    {row.CreatedAt}
                )
                ON CONFLICT (stage6_case_id, linked_object_type, linked_object_id, link_role)
                DO UPDATE
                SET metadata_json = EXCLUDED.metadata_json;
                """, ct);

            var persisted = await db.Stage6CaseLinks
                .AsNoTracking()
                .Where(x => x.Stage6CaseId == row.Stage6CaseId
                            && x.LinkedObjectType == row.LinkedObjectType
                            && x.LinkedObjectId == row.LinkedObjectId
                            && x.LinkRole == row.LinkRole)
                .OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id)
                .FirstAsync(ct);

            return ToDomain(persisted);
        }, ct);
    }

    public async Task<List<Stage6CaseLink>> GetLinksAsync(Guid stage6CaseId, CancellationToken ct = default)
    {
        return await WithDbContextAsync(async db =>
        {
            var rows = await db.Stage6CaseLinks
                .AsNoTracking()
                .Where(x => x.Stage6CaseId == stage6CaseId)
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(ct);
            return rows.Select(ToDomain).ToList();
        }, ct);
    }

    private static Stage6CaseRecord ToDomain(DbStage6Case row) => new()
    {
        Id = row.Id,
        ScopeCaseId = row.ScopeCaseId,
        ChatId = row.ChatId,
        ScopeType = row.ScopeType,
        CaseType = row.CaseType,
        CaseSubtype = row.CaseSubtype,
        Status = row.Status,
        Priority = row.Priority,
        Confidence = row.Confidence,
        ReasonSummary = row.ReasonSummary,
        ClarificationKind = row.ClarificationKind,
        QuestionText = row.QuestionText,
        ResponseMode = row.ResponseMode,
        ResponseChannelHint = row.ResponseChannelHint,
        EvidenceRefsJson = row.EvidenceRefsJson,
        SubjectRefsJson = row.SubjectRefsJson,
        TargetArtifactTypesJson = row.TargetArtifactTypesJson,
        ReopenTriggerRulesJson = row.ReopenTriggerRulesJson,
        ProvenanceJson = row.ProvenanceJson,
        SourceObjectType = row.SourceObjectType,
        SourceObjectId = row.SourceObjectId,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        ReadyAt = row.ReadyAt,
        ResolvedAt = row.ResolvedAt,
        RejectedAt = row.RejectedAt,
        StaleAt = row.StaleAt
    };

    private static Stage6CaseLink ToDomain(DbStage6CaseLink row) => new()
    {
        Id = row.Id,
        Stage6CaseId = row.Stage6CaseId,
        LinkedObjectType = row.LinkedObjectType,
        LinkedObjectId = row.LinkedObjectId,
        LinkRole = row.LinkRole,
        MetadataJson = row.MetadataJson,
        CreatedAt = row.CreatedAt
    };

    private static string NormalizePriority(string priority)
    {
        var normalized = (priority ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "blocking" or "important" or "optional" => normalized,
            _ => "important"
        };
    }

    private static string NormalizeCaseType(string caseType)
    {
        var normalized = (caseType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Stage6CaseTypes.NeedsInput or
            Stage6CaseTypes.NeedsReview or
            Stage6CaseTypes.Risk or
            Stage6CaseTypes.StateRefreshNeeded or
            Stage6CaseTypes.DossierCandidate or
            Stage6CaseTypes.DraftCandidate or
            Stage6CaseTypes.ClarificationMissingData or
            Stage6CaseTypes.ClarificationAmbiguity or
            Stage6CaseTypes.ClarificationEvidenceInterpretationConflict or
            Stage6CaseTypes.ClarificationNextStepBlocked or
            Stage6CaseTypes.UserContextCorrection or
            Stage6CaseTypes.UserContextConflictReview => normalized,
            _ => Stage6CaseTypes.NeedsReview
        };
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            Stage6CaseStatuses.New or
            Stage6CaseStatuses.Ready or
            Stage6CaseStatuses.NeedsUserInput or
            Stage6CaseStatuses.Resolved or
            Stage6CaseStatuses.Rejected or
            Stage6CaseStatuses.Stale => normalized,
            _ => Stage6CaseStatuses.New
        };
    }

    private static string NormalizeJson(string? json, string fallback)
    {
        return string.IsNullOrWhiteSpace(json) ? fallback : json;
    }

    private static void StampStatusTimestamps(DbStage6Case row, DateTime now)
    {
        switch (row.Status)
        {
            case Stage6CaseStatuses.Ready:
            case Stage6CaseStatuses.NeedsUserInput:
                row.ReadyAt ??= now;
                row.ResolvedAt = null;
                row.RejectedAt = null;
                row.StaleAt = null;
                break;
            case Stage6CaseStatuses.Resolved:
                row.ResolvedAt ??= now;
                row.RejectedAt = null;
                row.StaleAt = null;
                break;
            case Stage6CaseStatuses.Rejected:
                row.RejectedAt ??= now;
                row.ResolvedAt = null;
                row.StaleAt = null;
                break;
            case Stage6CaseStatuses.Stale:
                row.StaleAt ??= now;
                row.ResolvedAt = null;
                row.RejectedAt = null;
                break;
            default:
                row.ResolvedAt = null;
                row.RejectedAt = null;
                row.StaleAt = null;
                break;
        }
    }

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
}

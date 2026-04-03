using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class Stage6BootstrapRepository : IStage6BootstrapRepository
{
    public const string ActiveStatus = "active";

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage6BootstrapRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage6BootstrapScopeResolution> ResolveScopeAsync(Stage6BootstrapRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedScopeKey = NormalizeNullable(request.ScopeKey);
        if (request.PersonId == null && requestedScopeKey == null)
        {
            return BuildBlockedResolution(
                requestedScopeKey,
                "Stage 6 bootstrap requires person_id or scope_key.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        DbPerson? trackedPerson;
        if (request.PersonId != null)
        {
            trackedPerson = await db.Persons
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x => x.Id == request.PersonId.Value && x.Status == ActiveStatus,
                    ct);
            if (trackedPerson == null)
            {
                return BuildBlockedResolution(
                    requestedScopeKey,
                    $"Tracked person '{request.PersonId:D}' was not found.");
            }

            if (!string.Equals(trackedPerson.PersonType, "tracked_person", StringComparison.Ordinal))
            {
                return BuildBlockedResolution(
                    trackedPerson.ScopeKey,
                    $"Stage 6 bootstrap requires person_type 'tracked_person', got '{trackedPerson.PersonType}'.");
            }

            if (requestedScopeKey != null
                && !string.Equals(requestedScopeKey, trackedPerson.ScopeKey, StringComparison.Ordinal))
            {
                return BuildBlockedResolution(
                    trackedPerson.ScopeKey,
                    "person_id and scope_key point to different bootstrap scopes.");
            }
        }
        else
        {
            var trackedPeople = await db.Persons
                .AsNoTracking()
                .Where(x => x.ScopeKey == requestedScopeKey
                    && x.Status == ActiveStatus
                    && x.PersonType == "tracked_person")
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);
            if (trackedPeople.Count == 0)
            {
                return BuildNeedMoreDataResolution(
                    requestedScopeKey,
                    "No active tracked_person exists in the requested scope.",
                    "tracked_person_missing",
                    "No tracked person is attached to the requested scope yet.",
                    "seed_tracked_person");
            }

            if (trackedPeople.Count > 1)
            {
                return BuildClarificationResolution(
                    requestedScopeKey,
                    "Multiple tracked_person records exist in the requested scope.",
                    "tracked_person_ambiguous",
                    "Bootstrap scope contains more than one tracked person and needs explicit selection.",
                    "select_tracked_person");
            }

            trackedPerson = trackedPeople[0];
        }

        var scopeKey = trackedPerson.ScopeKey;
        var operatorLinkRows = await db.PersonOperatorLinks
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey
                && x.PersonId == trackedPerson.Id
                && x.Status == ActiveStatus)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        if (operatorLinkRows.Count == 0)
        {
            return BuildNeedMoreDataResolution(
                scopeKey,
                "Tracked person has no active operator attachment.",
                "operator_attachment_missing",
                "Tracked person is not attached to an operator root yet.",
                "attach_operator_root",
                trackedPerson);
        }

        var distinctOperatorIds = operatorLinkRows
            .Select(x => x.OperatorPersonId)
            .Distinct()
            .ToList();
        if (distinctOperatorIds.Count > 1)
        {
            return BuildClarificationResolution(
                scopeKey,
                "Tracked person has more than one active operator attachment.",
                "operator_attachment_ambiguous",
                "Tracked person is attached to multiple operator roots.",
                "select_operator_attachment",
                trackedPerson);
        }

        var operatorPerson = await db.Persons
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == distinctOperatorIds[0] && x.Status == ActiveStatus,
                ct);
        if (operatorPerson == null)
        {
            return BuildNeedMoreDataResolution(
                scopeKey,
                "Operator root referenced by person_operator_links was not found.",
                "operator_root_missing",
                "Tracked person points to an operator root that does not exist in persons.",
                "repair_operator_identity",
                trackedPerson);
        }

        var evidenceCount = await (
            from link in db.EvidenceItemPersonLinks.AsNoTracking()
            join evidence in db.EvidenceItems.AsNoTracking() on link.EvidenceItemId equals evidence.Id
            where link.ScopeKey == scopeKey
                && link.PersonId == trackedPerson.Id
                && evidence.Status == ActiveStatus
            select evidence.Id)
            .Distinct()
            .CountAsync(ct);
        if (evidenceCount == 0)
        {
            return BuildNeedMoreDataResolution(
                scopeKey,
                "Tracked person has no evidence-backed substrate coverage.",
                "tracked_person_evidence_missing",
                "Bootstrap graph initialization needs at least one evidence-backed substrate row for the tracked person.",
                "collect_evidence",
                trackedPerson,
                operatorPerson);
        }

        var sourceRows = await (
            from link in db.EvidenceItemPersonLinks.AsNoTracking()
            join evidence in db.EvidenceItems.AsNoTracking() on link.EvidenceItemId equals evidence.Id
            join source in db.SourceObjects.AsNoTracking() on evidence.SourceObjectId equals source.Id
            where link.ScopeKey == scopeKey
                && link.PersonId == trackedPerson.Id
                && evidence.Status == ActiveStatus
                && source.ScopeKey == scopeKey
            orderby evidence.ObservedAt descending, evidence.CreatedAt descending
            select new Stage6BootstrapSourceRef
            {
                SourceType = source.SourceKind,
                SourceRef = source.SourceRef,
                SourceObjectId = source.Id,
                EvidenceItemId = evidence.Id,
                ObservedAtUtc = evidence.ObservedAt
            })
            .Take(5)
            .ToListAsync(ct);

        return new Stage6BootstrapScopeResolution
        {
            ResolutionStatus = Stage6BootstrapStatuses.Ready,
            ScopeKey = scopeKey,
            TrackedPerson = MapPerson(trackedPerson),
            OperatorPerson = MapPerson(operatorPerson),
            EvidenceCount = evidenceCount,
            LatestEvidenceAtUtc = sourceRows.FirstOrDefault()?.ObservedAtUtc,
            SourceRefs = sourceRows,
            Reason = "Tracked person and operator root resolved successfully."
        };
    }

    public async Task<Stage6BootstrapGraphResult> UpsertGraphInitializationAsync(
        ModelPassAuditRecord auditRecord,
        Stage6BootstrapScopeResolution resolution,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditRecord);
        ArgumentNullException.ThrowIfNull(auditRecord.Envelope);
        ArgumentNullException.ThrowIfNull(resolution);

        if (!string.Equals(resolution.ResolutionStatus, Stage6BootstrapStatuses.Ready, StringComparison.Ordinal)
            || resolution.TrackedPerson == null
            || resolution.OperatorPerson == null)
        {
            throw new InvalidOperationException("Stage 6 bootstrap graph initialization requires a ready scope resolution.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var operatorNode = await UpsertNodeAsync(
            db,
            resolution.ScopeKey,
            resolution.OperatorPerson,
            Stage6BootstrapNodeTypes.OperatorRoot,
            auditRecord.ModelPassRunId,
            BuildOperatorNodePayload(resolution, auditRecord.ModelPassRunId),
            now,
            ct);
        var trackedNode = await UpsertNodeAsync(
            db,
            resolution.ScopeKey,
            resolution.TrackedPerson,
            Stage6BootstrapNodeTypes.TrackedPersonSeed,
            auditRecord.ModelPassRunId,
            BuildTrackedNodePayload(resolution, auditRecord.ModelPassRunId),
            now,
            ct);
        var edge = await UpsertEdgeAsync(
            db,
            resolution,
            auditRecord.ModelPassRunId,
            now,
            ct);

        await db.SaveChangesAsync(ct);

        return new Stage6BootstrapGraphResult
        {
            AuditRecord = auditRecord,
            GraphInitialized = true,
            Nodes =
            [
                MapNode(operatorNode),
                MapNode(trackedNode)
            ],
            Edges =
            [
                MapEdge(edge)
            ]
        };
    }

    private static async Task<DbBootstrapGraphNode> UpsertNodeAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Stage6BootstrapPersonRef person,
        string nodeType,
        Guid modelPassRunId,
        string payloadJson,
        DateTime now,
        CancellationToken ct)
    {
        var row = await db.BootstrapGraphNodes.FirstOrDefaultAsync(
            x => x.ScopeKey == scopeKey
                && x.NodeType == nodeType
                && x.NodeRef == person.PersonRef,
            ct);
        if (row == null)
        {
            row = new DbBootstrapGraphNode
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                NodeType = nodeType,
                NodeRef = person.PersonRef,
                CreatedAt = now
            };
            db.BootstrapGraphNodes.Add(row);
        }

        row.PersonId = person.PersonId;
        row.LastModelPassRunId = modelPassRunId;
        row.Status = ActiveStatus;
        row.PayloadJson = payloadJson;
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbBootstrapGraphEdge> UpsertEdgeAsync(
        TgAssistantDbContext db,
        Stage6BootstrapScopeResolution resolution,
        Guid modelPassRunId,
        DateTime now,
        CancellationToken ct)
    {
        var fromNodeRef = resolution.OperatorPerson!.PersonRef;
        var toNodeRef = resolution.TrackedPerson!.PersonRef;
        var row = await db.BootstrapGraphEdges.FirstOrDefaultAsync(
            x => x.ScopeKey == resolution.ScopeKey
                && x.FromNodeRef == fromNodeRef
                && x.ToNodeRef == toNodeRef
                && x.EdgeType == Stage6BootstrapEdgeTypes.TrackedPersonAttachment,
            ct);
        if (row == null)
        {
            row = new DbBootstrapGraphEdge
            {
                Id = Guid.NewGuid(),
                ScopeKey = resolution.ScopeKey,
                FromNodeRef = fromNodeRef,
                ToNodeRef = toNodeRef,
                EdgeType = Stage6BootstrapEdgeTypes.TrackedPersonAttachment,
                CreatedAt = now
            };
            db.BootstrapGraphEdges.Add(row);
        }

        row.LastModelPassRunId = modelPassRunId;
        row.Status = ActiveStatus;
        row.PayloadJson = BuildEdgePayload(resolution, modelPassRunId);
        row.UpdatedAt = now;
        return row;
    }

    private static Stage6BootstrapPersonRef MapPerson(DbPerson row)
    {
        return new Stage6BootstrapPersonRef
        {
            PersonId = row.Id,
            ScopeKey = row.ScopeKey,
            PersonType = row.PersonType,
            DisplayName = row.DisplayName,
            CanonicalName = row.CanonicalName
        };
    }

    private static Stage6BootstrapGraphNode MapNode(DbBootstrapGraphNode row)
    {
        return new Stage6BootstrapGraphNode
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonId = row.PersonId,
            LastModelPassRunId = row.LastModelPassRunId,
            NodeType = row.NodeType,
            NodeRef = row.NodeRef,
            Status = row.Status,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage6BootstrapGraphEdge MapEdge(DbBootstrapGraphEdge row)
    {
        return new Stage6BootstrapGraphEdge
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            LastModelPassRunId = row.LastModelPassRunId,
            FromNodeRef = row.FromNodeRef,
            ToNodeRef = row.ToNodeRef,
            EdgeType = row.EdgeType,
            Status = row.Status,
            PayloadJson = row.PayloadJson
        };
    }

    private static string BuildOperatorNodePayload(Stage6BootstrapScopeResolution resolution, Guid modelPassRunId)
        => JsonSerializer.Serialize(new
        {
            node_role = Stage6BootstrapNodeTypes.OperatorRoot,
            person_ref = resolution.OperatorPerson!.PersonRef,
            display_name = resolution.OperatorPerson.DisplayName,
            last_model_pass_run_id = modelPassRunId,
            tracked_person_ref = resolution.TrackedPerson!.PersonRef
        });

    private static string BuildTrackedNodePayload(Stage6BootstrapScopeResolution resolution, Guid modelPassRunId)
        => JsonSerializer.Serialize(new
        {
            node_role = Stage6BootstrapNodeTypes.TrackedPersonSeed,
            person_ref = resolution.TrackedPerson!.PersonRef,
            display_name = resolution.TrackedPerson.DisplayName,
            evidence_count = resolution.EvidenceCount,
            latest_evidence_at_utc = resolution.LatestEvidenceAtUtc?.ToUniversalTime().ToString("O"),
            source_refs = resolution.SourceRefs.Select(x => new { x.SourceType, x.SourceRef, x.SourceObjectId, x.EvidenceItemId }),
            last_model_pass_run_id = modelPassRunId
        });

    private static string BuildEdgePayload(Stage6BootstrapScopeResolution resolution, Guid modelPassRunId)
        => JsonSerializer.Serialize(new
        {
            edge_type = Stage6BootstrapEdgeTypes.TrackedPersonAttachment,
            operator_person_ref = resolution.OperatorPerson!.PersonRef,
            tracked_person_ref = resolution.TrackedPerson!.PersonRef,
            evidence_count = resolution.EvidenceCount,
            last_model_pass_run_id = modelPassRunId
        });

    private static Stage6BootstrapScopeResolution BuildBlockedResolution(string? scopeKey, string reason)
    {
        return new Stage6BootstrapScopeResolution
        {
            ResolutionStatus = Stage6BootstrapStatuses.BlockedInvalidInput,
            ScopeKey = scopeKey ?? "stage6_bootstrap:unresolved",
            Reason = reason
        };
    }

    private static Stage6BootstrapScopeResolution BuildNeedMoreDataResolution(
        string? scopeKey,
        string reason,
        string unknownType,
        string unknownSummary,
        string requiredAction,
        DbPerson? trackedPerson = null,
        DbPerson? operatorPerson = null)
    {
        return new Stage6BootstrapScopeResolution
        {
            ResolutionStatus = Stage6BootstrapStatuses.NeedMoreData,
            ScopeKey = scopeKey ?? trackedPerson?.ScopeKey ?? "stage6_bootstrap:unresolved",
            Reason = reason,
            TrackedPerson = trackedPerson == null ? null : MapPerson(trackedPerson),
            OperatorPerson = operatorPerson == null ? null : MapPerson(operatorPerson),
            Unknowns =
            [
                new ModelPassUnknown
                {
                    UnknownType = unknownType,
                    Summary = unknownSummary,
                    RequiredAction = requiredAction
                }
            ]
        };
    }

    private static Stage6BootstrapScopeResolution BuildClarificationResolution(
        string? scopeKey,
        string reason,
        string unknownType,
        string unknownSummary,
        string requiredAction,
        DbPerson? trackedPerson = null)
    {
        return new Stage6BootstrapScopeResolution
        {
            ResolutionStatus = Stage6BootstrapStatuses.NeedOperatorClarification,
            ScopeKey = scopeKey ?? trackedPerson?.ScopeKey ?? "stage6_bootstrap:unresolved",
            Reason = reason,
            TrackedPerson = trackedPerson == null ? null : MapPerson(trackedPerson),
            Unknowns =
            [
                new ModelPassUnknown
                {
                    UnknownType = unknownType,
                    Summary = unknownSummary,
                    RequiredAction = requiredAction
                }
            ]
        };
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

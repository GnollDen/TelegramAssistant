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
                SourceMessageId = source.SourceMessageId,
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

    public async Task<List<Stage6BootstrapDiscoveryOutput>> UpsertDiscoveryOutputsAsync(
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
            throw new InvalidOperationException("Stage 6 bootstrap discovery outputs require a ready scope resolution.");
        }

        var evidenceItemIds = resolution.SourceRefs
            .Where(x => x.EvidenceItemId != null)
            .Select(x => x.EvidenceItemId!.Value)
            .Distinct()
            .ToArray();
        var sourceMessageIds = resolution.SourceRefs
            .Where(x => x.SourceMessageId != null)
            .Select(x => x.SourceMessageId!.Value)
            .Distinct()
            .ToArray();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var outputs = new List<Stage6BootstrapDiscoveryOutput>();

        if (evidenceItemIds.Length > 0)
        {
            var linkedPersonRows = await (
                from link in db.EvidenceItemPersonLinks.AsNoTracking()
                join person in db.Persons.AsNoTracking() on link.PersonId equals person.Id
                where link.ScopeKey == resolution.ScopeKey
                    && evidenceItemIds.Contains(link.EvidenceItemId)
                    && person.Status == ActiveStatus
                    && person.Id != resolution.TrackedPerson.PersonId
                    && person.Id != resolution.OperatorPerson.PersonId
                select new
                {
                    person.Id,
                    person.ScopeKey,
                    person.PersonType,
                    person.DisplayName,
                    person.CanonicalName,
                    link.EvidenceItemId
                })
                .ToListAsync(ct);

            foreach (var personGroup in linkedPersonRows.GroupBy(x => x.Id).OrderBy(x => x.Key))
            {
                var person = personGroup.First();
                var personRef = new Stage6BootstrapPersonRef
                {
                    PersonId = person.Id,
                    ScopeKey = person.ScopeKey,
                    PersonType = person.PersonType,
                    DisplayName = person.DisplayName,
                    CanonicalName = person.CanonicalName
                };
                var payloadJson = JsonSerializer.Serialize(new
                {
                    discovery_type = Stage6BootstrapDiscoveryTypes.LinkedPerson,
                    tracked_person_ref = resolution.TrackedPerson.PersonRef,
                    person_ref = personRef.PersonRef,
                    display_name = personRef.DisplayName,
                    person_type = personRef.PersonType,
                    evidence_refs = personGroup
                        .Select(x => $"evidence:{x.EvidenceItemId:D}")
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                });

                var row = await UpsertDiscoveryOutputAsync(
                    db,
                    resolution.ScopeKey,
                    resolution.TrackedPerson.PersonId,
                    auditRecord.ModelPassRunId,
                    Stage6BootstrapDiscoveryTypes.LinkedPerson,
                    personRef.PersonRef,
                    payloadJson,
                    now,
                    personId: personRef.PersonId,
                    ct: ct);
                outputs.Add(MapDiscoveryOutput(row));
            }
        }

        if (sourceMessageIds.Length > 0)
        {
            var candidateRows = await db.CandidateIdentityStates
                .AsNoTracking()
                .Where(x => x.ScopeKey == resolution.ScopeKey
                    && x.SourceMessageId != null
                    && sourceMessageIds.Contains(x.SourceMessageId.Value))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);

            foreach (var row in candidateRows)
            {
                var discoveryType = ResolveDiscoveryType(row);
                if (discoveryType == null)
                {
                    continue;
                }

                var payloadJson = JsonSerializer.Serialize(new
                {
                    discovery_type = discoveryType,
                    tracked_person_ref = resolution.TrackedPerson.PersonRef,
                    candidate_identity_state_id = row.Id,
                    candidate_type = row.CandidateType,
                    display_label = row.DisplayLabel,
                    source_binding_type = row.SourceBindingType,
                    source_binding_value = row.SourceBindingValue,
                    source_binding_normalized = row.SourceBindingNormalized,
                    source_message_id = row.SourceMessageId,
                    matched_person_id = row.MatchedPersonId
                });

                var discoveryKey = $"{discoveryType}:{row.Id:D}";
                var outputRow = await UpsertDiscoveryOutputAsync(
                    db,
                    resolution.ScopeKey,
                    resolution.TrackedPerson.PersonId,
                    auditRecord.ModelPassRunId,
                    discoveryType,
                    discoveryKey,
                    payloadJson,
                    now,
                    candidateIdentityStateId: row.Id,
                    sourceMessageId: row.SourceMessageId,
                    ct: ct);
                outputs.Add(MapDiscoveryOutput(outputRow));
            }
        }

        await db.SaveChangesAsync(ct);
        return outputs
            .OrderBy(x => x.DiscoveryType, StringComparer.Ordinal)
            .ThenBy(x => x.DiscoveryKey, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Stage6BootstrapPoolOutputSet> UpsertPoolOutputsAsync(
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
            throw new InvalidOperationException("Stage 6 bootstrap pool outputs require a ready scope resolution.");
        }

        var sourceMessageIds = resolution.SourceRefs
            .Where(x => x.SourceMessageId != null)
            .Select(x => x.SourceMessageId!.Value)
            .Distinct()
            .ToArray();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var outputSet = new Stage6BootstrapPoolOutputSet();

        var candidateRows = sourceMessageIds.Length == 0
            ? []
            : await db.CandidateIdentityStates
                .AsNoTracking()
                .Where(x => x.ScopeKey == resolution.ScopeKey
                    && x.SourceMessageId != null
                    && sourceMessageIds.Contains(x.SourceMessageId.Value))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);

        var anchorRows = sourceMessageIds.Length == 0
            ? []
            : await db.RelationshipEdgeAnchors
                .AsNoTracking()
                .Where(x => x.ScopeKey == resolution.ScopeKey
                    && x.SourceMessageId != null
                    && sourceMessageIds.Contains(x.SourceMessageId.Value)
                    && x.Status == ActiveStatus
                    && (x.FromPersonId == resolution.TrackedPerson.PersonId || x.ToPersonId == resolution.TrackedPerson.PersonId))
                .OrderBy(x => x.CreatedAt)
                .ToListAsync(ct);

        foreach (var sliceGroup in resolution.SourceRefs
                     .GroupBy(BuildSliceOutputKey)
                     .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var sourceMessageId = sliceGroup.Select(x => x.SourceMessageId).Distinct().Count() == 1
                ? sliceGroup.First().SourceMessageId
                : null;
            var payloadJson = JsonSerializer.Serialize(new
            {
                output_type = Stage6BootstrapPoolOutputTypes.BootstrapSlice,
                tracked_person_ref = resolution.TrackedPerson.PersonRef,
                source_count = sliceGroup.Count(),
                start_observed_at_utc = sliceGroup.Min(x => x.ObservedAtUtc)?.ToUniversalTime().ToString("O"),
                end_observed_at_utc = sliceGroup.Max(x => x.ObservedAtUtc)?.ToUniversalTime().ToString("O"),
                source_refs = sliceGroup.Select(x => new
                {
                    x.SourceType,
                    x.SourceRef,
                    x.SourceObjectId,
                    x.EvidenceItemId,
                    x.SourceMessageId,
                    observed_at_utc = x.ObservedAtUtc?.ToUniversalTime().ToString("O")
                }).ToArray()
            });
            var row = await UpsertPoolOutputAsync(
                db,
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapPoolOutputTypes.BootstrapSlice,
                sliceGroup.Key,
                payloadJson,
                now,
                sourceMessageId: sourceMessageId,
                ct: ct);
            outputSet.SliceOutputs.Add(MapPoolOutput(row));
        }

        foreach (var candidateGroup in candidateRows
                     .Where(x => x.SourceMessageId != null)
                     .GroupBy(x => x.SourceMessageId!.Value)
                     .OrderBy(x => x.Key))
        {
            if (candidateGroup.Count() <= 1)
            {
                continue;
            }

            var payloadJson = JsonSerializer.Serialize(new
            {
                output_type = Stage6BootstrapPoolOutputTypes.AmbiguityPool,
                tracked_person_ref = resolution.TrackedPerson.PersonRef,
                source_message_id = candidateGroup.Key,
                candidate_count = candidateGroup.Count(),
                candidates = candidateGroup.Select(x => new
                {
                    candidate_identity_state_id = x.Id,
                    candidate_type = x.CandidateType,
                    status = x.Status,
                    display_label = x.DisplayLabel,
                    source_binding_normalized = x.SourceBindingNormalized,
                    matched_person_id = x.MatchedPersonId
                }).ToArray()
            });
            var row = await UpsertPoolOutputAsync(
                db,
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapPoolOutputTypes.AmbiguityPool,
                $"source_message:{candidateGroup.Key}",
                payloadJson,
                now,
                sourceMessageId: candidateGroup.Key,
                ct: ct);
            outputSet.AmbiguityOutputs.Add(MapPoolOutput(row));

            var matchedPersonIds = candidateGroup
                .Where(x => x.MatchedPersonId != null)
                .Select(x => x.MatchedPersonId!.Value)
                .Distinct()
                .ToArray();
            if (matchedPersonIds.Length > 1)
            {
                var contradictionRow = await UpsertPoolOutputAsync(
                    db,
                    resolution.ScopeKey,
                    resolution.TrackedPerson.PersonId,
                    auditRecord.ModelPassRunId,
                    Stage6BootstrapPoolOutputTypes.ContradictionPool,
                    $"source_message:{candidateGroup.Key}:candidate_match_conflict",
                    JsonSerializer.Serialize(new
                    {
                        output_type = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                        tracked_person_ref = resolution.TrackedPerson.PersonRef,
                        source_message_id = candidateGroup.Key,
                        contradiction_type = "candidate_identity_match_conflict",
                        matched_person_ids = matchedPersonIds,
                        candidate_identity_state_ids = candidateGroup.Select(x => x.Id).ToArray()
                    }),
                    now,
                    sourceMessageId: candidateGroup.Key,
                    ct: ct);
                outputSet.ContradictionOutputs.Add(MapPoolOutput(contradictionRow));
            }
        }

        foreach (var anchorGroup in anchorRows
                     .Where(x => x.SourceMessageId != null)
                     .GroupBy(x => new { SourceMessageId = x.SourceMessageId!.Value, x.AnchorType })
                     .OrderBy(x => x.Key.SourceMessageId)
                     .ThenBy(x => x.Key.AnchorType, StringComparer.Ordinal))
        {
            var counterpartyIds = anchorGroup
                .Select(x => ResolveCounterpartyPersonId(x, resolution.TrackedPerson.PersonId))
                .Where(x => x != null)
                .Select(x => x!.Value)
                .Distinct()
                .ToArray();
            if (counterpartyIds.Length <= 1)
            {
                continue;
            }

            var row = await UpsertPoolOutputAsync(
                db,
                resolution.ScopeKey,
                resolution.TrackedPerson.PersonId,
                auditRecord.ModelPassRunId,
                Stage6BootstrapPoolOutputTypes.ContradictionPool,
                $"source_message:{anchorGroup.Key.SourceMessageId}:anchor_type:{anchorGroup.Key.AnchorType}",
                JsonSerializer.Serialize(new
                {
                    output_type = Stage6BootstrapPoolOutputTypes.ContradictionPool,
                    tracked_person_ref = resolution.TrackedPerson.PersonRef,
                    source_message_id = anchorGroup.Key.SourceMessageId,
                    contradiction_type = "relationship_anchor_conflict",
                    anchor_type = anchorGroup.Key.AnchorType,
                    counterparty_person_ids = counterpartyIds,
                    relationship_edge_anchor_ids = anchorGroup.Select(x => x.Id).ToArray()
                }),
                now,
                sourceMessageId: anchorGroup.Key.SourceMessageId,
                relationshipEdgeAnchorId: anchorGroup.First().Id,
                ct: ct);
            outputSet.ContradictionOutputs.Add(MapPoolOutput(row));
        }

        await db.SaveChangesAsync(ct);

        outputSet.AmbiguityOutputs = outputSet.AmbiguityOutputs
            .OrderBy(x => x.OutputKey, StringComparer.Ordinal)
            .ToList();
        outputSet.ContradictionOutputs = outputSet.ContradictionOutputs
            .OrderBy(x => x.OutputKey, StringComparer.Ordinal)
            .ToList();
        outputSet.SliceOutputs = outputSet.SliceOutputs
            .OrderBy(x => x.OutputKey, StringComparer.Ordinal)
            .ToList();
        return outputSet;
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
            ScopeKey = resolution.ScopeKey,
            TrackedPerson = resolution.TrackedPerson,
            OperatorPerson = resolution.OperatorPerson,
            EvidenceCount = resolution.EvidenceCount,
            LatestEvidenceAtUtc = resolution.LatestEvidenceAtUtc,
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

    private static async Task<DbBootstrapDiscoveryOutput> UpsertDiscoveryOutputAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid trackedPersonId,
        Guid modelPassRunId,
        string discoveryType,
        string discoveryKey,
        string payloadJson,
        DateTime now,
        Guid? personId = null,
        Guid? candidateIdentityStateId = null,
        long? sourceMessageId = null,
        CancellationToken ct = default)
    {
        var row = await db.BootstrapDiscoveryOutputs.FirstOrDefaultAsync(
            x => x.ScopeKey == scopeKey
                && x.TrackedPersonId == trackedPersonId
                && x.DiscoveryType == discoveryType
                && x.DiscoveryKey == discoveryKey,
            ct);
        if (row == null)
        {
            row = new DbBootstrapDiscoveryOutput
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                DiscoveryType = discoveryType,
                DiscoveryKey = discoveryKey,
                CreatedAt = now
            };
            db.BootstrapDiscoveryOutputs.Add(row);
        }

        row.LastModelPassRunId = modelPassRunId;
        row.PersonId = personId;
        row.CandidateIdentityStateId = candidateIdentityStateId;
        row.SourceMessageId = sourceMessageId;
        row.Status = ActiveStatus;
        row.PayloadJson = payloadJson;
        row.UpdatedAt = now;
        return row;
    }

    private static async Task<DbBootstrapPoolOutput> UpsertPoolOutputAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid trackedPersonId,
        Guid modelPassRunId,
        string outputType,
        string outputKey,
        string payloadJson,
        DateTime now,
        Guid? candidateIdentityStateId = null,
        Guid? relationshipEdgeAnchorId = null,
        long? sourceMessageId = null,
        CancellationToken ct = default)
    {
        var row = await db.BootstrapPoolOutputs.FirstOrDefaultAsync(
            x => x.ScopeKey == scopeKey
                && x.TrackedPersonId == trackedPersonId
                && x.OutputType == outputType
                && x.OutputKey == outputKey,
            ct);
        if (row == null)
        {
            row = new DbBootstrapPoolOutput
            {
                Id = Guid.NewGuid(),
                ScopeKey = scopeKey,
                TrackedPersonId = trackedPersonId,
                OutputType = outputType,
                OutputKey = outputKey,
                CreatedAt = now
            };
            db.BootstrapPoolOutputs.Add(row);
        }

        row.LastModelPassRunId = modelPassRunId;
        row.CandidateIdentityStateId = candidateIdentityStateId;
        row.RelationshipEdgeAnchorId = relationshipEdgeAnchorId;
        row.SourceMessageId = sourceMessageId;
        row.Status = ActiveStatus;
        row.PayloadJson = payloadJson;
        row.UpdatedAt = now;
        return row;
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

    private static Stage6BootstrapDiscoveryOutput MapDiscoveryOutput(DbBootstrapDiscoveryOutput row)
    {
        return new Stage6BootstrapDiscoveryOutput
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            TrackedPersonId = row.TrackedPersonId,
            LastModelPassRunId = row.LastModelPassRunId,
            DiscoveryType = row.DiscoveryType,
            DiscoveryKey = row.DiscoveryKey,
            PersonId = row.PersonId,
            CandidateIdentityStateId = row.CandidateIdentityStateId,
            SourceMessageId = row.SourceMessageId,
            Status = row.Status,
            PayloadJson = row.PayloadJson
        };
    }

    private static Stage6BootstrapPoolOutput MapPoolOutput(DbBootstrapPoolOutput row)
    {
        return new Stage6BootstrapPoolOutput
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            TrackedPersonId = row.TrackedPersonId,
            LastModelPassRunId = row.LastModelPassRunId,
            OutputType = row.OutputType,
            OutputKey = row.OutputKey,
            CandidateIdentityStateId = row.CandidateIdentityStateId,
            RelationshipEdgeAnchorId = row.RelationshipEdgeAnchorId,
            SourceMessageId = row.SourceMessageId,
            Status = row.Status,
            PayloadJson = row.PayloadJson
        };
    }

    private static string? ResolveDiscoveryType(DbCandidateIdentityState row)
    {
        if (string.Equals(row.CandidateType, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.Status, Stage6BootstrapDiscoveryTypes.Mention, StringComparison.OrdinalIgnoreCase))
        {
            return Stage6BootstrapDiscoveryTypes.Mention;
        }

        return row.MatchedPersonId == null
            ? Stage6BootstrapDiscoveryTypes.CandidateIdentity
            : null;
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

    private static string BuildSliceOutputKey(Stage6BootstrapSourceRef sourceRef)
    {
        if (sourceRef.ObservedAtUtc != null)
        {
            return $"day:{sourceRef.ObservedAtUtc.Value.ToUniversalTime():yyyyMMdd}";
        }

        return sourceRef.SourceMessageId != null
            ? $"source_message:{sourceRef.SourceMessageId.Value}"
            : $"source_ref:{sourceRef.SourceRef}";
    }

    private static Guid? ResolveCounterpartyPersonId(DbRelationshipEdgeAnchor row, Guid trackedPersonId)
    {
        if (row.FromPersonId == trackedPersonId)
        {
            return row.ToPersonId;
        }

        return row.ToPersonId == trackedPersonId
            ? row.FromPersonId
            : null;
    }

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

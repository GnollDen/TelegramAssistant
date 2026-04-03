using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public class IdentityMergeRepository : IIdentityMergeRepository
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;
    private readonly ILogger<IdentityMergeRepository> _logger;

    public IdentityMergeRepository(
        IDbContextFactory<TgAssistantDbContext> dbFactory,
        ILogger<IdentityMergeRepository> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IdentityMergeRecord> ExecuteMergeAsync(
        IdentityMergeApplyRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var confidenceTier = NormalizeConfidenceTier(request.ConfidenceTier);
        var requestedBy = NormalizeActor(request.RequestedBy, fallback: "system");
        var reviewApproved = request.ReviewApproved;
        var reviewedBy = NormalizeNullable(request.ReviewedBy);
        if (reviewApproved && reviewedBy == null)
        {
            throw new InvalidOperationException("ReviewedBy is required when a review-gated identity merge is approved.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var persons = await db.Persons
            .Where(x => x.Id == request.TargetPersonId || x.Id == request.SourcePersonId)
            .ToListAsync(ct);
        var target = persons.FirstOrDefault(x => x.Id == request.TargetPersonId);
        var source = persons.FirstOrDefault(x => x.Id == request.SourcePersonId);
        if (target == null || source == null)
        {
            throw new InvalidOperationException("Identity merge requires both source and target persons to exist.");
        }

        if (!string.Equals(target.ScopeKey, source.ScopeKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Identity merge requires source and target persons to share the same scope.");
        }

        if (HasConflictingExplicitIdentity(target, source))
        {
            throw new InvalidOperationException("Identity merge blocked because source and target carry conflicting explicit actor or Telegram identities.");
        }

        var scopeKey = target.ScopeKey;
        var mergeId = Guid.NewGuid();
        var beforeSnapshot = await CaptureSnapshotAsync(db, scopeKey, target.Id, source.Id, ct);
        var afterSnapshot = BuildMergedSnapshot(beforeSnapshot, target.Id, source.Id, mergeId);
        var reviewRequired = RequiresReview(target, source, confidenceTier);
        var status = reviewRequired && !reviewApproved
            ? IdentityMergeStatuses.PendingReview
            : IdentityMergeStatuses.Applied;
        var reviewStatus = reviewRequired
            ? reviewApproved ? IdentityMergeReviewStatuses.Approved : IdentityMergeReviewStatuses.Pending
            : IdentityMergeReviewStatuses.NotRequired;
        var nowUtc = DateTime.UtcNow;

        var row = new DbIdentityMergeHistory
        {
            Id = mergeId,
            ScopeKey = scopeKey,
            TargetPersonId = target.Id,
            SourcePersonId = source.Id,
            ConfidenceTier = confidenceTier,
            Status = status,
            ReviewStatus = reviewStatus,
            Reason = NormalizeReason(request.Reason),
            RequestedBy = requestedBy,
            ReviewedBy = reviewedBy,
            ReviewNote = NormalizeNullable(request.ReviewNote),
            ModelPassRunId = request.ModelPassRunId,
            BeforeStateJson = SerializeSnapshot(beforeSnapshot),
            AfterStateJson = SerializeSnapshot(afterSnapshot),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            AppliedAtUtc = string.Equals(status, IdentityMergeStatuses.Applied, StringComparison.Ordinal) ? nowUtc : null
        };

        db.IdentityMergeHistories.Add(row);

        if (string.Equals(status, IdentityMergeStatuses.Applied, StringComparison.Ordinal))
        {
            await ApplySnapshotAsync(db, beforeSnapshot.ScopeKey, target.Id, source.Id, afterSnapshot, ct);
            _logger.LogInformation(
                "Identity merge applied: merge_id={MergeId}, scope_key={ScopeKey}, target_person_id={TargetPersonId}, source_person_id={SourcePersonId}, confidence_tier={ConfidenceTier}",
                row.Id,
                row.ScopeKey,
                row.TargetPersonId,
                row.SourcePersonId,
                row.ConfidenceTier);
        }
        else
        {
            _logger.LogInformation(
                "Identity merge review-gated: merge_id={MergeId}, scope_key={ScopeKey}, target_person_id={TargetPersonId}, source_person_id={SourcePersonId}, confidence_tier={ConfidenceTier}",
                row.Id,
                row.ScopeKey,
                row.TargetPersonId,
                row.SourcePersonId,
                row.ConfidenceTier);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Map(row);
    }

    public async Task<IdentityMergeRecord?> GetByIdAsync(
        Guid mergeId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.IdentityMergeHistories
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == mergeId, ct);
        return row == null ? null : Map(row);
    }

    public async Task<IdentityMergeRecord> ReverseAsync(
        IdentityMergeReverseRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var row = await db.IdentityMergeHistories
            .FirstOrDefaultAsync(x => x.Id == request.MergeId, ct);
        if (row == null)
        {
            throw new InvalidOperationException($"Identity merge '{request.MergeId:D}' was not found.");
        }

        if (!string.Equals(row.Status, IdentityMergeStatuses.Applied, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Identity merge '{request.MergeId:D}' is not in an applied state and cannot be reversed.");
        }

        var beforeSnapshot = DeserializeSnapshot(row.BeforeStateJson);
        await ApplySnapshotAsync(db, row.ScopeKey, row.TargetPersonId, row.SourcePersonId, beforeSnapshot, ct);

        var nowUtc = DateTime.UtcNow;
        row.Status = IdentityMergeStatuses.Reversed;
        row.ReversedAtUtc = nowUtc;
        row.ReversedBy = NormalizeActor(request.RequestedBy, fallback: "system");
        row.ReversalReason = NormalizeReason(request.Reason);
        row.UpdatedAtUtc = nowUtc;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _logger.LogInformation(
            "Identity merge reversed: merge_id={MergeId}, scope_key={ScopeKey}, target_person_id={TargetPersonId}, source_person_id={SourcePersonId}",
            row.Id,
            row.ScopeKey,
            row.TargetPersonId,
            row.SourcePersonId);

        return Map(row);
    }

    private static async Task<IdentityMergeSnapshot> CaptureSnapshotAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid targetPersonId,
        Guid sourcePersonId,
        CancellationToken ct)
    {
        var personIds = new[] { targetPersonId, sourcePersonId };

        var persons = await db.Persons
            .AsNoTracking()
            .Where(x => personIds.Contains(x.Id))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var operatorLinks = await db.PersonOperatorLinks
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey
                && (personIds.Contains(x.PersonId) || personIds.Contains(x.OperatorPersonId)))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        var bindings = await db.PersonIdentityBindings
            .AsNoTracking()
            .Where(x => personIds.Contains(x.PersonId))
            .OrderBy(x => x.Id)
            .ToListAsync(ct);
        var candidateStates = await db.CandidateIdentityStates
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey
                && x.MatchedPersonId != null
                && personIds.Contains(x.MatchedPersonId.Value))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
        var anchors = await db.RelationshipEdgeAnchors
            .AsNoTracking()
            .Where(x => x.ScopeKey == scopeKey
                && (personIds.Contains(x.FromPersonId) || personIds.Contains(x.ToPersonId)))
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

        return new IdentityMergeSnapshot
        {
            ScopeKey = scopeKey,
            Persons = persons.Select(MapPersonState).ToList(),
            PersonOperatorLinks = operatorLinks.Select(MapOperatorLinkState).ToList(),
            IdentityBindings = bindings.Select(MapBindingState).ToList(),
            CandidateStates = candidateStates.Select(MapCandidateState).ToList(),
            RelationshipAnchors = anchors.Select(MapRelationshipAnchorState).ToList()
        };
    }

    private static IdentityMergeSnapshot BuildMergedSnapshot(
        IdentityMergeSnapshot beforeSnapshot,
        Guid targetPersonId,
        Guid sourcePersonId,
        Guid mergeId)
    {
        var nowUtc = DateTime.UtcNow;
        var target = beforeSnapshot.Persons.First(x => x.Id == targetPersonId);
        var source = beforeSnapshot.Persons.First(x => x.Id == sourcePersonId);

        var persons = beforeSnapshot.Persons
            .Select(Clone)
            .OrderBy(x => x.CreatedAt)
            .ToList();
        var mergedTarget = persons.First(x => x.Id == targetPersonId);
        var mergedSource = persons.First(x => x.Id == sourcePersonId);
        mergedTarget.DisplayName = PreferTarget(target.DisplayName, source.DisplayName);
        mergedTarget.CanonicalName = PreferTarget(target.CanonicalName, source.CanonicalName);
        mergedTarget.PrimaryActorKey ??= source.PrimaryActorKey;
        mergedTarget.PrimaryTelegramUserId ??= source.PrimaryTelegramUserId;
        mergedTarget.PrimaryTelegramUsername ??= source.PrimaryTelegramUsername;
        mergedTarget.MetadataJson = MergeTargetMetadata(target.MetadataJson, source.MetadataJson, mergeId, sourcePersonId);
        mergedTarget.UpdatedAt = nowUtc;

        mergedSource.Status = "merged";
        mergedSource.MetadataJson = MergeSourceMetadata(source.MetadataJson, targetPersonId, mergeId, source.Status);
        mergedSource.UpdatedAt = nowUtc;

        var operatorLinks = beforeSnapshot.PersonOperatorLinks
            .Select(Clone)
            .Select(x =>
            {
                if (x.OperatorPersonId == sourcePersonId)
                {
                    x.OperatorPersonId = targetPersonId;
                    x.UpdatedAt = nowUtc;
                }

                if (x.PersonId == sourcePersonId)
                {
                    x.PersonId = targetPersonId;
                    x.UpdatedAt = nowUtc;
                }

                return x;
            })
            .Where(x => x.OperatorPersonId != x.PersonId)
            .GroupBy(x => new { x.ScopeKey, x.OperatorPersonId, x.PersonId, x.LinkType })
            .Select(group =>
            {
                var first = group.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).First();
                first.Status = group.Select(x => x.Status).FirstOrDefault(x => string.Equals(x, "active", StringComparison.Ordinal))
                    ?? first.Status;
                first.SourceMessageId ??= group.Select(x => x.SourceMessageId).FirstOrDefault(x => x != null);
                first.UpdatedAt = nowUtc;
                return first;
            })
            .OrderBy(x => x.Id)
            .ToList();

        var bindings = beforeSnapshot.IdentityBindings
            .Select(Clone)
            .Select(x =>
            {
                if (x.PersonId == sourcePersonId)
                {
                    x.PersonId = targetPersonId;
                    x.UpdatedAt = nowUtc;
                }

                return x;
            })
            .GroupBy(x => new { x.PersonId, x.BindingType, x.BindingNormalized })
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(x => x.IsPrimary)
                    .ThenByDescending(x => x.Confidence)
                    .ThenBy(x => x.CreatedAt)
                    .ThenBy(x => x.Id)
                    .First();
                best.IsPrimary = group.Any(x => x.IsPrimary);
                best.Confidence = group.Max(x => x.Confidence);
                best.UpdatedAt = nowUtc;
                return best;
            })
            .OrderBy(x => x.Id)
            .ToList();

        var candidateStates = beforeSnapshot.CandidateStates
            .Select(Clone)
            .Select(x =>
            {
                if (x.MatchedPersonId == sourcePersonId)
                {
                    x.MatchedPersonId = targetPersonId;
                    x.UpdatedAt = nowUtc;
                }

                return x;
            })
            .OrderBy(x => x.CreatedAt)
            .ToList();

        var anchors = beforeSnapshot.RelationshipAnchors
            .Select(Clone)
            .Select(x =>
            {
                if (x.FromPersonId == sourcePersonId)
                {
                    x.FromPersonId = targetPersonId;
                    x.UpdatedAt = nowUtc;
                }

                if (x.ToPersonId == sourcePersonId)
                {
                    x.ToPersonId = targetPersonId;
                    x.UpdatedAt = nowUtc;
                }

                return x;
            })
            .Where(x => x.FromPersonId != x.ToPersonId)
            .OrderBy(x => x.CreatedAt)
            .ToList();

        return new IdentityMergeSnapshot
        {
            ScopeKey = beforeSnapshot.ScopeKey,
            Persons = persons,
            PersonOperatorLinks = operatorLinks,
            IdentityBindings = bindings,
            CandidateStates = candidateStates,
            RelationshipAnchors = anchors
        };
    }

    private static async Task ApplySnapshotAsync(
        TgAssistantDbContext db,
        string scopeKey,
        Guid targetPersonId,
        Guid sourcePersonId,
        IdentityMergeSnapshot snapshot,
        CancellationToken ct)
    {
        var personIds = new[] { targetPersonId, sourcePersonId };
        var existingPersons = await db.Persons
            .Where(x => personIds.Contains(x.Id))
            .ToListAsync(ct);
        foreach (var personState in snapshot.Persons)
        {
            var row = existingPersons.FirstOrDefault(x => x.Id == personState.Id);
            if (row == null)
            {
                db.Persons.Add(MapPersonRow(personState));
                continue;
            }

            row.ScopeKey = personState.ScopeKey;
            row.PersonType = personState.PersonType;
            row.DisplayName = personState.DisplayName;
            row.CanonicalName = personState.CanonicalName;
            row.Status = personState.Status;
            row.PrimaryActorKey = personState.PrimaryActorKey;
            row.PrimaryTelegramUserId = personState.PrimaryTelegramUserId;
            row.PrimaryTelegramUsername = personState.PrimaryTelegramUsername;
            row.MetadataJson = personState.MetadataJson;
            row.CreatedAt = personState.CreatedAt;
            row.UpdatedAt = personState.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);

        var existingAnchors = await db.RelationshipEdgeAnchors
            .Where(x => x.ScopeKey == scopeKey
                && (personIds.Contains(x.FromPersonId) || personIds.Contains(x.ToPersonId)))
            .ToListAsync(ct);
        db.RelationshipEdgeAnchors.RemoveRange(existingAnchors);
        await db.SaveChangesAsync(ct);

        var existingCandidateStates = await db.CandidateIdentityStates
            .Where(x => x.ScopeKey == scopeKey
                && x.MatchedPersonId != null
                && personIds.Contains(x.MatchedPersonId.Value))
            .ToListAsync(ct);
        db.CandidateIdentityStates.RemoveRange(existingCandidateStates);

        var existingBindings = await db.PersonIdentityBindings
            .Where(x => personIds.Contains(x.PersonId))
            .ToListAsync(ct);
        db.PersonIdentityBindings.RemoveRange(existingBindings);

        var existingOperatorLinks = await db.PersonOperatorLinks
            .Where(x => x.ScopeKey == scopeKey
                && (personIds.Contains(x.PersonId) || personIds.Contains(x.OperatorPersonId)))
            .ToListAsync(ct);
        db.PersonOperatorLinks.RemoveRange(existingOperatorLinks);
        await db.SaveChangesAsync(ct);

        db.PersonOperatorLinks.AddRange(snapshot.PersonOperatorLinks.Select(MapOperatorLinkRow));
        db.PersonIdentityBindings.AddRange(snapshot.IdentityBindings.Select(MapBindingRow));
        db.CandidateIdentityStates.AddRange(snapshot.CandidateStates.Select(MapCandidateStateRow));
        await db.SaveChangesAsync(ct);

        db.RelationshipEdgeAnchors.AddRange(snapshot.RelationshipAnchors.Select(MapRelationshipAnchorRow));
        await db.SaveChangesAsync(ct);
    }

    private static string SerializeSnapshot(IdentityMergeSnapshot snapshot)
        => JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);

    private static IdentityMergeSnapshot DeserializeSnapshot(string json)
    {
        var snapshot = JsonSerializer.Deserialize<IdentityMergeSnapshot>(json, SnapshotJsonOptions);
        if (snapshot == null)
        {
            throw new InvalidOperationException("Identity merge snapshot payload could not be deserialized.");
        }

        return snapshot;
    }

    private static IdentityMergeRecord Map(DbIdentityMergeHistory row)
    {
        return new IdentityMergeRecord
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            TargetPersonId = row.TargetPersonId,
            SourcePersonId = row.SourcePersonId,
            ConfidenceTier = row.ConfidenceTier,
            Status = row.Status,
            ReviewStatus = row.ReviewStatus,
            Reason = row.Reason,
            RequestedBy = row.RequestedBy,
            ReviewedBy = row.ReviewedBy,
            ReviewNote = row.ReviewNote,
            ReversedBy = row.ReversedBy,
            ReversalReason = row.ReversalReason,
            ModelPassRunId = row.ModelPassRunId,
            BeforeStateJson = row.BeforeStateJson,
            AfterStateJson = row.AfterStateJson,
            CreatedAtUtc = row.CreatedAtUtc,
            UpdatedAtUtc = row.UpdatedAtUtc,
            AppliedAtUtc = row.AppliedAtUtc,
            ReversedAtUtc = row.ReversedAtUtc
        };
    }

    private static IdentityMergePersonState MapPersonState(DbPerson row)
    {
        return new IdentityMergePersonState
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            PersonType = row.PersonType,
            DisplayName = row.DisplayName,
            CanonicalName = row.CanonicalName,
            Status = row.Status,
            PrimaryActorKey = row.PrimaryActorKey,
            PrimaryTelegramUserId = row.PrimaryTelegramUserId,
            PrimaryTelegramUsername = row.PrimaryTelegramUsername,
            MetadataJson = row.MetadataJson,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static IdentityMergePersonOperatorLinkState MapOperatorLinkState(DbPersonOperatorLink row)
    {
        return new IdentityMergePersonOperatorLinkState
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            OperatorPersonId = row.OperatorPersonId,
            PersonId = row.PersonId,
            LinkType = row.LinkType,
            Status = row.Status,
            SourceBindingType = row.SourceBindingType,
            SourceBindingValue = row.SourceBindingValue,
            SourceBindingNormalized = row.SourceBindingNormalized,
            SourceMessageId = row.SourceMessageId,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static IdentityMergeBindingState MapBindingState(DbPersonIdentityBinding row)
    {
        return new IdentityMergeBindingState
        {
            Id = row.Id,
            PersonId = row.PersonId,
            ScopeKey = row.ScopeKey,
            BindingType = row.BindingType,
            BindingValue = row.BindingValue,
            BindingNormalized = row.BindingNormalized,
            SourceMessageId = row.SourceMessageId,
            Confidence = row.Confidence,
            IsPrimary = row.IsPrimary,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static IdentityMergeCandidateState MapCandidateState(DbCandidateIdentityState row)
    {
        return new IdentityMergeCandidateState
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            CandidateType = row.CandidateType,
            Status = row.Status,
            DisplayLabel = row.DisplayLabel,
            SourceBindingType = row.SourceBindingType,
            SourceBindingValue = row.SourceBindingValue,
            SourceBindingNormalized = row.SourceBindingNormalized,
            SourceMessageId = row.SourceMessageId,
            MatchedPersonId = row.MatchedPersonId,
            MetadataJson = row.MetadataJson,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static IdentityMergeRelationshipAnchorState MapRelationshipAnchorState(DbRelationshipEdgeAnchor row)
    {
        return new IdentityMergeRelationshipAnchorState
        {
            Id = row.Id,
            ScopeKey = row.ScopeKey,
            FromPersonId = row.FromPersonId,
            ToPersonId = row.ToPersonId,
            AnchorType = row.AnchorType,
            Status = row.Status,
            SourceBindingType = row.SourceBindingType,
            SourceBindingValue = row.SourceBindingValue,
            SourceBindingNormalized = row.SourceBindingNormalized,
            SourceMessageId = row.SourceMessageId,
            CandidateIdentityStateId = row.CandidateIdentityStateId,
            MetadataJson = row.MetadataJson,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
    }

    private static DbPerson MapPersonRow(IdentityMergePersonState state)
    {
        return new DbPerson
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            PersonType = state.PersonType,
            DisplayName = state.DisplayName,
            CanonicalName = state.CanonicalName,
            Status = state.Status,
            PrimaryActorKey = state.PrimaryActorKey,
            PrimaryTelegramUserId = state.PrimaryTelegramUserId,
            PrimaryTelegramUsername = state.PrimaryTelegramUsername,
            MetadataJson = state.MetadataJson,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static DbPersonOperatorLink MapOperatorLinkRow(IdentityMergePersonOperatorLinkState state)
    {
        return new DbPersonOperatorLink
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            OperatorPersonId = state.OperatorPersonId,
            PersonId = state.PersonId,
            LinkType = state.LinkType,
            Status = state.Status,
            SourceBindingType = state.SourceBindingType,
            SourceBindingValue = state.SourceBindingValue,
            SourceBindingNormalized = state.SourceBindingNormalized,
            SourceMessageId = state.SourceMessageId,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static DbPersonIdentityBinding MapBindingRow(IdentityMergeBindingState state)
    {
        return new DbPersonIdentityBinding
        {
            Id = state.Id,
            PersonId = state.PersonId,
            ScopeKey = state.ScopeKey,
            BindingType = state.BindingType,
            BindingValue = state.BindingValue,
            BindingNormalized = state.BindingNormalized,
            SourceMessageId = state.SourceMessageId,
            Confidence = state.Confidence,
            IsPrimary = state.IsPrimary,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static DbCandidateIdentityState MapCandidateStateRow(IdentityMergeCandidateState state)
    {
        return new DbCandidateIdentityState
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            CandidateType = state.CandidateType,
            Status = state.Status,
            DisplayLabel = state.DisplayLabel,
            SourceBindingType = state.SourceBindingType,
            SourceBindingValue = state.SourceBindingValue,
            SourceBindingNormalized = state.SourceBindingNormalized,
            SourceMessageId = state.SourceMessageId,
            MatchedPersonId = state.MatchedPersonId,
            MetadataJson = state.MetadataJson,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static DbRelationshipEdgeAnchor MapRelationshipAnchorRow(IdentityMergeRelationshipAnchorState state)
    {
        return new DbRelationshipEdgeAnchor
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            FromPersonId = state.FromPersonId,
            ToPersonId = state.ToPersonId,
            AnchorType = state.AnchorType,
            Status = state.Status,
            SourceBindingType = state.SourceBindingType,
            SourceBindingValue = state.SourceBindingValue,
            SourceBindingNormalized = state.SourceBindingNormalized,
            SourceMessageId = state.SourceMessageId,
            CandidateIdentityStateId = state.CandidateIdentityStateId,
            MetadataJson = state.MetadataJson,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static IdentityMergePersonState Clone(IdentityMergePersonState state)
    {
        return new IdentityMergePersonState
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            PersonType = state.PersonType,
            DisplayName = state.DisplayName,
            CanonicalName = state.CanonicalName,
            Status = state.Status,
            PrimaryActorKey = state.PrimaryActorKey,
            PrimaryTelegramUserId = state.PrimaryTelegramUserId,
            PrimaryTelegramUsername = state.PrimaryTelegramUsername,
            MetadataJson = state.MetadataJson,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static IdentityMergePersonOperatorLinkState Clone(IdentityMergePersonOperatorLinkState state)
    {
        return new IdentityMergePersonOperatorLinkState
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            OperatorPersonId = state.OperatorPersonId,
            PersonId = state.PersonId,
            LinkType = state.LinkType,
            Status = state.Status,
            SourceBindingType = state.SourceBindingType,
            SourceBindingValue = state.SourceBindingValue,
            SourceBindingNormalized = state.SourceBindingNormalized,
            SourceMessageId = state.SourceMessageId,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static IdentityMergeBindingState Clone(IdentityMergeBindingState state)
    {
        return new IdentityMergeBindingState
        {
            Id = state.Id,
            PersonId = state.PersonId,
            ScopeKey = state.ScopeKey,
            BindingType = state.BindingType,
            BindingValue = state.BindingValue,
            BindingNormalized = state.BindingNormalized,
            SourceMessageId = state.SourceMessageId,
            Confidence = state.Confidence,
            IsPrimary = state.IsPrimary,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static IdentityMergeCandidateState Clone(IdentityMergeCandidateState state)
    {
        return new IdentityMergeCandidateState
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            CandidateType = state.CandidateType,
            Status = state.Status,
            DisplayLabel = state.DisplayLabel,
            SourceBindingType = state.SourceBindingType,
            SourceBindingValue = state.SourceBindingValue,
            SourceBindingNormalized = state.SourceBindingNormalized,
            SourceMessageId = state.SourceMessageId,
            MatchedPersonId = state.MatchedPersonId,
            MetadataJson = state.MetadataJson,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static IdentityMergeRelationshipAnchorState Clone(IdentityMergeRelationshipAnchorState state)
    {
        return new IdentityMergeRelationshipAnchorState
        {
            Id = state.Id,
            ScopeKey = state.ScopeKey,
            FromPersonId = state.FromPersonId,
            ToPersonId = state.ToPersonId,
            AnchorType = state.AnchorType,
            Status = state.Status,
            SourceBindingType = state.SourceBindingType,
            SourceBindingValue = state.SourceBindingValue,
            SourceBindingNormalized = state.SourceBindingNormalized,
            SourceMessageId = state.SourceMessageId,
            CandidateIdentityStateId = state.CandidateIdentityStateId,
            MetadataJson = state.MetadataJson,
            CreatedAt = state.CreatedAt,
            UpdatedAt = state.UpdatedAt
        };
    }

    private static bool RequiresReview(
        DbPerson target,
        DbPerson source,
        string confidenceTier)
    {
        return string.Equals(confidenceTier, IdentityMergeConfidenceTiers.Weak, StringComparison.Ordinal)
            || !string.Equals(target.PersonType, source.PersonType, StringComparison.Ordinal);
    }

    private static bool HasConflictingExplicitIdentity(DbPerson target, DbPerson source)
    {
        if (!string.IsNullOrWhiteSpace(target.PrimaryActorKey)
            && !string.IsNullOrWhiteSpace(source.PrimaryActorKey)
            && !string.Equals(target.PrimaryActorKey, source.PrimaryActorKey, StringComparison.Ordinal))
        {
            return true;
        }

        return target.PrimaryTelegramUserId != null
            && source.PrimaryTelegramUserId != null
            && target.PrimaryTelegramUserId != source.PrimaryTelegramUserId;
    }

    private static string NormalizeConfidenceTier(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            IdentityMergeConfidenceTiers.Weak => IdentityMergeConfidenceTiers.Weak,
            IdentityMergeConfidenceTiers.Strong => IdentityMergeConfidenceTiers.Strong,
            _ => IdentityMergeConfidenceTiers.Medium
        };
    }

    private static string NormalizeReason(string? reason)
        => string.IsNullOrWhiteSpace(reason) ? "identity_resolution" : reason.Trim();

    private static string NormalizeActor(string? actor, string fallback)
        => string.IsNullOrWhiteSpace(actor) ? fallback : actor.Trim();

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string PreferTarget(string targetValue, string sourceValue)
        => !string.IsNullOrWhiteSpace(targetValue) ? targetValue : sourceValue;

    private static string MergeTargetMetadata(
        string? targetMetadataJson,
        string? sourceMetadataJson,
        Guid mergeId,
        Guid sourcePersonId)
    {
        var targetRoot = ParseObject(targetMetadataJson);
        var sourceRoot = ParseObject(sourceMetadataJson);
        foreach (var property in sourceRoot)
        {
            if (!targetRoot.ContainsKey(property.Key))
            {
                targetRoot[property.Key] = property.Value?.DeepClone();
            }
        }

        targetRoot["last_identity_merge_id"] = mergeId.ToString("D");
        targetRoot["last_merged_source_person_id"] = sourcePersonId.ToString("D");
        return targetRoot.ToJsonString();
    }

    private static string MergeSourceMetadata(
        string? sourceMetadataJson,
        Guid targetPersonId,
        Guid mergeId,
        string originalStatus)
    {
        var sourceRoot = ParseObject(sourceMetadataJson);
        sourceRoot["merged_into_person_id"] = targetPersonId.ToString("D");
        sourceRoot["identity_merge_id"] = mergeId.ToString("D");
        sourceRoot["original_status"] = originalStatus;
        return sourceRoot.ToJsonString();
    }

    private static JsonObject ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

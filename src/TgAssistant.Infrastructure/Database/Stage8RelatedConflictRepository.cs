using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TgAssistant.Core.Legacy.Models;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public sealed class Stage8RelatedConflictRepository : IStage8RelatedConflictRepository
{
    private static readonly IReadOnlyDictionary<string, string[]> TargetFamilyToObjectFamilies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Stage8RecomputeTargetFamilies.DossierProfile] =
            [
                Stage7DurableObjectFamilies.Dossier,
                Stage7DurableObjectFamilies.Profile
            ],
            [Stage8RecomputeTargetFamilies.PairDynamics] = [Stage7DurableObjectFamilies.PairDynamics],
            [Stage8RecomputeTargetFamilies.TimelineObjects] =
            [
                Stage7DurableObjectFamilies.Event,
                Stage7DurableObjectFamilies.TimelineEpisode,
                Stage7DurableObjectFamilies.StoryArc
            ]
        };

    private readonly IDbContextFactory<TgAssistantDbContext> _dbFactory;

    public Stage8RelatedConflictRepository(IDbContextFactory<TgAssistantDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<Stage8RelatedConflictReevaluationResult> ReevaluateAsync(
        Stage8RelatedConflictReevaluationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scopeKey = request.ScopeKey?.Trim();
        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return Skipped("scope_key_required");
        }

        if (!string.Equals(request.ResultStatus, ModelPassResultStatuses.ResultReady, StringComparison.Ordinal))
        {
            return Skipped("result_status_not_ready");
        }

        if (!TargetFamilyToObjectFamilies.TryGetValue(request.TargetFamily, out var objectFamilies)
            || objectFamilies.Length == 0)
        {
            return Skipped("target_family_not_supported");
        }

        if (!TryResolveChatScope(scopeKey, out var chatId))
        {
            return Skipped("scope_key_not_chat_bound");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var targetPersonId = ResolveTargetPersonId(request);
        var hasExplicitScopeOnlyTarget = string.Equals(request.TargetRef, $"scope:{scopeKey}", StringComparison.Ordinal);
        var metadataQuery = db.DurableObjectMetadata
            .Where(x => x.ScopeKey == scopeKey && objectFamilies.Contains(x.ObjectFamily));
        if (targetPersonId.HasValue && !hasExplicitScopeOnlyTarget)
        {
            metadataQuery = metadataQuery.Where(x =>
                x.OwnerPersonId == targetPersonId.Value
                || x.RelatedPersonId == targetPersonId.Value);
        }

        var metadataRows = await metadataQuery
            .AsNoTracking()
            .Select(x => new Stage8RelatedConflictSnapshot
            {
                MetadataId = x.Id,
                ObjectFamily = x.ObjectFamily,
                ObjectKey = x.ObjectKey,
                PromotionState = x.PromotionState,
                ContradictionCount = CountJsonArrayItems(x.ContradictionMarkersJson)
            })
            .ToListAsync(ct);

        var familySet = objectFamilies.ToHashSet(StringComparer.Ordinal);
        var existingRows = await db.ConflictRecords
            .Where(x => x.CaseId == chatId && x.ConflictType == Stage8RelatedConflictTypes.RecomputedContradiction)
            .Where(x =>
                (x.ObjectAType == "durable_object_metadata" && familySet.Contains(x.ObjectBType))
                || (x.ObjectBType == "durable_object_metadata" && familySet.Contains(x.ObjectAType)))
            .ToListAsync(ct);
        var existingConflicts = existingRows.Select(ToDomain).ToList();
        var operations = Stage8RelatedConflictReevaluationPlanner.Plan(metadataRows, existingConflicts, request);
        var now = DateTime.UtcNow;
        var activeConflictIds = new List<Guid>();
        var resolvedConflictIds = new List<Guid>();
        var createdCount = 0;
        var refreshedCount = 0;
        var resolvedCount = 0;
        var unchangedCount = 0;
        var existingById = existingRows.ToDictionary(x => x.Id);

        foreach (var operation in operations)
        {
            switch (operation.Kind)
            {
                case Stage8RelatedConflictOperationKinds.Create:
                {
                    var row = new DbConflictRecord
                    {
                        Id = Guid.NewGuid(),
                        ConflictType = Stage8RelatedConflictTypes.RecomputedContradiction,
                        ObjectAType = "durable_object_metadata",
                        ObjectAId = operation.MetadataId.ToString("D"),
                        ObjectBType = operation.ObjectFamily,
                        ObjectBId = operation.ObjectKey,
                        Summary = operation.Summary,
                        Severity = operation.Severity,
                        Status = "open",
                        CaseId = chatId,
                        ChatId = chatId,
                        LastActor = "stage8_recompute",
                        LastReason = operation.Reason,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.ConflictRecords.Add(row);
                    db.DomainReviewEvents.Add(BuildEvent(
                        row.Id,
                        "created_after_recompute",
                        actor: "stage8_recompute",
                        reason: operation.Reason,
                        newValueRef: JsonSerializer.Serialize(new
                        {
                            row.ConflictType,
                            row.ObjectAType,
                            row.ObjectAId,
                            row.ObjectBType,
                            row.ObjectBId,
                            row.Severity,
                            row.Status
                        })));
                    activeConflictIds.Add(row.Id);
                    createdCount += 1;
                    break;
                }
                case Stage8RelatedConflictOperationKinds.Refresh:
                {
                    if (!operation.ExistingConflictId.HasValue
                        || !existingById.TryGetValue(operation.ExistingConflictId.Value, out var row))
                    {
                        throw new InvalidOperationException($"Related conflict refresh expected existing row '{operation.ExistingConflictId?.ToString("D") ?? "missing"}'.");
                    }

                    var oldRef = JsonSerializer.Serialize(new
                    {
                        row.ObjectAType,
                        row.ObjectAId,
                        row.ObjectBType,
                        row.ObjectBId,
                        row.Summary,
                        row.Severity,
                        row.Status
                    });
                    row.ObjectAType = "durable_object_metadata";
                    row.ObjectAId = operation.MetadataId.ToString("D");
                    row.ObjectBType = operation.ObjectFamily;
                    row.ObjectBId = operation.ObjectKey;
                    row.Summary = operation.Summary;
                    row.Severity = operation.Severity;
                    row.Status = "open";
                    row.LastActor = "stage8_recompute";
                    row.LastReason = operation.Reason;
                    row.UpdatedAt = now;
                    db.DomainReviewEvents.Add(BuildEvent(
                        row.Id,
                        "updated_after_recompute",
                        actor: "stage8_recompute",
                        reason: operation.Reason,
                        oldValueRef: oldRef,
                        newValueRef: JsonSerializer.Serialize(new
                        {
                            row.ObjectAType,
                            row.ObjectAId,
                            row.ObjectBType,
                            row.ObjectBId,
                            row.Summary,
                            row.Severity,
                            row.Status
                        })));
                    activeConflictIds.Add(row.Id);
                    refreshedCount += 1;
                    break;
                }
                case Stage8RelatedConflictOperationKinds.Resolve:
                {
                    if (!operation.ExistingConflictId.HasValue
                        || !existingById.TryGetValue(operation.ExistingConflictId.Value, out var row))
                    {
                        throw new InvalidOperationException($"Related conflict resolve expected existing row '{operation.ExistingConflictId?.ToString("D") ?? "missing"}'.");
                    }

                    var oldRef = JsonSerializer.Serialize(new { row.Status });
                    row.Status = "resolved";
                    row.LastActor = "stage8_recompute";
                    row.LastReason = operation.Reason;
                    row.UpdatedAt = now;
                    db.DomainReviewEvents.Add(BuildEvent(
                        row.Id,
                        "auto_resolved_after_recompute",
                        actor: "stage8_recompute",
                        reason: operation.Reason,
                        oldValueRef: oldRef,
                        newValueRef: JsonSerializer.Serialize(new { row.Status })));
                    resolvedConflictIds.Add(row.Id);
                    resolvedCount += 1;
                    break;
                }
                case Stage8RelatedConflictOperationKinds.Unchanged:
                {
                    if (operation.ExistingConflictId.HasValue)
                    {
                        activeConflictIds.Add(operation.ExistingConflictId.Value);
                    }

                    unchangedCount += 1;
                    break;
                }
                default:
                    throw new InvalidOperationException($"Unsupported related conflict operation '{operation.Kind}'.");
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new Stage8RelatedConflictReevaluationResult
        {
            Applied = true,
            CreatedCount = createdCount,
            RefreshedCount = refreshedCount,
            ResolvedCount = resolvedCount,
            UnchangedCount = unchangedCount,
            ActiveConflictIds = activeConflictIds,
            ResolvedConflictIds = resolvedConflictIds
        };
    }

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

    private static DbDomainReviewEvent BuildEvent(
        Guid conflictId,
        string action,
        string actor,
        string reason,
        string? oldValueRef = null,
        string? newValueRef = null)
    {
        return new DbDomainReviewEvent
        {
            Id = Guid.NewGuid(),
            ObjectType = "conflict_record",
            ObjectId = conflictId.ToString("D"),
            Action = action,
            OldValueRef = oldValueRef,
            NewValueRef = newValueRef,
            Reason = reason,
            Actor = actor,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static int CountJsonArrayItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static Guid? ResolveTargetPersonId(Stage8RelatedConflictReevaluationRequest request)
    {
        if (request.PersonId.HasValue)
        {
            return request.PersonId.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.TargetRef)
            && request.TargetRef.StartsWith("person:", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(request.TargetRef["person:".Length..], out var parsedPersonId))
        {
            return parsedPersonId;
        }

        return null;
    }

    private static bool TryResolveChatScope(string scopeKey, out long chatId)
    {
        chatId = 0;
        if (!scopeKey.StartsWith("chat:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return long.TryParse(scopeKey["chat:".Length..], out chatId) && chatId > 0;
    }

    private static Stage8RelatedConflictReevaluationResult Skipped(string reason)
    {
        return new Stage8RelatedConflictReevaluationResult
        {
            Applied = false,
            SkipReason = reason
        };
    }
}

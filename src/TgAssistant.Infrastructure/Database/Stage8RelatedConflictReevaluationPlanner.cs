using TgAssistant.Core.Legacy.Models;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class Stage8RelatedConflictReevaluationPlanner
{
    public static IReadOnlyList<Stage8RelatedConflictOperation> Plan(
        IReadOnlyCollection<Stage8RelatedConflictSnapshot> snapshots,
        IReadOnlyCollection<ConflictRecord> existingConflicts,
        Stage8RelatedConflictReevaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(existingConflicts);
        ArgumentNullException.ThrowIfNull(request);

        var operations = new List<Stage8RelatedConflictOperation>();
        var activeSnapshots = snapshots
            .Where(x => x.ContradictionCount > 0)
            .OrderBy(x => x.ObjectFamily, StringComparer.Ordinal)
            .ThenBy(x => x.ObjectKey, StringComparer.Ordinal)
            .ToList();
        var existingByMetadataId = existingConflicts
            .Select(conflict => new { Conflict = conflict, MetadataId = TryGetMetadataId(conflict) })
            .Where(x => x.MetadataId.HasValue)
            .GroupBy(x => x.MetadataId!.Value)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.Conflict.UpdatedAt).First().Conflict);

        foreach (var snapshot in activeSnapshots)
        {
            var summary = BuildSummary(snapshot);
            var severity = ResolveSeverity(snapshot);
            if (existingByMetadataId.TryGetValue(snapshot.MetadataId, out var existing))
            {
                var shouldRefresh =
                    !string.Equals(existing.Status, "open", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.Summary, summary, StringComparison.Ordinal)
                    || !string.Equals(existing.Severity, severity, StringComparison.Ordinal)
                    || !MatchesLinkedObject(existing, snapshot);

                operations.Add(new Stage8RelatedConflictOperation
                {
                    Kind = shouldRefresh
                        ? Stage8RelatedConflictOperationKinds.Refresh
                        : Stage8RelatedConflictOperationKinds.Unchanged,
                    ExistingConflictId = existing.Id,
                    MetadataId = snapshot.MetadataId,
                    ObjectFamily = snapshot.ObjectFamily,
                    ObjectKey = snapshot.ObjectKey,
                    Summary = summary,
                    Severity = severity,
                    Reason = BuildReason(
                        shouldRefresh ? "revalidated_after_recompute" : "still_active_after_recompute",
                        request,
                        snapshot)
                });
                continue;
            }

            operations.Add(new Stage8RelatedConflictOperation
            {
                Kind = Stage8RelatedConflictOperationKinds.Create,
                MetadataId = snapshot.MetadataId,
                ObjectFamily = snapshot.ObjectFamily,
                ObjectKey = snapshot.ObjectKey,
                Summary = summary,
                Severity = severity,
                Reason = BuildReason("created_after_recompute", request, snapshot)
            });
        }

        var activeIds = activeSnapshots.Select(x => x.MetadataId).ToHashSet();
        foreach (var existing in existingByMetadataId.Values
                     .Where(x => !string.Equals(x.Status, "resolved", StringComparison.OrdinalIgnoreCase)))
        {
            var metadataId = TryGetMetadataId(existing);
            if (!metadataId.HasValue || activeIds.Contains(metadataId.Value))
            {
                continue;
            }

            operations.Add(new Stage8RelatedConflictOperation
            {
                Kind = Stage8RelatedConflictOperationKinds.Resolve,
                ExistingConflictId = existing.Id,
                MetadataId = metadataId.Value,
                ObjectFamily = ResolveObjectFamily(existing),
                ObjectKey = ResolveObjectKey(existing),
                Summary = existing.Summary,
                Severity = existing.Severity,
                Reason = BuildResolveReason(request, existing, metadataId.Value)
            });
        }

        return operations;
    }

    private static bool MatchesLinkedObject(ConflictRecord conflict, Stage8RelatedConflictSnapshot snapshot)
    {
        return string.Equals(ResolveObjectFamily(conflict), snapshot.ObjectFamily, StringComparison.Ordinal)
               && string.Equals(ResolveObjectKey(conflict), snapshot.ObjectKey, StringComparison.Ordinal);
    }

    private static string ResolveSeverity(Stage8RelatedConflictSnapshot snapshot)
    {
        return snapshot.ContradictionCount > 1
               || string.Equals(snapshot.PromotionState, Stage8PromotionStates.PromotionBlocked, StringComparison.Ordinal)
            ? "high"
            : "medium";
    }

    private static string BuildSummary(Stage8RelatedConflictSnapshot snapshot)
    {
        var markerLabel = snapshot.ContradictionCount == 1 ? "marker" : "markers";
        return $"{DescribeFamily(snapshot.ObjectFamily)} '{snapshot.ObjectKey}' still carries {snapshot.ContradictionCount} contradiction {markerLabel} after recompute.";
    }

    private static string BuildReason(
        string action,
        Stage8RelatedConflictReevaluationRequest request,
        Stage8RelatedConflictSnapshot snapshot)
    {
        return $"{action}: queue_item={request.QueueItemId:D}; target_family={request.TargetFamily}; object_family={snapshot.ObjectFamily}; metadata_id={snapshot.MetadataId:D}; contradiction_count={snapshot.ContradictionCount}; model_pass_run={request.ModelPassRunId?.ToString("D") ?? "none"}";
    }

    private static string BuildResolveReason(
        Stage8RelatedConflictReevaluationRequest request,
        ConflictRecord existing,
        Guid metadataId)
    {
        return $"auto_closed_after_recompute_no_remaining_contradictions: queue_item={request.QueueItemId:D}; target_family={request.TargetFamily}; metadata_id={metadataId:D}; object_key={ResolveObjectKey(existing)}; model_pass_run={request.ModelPassRunId?.ToString("D") ?? "none"}";
    }

    private static Guid? TryGetMetadataId(ConflictRecord conflict)
    {
        if (string.Equals(conflict.ObjectAType, "durable_object_metadata", StringComparison.Ordinal)
            && Guid.TryParse(conflict.ObjectAId, out var objectAMetadataId))
        {
            return objectAMetadataId;
        }

        if (string.Equals(conflict.ObjectBType, "durable_object_metadata", StringComparison.Ordinal)
            && Guid.TryParse(conflict.ObjectBId, out var objectBMetadataId))
        {
            return objectBMetadataId;
        }

        return null;
    }

    private static string ResolveObjectFamily(ConflictRecord conflict)
    {
        return string.Equals(conflict.ObjectAType, "durable_object_metadata", StringComparison.Ordinal)
            ? conflict.ObjectBType
            : conflict.ObjectAType;
    }

    private static string ResolveObjectKey(ConflictRecord conflict)
    {
        return string.Equals(conflict.ObjectAType, "durable_object_metadata", StringComparison.Ordinal)
            ? conflict.ObjectBId
            : conflict.ObjectAId;
    }

    private static string DescribeFamily(string objectFamily)
    {
        return objectFamily switch
        {
            Stage7DurableObjectFamilies.Dossier => "Dossier",
            Stage7DurableObjectFamilies.Profile => "Profile",
            Stage7DurableObjectFamilies.PairDynamics => "Pair dynamics",
            Stage7DurableObjectFamilies.Event => "Event",
            Stage7DurableObjectFamilies.TimelineEpisode => "Timeline episode",
            Stage7DurableObjectFamilies.StoryArc => "Story arc",
            _ => objectFamily
        };
    }
}

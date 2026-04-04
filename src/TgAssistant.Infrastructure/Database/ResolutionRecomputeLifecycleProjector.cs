using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Infrastructure.Database;

public static class ResolutionRecomputeLifecycleProjector
{
    private const string ResolutionActionTriggerPrefix = "resolution_action:";

    public static void ApplyInitialRunningState(DbOperatorResolutionAction actionRow, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(actionRow);

        actionRow.RecomputeStatus = ResolutionRecomputeLifecycleStatuses.Running;
        actionRow.RecomputeStatusUpdatedAtUtc = nowUtc;
        actionRow.RecomputeCompletedAtUtc = null;
        actionRow.RecomputeLastResultStatus = null;
        actionRow.RecomputeLastError = null;
    }

    public static bool TryParseResolutionActionId(string? triggerRef, out Guid actionId)
    {
        actionId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(triggerRef)
            || !triggerRef.StartsWith(ResolutionActionTriggerPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return Guid.TryParse(triggerRef[ResolutionActionTriggerPrefix.Length..], out actionId);
    }

    public static ResolutionRecomputeContract? ProjectContract(
        ResolutionRecomputeContract? contract,
        DbOperatorResolutionAction? actionRow,
        IReadOnlyCollection<DbStage8RecomputeQueueItem> queueItems)
    {
        if (contract == null)
        {
            return null;
        }

        var queueById = queueItems.ToDictionary(x => x.Id, x => x);
        var projectedTargets = new List<ResolutionRecomputeTarget>(contract.Targets.Count);
        foreach (var target in contract.Targets)
        {
            DbStage8RecomputeQueueItem? queueItem = null;
            if (target.QueueItemId.HasValue)
            {
                queueById.TryGetValue(target.QueueItemId.Value, out queueItem);
            }

            projectedTargets.Add(ProjectTarget(target, queueItem));
        }

        var aggregate = BuildAggregate(
            projectedTargets,
            fallbackUpdatedAtUtc: actionRow?.RecomputeStatusUpdatedAtUtc,
            fallbackCompletedAtUtc: actionRow?.RecomputeCompletedAtUtc,
            fallbackLastResultStatus: actionRow?.RecomputeLastResultStatus,
            fallbackLastError: actionRow?.RecomputeLastError,
            fallbackStatus: actionRow?.RecomputeStatus);

        return new ResolutionRecomputeContract
        {
            Enqueued = contract.Enqueued,
            TriggerKind = contract.TriggerKind,
            TriggerRef = contract.TriggerRef,
            LifecycleStatus = aggregate.Status,
            LifecycleUpdatedAtUtc = aggregate.UpdatedAtUtc,
            CompletedAtUtc = aggregate.CompletedAtUtc,
            LastResultStatus = aggregate.LastResultStatus,
            FailureReason = aggregate.LastError,
            Targets = projectedTargets
        };
    }

    public static ResolutionRecomputeLifecycleAggregate BuildAggregate(
        IReadOnlyCollection<ResolutionRecomputeTarget> targets,
        DateTime? fallbackUpdatedAtUtc = null,
        DateTime? fallbackCompletedAtUtc = null,
        string? fallbackLastResultStatus = null,
        string? fallbackLastError = null,
        string? fallbackStatus = null)
    {
        if (targets.Count == 0)
        {
            return new ResolutionRecomputeLifecycleAggregate
            {
                Status = string.IsNullOrWhiteSpace(fallbackStatus)
                    ? ResolutionRecomputeLifecycleStatuses.Running
                    : fallbackStatus!,
                UpdatedAtUtc = fallbackUpdatedAtUtc,
                CompletedAtUtc = fallbackCompletedAtUtc,
                LastResultStatus = fallbackLastResultStatus,
                LastError = fallbackLastError
            };
        }

        var ordered = targets
            .OrderBy(GetSeverityRank)
            .ThenByDescending(x => x.LifecycleUpdatedAtUtc ?? DateTime.MinValue)
            .ToList();
        var representative = ordered[0];
        var hasFailed = targets.Any(x => string.Equals(x.LifecycleStatus, ResolutionRecomputeLifecycleStatuses.Failed, StringComparison.Ordinal));
        var hasClarificationBlocked = targets.Any(x => string.Equals(x.LifecycleStatus, ResolutionRecomputeLifecycleStatuses.ClarificationBlocked, StringComparison.Ordinal));
        var hasRunning = targets.Any(x => string.Equals(x.LifecycleStatus, ResolutionRecomputeLifecycleStatuses.Running, StringComparison.Ordinal));

        return new ResolutionRecomputeLifecycleAggregate
        {
            Status = hasFailed
                ? ResolutionRecomputeLifecycleStatuses.Failed
                : hasClarificationBlocked
                    ? ResolutionRecomputeLifecycleStatuses.ClarificationBlocked
                    : hasRunning
                        ? ResolutionRecomputeLifecycleStatuses.Running
                        : ResolutionRecomputeLifecycleStatuses.Done,
            UpdatedAtUtc = targets.Max(x => x.LifecycleUpdatedAtUtc) ?? fallbackUpdatedAtUtc,
            CompletedAtUtc = targets.All(IsTerminal)
                ? targets.Max(x => x.CompletedAtUtc ?? x.LifecycleUpdatedAtUtc) ?? fallbackCompletedAtUtc
                : null,
            LastResultStatus = representative.LastResultStatus ?? fallbackLastResultStatus,
            LastError = representative.FailureReason ?? fallbackLastError
        };
    }

    private static ResolutionRecomputeTarget ProjectTarget(
        ResolutionRecomputeTarget target,
        DbStage8RecomputeQueueItem? queueItem)
    {
        if (queueItem == null)
        {
            return new ResolutionRecomputeTarget
            {
                QueueItemId = target.QueueItemId,
                TargetFamily = target.TargetFamily,
                TargetRef = target.TargetRef,
                MappingRule = target.MappingRule,
                Priority = target.Priority,
                LifecycleStatus = string.IsNullOrWhiteSpace(target.LifecycleStatus)
                    ? ResolutionRecomputeLifecycleStatuses.Running
                    : target.LifecycleStatus,
                LifecycleUpdatedAtUtc = target.LifecycleUpdatedAtUtc,
                CompletedAtUtc = target.CompletedAtUtc,
                LastResultStatus = target.LastResultStatus,
                FailureReason = target.FailureReason
            };
        }

        var lifecycleStatus = MapQueueItemLifecycleStatus(queueItem);
        return new ResolutionRecomputeTarget
        {
            QueueItemId = target.QueueItemId ?? queueItem.Id,
            TargetFamily = string.IsNullOrWhiteSpace(target.TargetFamily) ? queueItem.TargetFamily : target.TargetFamily,
            TargetRef = string.IsNullOrWhiteSpace(target.TargetRef) ? queueItem.TargetRef : target.TargetRef,
            MappingRule = target.MappingRule,
            Priority = target.Priority,
            LifecycleStatus = lifecycleStatus,
            LifecycleUpdatedAtUtc = queueItem.UpdatedAtUtc,
            CompletedAtUtc = IsTerminal(lifecycleStatus)
                ? queueItem.CompletedAtUtc ?? queueItem.UpdatedAtUtc
                : null,
            LastResultStatus = queueItem.LastResultStatus,
            FailureReason = queueItem.LastError
        };
    }

    public static string MapQueueItemLifecycleStatus(DbStage8RecomputeQueueItem queueItem)
    {
        if (string.Equals(queueItem.Status, Stage8RecomputeQueueStatuses.Failed, StringComparison.Ordinal)
            || string.Equals(queueItem.LastResultStatus, ModelPassResultStatuses.BlockedInvalidInput, StringComparison.Ordinal)
            || string.Equals(queueItem.LastResultStatus, ModelPassResultStatuses.NeedMoreData, StringComparison.Ordinal)
            || string.Equals(queueItem.LastResultStatus, Stage8RecomputeExecutionStatuses.FailedTerminally, StringComparison.Ordinal))
        {
            return ResolutionRecomputeLifecycleStatuses.Failed;
        }

        if (string.Equals(queueItem.Status, Stage8RecomputeQueueStatuses.Completed, StringComparison.Ordinal)
            && string.Equals(queueItem.LastResultStatus, ModelPassResultStatuses.NeedOperatorClarification, StringComparison.Ordinal))
        {
            return ResolutionRecomputeLifecycleStatuses.ClarificationBlocked;
        }

        if (string.Equals(queueItem.Status, Stage8RecomputeQueueStatuses.Pending, StringComparison.Ordinal)
            || string.Equals(queueItem.Status, Stage8RecomputeQueueStatuses.Leased, StringComparison.Ordinal))
        {
            return ResolutionRecomputeLifecycleStatuses.Running;
        }

        return ResolutionRecomputeLifecycleStatuses.Done;
    }

    private static int GetSeverityRank(ResolutionRecomputeTarget target)
    {
        return target.LifecycleStatus switch
        {
            ResolutionRecomputeLifecycleStatuses.Failed => 0,
            ResolutionRecomputeLifecycleStatuses.ClarificationBlocked => 1,
            ResolutionRecomputeLifecycleStatuses.Running => 2,
            _ => 3
        };
    }

    private static bool IsTerminal(ResolutionRecomputeTarget target)
        => IsTerminal(target.LifecycleStatus);

    private static bool IsTerminal(string? lifecycleStatus)
    {
        return string.Equals(lifecycleStatus, ResolutionRecomputeLifecycleStatuses.Done, StringComparison.Ordinal)
            || string.Equals(lifecycleStatus, ResolutionRecomputeLifecycleStatuses.Failed, StringComparison.Ordinal)
            || string.Equals(lifecycleStatus, ResolutionRecomputeLifecycleStatuses.ClarificationBlocked, StringComparison.Ordinal);
    }
}

public sealed class ResolutionRecomputeLifecycleAggregate
{
    public string Status { get; init; } = ResolutionRecomputeLifecycleStatuses.Running;
    public DateTime? UpdatedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string? LastResultStatus { get; init; }
    public string? LastError { get; init; }
}

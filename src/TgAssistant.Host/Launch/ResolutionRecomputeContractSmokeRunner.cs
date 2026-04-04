using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Infrastructure.Database.Ef;

namespace TgAssistant.Host.Launch;

public static class ResolutionRecomputeContractSmokeRunner
{
    public static void Run()
    {
        var trackedPersonId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        const string scopeKey = "chat:resolution-recompute-smoke";

        AssertContract(
            actionType: ResolutionActionTypes.Clarify,
            trackedPersonId,
            scopeKey,
            new ResolutionItemDetail
            {
                SourceKind = "clarification_branch",
                AffectedFamily = Stage8RecomputeTargetFamilies.TimelineObjects,
                AffectedObjectRef = "person:11111111-1111-1111-1111-111111111111"
            },
            expectedFamily: Stage8RecomputeTargetFamilies.TimelineObjects,
            expectedRule: ResolutionRecomputeMappingRules.AffectedFamilyExact,
            expectedPriority: 15,
            expectedTriggerKind: "resolution_clarify");

        AssertContract(
            actionType: ResolutionActionTypes.Approve,
            trackedPersonId,
            scopeKey,
            new ResolutionItemDetail
            {
                SourceKind = "runtime_control_state",
                AffectedFamily = "runtime_control",
                AffectedObjectRef = $"scope:{scopeKey}"
            },
            expectedFamily: Stage8RecomputeTargetFamilies.Stage6Bootstrap,
            expectedRule: ResolutionRecomputeMappingRules.RuntimeControlScopeBootstrap,
            expectedPriority: 20,
            expectedTriggerKind: "resolution_approve");

        AssertContract(
            actionType: ResolutionActionTypes.Reject,
            trackedPersonId,
            scopeKey,
            new ResolutionItemDetail
            {
                SourceKind = "runtime_defect",
                AffectedFamily = RuntimeDefectClasses.ControlPlane,
                AffectedObjectRef = $"scope:{scopeKey}"
            },
            expectedFamily: Stage8RecomputeTargetFamilies.Stage6Bootstrap,
            expectedRule: ResolutionRecomputeMappingRules.RuntimeDefectScopeBootstrap,
            expectedPriority: 20,
            expectedTriggerKind: "resolution_reject");

        var unsupported = ResolutionRecomputePlanner.BuildContract(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ResolutionActionTypes.Approve,
            scopeKey,
            trackedPersonId,
            new ResolutionItemDetail
            {
                SourceKind = "unknown_source",
                AffectedFamily = "unknown_family",
                AffectedObjectRef = "scope:unknown"
            });
        if (unsupported != null)
        {
            throw new InvalidOperationException("Resolution recompute contract smoke failed: unsupported resolution item unexpectedly produced a recompute contract.");
        }

        AssertLifecycleProjection(
            "running",
            new DbStage8RecomputeQueueItem
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Status = Stage8RecomputeQueueStatuses.Pending,
                TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                TargetRef = $"person:{trackedPersonId:D}",
                UpdatedAtUtc = DateTime.UtcNow
            },
            ResolutionRecomputeLifecycleStatuses.Running,
            lastResultStatus: null);

        AssertLifecycleProjection(
            "done",
            new DbStage8RecomputeQueueItem
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Status = Stage8RecomputeQueueStatuses.Completed,
                LastResultStatus = ModelPassResultStatuses.ResultReady,
                TargetFamily = Stage8RecomputeTargetFamilies.PairDynamics,
                TargetRef = $"person:{trackedPersonId:D}",
                UpdatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            },
            ResolutionRecomputeLifecycleStatuses.Done,
            lastResultStatus: ModelPassResultStatuses.ResultReady);

        AssertLifecycleProjection(
            "clarification_blocked",
            new DbStage8RecomputeQueueItem
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Status = Stage8RecomputeQueueStatuses.Completed,
                LastResultStatus = ModelPassResultStatuses.NeedOperatorClarification,
                TargetFamily = Stage8RecomputeTargetFamilies.TimelineObjects,
                TargetRef = $"person:{trackedPersonId:D}",
                UpdatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            },
            ResolutionRecomputeLifecycleStatuses.ClarificationBlocked,
            lastResultStatus: ModelPassResultStatuses.NeedOperatorClarification);

        AssertLifecycleProjection(
            "failed",
            new DbStage8RecomputeQueueItem
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Status = Stage8RecomputeQueueStatuses.Completed,
                LastResultStatus = ModelPassResultStatuses.NeedMoreData,
                LastError = "insufficient evidence",
                TargetFamily = Stage8RecomputeTargetFamilies.DossierProfile,
                TargetRef = $"person:{trackedPersonId:D}",
                UpdatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow
            },
            ResolutionRecomputeLifecycleStatuses.Failed,
            lastResultStatus: ModelPassResultStatuses.NeedMoreData);
    }

    private static void AssertContract(
        string actionType,
        Guid trackedPersonId,
        string scopeKey,
        ResolutionItemDetail item,
        string expectedFamily,
        string expectedRule,
        int expectedPriority,
        string expectedTriggerKind)
    {
        var actionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var contract = ResolutionRecomputePlanner.BuildContract(actionId, actionType, scopeKey, trackedPersonId, item)
            ?? throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected contract for source '{item.SourceKind}' and family '{item.AffectedFamily}'.");

        if (!contract.Enqueued)
        {
            throw new InvalidOperationException("Resolution recompute contract smoke failed: contract did not mark recompute as enqueued.");
        }

        if (!string.Equals(contract.TriggerKind, expectedTriggerKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected trigger kind '{expectedTriggerKind}' but got '{contract.TriggerKind}'.");
        }

        if (!string.Equals(contract.TriggerRef, $"resolution_action:{actionId:D}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolution recompute contract smoke failed: trigger ref did not bind to the resolution action id.");
        }

        if (contract.Targets.Count != 1)
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected exactly one target but got {contract.Targets.Count}.");
        }

        var target = contract.Targets[0];
        if (!string.Equals(target.TargetFamily, expectedFamily, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected target family '{expectedFamily}' but got '{target.TargetFamily}'.");
        }

        if (!string.Equals(target.MappingRule, expectedRule, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected mapping rule '{expectedRule}' but got '{target.MappingRule}'.");
        }

        if (!string.Equals(target.TargetRef, $"person:{trackedPersonId:D}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Resolution recompute contract smoke failed: target ref was not person-bounded.");
        }

        if (target.Priority != expectedPriority)
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected priority {expectedPriority} but got {target.Priority}.");
        }
    }

    private static void AssertLifecycleProjection(
        string label,
        DbStage8RecomputeQueueItem queueItem,
        string expectedLifecycleStatus,
        string? lastResultStatus)
    {
        var contract = new ResolutionRecomputeContract
        {
            Enqueued = true,
            TriggerKind = "resolution_approve",
            TriggerRef = "resolution_action:aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            Targets =
            [
                new ResolutionRecomputeTarget
                {
                    QueueItemId = queueItem.Id,
                    TargetFamily = queueItem.TargetFamily,
                    TargetRef = queueItem.TargetRef,
                    MappingRule = ResolutionRecomputeMappingRules.AffectedFamilyExact,
                    Priority = 20
                }
            ]
        };

        var projected = ResolutionRecomputeLifecycleProjector.ProjectContract(contract, actionRow: null, [queueItem])
            ?? throw new InvalidOperationException($"Resolution recompute contract smoke failed: lifecycle projection '{label}' returned null.");

        if (!string.Equals(projected.LifecycleStatus, expectedLifecycleStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected action lifecycle '{expectedLifecycleStatus}' but got '{projected.LifecycleStatus}' for '{label}'.");
        }

        if (!string.Equals(projected.Targets[0].LifecycleStatus, expectedLifecycleStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected target lifecycle '{expectedLifecycleStatus}' but got '{projected.Targets[0].LifecycleStatus}' for '{label}'.");
        }

        if (!string.Equals(projected.LastResultStatus, lastResultStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Resolution recompute contract smoke failed: expected last result status '{lastResultStatus ?? "<null>"}' but got '{projected.LastResultStatus ?? "<null>"}' for '{label}'.");
        }
    }
}

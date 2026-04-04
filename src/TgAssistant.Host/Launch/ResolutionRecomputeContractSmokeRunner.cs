using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;

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
}

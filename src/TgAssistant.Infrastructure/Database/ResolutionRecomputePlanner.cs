using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public static class ResolutionRecomputePlanner
{
    public static ResolutionRecomputeContract? BuildContract(
        Guid actionId,
        string actionType,
        string scopeKey,
        Guid trackedPersonId,
        ResolutionItemDetail item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var normalizedScopeKey = scopeKey?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedScopeKey) || trackedPersonId == Guid.Empty)
        {
            return null;
        }

        var target = ResolveTarget(trackedPersonId, item);
        if (target == null)
        {
            return null;
        }

        var normalizedAction = ResolutionActionTypes.Normalize(actionType);
        target.Priority = ResolvePriority(normalizedAction);
        return new ResolutionRecomputeContract
        {
            Enqueued = true,
            TriggerKind = BuildTriggerKind(normalizedAction),
            TriggerRef = $"resolution_action:{actionId:D}",
            Targets = [target]
        };
    }

    public static int ResolvePriority(string actionType)
    {
        return ResolutionActionTypes.Normalize(actionType) switch
        {
            ResolutionActionTypes.Clarify => 15,
            ResolutionActionTypes.Approve => 20,
            ResolutionActionTypes.Reject => 20,
            ResolutionActionTypes.Defer => 40,
            _ => 50
        };
    }

    private static ResolutionRecomputeTarget? ResolveTarget(
        Guid trackedPersonId,
        ResolutionItemDetail item)
    {
        var normalizedFamily = item.AffectedFamily?.Trim() ?? string.Empty;
        if (TryResolveStage8TargetFamily(normalizedFamily, out var targetFamily))
        {
            return new ResolutionRecomputeTarget
            {
                TargetFamily = targetFamily,
                TargetRef = $"person:{trackedPersonId:D}",
                MappingRule = ResolutionRecomputeMappingRules.AffectedFamilyExact
            };
        }

        var normalizedSourceKind = item.SourceKind?.Trim() ?? string.Empty;
        if (string.Equals(normalizedSourceKind, "runtime_control_state", StringComparison.Ordinal))
        {
            return new ResolutionRecomputeTarget
            {
                TargetFamily = Stage8RecomputeTargetFamilies.Stage6Bootstrap,
                TargetRef = $"person:{trackedPersonId:D}",
                MappingRule = ResolutionRecomputeMappingRules.RuntimeControlScopeBootstrap
            };
        }

        if (string.Equals(normalizedSourceKind, "runtime_defect", StringComparison.Ordinal))
        {
            return new ResolutionRecomputeTarget
            {
                TargetFamily = Stage8RecomputeTargetFamilies.Stage6Bootstrap,
                TargetRef = $"person:{trackedPersonId:D}",
                MappingRule = ResolutionRecomputeMappingRules.RuntimeDefectScopeBootstrap
            };
        }

        return null;
    }

    private static bool TryResolveStage8TargetFamily(string affectedFamily, out string targetFamily)
    {
        targetFamily = string.Empty;
        if (string.IsNullOrWhiteSpace(affectedFamily))
        {
            return false;
        }

        var normalized = affectedFamily.Trim();
        if (StageSemanticContract.TryMapStage8RecomputeTargetFamilyToSemanticFamily(normalized, out var semanticFamily)
            && !string.IsNullOrWhiteSpace(semanticFamily)
            && StageSemanticContract.TryMapSemanticFamilyToStage8RecomputeTargetFamily(semanticFamily, out var stage8TargetFamily)
            && !string.IsNullOrWhiteSpace(stage8TargetFamily))
        {
            targetFamily = stage8TargetFamily;
            return true;
        }

        var semanticFromStage7Object = normalized switch
        {
            Stage7DurableObjectFamilies.Dossier => StageSemanticOwnedOutputFamilies.Stage7DurableProfile,
            Stage7DurableObjectFamilies.Profile => StageSemanticOwnedOutputFamilies.Stage7DurableProfile,
            Stage7DurableObjectFamilies.PairDynamics => StageSemanticOwnedOutputFamilies.Stage7PairDynamics,
            Stage7DurableObjectFamilies.Event => StageSemanticOwnedOutputFamilies.Stage7DurableTimeline,
            Stage7DurableObjectFamilies.TimelineEpisode => StageSemanticOwnedOutputFamilies.Stage7DurableTimeline,
            Stage7DurableObjectFamilies.StoryArc => StageSemanticOwnedOutputFamilies.Stage7DurableTimeline,
            _ => null
        };

        if (!string.IsNullOrWhiteSpace(semanticFromStage7Object)
            && StageSemanticContract.TryMapSemanticFamilyToStage8RecomputeTargetFamily(semanticFromStage7Object, out var mappedTargetFamily)
            && !string.IsNullOrWhiteSpace(mappedTargetFamily))
        {
            targetFamily = mappedTargetFamily;
            return true;
        }

        return false;
    }

    private static string BuildTriggerKind(string normalizedAction)
    {
        return normalizedAction switch
        {
            ResolutionActionTypes.Approve => "resolution_approve",
            ResolutionActionTypes.Reject => "resolution_reject",
            ResolutionActionTypes.Defer => "resolution_defer",
            ResolutionActionTypes.Clarify => "resolution_clarify",
            _ => "resolution_action"
        };
    }
}

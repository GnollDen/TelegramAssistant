using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage8Recompute;

public class RuntimeControlStateService : IRuntimeControlStateService
{
    private readonly IRuntimeDefectRepository _runtimeDefectRepository;
    private readonly IRuntimeControlStateRepository _runtimeControlStateRepository;

    public RuntimeControlStateService(
        IRuntimeDefectRepository runtimeDefectRepository,
        IRuntimeControlStateRepository runtimeControlStateRepository)
    {
        _runtimeDefectRepository = runtimeDefectRepository;
        _runtimeControlStateRepository = runtimeControlStateRepository;
    }

    public async Task<RuntimeControlEnforcementDecision> EvaluateAndApplyFromDefectsAsync(CancellationToken ct = default)
    {
        var defects = await _runtimeDefectRepository.GetOpenAsync(limit: 500, ct);
        var (state, reason) = ResolveState(defects);
        var detailsJson = JsonSerializer.Serialize(new
        {
            open_defect_count = defects.Count,
            highest_escalation = defects.Select(x => x.EscalationAction).Distinct(StringComparer.Ordinal).OrderBy(x => x).LastOrDefault()
        });

        var active = await _runtimeControlStateRepository.SetActiveAsync(
            state,
            reason,
            "runtime_defect_taxonomy",
            detailsJson,
            ct);

        return BuildDecision(active.State, active.Reason);
    }

    private static (string State, string Reason) ResolveState(IReadOnlyList<RuntimeDefectRecord> defects)
    {
        if (defects.Any(x => string.Equals(x.EscalationAction, RuntimeDefectEscalationActions.SafeMode, StringComparison.Ordinal)))
        {
            return (RuntimeControlStates.SafeMode, "Critical control-plane defects triggered safe mode.");
        }

        if (defects.Any(x => string.Equals(x.EscalationAction, RuntimeDefectEscalationActions.BudgetProtected, StringComparison.Ordinal)))
        {
            return (RuntimeControlStates.BudgetProtected, "Cost defects triggered budget-protected mode.");
        }

        if (defects.Any(x => string.Equals(x.EscalationAction, RuntimeDefectEscalationActions.ReviewOnly, StringComparison.Ordinal)))
        {
            return (RuntimeControlStates.ReviewOnly, "Model/normalization defects require review-only mode.");
        }

        if (defects.Any(x =>
                string.Equals(x.DefectClass, RuntimeDefectClasses.SemanticDrift, StringComparison.Ordinal)
                && (string.Equals(x.Severity, RuntimeDefectSeverities.High, StringComparison.Ordinal)
                    || string.Equals(x.Severity, RuntimeDefectSeverities.Critical, StringComparison.Ordinal))))
        {
            return (RuntimeControlStates.PromotionBlocked, "Semantic-drift defects require temporary promotion blocking.");
        }

        var highControlPlaneCount = defects.Count(x =>
            string.Equals(x.DefectClass, RuntimeDefectClasses.ControlPlane, StringComparison.Ordinal)
            && (string.Equals(x.Severity, RuntimeDefectSeverities.High, StringComparison.Ordinal)
                || string.Equals(x.Severity, RuntimeDefectSeverities.Critical, StringComparison.Ordinal)));
        if (highControlPlaneCount >= 3)
        {
            return (RuntimeControlStates.Degraded, "Repeated control-plane defects triggered degraded mode.");
        }

        return (RuntimeControlStates.Normal, "No active defects require runtime restrictions.");
    }

    private static RuntimeControlEnforcementDecision BuildDecision(string state, string reason)
    {
        return state switch
        {
            RuntimeControlStates.SafeMode => new RuntimeControlEnforcementDecision
            {
                State = state,
                Reason = reason,
                PauseAllExecution = true
            },
            RuntimeControlStates.BudgetProtected => new RuntimeControlEnforcementDecision
            {
                State = state,
                Reason = reason,
                RestrictToBootstrapOnly = true
            },
            RuntimeControlStates.ReviewOnly => new RuntimeControlEnforcementDecision
            {
                State = state,
                Reason = reason,
                ForcePromotionBlocked = true
            },
            RuntimeControlStates.PromotionBlocked => new RuntimeControlEnforcementDecision
            {
                State = state,
                Reason = reason,
                ForcePromotionBlocked = true
            },
            RuntimeControlStates.Degraded => new RuntimeControlEnforcementDecision
            {
                State = state,
                Reason = reason,
                DeferTimelineTargets = true
            },
            _ => new RuntimeControlEnforcementDecision
            {
                State = RuntimeControlStates.Normal,
                Reason = reason
            }
        };
    }
}

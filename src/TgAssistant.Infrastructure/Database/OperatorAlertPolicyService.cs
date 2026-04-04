using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.Database;

public sealed class OperatorAlertPolicyService : IOperatorAlertPolicyService
{
    private static readonly IReadOnlyList<OperatorAlertPolicyRuleDefinition> Rules =
    [
        new()
        {
            RuleId = OperatorAlertRuleIds.CriticalClarificationBlock,
            SourceClasses = [OperatorAlertSourceClasses.ResolutionBlocker],
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            RequiresCriticality = true,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = true,
            Description = "Critical clarification blocks push to Telegram and require operator acknowledgement."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.CriticalBlockingReview,
            SourceClasses = [OperatorAlertSourceClasses.ResolutionBlocker],
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            RequiresCriticality = true,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = true,
            Description = "Critical review blockers push to Telegram when they block active workflow."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.CriticalBlockingResolution,
            SourceClasses = [OperatorAlertSourceClasses.ResolutionBlocker],
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            RequiresCriticality = true,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = true,
            Description = "Other critical blocking resolution items push to Telegram when they stop progression."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.RuntimeDegradedActiveWorkflow,
            SourceClasses = [OperatorAlertSourceClasses.RuntimeControlState],
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            RequiresCriticality = false,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = true,
            Description = "Runtime degraded/safe-mode states affecting active workflow push to Telegram."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.MaterializationFailureStopsProgression,
            SourceClasses = [OperatorAlertSourceClasses.MaterializationFailure],
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            RequiresCriticality = false,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = true,
            Description = "Materialization failures that block progression push to Telegram."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.ControlPlaneStopActiveScope,
            SourceClasses = [OperatorAlertSourceClasses.RuntimeDefect, OperatorAlertSourceClasses.RuntimeControlState],
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            RequiresCriticality = true,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = true,
            Description = "Critical control-plane stops on active tracked-person scope push to Telegram."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.CriticalWorkflowBlockerWebOnly,
            SourceClasses =
            [
                OperatorAlertSourceClasses.ResolutionBlocker,
                OperatorAlertSourceClasses.RuntimeDefect,
                OperatorAlertSourceClasses.RuntimeControlState,
                OperatorAlertSourceClasses.MaterializationFailure
            ],
            EscalationBoundary = OperatorAlertEscalationBoundaries.WebOnly,
            RequiresCriticality = true,
            RequiresBlockingWorkflow = true,
            RequiresActiveTrackedPersonScope = false,
            Description = "Critical blockers outside Telegram push boundary remain visible in web alerts."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.SuppressedStateChurn,
            SourceClasses = [OperatorAlertSourceClasses.StateTransition],
            EscalationBoundary = OperatorAlertEscalationBoundaries.Suppressed,
            RequiresCriticality = false,
            RequiresBlockingWorkflow = false,
            RequiresActiveTrackedPersonScope = false,
            Description = "State-transition churn is suppressed by default."
        },
        new()
        {
            RuleId = OperatorAlertRuleIds.SuppressedNonCriticalDefault,
            SourceClasses = [],
            EscalationBoundary = OperatorAlertEscalationBoundaries.Suppressed,
            RequiresCriticality = false,
            RequiresBlockingWorkflow = false,
            RequiresActiveTrackedPersonScope = false,
            Description = "Non-critical and non-blocking transitions are suppressed by default."
        }
    ];

    public OperatorAlertPolicyDecision Evaluate(OperatorAlertPolicyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sourceClass = Normalize(input.SourceClass);
        var itemType = Normalize(input.ItemType);
        var priority = Normalize(input.Priority);
        var runtimeState = Normalize(input.RuntimeState);
        var defectClass = Normalize(input.RuntimeDefectClass);
        var defectSeverity = Normalize(input.RuntimeDefectSeverity);
        var isCritical = string.Equals(priority, ResolutionItemPriorities.Critical, StringComparison.Ordinal)
            || string.Equals(defectSeverity, RuntimeDefectSeverities.Critical, StringComparison.Ordinal)
            || string.Equals(runtimeState, RuntimeControlStates.SafeMode, StringComparison.Ordinal);

        if (sourceClass == OperatorAlertSourceClasses.StateTransition || input.IsStateTransitionOnly)
        {
            return SuppressedDecision(
                OperatorAlertRuleIds.SuppressedStateChurn,
                "State-transition churn is suppressed by default to avoid non-critical alert noise.");
        }

        if (sourceClass == OperatorAlertSourceClasses.MaterializationFailure
            && input.IsMaterializationFailure
            && input.IsBlockingWorkflow
            && input.IsActiveTrackedPersonScope)
        {
            return PushDecision(
                OperatorAlertRuleIds.MaterializationFailureStopsProgression,
                "Materialization failure blocks progression in active scope.");
        }

        if ((sourceClass == OperatorAlertSourceClasses.RuntimeControlState
                && input.IsActiveTrackedPersonScope
                && input.IsBlockingWorkflow
                && (string.Equals(runtimeState, RuntimeControlStates.Degraded, StringComparison.Ordinal)
                    || string.Equals(runtimeState, RuntimeControlStates.SafeMode, StringComparison.Ordinal)))
            || ((sourceClass == OperatorAlertSourceClasses.RuntimeDefect || sourceClass == OperatorAlertSourceClasses.RuntimeControlState)
                && input.IsActiveTrackedPersonScope
                && input.IsBlockingWorkflow
                && string.Equals(defectClass, RuntimeDefectClasses.ControlPlane, StringComparison.Ordinal)
                && isCritical))
        {
            return PushDecision(
                string.Equals(defectClass, RuntimeDefectClasses.ControlPlane, StringComparison.Ordinal)
                    ? OperatorAlertRuleIds.ControlPlaneStopActiveScope
                    : OperatorAlertRuleIds.RuntimeDegradedActiveWorkflow,
                "Runtime control-plane degradation or stop is blocking active workflow.");
        }

        if (sourceClass == OperatorAlertSourceClasses.ResolutionBlocker
            && input.IsBlockingWorkflow
            && input.IsActiveTrackedPersonScope
            && isCritical)
        {
            if (string.Equals(itemType, ResolutionItemTypes.Clarification, StringComparison.Ordinal))
            {
                return PushDecision(
                    OperatorAlertRuleIds.CriticalClarificationBlock,
                    "Critical clarification block requires immediate operator acknowledgement.");
            }

            if (string.Equals(itemType, ResolutionItemTypes.Review, StringComparison.Ordinal))
            {
                return PushDecision(
                    OperatorAlertRuleIds.CriticalBlockingReview,
                    "Critical review item is blocking workflow progression.");
            }

            return PushDecision(
                OperatorAlertRuleIds.CriticalBlockingResolution,
                "Critical resolution blocker is preventing workflow progression.");
        }

        if (isCritical && input.IsBlockingWorkflow)
        {
            return new OperatorAlertPolicyDecision
            {
                RuleId = OperatorAlertRuleIds.CriticalWorkflowBlockerWebOnly,
                EscalationBoundary = OperatorAlertEscalationBoundaries.WebOnly,
                CreateWebAlert = true,
                PushTelegram = false,
                RequiresAcknowledgement = false,
                EnterResolutionContext = true,
                Reason = "Critical workflow blocker is retained in web alerts without default Telegram push."
            };
        }

        return SuppressedDecision(
            OperatorAlertRuleIds.SuppressedNonCriticalDefault,
            "Non-critical transition suppressed by default policy.");
    }

    public IReadOnlyList<OperatorAlertPolicyRuleDefinition> GetRules() => Rules;

    private static OperatorAlertPolicyDecision PushDecision(string ruleId, string reason)
    {
        return new OperatorAlertPolicyDecision
        {
            RuleId = ruleId,
            EscalationBoundary = OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
            CreateWebAlert = true,
            PushTelegram = true,
            RequiresAcknowledgement = true,
            EnterResolutionContext = true,
            Reason = reason
        };
    }

    private static OperatorAlertPolicyDecision SuppressedDecision(string ruleId, string reason)
    {
        return new OperatorAlertPolicyDecision
        {
            RuleId = ruleId,
            EscalationBoundary = OperatorAlertEscalationBoundaries.Suppressed,
            CreateWebAlert = false,
            PushTelegram = false,
            RequiresAcknowledgement = false,
            EnterResolutionContext = false,
            Reason = reason
        };
    }

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}

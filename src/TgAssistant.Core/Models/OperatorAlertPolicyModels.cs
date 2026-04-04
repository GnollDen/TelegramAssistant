namespace TgAssistant.Core.Models;

public static class OperatorAlertSourceClasses
{
    public const string ResolutionBlocker = "resolution_blocker";
    public const string RuntimeDefect = "runtime_defect";
    public const string RuntimeControlState = "runtime_control_state";
    public const string MaterializationFailure = "materialization_failure";
    public const string StateTransition = "state_transition";
}

public static class OperatorAlertEscalationBoundaries
{
    public const string Suppressed = "suppressed";
    public const string WebOnly = "web_only";
    public const string TelegramPushAcknowledge = "telegram_push_acknowledge";
}

public static class OperatorAlertRuleIds
{
    public const string CriticalClarificationBlock = "critical_clarification_block";
    public const string CriticalBlockingReview = "critical_blocking_review";
    public const string CriticalBlockingResolution = "critical_blocking_resolution";
    public const string RuntimeDegradedActiveWorkflow = "runtime_degraded_active_workflow";
    public const string MaterializationFailureStopsProgression = "materialization_failure_stops_progression";
    public const string ControlPlaneStopActiveScope = "control_plane_stop_active_scope";
    public const string CriticalWorkflowBlockerWebOnly = "critical_workflow_blocker_web_only";
    public const string SuppressedStateChurn = "suppressed_state_churn";
    public const string SuppressedNonCriticalDefault = "suppressed_non_critical_default";
}

public class OperatorAlertPolicyInput
{
    public string SourceClass { get; set; } = string.Empty;
    public string ScopeKey { get; set; } = string.Empty;
    public Guid? TrackedPersonId { get; set; }
    public string? ScopeItemKey { get; set; }
    public string? ItemType { get; set; }
    public string? Priority { get; set; }
    public string? RuntimeState { get; set; }
    public string? RuntimeDefectClass { get; set; }
    public string? RuntimeDefectSeverity { get; set; }
    public bool IsBlockingWorkflow { get; set; }
    public bool IsActiveTrackedPersonScope { get; set; }
    public bool IsMaterializationFailure { get; set; }
    public bool IsStateTransitionOnly { get; set; }
}

public class OperatorAlertPolicyDecision
{
    public string RuleId { get; set; } = string.Empty;
    public string EscalationBoundary { get; set; } = OperatorAlertEscalationBoundaries.Suppressed;
    public bool CreateWebAlert { get; set; }
    public bool PushTelegram { get; set; }
    public bool RequiresAcknowledgement { get; set; }
    public bool EnterResolutionContext { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class OperatorAlertPolicyRuleDefinition
{
    public string RuleId { get; set; } = string.Empty;
    public List<string> SourceClasses { get; set; } = [];
    public string EscalationBoundary { get; set; } = OperatorAlertEscalationBoundaries.Suppressed;
    public bool RequiresCriticality { get; set; }
    public bool RequiresBlockingWorkflow { get; set; }
    public bool RequiresActiveTrackedPersonScope { get; set; }
    public string Description { get; set; } = string.Empty;
}

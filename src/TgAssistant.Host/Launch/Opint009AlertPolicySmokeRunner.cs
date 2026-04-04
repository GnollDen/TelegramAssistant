using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.Launch;

public static class Opint009AlertPolicySmokeRunner
{
    public static async Task<Opint009AlertPolicySmokeReport> RunAsync(
        IOperatorAlertPolicyService policyService,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policyService);

        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var nowUtc = DateTime.UtcNow;
        var report = new Opint009AlertPolicySmokeReport
        {
            GeneratedAtUtc = nowUtc,
            OutputPath = resolvedOutputPath,
            RuleDefinitions = policyService.GetRules().ToList()
        };

        Exception? fatal = null;
        try
        {
            report.CriticalClarification = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.ResolutionBlocker,
                    ItemType = ResolutionItemTypes.Clarification,
                    Priority = ResolutionItemPriorities.Critical,
                    IsBlockingWorkflow = true,
                    IsActiveTrackedPersonScope = true
                }),
                expectedRuleId: OperatorAlertRuleIds.CriticalClarificationBlock,
                expectedBoundary: OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
                expectTelegramPush: true,
                expectAcknowledgement: true);

            report.CriticalBlockingReview = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.ResolutionBlocker,
                    ItemType = ResolutionItemTypes.Review,
                    Priority = ResolutionItemPriorities.Critical,
                    IsBlockingWorkflow = true,
                    IsActiveTrackedPersonScope = true
                }),
                expectedRuleId: OperatorAlertRuleIds.CriticalBlockingReview,
                expectedBoundary: OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
                expectTelegramPush: true,
                expectAcknowledgement: true);

            report.RuntimeDegraded = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.RuntimeControlState,
                    RuntimeState = RuntimeControlStates.Degraded,
                    IsBlockingWorkflow = true,
                    IsActiveTrackedPersonScope = true
                }),
                expectedRuleId: OperatorAlertRuleIds.RuntimeDegradedActiveWorkflow,
                expectedBoundary: OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
                expectTelegramPush: true,
                expectAcknowledgement: true);

            report.MaterializationFailure = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.MaterializationFailure,
                    IsMaterializationFailure = true,
                    IsBlockingWorkflow = true,
                    IsActiveTrackedPersonScope = true
                }),
                expectedRuleId: OperatorAlertRuleIds.MaterializationFailureStopsProgression,
                expectedBoundary: OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
                expectTelegramPush: true,
                expectAcknowledgement: true);

            report.ControlPlaneStop = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.RuntimeDefect,
                    RuntimeDefectClass = RuntimeDefectClasses.ControlPlane,
                    RuntimeDefectSeverity = RuntimeDefectSeverities.Critical,
                    IsBlockingWorkflow = true,
                    IsActiveTrackedPersonScope = true
                }),
                expectedRuleId: OperatorAlertRuleIds.ControlPlaneStopActiveScope,
                expectedBoundary: OperatorAlertEscalationBoundaries.TelegramPushAcknowledge,
                expectTelegramPush: true,
                expectAcknowledgement: true);

            report.CriticalWebOnly = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.RuntimeDefect,
                    RuntimeDefectClass = RuntimeDefectClasses.Data,
                    RuntimeDefectSeverity = RuntimeDefectSeverities.Critical,
                    IsBlockingWorkflow = true,
                    IsActiveTrackedPersonScope = false
                }),
                expectedRuleId: OperatorAlertRuleIds.CriticalWorkflowBlockerWebOnly,
                expectedBoundary: OperatorAlertEscalationBoundaries.WebOnly,
                expectTelegramPush: false,
                expectAcknowledgement: false);

            report.SuppressedStateTransition = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.StateTransition,
                    IsStateTransitionOnly = true
                }),
                expectedRuleId: OperatorAlertRuleIds.SuppressedStateChurn,
                expectedBoundary: OperatorAlertEscalationBoundaries.Suppressed,
                expectTelegramPush: false,
                expectAcknowledgement: false);

            report.SuppressedNonCritical = AssertDecision(
                policyService.Evaluate(new OperatorAlertPolicyInput
                {
                    SourceClass = OperatorAlertSourceClasses.ResolutionBlocker,
                    ItemType = ResolutionItemTypes.Review,
                    Priority = ResolutionItemPriorities.High,
                    IsBlockingWorkflow = false,
                    IsActiveTrackedPersonScope = true
                }),
                expectedRuleId: OperatorAlertRuleIds.SuppressedNonCriticalDefault,
                expectedBoundary: OperatorAlertEscalationBoundaries.Suppressed,
                expectTelegramPush: false,
                expectAcknowledgement: false);

            report.AllChecksPassed = true;
        }
        catch (Exception ex)
        {
            fatal = ex;
            report.AllChecksPassed = false;
            report.FatalError = ex.Message;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        }

        if (!report.AllChecksPassed)
        {
            throw new InvalidOperationException(
                "OPINT-009-A alert policy smoke failed: critical-only taxonomy and suppression boundaries are not correctly enforced.",
                fatal);
        }

        return report;
    }

    private static Opint009AlertPolicyDecisionResult AssertDecision(
        OperatorAlertPolicyDecision decision,
        string expectedRuleId,
        string expectedBoundary,
        bool expectTelegramPush,
        bool expectAcknowledgement)
    {
        Ensure(
            string.Equals(decision.RuleId, expectedRuleId, StringComparison.Ordinal),
            $"Rule mismatch. expected={expectedRuleId}, actual={decision.RuleId}.");
        Ensure(
            string.Equals(decision.EscalationBoundary, expectedBoundary, StringComparison.Ordinal),
            $"Boundary mismatch. expected={expectedBoundary}, actual={decision.EscalationBoundary}.");
        Ensure(decision.PushTelegram == expectTelegramPush, $"PushTelegram mismatch for rule {expectedRuleId}.");
        Ensure(decision.RequiresAcknowledgement == expectAcknowledgement, $"RequiresAcknowledgement mismatch for rule {expectedRuleId}.");

        return new Opint009AlertPolicyDecisionResult
        {
            RuleId = decision.RuleId,
            EscalationBoundary = decision.EscalationBoundary,
            PushTelegram = decision.PushTelegram,
            CreateWebAlert = decision.CreateWebAlert,
            RequiresAcknowledgement = decision.RequiresAcknowledgement,
            EnterResolutionContext = decision.EnterResolutionContext,
            Reason = decision.Reason
        };
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "logs", "opint-009-a-policy-smoke-report.json"));
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class Opint009AlertPolicySmokeReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public bool AllChecksPassed { get; set; }
    public string? FatalError { get; set; }
    public List<OperatorAlertPolicyRuleDefinition> RuleDefinitions { get; set; } = [];
    public Opint009AlertPolicyDecisionResult CriticalClarification { get; set; } = new();
    public Opint009AlertPolicyDecisionResult CriticalBlockingReview { get; set; } = new();
    public Opint009AlertPolicyDecisionResult RuntimeDegraded { get; set; } = new();
    public Opint009AlertPolicyDecisionResult MaterializationFailure { get; set; } = new();
    public Opint009AlertPolicyDecisionResult ControlPlaneStop { get; set; } = new();
    public Opint009AlertPolicyDecisionResult CriticalWebOnly { get; set; } = new();
    public Opint009AlertPolicyDecisionResult SuppressedStateTransition { get; set; } = new();
    public Opint009AlertPolicyDecisionResult SuppressedNonCritical { get; set; } = new();
}

public sealed class Opint009AlertPolicyDecisionResult
{
    public string RuleId { get; set; } = string.Empty;
    public string EscalationBoundary { get; set; } = string.Empty;
    public bool CreateWebAlert { get; set; }
    public bool PushTelegram { get; set; }
    public bool RequiresAcknowledgement { get; set; }
    public bool EnterResolutionContext { get; set; }
    public string Reason { get; set; } = string.Empty;
}

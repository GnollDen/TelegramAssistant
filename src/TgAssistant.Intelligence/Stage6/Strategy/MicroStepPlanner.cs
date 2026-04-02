// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class MicroStepPlanner : IMicroStepPlanner
{
    public (string MicroStep, IReadOnlyList<string> Horizon) Plan(
        StrategyEvaluationContext context,
        StrategyCandidateOption primaryOption,
        StrategyConfidenceAssessment confidence)
    {
        var microStep = primaryOption.ActionType switch
        {
            "wait" => "Pause for 24 hours, then reassess new signals before sending anything substantial.",
            "hold_rapport" => "Send one brief neutral-warm touchpoint without asks or emotional pressure.",
            "check_in" => "Send a concise check-in that acknowledges current pace and asks nothing heavy.",
            "clarify" => "Ask one concrete clarifying question tied to the current uncertainty.",
            "deescalate" => "Acknowledge pressure and explicitly lower expectations for immediate progress.",
            "repair" => "Own one friction point clearly and offer a low-pressure reset.",
            "warm_reply" => "Reply with clear warmth while matching the observed response cadence.",
            "light_test" => "Use one lightweight initiative probe with easy opt-out.",
            "acknowledge_separation" => "Acknowledge the separation calmly and remove hidden expectations for immediate reunion.",
            "test_receptivity" => "Send one low-intensity receptivity probe with explicit permission not to reply quickly.",
            "re_establish_contact" => "Re-establish contact via one respectful opener without pushing for immediate escalation.",
            "invite" => "Offer one concrete low-pressure invite with flexible timing options.",
            "deepen" => "Introduce one deeper topic, but stop after a single opening prompt.",
            "boundaries" => "Set one explicit boundary on pace and emotional intensity, respectfully.",
            _ => "Use one conservative, low-pressure move and reassess outcome signals."
        };

        if (!confidence.HorizonAllowed)
        {
            return (microStep, []);
        }

        var horizon = BuildHorizon(primaryOption.ActionType);
        return (microStep, horizon);
    }

    private static IReadOnlyList<string> BuildHorizon(string actionType)
    {
        return actionType switch
        {
            "clarify" =>
            [
                "Immediate: ask one focused clarification question.",
                "Follow-up: update state interpretation from the answer before stronger action.",
                "Follow-up: choose either hold_rapport or check_in based on reply quality."
            ],
            "invite" =>
            [
                "Immediate: send one concrete low-pressure invite.",
                "Follow-up: if deferred, offer one flexible reschedule option.",
                "Follow-up: if still unclear, switch to clarify/hold_rapport."
            ],
            "repair" =>
            [
                "Immediate: send concise repair acknowledgment.",
                "Follow-up: give space and look for tone softening.",
                "Follow-up: move to gentle check_in once stability returns."
            ],
            "acknowledge_separation" =>
            [
                "Immediate: acknowledge separation and reduce pressure explicitly.",
                "Follow-up: wait for signal of emotional safety before new asks.",
                "Follow-up: switch to test_receptivity only if tone remains stable."
            ],
            "test_receptivity" =>
            [
                "Immediate: send one micro-touchpoint with an easy opt-out.",
                "Follow-up: evaluate response quality, not just response speed.",
                "Follow-up: move to re_establish_contact only after cooperative signal."
            ],
            "re_establish_contact" =>
            [
                "Immediate: send one respectful reconnect opener.",
                "Follow-up: if no reply, do not chase; allow cooling window.",
                "Follow-up: if reply arrives, keep momentum low-pressure for at least one cycle."
            ],
            _ =>
            [
                $"Immediate: execute '{actionType}' as the primary move.",
                "Follow-up: evaluate response signs within 24-72 hours.",
                "Follow-up: continue with the least risky compatible move."
            ]
        };
    }
}

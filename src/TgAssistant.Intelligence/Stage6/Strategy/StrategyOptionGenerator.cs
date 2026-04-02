// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Strategy;

public class StrategyOptionGenerator : IStrategyOptionGenerator
{
    private static readonly string[] ActionTaxonomy =
    [
        "wait",
        "warm_reply",
        "hold_rapport",
        "check_in",
        "light_test",
        "clarify",
        "re_establish_contact",
        "acknowledge_separation",
        "test_receptivity",
        "invite",
        "deepen",
        "repair",
        "boundaries",
        "deescalate"
    ];

    public Task<IReadOnlyList<StrategyCandidateOption>> GenerateAsync(
        StrategyEvaluationContext context,
        CancellationToken ct = default)
    {
        var options = new List<StrategyCandidateOption>();
        var state = context.CurrentState;

        var ambiguity = state?.AmbiguityScore ?? 0.65f;
        var avoidance = state?.AvoidanceRiskScore ?? 0.5f;
        var warmth = state?.WarmthScore ?? 0.5f;
        var readiness = state?.EscalationReadinessScore ?? 0.4f;
        var status = state?.RelationshipStatus ?? "ambiguous";
        var alternativeStatus = state?.AlternativeStatus;
        var breakupAwareState = IsBreakupAwareStatus(status) || IsBreakupAwareStatus(alternativeStatus);

        var hasOpenBlockingClarification = context.ClarificationQuestions.Any(x =>
            x.Status.Equals("open", StringComparison.OrdinalIgnoreCase)
            && x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase));
        var hasOpenConflicts = context.Conflicts.Any(x => x.Status.Equals("open", StringComparison.OrdinalIgnoreCase));

        AddOption(options, "wait", context.SelfStyleHint,
            summary: "Pause briefly and avoid forcing momentum while signals are still settling.",
            purpose: "Prevent overreach under ambiguity and preserve optionality.",
            whenToUse: "Use when ambiguity or delayed responses are high.",
            successSigns: "Response quality improves after space; tone stabilizes.",
            failureSigns: "Distance increases without any re-engagement signal.");

        AddOption(options, "hold_rapport", context.SelfStyleHint,
            summary: "Send a low-pressure rapport-preserving touchpoint.",
            purpose: "Maintain connection without demanding immediate commitment.",
            whenToUse: "Use when contact exists but confidence is moderate.",
            successSigns: "Steady reciprocal tone and shorter reply delays.",
            failureSigns: "Replies stay minimal or become colder.");

        AddOption(options, "check_in", context.SelfStyleHint,
            summary: "Check in with concise warmth and no heavy ask.",
            purpose: "Re-open lightweight interaction channel.",
            whenToUse: "Use when current dynamic is cooling but not detached.",
            successSigns: "Neutral or warm response arrives without defensiveness.",
            failureSigns: "Response avoidance or visible irritation.");

        AddOption(options, "clarify", context.SelfStyleHint,
            summary: "Ask one focused clarification question to reduce uncertainty.",
            purpose: "Improve decision quality before stronger moves.",
            whenToUse: "Use when blocking unknowns remain.",
            successSigns: "New concrete answer reduces ambiguity.",
            failureSigns: "Question ignored or adds friction.");

        AddOption(options, "deescalate", context.SelfStyleHint,
            summary: "Lower emotional/tempo pressure and acknowledge limits.",
            purpose: "Contain risk and avoid escalation loops.",
            whenToUse: "Use when conflict or stress signals are active.",
            successSigns: "Tone softens and defensive language drops.",
            failureSigns: "Silence deepens or conflict re-triggers.");

        if (warmth >= 0.52f && ambiguity <= 0.6f)
        {
            AddOption(options, "warm_reply", context.SelfStyleHint,
                summary: "Reply with clear warmth while matching current pace.",
                purpose: "Strengthen momentum with emotionally safe reciprocity.",
                whenToUse: "Use when warm signals are present and stable.",
                successSigns: "Mutual warmth and follow-up continuity.",
                failureSigns: "Warmth is not reciprocated consistently.");
        }

        if (hasOpenConflicts || avoidance > 0.56f)
        {
            AddOption(options, "repair", context.SelfStyleHint,
                summary: "Use a repair-oriented move that acknowledges friction directly.",
                purpose: "Rebuild trust and reduce defensive stance.",
                whenToUse: "Use when unresolved contradiction/conflict is active.",
                successSigns: "Acknowledgment accepted and tone de-intensifies.",
                failureSigns: "Repair attempt interpreted as pressure or blame.");
        }

        if (breakupAwareState)
        {
            if (status.Equals("post_breakup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(alternativeStatus, "post_breakup", StringComparison.OrdinalIgnoreCase))
            {
                AddOption(options, "acknowledge_separation", context.SelfStyleHint,
                    summary: "Acknowledge separation directly and remove hidden pressure for fast reconciliation.",
                    purpose: "Restore trust baseline and reduce defensive interpretation of contact.",
                    whenToUse: "Use when breakup context is explicit and tone needs dignity-first reset.",
                    successSigns: "Acknowledgment is received without escalation or blame loops.",
                    failureSigns: "Message is read as guilt pressure or emotional bargaining.");
            }

            AddOption(options, "test_receptivity", context.SelfStyleHint,
                summary: "Test receptivity with one ultra-light touchpoint and a clear opt-out.",
                purpose: "Measure openness before any stronger reconnect attempt.",
                whenToUse: "Use when contact quality is uncertain after breakup/no-contact period.",
                successSigns: "Short but cooperative response arrives without defensiveness.",
                failureSigns: "No reply or immediate withdrawal signals.");

            if (status.Equals("no_contact", StringComparison.OrdinalIgnoreCase)
                || string.Equals(alternativeStatus, "no_contact", StringComparison.OrdinalIgnoreCase)
                || readiness >= 0.42f)
            {
                AddOption(options, "re_establish_contact", context.SelfStyleHint,
                    summary: "Re-establish contact with one respectful low-intensity opener and no escalation ask.",
                    purpose: "Re-open the communication channel safely after a distance gap.",
                    whenToUse: "Use when no-contact or prolonged silence is the main blocker.",
                    successSigns: "Channel reopens and response cadence starts recovering.",
                    failureSigns: "Silence persists or recontact is rejected as pressure.");
            }
        }

        if (!breakupAwareState && readiness >= 0.56f && ambiguity <= 0.5f && !hasOpenBlockingClarification)
        {
            AddOption(options, "invite", context.SelfStyleHint,
                summary: "Propose a concrete low-pressure invite with clear timing.",
                purpose: "Convert positive momentum into real interaction.",
                whenToUse: "Use when readiness is high and ambiguity is controlled.",
                successSigns: "Specific acceptance or constructive rescheduling.",
                failureSigns: "Repeated deferral without alternative.");

            AddOption(options, "light_test", context.SelfStyleHint,
                summary: "Run a light test of initiative/availability with minimal emotional load.",
                purpose: "Validate near-term responsiveness before deeper asks.",
                whenToUse: "Use when signals are mixed but mostly positive.",
                successSigns: "Prompt cooperative response.",
                failureSigns: "Non-response or visible retreat.");
        }

        if (!breakupAwareState && readiness >= 0.64f && warmth >= 0.58f && ambiguity <= 0.45f)
        {
            AddOption(options, "deepen", context.SelfStyleHint,
                summary: "Take one step toward deeper relational conversation.",
                purpose: "Increase depth only when stability and reciprocity support it.",
                whenToUse: "Use when recent exchanges are warm and consistent.",
                successSigns: "Mutual openness increases.",
                failureSigns: "Depth attempt causes retreat or silence.");
        }

        if (hasOpenConflicts && ambiguity >= 0.6f)
        {
            AddOption(options, "boundaries", context.SelfStyleHint,
                summary: "Set a clear boundary on pace/intensity while keeping respect.",
                purpose: "Protect stability and avoid repeated escalation cycles.",
                whenToUse: "Use when interaction repeatedly crosses safety comfort.",
                successSigns: "Boundary is respected and volatility decreases.",
                failureSigns: "Boundary ignored or challenged repeatedly.");
        }

        options = options
            .Where(x => ActionTaxonomy.Contains(x.ActionType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<StrategyCandidateOption>>(options);
    }

    private static bool IsBreakupAwareStatus(string? status)
    {
        return status != null
            && (status.Equals("post_breakup", StringComparison.OrdinalIgnoreCase)
                || status.Equals("no_contact", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddOption(
        ICollection<StrategyCandidateOption> options,
        string actionType,
        string styleHint,
        string summary,
        string purpose,
        string whenToUse,
        string successSigns,
        string failureSigns)
    {
        var stylePrefix = styleHint switch
        {
            "brief_guarded" => "Keep it short. ",
            "detailed_expressive" => "Keep warmth explicit but concise. ",
            _ => string.Empty
        };

        options.Add(new StrategyCandidateOption
        {
            ActionType = actionType,
            Summary = stylePrefix + summary,
            Purpose = purpose,
            WhenToUse = whenToUse,
            SuccessSigns = successSigns,
            FailureSigns = failureSigns
        });
    }
}

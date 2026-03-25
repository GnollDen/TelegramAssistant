using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class PatternSynthesisService : IPatternSynthesisService
{
    public Task<IReadOnlyList<ProfilePatternRecord>> BuildPatternsAsync(
        string subjectType,
        string subjectId,
        ProfileEvidenceContext context,
        Period? period,
        CancellationToken ct = default)
    {
        var messages = FilterMessages(context, subjectType, period);
        var answers = FilterAnswers(context, period);
        var events = FilterEvents(context, period);

        var worksConfidence = 0.45f;
        var failsConfidence = 0.45f;
        var worksEvidence = new List<EvidenceRef>();
        var failsEvidence = new List<EvidenceRef>();

        var supportiveMessages = messages
            .Where(x => ContainsAny(x.Text, "thanks", "appreciate", "glad", "can we", "understand"))
            .Take(3)
            .ToList();
        if (supportiveMessages.Count > 0)
        {
            worksConfidence += 0.15f;
            worksEvidence.AddRange(supportiveMessages.Select(x => new EvidenceRef
            {
                Type = "message",
                Id = x.Id.ToString(),
                Note = "supportive_signal"
            }));
        }

        var clearAnswers = answers
            .Where(x => x.AnswerType.Equals("boolean", StringComparison.OrdinalIgnoreCase)
                        || x.AnswerType.Equals("choice", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (clearAnswers.Count > 0)
        {
            worksConfidence += 0.1f;
            worksEvidence.AddRange(clearAnswers.Select(x => new EvidenceRef
            {
                Type = "clarification_answer",
                Id = x.Id.ToString(),
                Note = "clarity_signal"
            }));
        }

        var stressEvents = events
            .Where(x => x.EventType.Contains("stress", StringComparison.OrdinalIgnoreCase)
                        || x.EventType.Contains("pressure", StringComparison.OrdinalIgnoreCase)
                        || x.EventType.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (stressEvents.Count > 0)
        {
            failsConfidence += 0.15f;
            failsEvidence.AddRange(stressEvents.Select(x => new EvidenceRef
            {
                Type = "offline_event",
                Id = x.Id.ToString(),
                Note = x.EventType
            }));
        }

        var distancingMessages = messages
            .Where(x => ContainsAny(x.Text, "later", "busy", "not sure", "cannot", "cant", "maybe"))
            .Take(3)
            .ToList();
        if (distancingMessages.Count > 0)
        {
            failsConfidence += 0.12f;
            failsEvidence.AddRange(distancingMessages.Select(x => new EvidenceRef
            {
                Type = "message",
                Id = x.Id.ToString(),
                Note = "distance_signal"
            }));
        }

        worksConfidence = Math.Clamp(worksConfidence, 0.3f, 0.85f);
        failsConfidence = Math.Clamp(failsConfidence, 0.3f, 0.85f);

        var worksSummary = supportiveMessages.Count > 0
            ? "Explicit appreciation, concise clarification, and low-pressure check-ins tend to improve response quality."
            : "Clear low-pressure messages tend to work when context is explicit.";

        var failsSummary = (stressEvents.Count > 0 || distancingMessages.Count > 0)
            ? "High pressure windows, ambiguous timing, and repeated follow-ups during delay windows tend to fail."
            : "Assumption-heavy interpretation without clarification tends to fail.";

        var participantPatternSummary = BuildParticipantPatternSummary(subjectType, supportiveMessages.Count, distancingMessages.Count, clearAnswers.Count);
        var pairDynamicsSummary = BuildPairDynamicsSummary(subjectType, supportiveMessages.Count, stressEvents.Count, distancingMessages.Count);
        var repeatedInteractionSummary = BuildRepeatedModeSummary(supportiveMessages.Count, distancingMessages.Count, clearAnswers.Count);
        var changesOverTimeSummary = BuildChangeOverTimeSummary(messages, period);

        var records = new List<ProfilePatternRecord>
        {
            new()
            {
                PatternType = "what_works",
                SubjectType = subjectType,
                SubjectId = subjectId,
                PeriodId = period?.Id,
                Summary = worksSummary,
                Confidence = worksConfidence,
                EvidenceRefs = worksEvidence.Take(4).ToList()
            },
            new()
            {
                PatternType = "what_fails",
                SubjectType = subjectType,
                SubjectId = subjectId,
                PeriodId = period?.Id,
                Summary = failsSummary,
                Confidence = failsConfidence,
                EvidenceRefs = failsEvidence.Take(4).ToList()
            },
            new()
            {
                PatternType = "participant_patterns",
                SubjectType = subjectType,
                SubjectId = subjectId,
                PeriodId = period?.Id,
                Summary = participantPatternSummary,
                Confidence = Math.Clamp((worksConfidence + failsConfidence) / 2f, 0.35f, 0.85f),
                EvidenceRefs = worksEvidence.Concat(failsEvidence).Take(4).ToList()
            },
            new()
            {
                PatternType = "pair_dynamics",
                SubjectType = subjectType,
                SubjectId = subjectId,
                PeriodId = period?.Id,
                Summary = pairDynamicsSummary,
                Confidence = Math.Clamp((worksConfidence + failsConfidence) / 2f, 0.35f, 0.85f),
                EvidenceRefs = worksEvidence.Concat(failsEvidence).Take(4).ToList()
            },
            new()
            {
                PatternType = "repeated_interaction_modes",
                SubjectType = subjectType,
                SubjectId = subjectId,
                PeriodId = period?.Id,
                Summary = repeatedInteractionSummary,
                Confidence = Math.Clamp(worksConfidence, 0.35f, 0.85f),
                EvidenceRefs = worksEvidence.Take(4).ToList()
            },
            new()
            {
                PatternType = "changes_over_time",
                SubjectType = subjectType,
                SubjectId = subjectId,
                PeriodId = period?.Id,
                Summary = changesOverTimeSummary,
                Confidence = Math.Clamp((worksConfidence + failsConfidence) / 2f, 0.35f, 0.85f),
                EvidenceRefs = worksEvidence.Concat(failsEvidence).Take(4).ToList()
            }
        };

        return Task.FromResult<IReadOnlyList<ProfilePatternRecord>>(records);
    }

    private static List<Message> FilterMessages(ProfileEvidenceContext context, string subjectType, Period? period)
    {
        var query = context.Messages.AsEnumerable();
        if (period != null)
        {
            query = query.Where(x => x.Timestamp >= period.StartAt && x.Timestamp <= (period.EndAt ?? DateTime.MaxValue));
        }

        if (subjectType.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SenderId == context.SelfSenderId);
        }
        else if (subjectType.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SenderId == context.OtherSenderId);
        }

        return query.OrderByDescending(x => x.Timestamp).Take(250).ToList();
    }

    private static List<ClarificationAnswer> FilterAnswers(ProfileEvidenceContext context, Period? period)
    {
        var query = context.ClarificationAnswers.AsEnumerable();
        if (period != null)
        {
            query = query.Where(x => x.CreatedAt >= period.StartAt && x.CreatedAt <= (period.EndAt ?? DateTime.MaxValue));
        }

        return query.OrderByDescending(x => x.CreatedAt).Take(40).ToList();
    }

    private static List<OfflineEvent> FilterEvents(ProfileEvidenceContext context, Period? period)
    {
        var query = context.OfflineEvents.AsEnumerable();
        if (period != null)
        {
            query = query.Where(x => x.TimestampStart >= period.StartAt && x.TimestampStart <= (period.EndAt ?? DateTime.MaxValue));
        }

        return query.OrderByDescending(x => x.TimestampStart).Take(30).ToList();
    }

    private static bool ContainsAny(string? text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildParticipantPatternSummary(string subjectType, int supportiveCount, int distancingCount, int clearAnswerCount)
    {
        var role = subjectType.Equals("self", StringComparison.OrdinalIgnoreCase)
            ? "Self"
            : subjectType.Equals("other", StringComparison.OrdinalIgnoreCase)
                ? "Other participant"
                : "Pair";

        if (supportiveCount > distancingCount + 1)
        {
            return $"{role}: tends to stabilize contact through low-pressure supportive signals.";
        }

        if (distancingCount > supportiveCount + 1)
        {
            return $"{role}: tends to use distance/latency as a regulation pattern under pressure.";
        }

        return clearAnswerCount > 0
            ? $"{role}: mixed pattern; clarity improves when explicit questions are answered."
            : $"{role}: mixed pattern with limited clarity signals.";
    }

    private static string BuildPairDynamicsSummary(string subjectType, int supportiveCount, int stressCount, int distancingCount)
    {
        if (!subjectType.Equals("pair", StringComparison.OrdinalIgnoreCase))
        {
            return "Pair dynamics summarized from shared interaction context.";
        }

        if (stressCount > 0 && distancingCount > 0)
        {
            return "Pair dynamic oscillates between proximity and withdrawal during stress windows.";
        }

        if (supportiveCount > distancingCount)
        {
            return "Pair dynamic is mostly cooperative with periodic low-intensity distance regulation.";
        }

        return "Pair dynamic remains ambiguous and needs additional clarification input.";
    }

    private static string BuildRepeatedModeSummary(int supportiveCount, int distancingCount, int clearAnswerCount)
    {
        var modes = new List<string>();
        if (supportiveCount > 0)
        {
            modes.Add("supportive check-ins");
        }

        if (distancingCount > 0)
        {
            modes.Add("delay/distance responses");
        }

        if (clearAnswerCount > 0)
        {
            modes.Add("clarification-driven resets");
        }

        return modes.Count == 0
            ? "No repeated interaction mode is confidently established yet."
            : $"Repeated interaction modes: {string.Join(", ", modes)}.";
    }

    private static string BuildChangeOverTimeSummary(IReadOnlyList<Message> messages, Period? period)
    {
        if (messages.Count < 4)
        {
            return period == null
                ? "Change-over-time evidence is sparse."
                : "Period slice has limited message evidence for trend detection.";
        }

        var ordered = messages.OrderBy(x => x.Timestamp).ToList();
        var half = Math.Max(1, ordered.Count / 2);
        var firstHalf = ordered.Take(half).ToList();
        var secondHalf = ordered.Skip(half).ToList();

        var earlySupport = firstHalf.Count(x => ContainsAny(x.Text, "thanks", "appreciate", "glad", "care"));
        var lateSupport = secondHalf.Count(x => ContainsAny(x.Text, "thanks", "appreciate", "glad", "care"));
        if (lateSupport > earlySupport)
        {
            return "Change over time: later window shows higher supportive tone than earlier window.";
        }

        var earlyDistance = firstHalf.Count(x => ContainsAny(x.Text, "later", "busy", "not sure", "can't", "maybe"));
        var lateDistance = secondHalf.Count(x => ContainsAny(x.Text, "later", "busy", "not sure", "can't", "maybe"));
        if (lateDistance > earlyDistance)
        {
            return "Change over time: later window shows higher distancing signals.";
        }

        return "Change over time: no strong directional shift detected across current evidence window.";
    }
}

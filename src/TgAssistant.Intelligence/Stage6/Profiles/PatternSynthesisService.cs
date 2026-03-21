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
}

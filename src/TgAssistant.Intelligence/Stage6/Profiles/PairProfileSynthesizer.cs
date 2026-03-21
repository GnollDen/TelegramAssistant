using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class PairProfileSynthesizer : IPairProfileSynthesizer
{
    private static readonly string[] PairTraitKeys =
    [
        "initiative_balance",
        "contact_rhythm",
        "repair_capacity",
        "distance_recovery",
        "escalation_fit",
        "ambiguity_tolerance_pair",
        "pressure_mismatch",
        "warmth_asymmetry"
    ];

    public Task<IReadOnlyList<ProfileTraitDraft>> SynthesizeAsync(
        ProfileEvidenceContext context,
        Period? period,
        CancellationToken ct = default)
    {
        var messages = FilterMessages(context, period);
        var answers = FilterAnswers(context, period);
        var events = FilterEvents(context, period);
        var evidenceCount = messages.Count + answers.Count + events.Count;

        var selfCount = messages.Count(x => x.SenderId == context.SelfSenderId);
        var otherCount = messages.Count(x => x.SenderId == context.OtherSenderId);
        var total = Math.Max(1, selfCount + otherCount);
        var initiativeDelta = Math.Abs(selfCount - otherCount) / (float)total;

        var medianLag = ComputeMedianLag(messages);
        var conflictSignals = CountSignals(messages, "no", "not now", "stop", "angry", "later");
        var repairSignals = CountSignals(messages, "sorry", "understand", "thanks", "can we talk", "appreciate");
        var ambiguitySignals = CountSignals(messages, "maybe", "not sure", "unclear", "confused");

        var baseConfidence = evidenceCount switch
        {
            >= 30 => 0.8f,
            >= 16 => 0.7f,
            >= 10 => 0.62f,
            _ => 0.48f
        };

        var baseStability = period == null
            ? 0.68f
            : 0.5f;

        if (medianLag > 24)
        {
            baseStability -= 0.1f;
        }

        var traits = new List<ProfileTraitDraft>
        {
            Build(PairTraitKeys[0],
                initiativeDelta < 0.18f ? "balanced" : selfCount > otherCount ? "self_leading" : "other_leading",
                baseConfidence,
                baseStability,
                sensitive: false,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 5)),
            Build(PairTraitKeys[1],
                medianLag switch
                {
                    < 4 => "high_frequency",
                    < 18 => "moderate_frequency",
                    _ => "fragmented_or_slow"
                },
                baseConfidence,
                baseStability - 0.05f,
                sensitive: false,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 5)),
            Build(PairTraitKeys[2],
                repairSignals > conflictSignals ? "repair_capable" : "repair_fragile",
                baseConfidence - 0.04f,
                baseStability - 0.08f,
                sensitive: true,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 6)),
            Build(PairTraitKeys[3],
                medianLag < 24 && repairSignals > 0 ? "recovers_after_distance" : "slow_recovery",
                baseConfidence - 0.03f,
                baseStability - 0.1f,
                sensitive: false,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 5)),
            Build(PairTraitKeys[4],
                ambiguitySignals <= 1 && conflictSignals <= 1 ? "aligned" : "misaligned",
                baseConfidence - 0.06f,
                baseStability - 0.1f,
                sensitive: true,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 6)),
            Build(PairTraitKeys[5],
                ambiguitySignals <= 2 ? "moderate_tolerance" : "low_tolerance",
                baseConfidence - 0.04f,
                baseStability - 0.06f,
                sensitive: false,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 4)),
            Build(PairTraitKeys[6],
                events.Count(x => x.EventType.Contains("stress", StringComparison.OrdinalIgnoreCase) || x.EventType.Contains("pressure", StringComparison.OrdinalIgnoreCase)) >= 2
                    ? "present"
                    : "limited",
                baseConfidence - 0.07f,
                baseStability - 0.12f,
                sensitive: true,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 6)),
            Build(PairTraitKeys[7],
                Math.Abs(CountSignals(messages.Where(x => x.SenderId == context.SelfSenderId), "thanks", "care", "appreciate")
                         - CountSignals(messages.Where(x => x.SenderId == context.OtherSenderId), "thanks", "care", "appreciate")) > 1
                    ? "noticeable"
                    : "low",
                baseConfidence - 0.05f,
                baseStability - 0.1f,
                sensitive: false,
                periodSpecific: period != null,
                evidence: BuildEvidence(messages, answers, events, 4))
        };

        foreach (var sensitive in traits.Where(x => x.IsSensitive))
        {
            if (evidenceCount >= 12)
            {
                sensitive.Confidence = Math.Min(sensitive.Confidence, 0.72f);
                continue;
            }

            sensitive.ValueLabel = "insufficient_evidence";
            sensitive.Confidence = Math.Min(sensitive.Confidence, 0.4f);
            sensitive.Stability = Math.Min(sensitive.Stability, 0.38f);
            sensitive.IsTemporary = true;
        }

        return Task.FromResult<IReadOnlyList<ProfileTraitDraft>>(traits);
    }

    private static List<Message> FilterMessages(ProfileEvidenceContext context, Period? period)
    {
        var query = context.Messages.AsEnumerable();
        if (period != null)
        {
            query = query.Where(x => x.Timestamp >= period.StartAt && x.Timestamp <= (period.EndAt ?? DateTime.MaxValue));
        }

        return query
            .Where(x => x.SenderId == context.SelfSenderId || x.SenderId == context.OtherSenderId)
            .OrderByDescending(x => x.Timestamp)
            .Take(360)
            .ToList();
    }

    private static List<ClarificationAnswer> FilterAnswers(ProfileEvidenceContext context, Period? period)
    {
        var query = context.ClarificationAnswers.AsEnumerable();
        if (period != null)
        {
            query = query.Where(x => x.CreatedAt >= period.StartAt && x.CreatedAt <= (period.EndAt ?? DateTime.MaxValue));
        }

        return query
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .ToList();
    }

    private static List<OfflineEvent> FilterEvents(ProfileEvidenceContext context, Period? period)
    {
        var query = context.OfflineEvents.AsEnumerable();
        if (period != null)
        {
            query = query.Where(x => x.TimestampStart >= period.StartAt && x.TimestampStart <= (period.EndAt ?? DateTime.MaxValue));
        }

        return query
            .OrderByDescending(x => x.TimestampStart)
            .Take(40)
            .ToList();
    }

    private static double ComputeMedianLag(IReadOnlyList<Message> messages)
    {
        var ordered = messages.OrderBy(x => x.Timestamp).ToList();
        if (ordered.Count < 2)
        {
            return 48;
        }

        var lags = new List<double>();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].SenderId == ordered[i - 1].SenderId)
            {
                continue;
            }

            var lag = (ordered[i].Timestamp - ordered[i - 1].Timestamp).TotalHours;
            if (lag >= 0 && lag <= 168)
            {
                lags.Add(lag);
            }
        }

        if (lags.Count == 0)
        {
            return 48;
        }

        lags.Sort();
        return lags[lags.Count / 2];
    }

    private static int CountSignals(IEnumerable<Message> messages, params string[] tokens)
    {
        var count = 0;
        foreach (var message in messages)
        {
            var text = message.Text ?? string.Empty;
            if (tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                count++;
            }
        }

        return count;
    }

    private static ProfileTraitDraft Build(
        string key,
        string value,
        float confidence,
        float stability,
        bool sensitive,
        bool periodSpecific,
        List<EvidenceRef> evidence)
    {
        var clampedStability = Math.Clamp(stability, 0.2f, 0.9f);
        return new ProfileTraitDraft
        {
            TraitKey = key,
            ValueLabel = value,
            Confidence = Math.Clamp(confidence, 0.2f, 0.9f),
            Stability = clampedStability,
            IsSensitive = sensitive,
            IsTemporary = clampedStability < 0.45f,
            IsPeriodSpecific = periodSpecific,
            EvidenceRefs = evidence
        };
    }

    private static List<EvidenceRef> BuildEvidence(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ClarificationAnswer> answers,
        IReadOnlyList<OfflineEvent> events,
        int limit)
    {
        var refs = new List<EvidenceRef>();
        refs.AddRange(messages.Take(limit - 2).Select(x => new EvidenceRef
        {
            Type = "message",
            Id = x.Id.ToString(),
            Note = x.Timestamp.ToString("yyyy-MM-dd")
        }));

        refs.AddRange(answers.Take(1).Select(x => new EvidenceRef
        {
            Type = "clarification_answer",
            Id = x.Id.ToString(),
            Note = x.AnswerType
        }));

        refs.AddRange(events.Take(1).Select(x => new EvidenceRef
        {
            Type = "offline_event",
            Id = x.Id.ToString(),
            Note = x.EventType
        }));

        return refs.Take(limit).ToList();
    }
}

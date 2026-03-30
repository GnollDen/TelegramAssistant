using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class ProfileTraitExtractor : IProfileTraitExtractor
{
    private static readonly string[] WarmthSupportTokens =
    [
        "thanks", "appreciate", "glad", "care", "love",
        "спасибо", "благодар", "рад", "рада", "ценю", "забоч", "люблю", "поддерж"
    ];

    private static readonly string[] DistancingAvoidanceTokens =
    [
        "later", "busy", "not sure", "can't", "maybe", "avoid",
        "позже", "потом", "занят", "занята", "не уверен", "не уверена", "не могу", "не сейчас", "избег"
    ];

    private static readonly string[] RepairTokens =
    [
        "sorry", "understand", "let's fix", "thanks for saying", "can we talk",
        "извини", "прости", "понимаю", "давай обсудим", "давай поговорим", "давай решим", "спасибо что сказал", "спасибо что сказала"
    ];

    private static readonly string[] ConflictTokens =
    [
        "no", "stop", "leave", "angry", "upset", "tired of",
        "нет", "хватит", "оставь", "уйди", "злюсь", "злой", "зла", "расстро", "устал от", "устала от"
    ];

    private static readonly string[] SelfOtherTraitKeys =
    [
        "communication_style",
        "closeness_distance_behavior",
        "conflict_repair_behavior"
    ];

    public Task<IReadOnlyList<ProfileTraitDraft>> ExtractAsync(
        string subjectType,
        string subjectId,
        ProfileEvidenceContext context,
        Period? period,
        CancellationToken ct = default)
    {
        var inScopeMessages = FilterMessages(context, subjectType, period);
        var inScopeAnswers = FilterAnswers(context, period);
        var inScopeEvents = FilterEvents(context, period);

        var evidenceCount = inScopeMessages.Count + inScopeAnswers.Count + inScopeEvents.Count;
        var hasPeriod = period != null;

        var responseLagHours = CalculateResponseLagHours(inScopeMessages, context.SelfSenderId, context.OtherSenderId);
        var avgLength = inScopeMessages.Count == 0
            ? 0
            : (float)inScopeMessages.Average(m => (m.Text ?? string.Empty).Length);

        var warmthSignals = CountSignals(inScopeMessages, WarmthSupportTokens);
        var distancingSignals = CountSignals(inScopeMessages, DistancingAvoidanceTokens);
        var repairSignals = CountSignals(inScopeMessages, RepairTokens);
        var conflictSignals = CountSignals(inScopeMessages, ConflictTokens);

        var communicationStyle = avgLength switch
        {
            < 25 => "brief_guarded",
            < 80 => "balanced_pragmatic",
            _ => "detailed_expressive"
        };

        if (responseLagHours > 18)
        {
            communicationStyle = "irregular_latency";
        }

        var closenessDistanceBehavior = warmthSignals >= distancingSignals + 2
            ? "closeness_leaning"
            : distancingSignals >= warmthSignals + 2
                ? "distance_protective"
                : "mixed_distance_management";

        var conflictRepairBehavior = repairSignals > conflictSignals
            ? "repair_oriented"
            : conflictSignals > repairSignals + 1
                ? "conflict_avoidant_or_defensive"
                : "low_signal_repair_pattern";

        var ambiguity = EstimateAmbiguity(context, period);
        var baseConfidence = BaseConfidence(evidenceCount, hasPeriod, ambiguity);
        var baseStability = BaseStability(evidenceCount, hasPeriod, ambiguity);

        var drafts = new List<ProfileTraitDraft>
        {
            BuildDraft(
                SelfOtherTraitKeys[0],
                communicationStyle,
                baseConfidence,
                baseStability,
                isSensitive: false,
                hasPeriod,
                evidence: BuildEvidence(inScopeMessages, inScopeAnswers, inScopeEvents, 4)),
            BuildDraft(
                SelfOtherTraitKeys[1],
                closenessDistanceBehavior,
                baseConfidence,
                Math.Clamp(baseStability - 0.05f, 0f, 1f),
                isSensitive: false,
                hasPeriod,
                evidence: BuildEvidence(inScopeMessages, inScopeAnswers, inScopeEvents, 4)),
            BuildDraft(
                SelfOtherTraitKeys[2],
                conflictRepairBehavior,
                Math.Clamp(baseConfidence - 0.05f, 0f, 1f),
                Math.Clamp(baseStability - 0.1f, 0f, 1f),
                isSensitive: true,
                hasPeriod,
                evidence: BuildEvidence(inScopeMessages, inScopeAnswers, inScopeEvents, 6))
        };

        ApplySensitiveGuardrails(drafts, evidenceCount);
        AddCustomTraitIfNeeded(drafts, subjectType, inScopeEvents, baseConfidence, baseStability, hasPeriod);

        return Task.FromResult<IReadOnlyList<ProfileTraitDraft>>(drafts);
    }

    private static List<Message> FilterMessages(ProfileEvidenceContext context, string subjectType, Period? period)
    {
        var from = period?.StartAt;
        var to = period?.EndAt;

        var query = context.Messages.AsEnumerable();
        if (from.HasValue)
        {
            query = query.Where(x => x.Timestamp >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.Timestamp <= to.Value);
        }

        if (subjectType.Equals("self", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SenderId == context.SelfSenderId);
        }
        else if (subjectType.Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(x => x.SenderId == context.OtherSenderId);
        }

        return query
            .OrderByDescending(x => x.Timestamp)
            .Take(300)
            .ToList();
    }

    private static List<ClarificationAnswer> FilterAnswers(ProfileEvidenceContext context, Period? period)
    {
        if (period == null)
        {
            return context.ClarificationAnswers
                .OrderByDescending(x => x.CreatedAt)
                .Take(80)
                .ToList();
        }

        return context.ClarificationAnswers
            .Where(x => x.CreatedAt >= period.StartAt && x.CreatedAt <= (period.EndAt ?? DateTime.MaxValue))
            .OrderByDescending(x => x.CreatedAt)
            .Take(40)
            .ToList();
    }

    private static List<OfflineEvent> FilterEvents(ProfileEvidenceContext context, Period? period)
    {
        if (period == null)
        {
            return context.OfflineEvents
                .OrderByDescending(x => x.TimestampStart)
                .Take(40)
                .ToList();
        }

        return context.OfflineEvents
            .Where(x => x.TimestampStart >= period.StartAt && x.TimestampStart <= (period.EndAt ?? DateTime.MaxValue))
            .OrderByDescending(x => x.TimestampStart)
            .Take(25)
            .ToList();
    }

    private static float CalculateResponseLagHours(IReadOnlyList<Message> messages, long selfSenderId, long otherSenderId)
    {
        var ordered = messages.OrderBy(x => x.Timestamp).ToList();
        var lags = new List<double>();

        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var cur = ordered[i];
            if (prev.SenderId == cur.SenderId)
            {
                continue;
            }

            if ((prev.SenderId != selfSenderId && prev.SenderId != otherSenderId)
                || (cur.SenderId != selfSenderId && cur.SenderId != otherSenderId))
            {
                continue;
            }

            var delta = (cur.Timestamp - prev.Timestamp).TotalHours;
            if (delta >= 0 && delta <= 120)
            {
                lags.Add(delta);
            }
        }

        return lags.Count == 0 ? 24f : (float)lags.Average();
    }

    private static int CountSignals(IEnumerable<Message> messages, params string[] needles)
    {
        var count = 0;
        foreach (var message in messages)
        {
            var text = message.Text ?? string.Empty;
            if (needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            {
                count++;
            }
        }

        return count;
    }

    private static float EstimateAmbiguity(ProfileEvidenceContext context, Period? period)
    {
        var snapshots = period == null
            ? context.StateSnapshots
            : context.StateSnapshots.Where(x => x.PeriodId == period.Id).ToList();

        if (snapshots.Count == 0)
        {
            return 0.5f;
        }

        return snapshots.Average(x => x.AmbiguityScore);
    }

    private static float BaseConfidence(int evidenceCount, bool periodSpecific, float ambiguity)
    {
        var baseConfidence = evidenceCount switch
        {
            >= 24 => 0.82f,
            >= 14 => 0.72f,
            >= 8 => 0.62f,
            _ => 0.5f
        };

        if (periodSpecific)
        {
            baseConfidence -= 0.05f;
        }

        baseConfidence -= ambiguity * 0.15f;
        return Math.Clamp(baseConfidence, 0.25f, 0.9f);
    }

    private static float BaseStability(int evidenceCount, bool periodSpecific, float ambiguity)
    {
        var stability = evidenceCount switch
        {
            >= 24 => 0.78f,
            >= 14 => 0.66f,
            >= 8 => 0.54f,
            _ => 0.4f
        };

        if (periodSpecific)
        {
            stability -= 0.2f;
        }

        stability -= ambiguity * 0.1f;
        return Math.Clamp(stability, 0.2f, 0.9f);
    }

    private static ProfileTraitDraft BuildDraft(
        string key,
        string value,
        float confidence,
        float stability,
        bool isSensitive,
        bool periodSpecific,
        List<EvidenceRef> evidence)
    {
        return new ProfileTraitDraft
        {
            TraitKey = key,
            ValueLabel = value,
            Confidence = Math.Clamp(confidence, 0f, 1f),
            Stability = Math.Clamp(stability, 0f, 1f),
            IsSensitive = isSensitive,
            IsTemporary = stability < 0.45f,
            IsPeriodSpecific = periodSpecific,
            EvidenceRefs = evidence
        };
    }

    private static void ApplySensitiveGuardrails(List<ProfileTraitDraft> traits, int evidenceCount)
    {
        foreach (var trait in traits.Where(x => x.IsSensitive))
        {
            if (evidenceCount >= 10)
            {
                trait.Confidence = Math.Min(trait.Confidence, 0.72f);
                continue;
            }

            trait.ValueLabel = "insufficient_evidence";
            trait.Confidence = Math.Min(trait.Confidence, 0.42f);
            trait.Stability = Math.Min(trait.Stability, 0.38f);
            trait.IsTemporary = true;
        }
    }

    private static void AddCustomTraitIfNeeded(
        ICollection<ProfileTraitDraft> traits,
        string subjectType,
        IReadOnlyList<OfflineEvent> events,
        float baseConfidence,
        float baseStability,
        bool periodSpecific)
    {
        var highPressureEvents = events.Count(x =>
            x.EventType.Contains("pressure", StringComparison.OrdinalIgnoreCase)
            || x.EventType.Contains("stress", StringComparison.OrdinalIgnoreCase)
            || x.Title.Contains("pressure", StringComparison.OrdinalIgnoreCase));

        if (highPressureEvents < 2)
        {
            return;
        }

        traits.Add(new ProfileTraitDraft
        {
            TraitKey = subjectType.Equals("self", StringComparison.OrdinalIgnoreCase)
                ? "custom_self_pressure_sensitivity"
                : "custom_other_pressure_sensitivity",
            ValueLabel = "elevated_under_external_pressure",
            Confidence = Math.Clamp(baseConfidence - 0.08f, 0f, 1f),
            Stability = Math.Clamp(baseStability - 0.12f, 0f, 1f),
            IsSensitive = true,
            IsTemporary = true,
            IsPeriodSpecific = periodSpecific,
            EvidenceRefs = events
                .OrderByDescending(x => x.TimestampStart)
                .Take(3)
                .Select(x => new EvidenceRef
                {
                    Type = "offline_event",
                    Id = x.Id.ToString(),
                    Note = x.EventType
                })
                .ToList()
        });
    }

    private static List<EvidenceRef> BuildEvidence(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ClarificationAnswer> answers,
        IReadOnlyList<OfflineEvent> events,
        int limit)
    {
        var refs = new List<EvidenceRef>();

        refs.AddRange(messages
            .OrderByDescending(x => x.Timestamp)
            .Take(Math.Max(1, limit / 2))
            .Select(x => new EvidenceRef
            {
                Type = "message",
                Id = x.Id.ToString(),
                Note = x.Timestamp.ToString("yyyy-MM-dd")
            }));

        refs.AddRange(answers
            .OrderByDescending(x => x.CreatedAt)
            .Take(1)
            .Select(x => new EvidenceRef
            {
                Type = "clarification_answer",
                Id = x.Id.ToString(),
                Note = x.AnswerType
            }));

        refs.AddRange(events
            .OrderByDescending(x => x.TimestampStart)
            .Take(1)
            .Select(x => new EvidenceRef
            {
                Type = "offline_event",
                Id = x.Id.ToString(),
                Note = x.EventType
            }));

        return refs.Take(limit).ToList();
    }
}

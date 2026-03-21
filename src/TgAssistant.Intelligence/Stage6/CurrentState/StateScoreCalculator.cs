using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.CurrentState;

public class StateScoreCalculator : IStateScoreCalculator
{
    public Task<StateScoreResult> CalculateAsync(CurrentStateContext context, CancellationToken ct = default)
    {
        var now = context.AsOfUtc;
        var periodStart = context.CurrentPeriod?.StartAt ?? now.AddDays(-21);
        var periodEnd = context.CurrentPeriod?.EndAt ?? now;
        if (periodEnd < periodStart)
        {
            periodEnd = now;
        }

        var periodMessages = context.RecentMessages
            .Where(x => x.Timestamp >= periodStart && x.Timestamp <= periodEnd)
            .OrderBy(x => x.Timestamp)
            .ToList();
        var recencyMessages = context.RecentMessages
            .Where(x => x.Timestamp >= now.AddDays(-14))
            .OrderBy(x => x.Timestamp)
            .ToList();

        var scopeMessages = periodMessages.Count >= 8 ? periodMessages : recencyMessages;
        if (scopeMessages.Count == 0)
        {
            scopeMessages = context.RecentMessages.OrderByDescending(x => x.Timestamp).Take(30).OrderBy(x => x.Timestamp).ToList();
        }

        var openQuestions = context.ClarificationQuestions.Count(x => IsOpenState(x.Status));
        var blockingQuestions = context.ClarificationQuestions.Count(x => IsOpenState(x.Status) && x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase));
        var unresolvedPeriods = context.Periods.Count(x => x.ReviewPriority >= 4 || x.BoundaryConfidence < 0.5f || x.InterpretationConfidence < 0.5f);

        var initiativeCurrent = ComputeInitiative(scopeMessages);
        var responsivenessCurrent = ComputeResponsiveness(scopeMessages);
        var opennessCurrent = ComputeOpenness(scopeMessages, context.ClarificationAnswers.Count);
        var warmthCurrent = ComputeWarmth(scopeMessages, context.ClarificationAnswers);
        var reciprocityCurrent = ComputeReciprocity(scopeMessages);
        var ambiguityCurrent = Clamp01(
            0.18f
            + openQuestions * 0.07f
            + blockingQuestions * 0.08f
            + unresolvedPeriods * 0.06f
            + (context.Conflicts.Count > 0 ? 0.12f : 0f)
            + (scopeMessages.Count <= 4 ? 0.08f : 0f));
        var avoidanceCurrent = ComputeAvoidanceRisk(scopeMessages, now, blockingQuestions, openQuestions);
        var escalationCurrent = ComputeEscalationReadiness(opennessCurrent, warmthCurrent, responsivenessCurrent, ambiguityCurrent, avoidanceCurrent);
        var externalPressureCurrent = ComputeExternalPressure(context.OfflineEvents, context.Conflicts, periodStart, periodEnd);

        var history = context.HistoricalSnapshots.OrderByDescending(x => x.AsOf).Take(6).ToList();
        var hasHistory = history.Count >= 2;
        var historyWeight = 0f;
        var historyConflict = false;

        if (hasHistory)
        {
            var avgWarmth = history.Average(x => x.WarmthScore);
            var avgResponsiveness = history.Average(x => x.ResponsivenessScore);
            var avgAmbiguity = history.Average(x => x.AmbiguityScore);
            var deltaWarmth = Math.Abs(avgWarmth - warmthCurrent);
            var deltaResponsiveness = Math.Abs(avgResponsiveness - responsivenessCurrent);
            var deltaAmbiguity = Math.Abs(avgAmbiguity - ambiguityCurrent);
            var divergence = (deltaWarmth + deltaResponsiveness + deltaAmbiguity) / 3f;

            historyConflict = divergence >= 0.34f;
            var similarEnough = divergence <= 0.26f;
            historyWeight = similarEnough ? 0.22f : 0.10f;

            initiativeCurrent = Modulate(initiativeCurrent, (float)history.Average(x => x.InitiativeScore), historyWeight);
            responsivenessCurrent = Modulate(responsivenessCurrent, (float)history.Average(x => x.ResponsivenessScore), historyWeight);
            opennessCurrent = Modulate(opennessCurrent, (float)history.Average(x => x.OpennessScore), historyWeight);
            warmthCurrent = Modulate(warmthCurrent, avgWarmth, historyWeight);
            reciprocityCurrent = Modulate(reciprocityCurrent, (float)history.Average(x => x.ReciprocityScore), historyWeight);
            ambiguityCurrent = Modulate(ambiguityCurrent, avgAmbiguity, historyWeight * 0.7f);
            avoidanceCurrent = Modulate(avoidanceCurrent, (float)history.Average(x => x.AvoidanceRiskScore), historyWeight);
            escalationCurrent = Modulate(escalationCurrent, (float)history.Average(x => x.EscalationReadinessScore), historyWeight);
            externalPressureCurrent = Modulate(externalPressureCurrent, (float)history.Average(x => x.ExternalPressureScore), historyWeight * 0.8f);

            if (historyConflict)
            {
                ambiguityCurrent = Clamp01(ambiguityCurrent + 0.12f);
                avoidanceCurrent = Clamp01(avoidanceCurrent + 0.08f);
            }
        }

        var signalRefs = BuildSignalRefs(scopeMessages, context, periodStart, periodEnd);
        var riskRefs = BuildRiskRefs(context, blockingQuestions, historyConflict);

        var result = new StateScoreResult
        {
            Initiative = initiativeCurrent,
            Responsiveness = responsivenessCurrent,
            Openness = opennessCurrent,
            Warmth = warmthCurrent,
            Reciprocity = reciprocityCurrent,
            Ambiguity = ambiguityCurrent,
            AvoidanceRisk = avoidanceCurrent,
            EscalationReadiness = escalationCurrent,
            ExternalPressure = externalPressureCurrent,
            HistoricalModulationWeight = historyWeight,
            HistoryConflictDetected = historyConflict,
            SignalRefs = signalRefs,
            RiskRefs = riskRefs
        };

        return Task.FromResult(result);
    }

    private static float ComputeInitiative(IReadOnlyList<Message> messages)
    {
        if (messages.Count < 2)
        {
            return 0.45f;
        }

        var grouped = messages.GroupBy(x => x.SenderId).Select(g => g.Count()).OrderByDescending(x => x).ToList();
        if (grouped.Count == 1)
        {
            return 0.22f;
        }

        var majorRatio = grouped[0] / (float)messages.Count;
        var balance = 1f - Math.Abs(0.5f - majorRatio) * 2f;
        return Clamp01(0.2f + balance * 0.8f);
    }

    private static float ComputeResponsiveness(IReadOnlyList<Message> messages)
    {
        if (messages.Count < 2)
        {
            return 0.35f;
        }

        var gaps = new List<double>();
        for (var i = 1; i < messages.Count; i++)
        {
            if (messages[i].SenderId == messages[i - 1].SenderId)
            {
                continue;
            }

            var gapHours = (messages[i].Timestamp - messages[i - 1].Timestamp).TotalHours;
            if (gapHours >= 0)
            {
                gaps.Add(gapHours);
            }
        }

        if (gaps.Count == 0)
        {
            return 0.4f;
        }

        gaps.Sort();
        var median = gaps[gaps.Count / 2];
        if (median <= 1)
        {
            return 0.9f;
        }

        if (median <= 4)
        {
            return 0.74f;
        }

        if (median <= 12)
        {
            return 0.56f;
        }

        if (median <= 36)
        {
            return 0.38f;
        }

        return 0.22f;
    }

    private static float ComputeOpenness(IReadOnlyList<Message> messages, int clarificationAnswerCount)
    {
        if (messages.Count == 0)
        {
            return 0.3f;
        }

        var avgLength = messages.Average(x => (x.Text ?? string.Empty).Length);
        var opennessTerms = messages.Count(x => ContainsAny(x.Text, "feel", "felt", "think", "want", "need", "miss", "honest", "share"));
        var baseScore = (float)Math.Clamp(avgLength / 180.0, 0.05, 0.7);
        var lexical = opennessTerms / (float)messages.Count;
        var clarificationLift = Math.Min(clarificationAnswerCount, 4) * 0.03f;
        return Clamp01(0.2f + baseScore * 0.5f + lexical * 0.3f + clarificationLift);
    }

    private static float ComputeWarmth(IReadOnlyList<Message> messages, IReadOnlyList<ClarificationAnswer> answers)
    {
        if (messages.Count == 0)
        {
            return 0.35f;
        }

        var positive = messages.Count(x => ContainsAny(x.Text, "thanks", "thank", "glad", "good", "great", "care", "appreciate", "happy"));
        var negative = messages.Count(x => ContainsAny(x.Text, "busy", "later", "stop", "no", "cannot", "can't", "angry", "upset"));
        var stance = (positive - negative) / (float)Math.Max(1, messages.Count);
        var answerWarm = answers.Count(x => x.AnswerValue.Equals("yes", StringComparison.OrdinalIgnoreCase) || x.AnswerValue.Contains("closer", StringComparison.OrdinalIgnoreCase));
        return Clamp01(0.45f + stance * 0.75f + answerWarm * 0.03f);
    }

    private static float ComputeReciprocity(IReadOnlyList<Message> messages)
    {
        if (messages.Count < 2)
        {
            return 0.3f;
        }

        var bySender = messages.GroupBy(x => x.SenderId).Select(g => g.Count()).OrderByDescending(x => x).ToList();
        if (bySender.Count < 2)
        {
            return 0.25f;
        }

        var dominance = bySender[0] / (float)Math.Max(1, bySender[1]);
        var balanceScore = dominance <= 1.2f
            ? 0.92f
            : dominance <= 1.8f
                ? 0.74f
                : dominance <= 2.5f
                    ? 0.52f
                    : 0.3f;

        var turnSwitches = 0;
        for (var i = 1; i < messages.Count; i++)
        {
            if (messages[i].SenderId != messages[i - 1].SenderId)
            {
                turnSwitches++;
            }
        }

        var switchScore = turnSwitches / (float)Math.Max(1, messages.Count - 1);
        return Clamp01(balanceScore * 0.65f + switchScore * 0.35f);
    }

    private static float ComputeAvoidanceRisk(
        IReadOnlyList<Message> messages,
        DateTime now,
        int blockingQuestions,
        int openQuestions)
    {
        if (messages.Count == 0)
        {
            return 0.8f;
        }

        var latestAt = messages.Max(x => x.Timestamp);
        var gapDays = Math.Max(0, (now - latestAt).TotalDays);
        var lexicalAvoidance = messages.Count(x => ContainsAny(x.Text, "later", "busy", "not now", "can't talk", "avoid", "skip"));

        var gapRisk = gapDays >= 10 ? 0.75f : gapDays >= 5 ? 0.5f : gapDays >= 2 ? 0.3f : 0.15f;
        var unresolvedRisk = Math.Min(0.24f, blockingQuestions * 0.08f + openQuestions * 0.03f);
        var lexicalRisk = Math.Min(0.22f, lexicalAvoidance * 0.04f);

        return Clamp01(0.1f + gapRisk + unresolvedRisk + lexicalRisk);
    }

    private static float ComputeEscalationReadiness(
        float openness,
        float warmth,
        float responsiveness,
        float ambiguity,
        float avoidance)
    {
        var positive = openness * 0.28f + warmth * 0.36f + responsiveness * 0.24f;
        var inhibitors = ambiguity * 0.28f + avoidance * 0.3f;
        return Clamp01(0.18f + positive - inhibitors);
    }

    private static float ComputeExternalPressure(
        IReadOnlyList<OfflineEvent> events,
        IReadOnlyList<ConflictRecord> conflicts,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var scopedEvents = events
            .Where(x => x.TimestampStart >= periodStart.AddDays(-14) && x.TimestampStart <= periodEnd)
            .ToList();

        if (scopedEvents.Count == 0 && conflicts.Count == 0)
        {
            return 0.2f;
        }

        var pressureEvents = scopedEvents.Count(x =>
            x.EventType.Contains("work", StringComparison.OrdinalIgnoreCase)
            || x.EventType.Contains("family", StringComparison.OrdinalIgnoreCase)
            || x.EventType.Contains("travel", StringComparison.OrdinalIgnoreCase)
            || x.EventType.Contains("health", StringComparison.OrdinalIgnoreCase)
            || x.EventType.Contains("stress", StringComparison.OrdinalIgnoreCase));

        var pressure = 0.2f + pressureEvents * 0.12f + conflicts.Count * 0.08f;
        return Clamp01(pressure);
    }

    private static List<string> BuildSignalRefs(
        IReadOnlyList<Message> scopeMessages,
        CurrentStateContext context,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var refs = new List<string>();

        foreach (var msg in scopeMessages.TakeLast(6))
        {
            refs.Add($"message:{msg.Id}");
        }

        foreach (var session in context.RecentSessions
                     .Where(x => x.EndDate >= periodStart && x.StartDate <= periodEnd)
                     .OrderByDescending(x => x.EndDate)
                     .Take(3))
        {
            refs.Add($"chat_session:{session.Id}");
        }

        foreach (var answer in context.ClarificationAnswers.OrderByDescending(x => x.CreatedAt).Take(3))
        {
            refs.Add($"clarification_answer:{answer.Id}");
        }

        foreach (var evt in context.OfflineEvents.OrderByDescending(x => x.TimestampStart).Take(2))
        {
            refs.Add($"offline_event:{evt.Id}");
        }

        if (context.CurrentPeriod is not null)
        {
            refs.Add($"period:{context.CurrentPeriod.Id}");
        }

        return refs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> BuildRiskRefs(CurrentStateContext context, int blockingQuestions, bool historyConflict)
    {
        var refs = new List<string>();

        foreach (var conflict in context.Conflicts.Where(x => IsOpenState(x.Status)).Take(4))
        {
            refs.Add($"conflict:{conflict.Id}");
        }

        foreach (var question in context.ClarificationQuestions
                     .Where(x => IsOpenState(x.Status) && x.Priority.Equals("blocking", StringComparison.OrdinalIgnoreCase))
                     .Take(4))
        {
            refs.Add($"clarification_question:{question.Id}");
        }

        if (blockingQuestions > 0)
        {
            refs.Add("risk:blocking_clarifications");
        }

        if (historyConflict)
        {
            refs.Add("risk:history_conflict");
        }

        return refs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsOpenState(string status)
    {
        return status.Equals("open", StringComparison.OrdinalIgnoreCase)
            || status.Equals("in_progress", StringComparison.OrdinalIgnoreCase)
            || status.Equals("answered", StringComparison.OrdinalIgnoreCase);
    }

    private static float Modulate(float current, float historical, float weight)
    {
        weight = Clamp01(weight);
        return Clamp01(current * (1f - weight) + historical * weight);
    }

    private static float Clamp01(float value)
    {
        return Math.Clamp(value, 0f, 1f);
    }

    private static bool ContainsAny(string? text, params string[] tokens)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

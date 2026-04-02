// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Profiles;

public class PatternSynthesisService : IPatternSynthesisService
{
    private static readonly string[] SupportiveTokens =
    [
        "thanks", "appreciate", "glad", "can we", "understand",
        "спасибо", "благодар", "рад", "рада", "давай обсудим", "давай поговорим", "понимаю"
    ];

    private static readonly string[] DistancingTokens =
    [
        "later", "busy", "not sure", "cannot", "cant", "maybe",
        "позже", "потом", "занят", "занята", "не уверен", "не уверена", "не сейчас", "не могу"
    ];

    private static readonly string[] StressEventTokens =
    [
        "stress", "pressure", "conflict",
        "стресс", "давление", "конфликт"
    ];

    private static readonly string[] SupportiveToneTokens =
    [
        "thanks", "appreciate", "glad", "care",
        "спасибо", "благодар", "рад", "рада", "забоч", "ценю"
    ];

    private static readonly string[] DistancingToneTokens =
    [
        "later", "busy", "not sure", "can't", "maybe",
        "позже", "потом", "занят", "занята", "не уверен", "не уверена", "не могу", "не сейчас"
    ];

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
        var preferRu = PreferRussianOutput(messages);

        var supportiveMessages = messages
            .Where(x => ContainsAny(x.Text, SupportiveTokens))
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
            .Where(x => ContainsAny(x.EventType, StressEventTokens))
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
            .Where(x => ContainsAny(x.Text, DistancingTokens))
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
            ? preferRu
                ? "Явная благодарность, короткие уточнения и бережные сообщения обычно улучшают качество ответа."
                : "Explicit appreciation, concise clarification, and low-pressure check-ins tend to improve response quality."
            : preferRu
                ? "Лучше работают ясные сообщения без давления, когда контекст проговорен явно."
                : "Clear low-pressure messages tend to work when context is explicit.";

        var failsSummary = (stressEvents.Count > 0 || distancingMessages.Count > 0)
            ? preferRu
                ? "Окна высокого давления, расплывчатые сроки и частые пинги в период паузы обычно ухудшают контакт."
                : "High pressure windows, ambiguous timing, and repeated follow-ups during delay windows tend to fail."
            : preferRu
                ? "Интерпретации на предположениях без уточнений чаще приводят к сбою."
                : "Assumption-heavy interpretation without clarification tends to fail.";

        var participantPatternSummary = BuildParticipantPatternSummary(subjectType, supportiveMessages.Count, distancingMessages.Count, clearAnswers.Count, preferRu);
        var pairDynamicsSummary = BuildPairDynamicsSummary(subjectType, supportiveMessages.Count, stressEvents.Count, distancingMessages.Count, preferRu);
        var repeatedInteractionSummary = BuildRepeatedModeSummary(supportiveMessages.Count, distancingMessages.Count, clearAnswers.Count, preferRu);
        var changesOverTimeSummary = BuildChangeOverTimeSummary(messages, period, preferRu);

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

    private static bool PreferRussianOutput(IReadOnlyList<Message> messages)
    {
        var nonEmpty = messages
            .Select(x => x.Text)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Take(20)
            .ToList();

        if (nonEmpty.Count == 0)
        {
            return false;
        }

        var ruSignals = nonEmpty.Count(ContainsCyrillic);
        return ruSignals >= Math.Max(2, nonEmpty.Count / 3);
    }

    private static bool ContainsCyrillic(string text)
    {
        foreach (var c in text)
        {
            if (c is >= '\u0400' and <= '\u04FF')
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildParticipantPatternSummary(string subjectType, int supportiveCount, int distancingCount, int clearAnswerCount, bool preferRu)
    {
        var role = preferRu
            ? subjectType.Equals("self", StringComparison.OrdinalIgnoreCase)
                ? "Собственный стиль"
                : subjectType.Equals("other", StringComparison.OrdinalIgnoreCase)
                    ? "Другой участник"
                    : "Пара"
            : subjectType.Equals("self", StringComparison.OrdinalIgnoreCase)
                ? "Self"
                : subjectType.Equals("other", StringComparison.OrdinalIgnoreCase)
                    ? "Other participant"
                    : "Pair";

        if (supportiveCount > distancingCount + 1)
        {
            return preferRu
                ? $"{role}: чаще стабилизирует контакт через бережные поддерживающие сигналы."
                : $"{role}: tends to stabilize contact through low-pressure supportive signals.";
        }

        if (distancingCount > supportiveCount + 1)
        {
            return preferRu
                ? $"{role}: чаще использует дистанцию и задержку как способ саморегуляции под давлением."
                : $"{role}: tends to use distance/latency as a regulation pattern under pressure.";
        }

        return clearAnswerCount > 0
            ? preferRu
                ? $"{role}: смешанный паттерн; ясность растет, когда есть прямые ответы на вопросы."
                : $"{role}: mixed pattern; clarity improves when explicit questions are answered."
            : preferRu
                ? $"{role}: смешанный паттерн при ограниченных сигналах ясности."
                : $"{role}: mixed pattern with limited clarity signals.";
    }

    private static string BuildPairDynamicsSummary(string subjectType, int supportiveCount, int stressCount, int distancingCount, bool preferRu)
    {
        if (!subjectType.Equals("pair", StringComparison.OrdinalIgnoreCase))
        {
            return preferRu
                ? "Динамика пары сформирована из общего контекста взаимодействия."
                : "Pair dynamics summarized from shared interaction context.";
        }

        if (stressCount > 0 && distancingCount > 0)
        {
            return preferRu
                ? "Динамика пары колеблется между сближением и отдалением в стрессовых окнах."
                : "Pair dynamic oscillates between proximity and withdrawal during stress windows.";
        }

        if (supportiveCount > distancingCount)
        {
            return preferRu
                ? "Динамика пары в основном кооперативная, с периодическими мягкими паузами дистанции."
                : "Pair dynamic is mostly cooperative with periodic low-intensity distance regulation.";
        }

        return preferRu
            ? "Динамика пары остается неоднозначной и требует дополнительных уточнений."
            : "Pair dynamic remains ambiguous and needs additional clarification input.";
    }

    private static string BuildRepeatedModeSummary(int supportiveCount, int distancingCount, int clearAnswerCount, bool preferRu)
    {
        var modes = new List<string>();
        if (supportiveCount > 0)
        {
            modes.Add(preferRu ? "поддерживающие касания" : "supportive check-ins");
        }

        if (distancingCount > 0)
        {
            modes.Add(preferRu ? "ответы с паузой/дистанцией" : "delay/distance responses");
        }

        if (clearAnswerCount > 0)
        {
            modes.Add(preferRu ? "перезапуски через уточнение" : "clarification-driven resets");
        }

        return modes.Count == 0
            ? preferRu
                ? "Пока нет уверенно подтвержденного повторяющегося режима взаимодействия."
                : "No repeated interaction mode is confidently established yet."
            : preferRu
                ? $"Повторяющиеся режимы взаимодействия: {string.Join(", ", modes)}."
                : $"Repeated interaction modes: {string.Join(", ", modes)}.";
    }

    private static string BuildChangeOverTimeSummary(IReadOnlyList<Message> messages, Period? period, bool preferRu)
    {
        if (messages.Count < 4)
        {
            return period == null
                ? preferRu
                    ? "Данных по изменению во времени пока недостаточно."
                    : "Change-over-time evidence is sparse."
                : preferRu
                    ? "В срезе периода мало сообщений для уверенного тренда."
                    : "Period slice has limited message evidence for trend detection.";
        }

        var ordered = messages.OrderBy(x => x.Timestamp).ToList();
        var half = Math.Max(1, ordered.Count / 2);
        var firstHalf = ordered.Take(half).ToList();
        var secondHalf = ordered.Skip(half).ToList();

        var earlySupport = firstHalf.Count(x => ContainsAny(x.Text, SupportiveToneTokens));
        var lateSupport = secondHalf.Count(x => ContainsAny(x.Text, SupportiveToneTokens));
        if (lateSupport > earlySupport)
        {
            return preferRu
                ? "Изменение во времени: в более позднем окне больше поддерживающего тона, чем раньше."
                : "Change over time: later window shows higher supportive tone than earlier window.";
        }

        var earlyDistance = firstHalf.Count(x => ContainsAny(x.Text, DistancingToneTokens));
        var lateDistance = secondHalf.Count(x => ContainsAny(x.Text, DistancingToneTokens));
        if (lateDistance > earlyDistance)
        {
            return preferRu
                ? "Изменение во времени: в более позднем окне больше сигналов дистанцирования."
                : "Change over time: later window shows higher distancing signals.";
        }

        return preferRu
            ? "Изменение во времени: в текущем окне нет сильного направленного сдвига."
            : "Change over time: no strong directional shift detected across current evidence window.";
    }
}

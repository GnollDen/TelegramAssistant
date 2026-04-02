// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class PeriodEvidenceAssembler : IPeriodEvidenceAssembler
{
    public Task<PeriodEvidencePack> BuildEvidenceAsync(
        PeriodizationRunRequest request,
        Period period,
        IReadOnlyList<Message> periodMessages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<OfflineEvent> offlineEvents,
        IReadOnlyList<AudioSnippet> audioSnippets,
        IReadOnlyList<ClarificationQuestion> clarificationQuestions,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        CancellationToken ct = default)
    {
        var evidenceRefs = new List<EvidenceRef>();
        evidenceRefs.AddRange(periodMessages
            .Take(12)
            .Select(x => new EvidenceRef { Type = "message", Id = x.Id.ToString(), Note = x.Timestamp.ToString("O") }));

        var overlappingSessions = sessions
            .Where(x => Overlaps(period.StartAt, period.EndAt, x.StartDate, x.EndDate))
            .ToList();
        evidenceRefs.AddRange(overlappingSessions
            .Take(6)
            .Select(x => new EvidenceRef { Type = "chat_session", Id = x.Id.ToString(), Note = x.SessionIndex.ToString() }));

        var overlappingEvents = offlineEvents
            .Where(x => Overlaps(period.StartAt, period.EndAt, x.TimestampStart, x.TimestampEnd ?? x.TimestampStart))
            .ToList();
        evidenceRefs.AddRange(overlappingEvents
            .Take(6)
            .Select(x => new EvidenceRef { Type = "offline_event", Id = x.Id.ToString(), Note = x.EventType }));

        evidenceRefs.AddRange(clarificationAnswers
            .Take(8)
            .Select(x => new EvidenceRef { Type = "clarification_answer", Id = x.Id.ToString(), Note = x.SourceClass }));
        evidenceRefs.AddRange(audioSnippets
            .Take(8)
            .Select(x => new EvidenceRef { Type = "audio_snippet", Id = x.Id.ToString(), Note = x.SnippetType }));

        var keySignals = BuildKeySignals(periodMessages, overlappingSessions, overlappingEvents, audioSnippets, clarificationAnswers);
        var openQuestions = clarificationQuestions
            .Where(x => string.Equals(x.Status, "open", StringComparison.OrdinalIgnoreCase) || string.Equals(x.Status, "in_progress", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var helped = InferWhatHelped(periodMessages, overlappingEvents, clarificationAnswers);
        var hurt = InferWhatHurt(periodMessages, openQuestions);

        var interpretationConfidence = ComputeInterpretationConfidence(periodMessages.Count, clarificationAnswers.Count, openQuestions.Count, overlappingEvents.Count);

        return Task.FromResult(new PeriodEvidencePack
        {
            KeySignals = keySignals,
            EvidenceRefs = evidenceRefs,
            OpenQuestionsCount = openQuestions.Count,
            OpenQuestionRefs = openQuestions.Select(x => x.Id.ToString()).Take(8).ToList(),
            WhatHelped = helped,
            WhatHurt = hurt,
            InterpretationConfidence = interpretationConfidence
        });
    }

    private static List<string> BuildKeySignals(
        IReadOnlyList<Message> messages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<OfflineEvent> events,
        IReadOnlyList<AudioSnippet> audioSnippets,
        IReadOnlyList<ClarificationAnswer> answers)
    {
        var signals = new List<string>
        {
            $"message_count:{messages.Count}",
            $"session_count:{sessions.Count}",
            $"offline_event_count:{events.Count}",
            $"audio_snippet_count:{audioSnippets.Count}",
            $"clarification_answer_count:{answers.Count}",
            $"distinct_senders:{messages.Select(x => x.SenderId).Distinct().Count()}"
        };

        var textMessages = messages.Where(x => !string.IsNullOrWhiteSpace(x.Text)).ToList();
        if (textMessages.Count > 0)
        {
            var avgLength = textMessages.Average(x => (x.Text ?? string.Empty).Length);
            signals.Add($"avg_message_length:{Math.Round(avgLength, 1)}");
        }

        var volatility = ComputeVolatility(messages);
        if (volatility >= 0.6f)
        {
            signals.Add("volatility_high");
        }

        return signals;
    }

    private static string InferWhatHelped(
        IReadOnlyList<Message> messages,
        IReadOnlyList<OfflineEvent> events,
        IReadOnlyList<ClarificationAnswer> answers)
    {
        var positiveTokens = CountTokenHits(messages, ["thanks", "ok", "да", "понял", "хорошо", "great", "sure"]);
        if (positiveTokens >= 4)
        {
            return "Stable constructive exchanges were present across this period.";
        }

        if (events.Any())
        {
            return "Key offline event(s) likely provided clarifying context for interaction dynamics.";
        }

        if (answers.Count >= 2)
        {
            return "Clarification answers reduced ambiguity and improved interpretability.";
        }

        return "No strong positive stabilizer detected; manual review may refine helpful factors.";
    }

    private static string InferWhatHurt(IReadOnlyList<Message> messages, IReadOnlyList<ClarificationQuestion> openQuestions)
    {
        var negativeTokens = CountTokenHits(messages, ["ignore", "later", "не знаю", "потом", "нет", "stop", "busy"]);
        if (negativeTokens >= 4)
        {
            return "Repeated deferrals/negative interaction cues likely increased instability.";
        }

        if (openQuestions.Count >= 2)
        {
            return "Open clarification gaps remain and limit interpretation confidence.";
        }

        return "No strong destabilizer detected in canonical evidence; confidence remains moderate.";
    }

    private static float ComputeInterpretationConfidence(int messageCount, int answerCount, int openQuestionCount, int eventCount)
    {
        var confidence = 0.35f;
        confidence += Math.Clamp(messageCount / 80f, 0, 0.3f);
        confidence += Math.Clamp(answerCount / 6f, 0, 0.2f);
        confidence += Math.Clamp(eventCount / 4f, 0, 0.1f);
        confidence -= Math.Clamp(openQuestionCount / 5f, 0, 0.25f);
        return Math.Clamp(confidence, 0.3f, 0.95f);
    }

    private static bool Overlaps(DateTime startA, DateTime? endA, DateTime startB, DateTime endB)
    {
        var aEnd = endA ?? DateTime.MaxValue;
        return startA <= endB && startB <= aEnd;
    }

    private static int CountTokenHits(IReadOnlyList<Message> messages, string[] tokens)
    {
        var count = 0;
        foreach (var message in messages)
        {
            var text = message.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (var token in tokens)
            {
                if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    break;
                }
            }
        }

        return count;
    }

    private static float ComputeVolatility(IReadOnlyList<Message> messages)
    {
        if (messages.Count < 6)
        {
            return 0.2f;
        }

        var ordered = messages.OrderBy(x => x.Timestamp).ToList();
        var gaps = new List<double>();
        for (var i = 1; i < ordered.Count; i++)
        {
            gaps.Add((ordered[i].Timestamp - ordered[i - 1].Timestamp).TotalHours);
        }

        if (gaps.Count == 0)
        {
            return 0.2f;
        }

        var avg = gaps.Average();
        var variance = gaps.Sum(x => Math.Pow(x - avg, 2)) / gaps.Count;
        var normalized = Math.Clamp(Math.Sqrt(variance) / 24.0, 0, 1);
        return (float)normalized;
    }
}

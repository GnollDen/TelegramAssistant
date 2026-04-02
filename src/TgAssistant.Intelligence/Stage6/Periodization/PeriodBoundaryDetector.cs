// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class PeriodBoundaryDetector : IPeriodBoundaryDetector
{
    private static readonly TimeSpan MinBoundaryDistance = TimeSpan.FromHours(6);
    private static readonly TimeSpan MergeDistance = TimeSpan.FromHours(12);

    public Task<IReadOnlyList<PeriodBoundaryCandidate>> DetectAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Message> messages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<OfflineEvent> offlineEvents,
        IReadOnlyList<ClarificationQuestion> clarificationQuestions,
        CancellationToken ct = default)
    {
        var candidates = new List<PeriodBoundaryCandidate>();

        var sortedSessions = sessions.OrderBy(x => x.StartDate).ToList();
        for (var i = 0; i < sortedSessions.Count - 1; i++)
        {
            var current = sortedSessions[i];
            var next = sortedSessions[i + 1];
            var gap = next.StartDate - current.EndDate;
            if (gap < TimeSpan.FromHours(36))
            {
                continue;
            }

            var pauseScore = (float)Math.Clamp(gap.TotalHours / (24 * 7), 0.35, 1.0);
            candidates.Add(new PeriodBoundaryCandidate
            {
                BoundaryAt = next.StartDate,
                PauseScore = pauseScore,
                EventScore = 0,
                DynamicShiftScore = 0,
                HasLongPause = true,
                ReasonSummary = $"session_gap:{Math.Round(gap.TotalHours)}h"
            });
        }

        foreach (var evt in offlineEvents)
        {
            candidates.Add(new PeriodBoundaryCandidate
            {
                BoundaryAt = evt.TimestampStart,
                PauseScore = 0,
                EventScore = 1.0f,
                DynamicShiftScore = 0,
                HasKeyEvent = true,
                ReasonSummary = $"offline_event:{evt.EventType}"
            });
        }

        var sortedMessages = messages.OrderBy(x => x.Timestamp).ToList();
        var dynamicCandidates = DetectDynamicShiftCandidates(sortedMessages, sortedSessions);
        candidates.AddRange(dynamicCandidates);

        foreach (var question in clarificationQuestions)
        {
            if (!question.QuestionType.Contains("transition", StringComparison.OrdinalIgnoreCase) &&
                !question.QuestionType.Contains("timeline", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var anchorAt = question.ResolvedAt ?? question.CreatedAt;
            candidates.Add(new PeriodBoundaryCandidate
            {
                BoundaryAt = anchorAt,
                PauseScore = 0,
                EventScore = 0.8f,
                DynamicShiftScore = 0.2f,
                HasKeyEvent = true,
                ReasonSummary = "clarification_transition"
            });
        }

        var merged = MergeNearby(candidates);
        var filtered = merged
            .Where(x =>
                x.HasKeyEvent ||
                x.HasDynamicShift ||
                (x.HasLongPause && (x.PauseScore >= 0.75f || x.EventScore > 0 || x.DynamicShiftScore > 0)))
            .OrderBy(x => x.BoundaryAt)
            .ToList();

        if (sortedMessages.Count > 1)
        {
            var bounded = new List<PeriodBoundaryCandidate>();
            var first = sortedMessages[0].Timestamp;
            var last = sortedMessages[^1].Timestamp;
            foreach (var candidate in filtered)
            {
                if (candidate.BoundaryAt <= first + MinBoundaryDistance || candidate.BoundaryAt >= last - MinBoundaryDistance)
                {
                    continue;
                }

                bounded.Add(candidate);
            }

            return Task.FromResult<IReadOnlyList<PeriodBoundaryCandidate>>(bounded);
        }

        return Task.FromResult<IReadOnlyList<PeriodBoundaryCandidate>>(filtered);
    }

    private static List<PeriodBoundaryCandidate> DetectDynamicShiftCandidates(IReadOnlyList<Message> messages, IReadOnlyList<ChatSession> sessions)
    {
        var result = new List<PeriodBoundaryCandidate>();
        if (sessions.Count < 2)
        {
            return result;
        }

        for (var i = 0; i < sessions.Count - 1; i++)
        {
            var left = sessions[i];
            var right = sessions[i + 1];
            var leftMessages = messages.Where(x => x.Timestamp >= left.StartDate && x.Timestamp <= left.EndDate).ToList();
            var rightMessages = messages.Where(x => x.Timestamp >= right.StartDate && x.Timestamp <= right.EndDate).ToList();
            if (leftMessages.Count < 3 || rightMessages.Count < 3)
            {
                continue;
            }

            var leftMetric = BuildDynamicMetric(leftMessages);
            var rightMetric = BuildDynamicMetric(rightMessages);
            var delta = Math.Abs(leftMetric.SenderBalance - rightMetric.SenderBalance)
                        + Math.Abs(leftMetric.ReplyTempoHours - rightMetric.ReplyTempoHours) / 12f
                        + Math.Abs(leftMetric.LongMessageRatio - rightMetric.LongMessageRatio);
            if (delta < 0.7f)
            {
                continue;
            }

            result.Add(new PeriodBoundaryCandidate
            {
                BoundaryAt = right.StartDate,
                PauseScore = 0,
                EventScore = 0,
                DynamicShiftScore = (float)Math.Clamp(delta / 2.5f, 0.4f, 1.0f),
                HasDynamicShift = true,
                ReasonSummary = "interaction_dynamic_shift"
            });
        }

        return result;
    }

    private static DynamicMetric BuildDynamicMetric(IReadOnlyList<Message> messages)
    {
        var ordered = messages.OrderBy(x => x.Timestamp).ToList();
        var senderGroups = ordered.GroupBy(x => x.SenderId).Select(x => x.Count()).OrderByDescending(x => x).ToList();
        var top = senderGroups.FirstOrDefault();
        var balance = ordered.Count == 0 ? 0.5f : (float)top / ordered.Count;

        var interSenderGaps = new List<double>();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].SenderId == ordered[i].SenderId)
            {
                continue;
            }

            interSenderGaps.Add((ordered[i].Timestamp - ordered[i - 1].Timestamp).TotalHours);
        }

        var tempo = interSenderGaps.Count == 0 ? 12.0 : interSenderGaps.Average();
        var longMessages = ordered.Count(x => (x.Text?.Length ?? 0) >= 120);
        var longRatio = ordered.Count == 0 ? 0 : (float)longMessages / ordered.Count;

        return new DynamicMetric
        {
            SenderBalance = balance,
            ReplyTempoHours = (float)Math.Clamp(tempo, 0, 72),
            LongMessageRatio = longRatio
        };
    }

    private static List<PeriodBoundaryCandidate> MergeNearby(IReadOnlyList<PeriodBoundaryCandidate> raw)
    {
        var sorted = raw.OrderBy(x => x.BoundaryAt).ToList();
        var merged = new List<PeriodBoundaryCandidate>();

        foreach (var candidate in sorted)
        {
            var last = merged.LastOrDefault();
            if (last == null || candidate.BoundaryAt - last.BoundaryAt > MergeDistance)
            {
                merged.Add(Clone(candidate));
                continue;
            }

            last.PauseScore = Math.Max(last.PauseScore, candidate.PauseScore);
            last.EventScore = Math.Max(last.EventScore, candidate.EventScore);
            last.DynamicShiftScore = Math.Max(last.DynamicShiftScore, candidate.DynamicShiftScore);
            last.HasKeyEvent = last.HasKeyEvent || candidate.HasKeyEvent;
            last.HasDynamicShift = last.HasDynamicShift || candidate.HasDynamicShift;
            last.HasLongPause = last.HasLongPause || candidate.HasLongPause;
            last.ReasonSummary = string.Join(",",
                new[] { last.ReasonSummary, candidate.ReasonSummary }
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        return merged;
    }

    private static PeriodBoundaryCandidate Clone(PeriodBoundaryCandidate source)
    {
        return new PeriodBoundaryCandidate
        {
            BoundaryAt = source.BoundaryAt,
            PauseScore = source.PauseScore,
            EventScore = source.EventScore,
            DynamicShiftScore = source.DynamicShiftScore,
            HasKeyEvent = source.HasKeyEvent,
            HasDynamicShift = source.HasDynamicShift,
            HasLongPause = source.HasLongPause,
            ReasonSummary = source.ReasonSummary
        };
    }

    private sealed class DynamicMetric
    {
        public float SenderBalance { get; set; }
        public float ReplyTempoHours { get; set; }
        public float LongMessageRatio { get; set; }
    }
}

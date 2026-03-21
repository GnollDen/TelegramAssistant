using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class TimelineAssembler : ITimelineAssembler
{
    public Task<IReadOnlyList<Period>> AssembleAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Message> messages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<PeriodBoundaryCandidate> boundaries,
        CancellationToken ct = default)
    {
        var timelinePoints = new List<DateTime>();
        timelinePoints.AddRange(messages.Select(x => x.Timestamp));
        timelinePoints.AddRange(sessions.Select(x => x.StartDate));
        timelinePoints.AddRange(sessions.Select(x => x.EndDate));

        if (timelinePoints.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<Period>>([]);
        }

        var startAt = timelinePoints.Min();
        var endAt = timelinePoints.Max();
        var sortedBoundaries = boundaries
            .Select(x => x.BoundaryAt)
            .Where(x => x > startAt && x < endAt)
            .OrderBy(x => x)
            .Distinct()
            .ToList();

        var ranges = new List<(DateTime Start, DateTime End)>();
        var cursor = startAt;
        foreach (var boundary in sortedBoundaries)
        {
            if (boundary <= cursor)
            {
                continue;
            }

            ranges.Add((cursor, boundary));
            cursor = boundary;
        }

        ranges.Add((cursor, endAt));

        var periods = new List<Period>(ranges.Count);
        for (var i = 0; i < ranges.Count; i++)
        {
            var range = ranges[i];
            var previousBoundary = i == 0 ? null : boundaries.Where(x => x.BoundaryAt == range.Start).ToList();
            var boundaryConfidence = ComputeBoundaryConfidence(previousBoundary);
            periods.Add(new Period
            {
                Id = Guid.NewGuid(),
                CaseId = request.CaseId,
                ChatId = request.ChatId,
                Label = $"period_{i + 1:00}",
                StartAt = range.Start,
                EndAt = i == ranges.Count - 1 && request.ToUtc == null ? null : range.End,
                IsOpen = i == ranges.Count - 1 && request.ToUtc == null,
                Summary = string.Empty,
                KeySignalsJson = "[]",
                WhatHelped = string.Empty,
                WhatHurt = string.Empty,
                OpenQuestionsCount = 0,
                BoundaryConfidence = boundaryConfidence,
                InterpretationConfidence = 0.5f,
                ReviewPriority = 0,
                StatusSnapshot = "unknown",
                DynamicSnapshot = "mixed",
                SourceType = request.SourceType,
                SourceId = request.SourceId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        return Task.FromResult<IReadOnlyList<Period>>(periods);
    }

    private static float ComputeBoundaryConfidence(List<PeriodBoundaryCandidate>? boundaryCandidates)
    {
        if (boundaryCandidates == null || boundaryCandidates.Count == 0)
        {
            return 0.45f;
        }

        var maxPause = boundaryCandidates.Max(x => x.PauseScore);
        var maxEvent = boundaryCandidates.Max(x => x.EventScore);
        var maxDynamic = boundaryCandidates.Max(x => x.DynamicShiftScore);
        var confidence = (maxPause * 0.2f) + (maxEvent * 0.45f) + (maxDynamic * 0.35f);
        return (float)Math.Clamp(confidence, 0.4, 0.95);
    }
}

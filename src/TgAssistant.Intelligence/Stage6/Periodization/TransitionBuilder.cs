using System.Text.Json;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Periodization;

public class TransitionBuilder : ITransitionBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<PeriodTransition>> BuildAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Period> periods,
        IReadOnlyList<PeriodBoundaryCandidate> boundaries,
        CancellationToken ct = default)
    {
        var transitions = new List<PeriodTransition>();
        if (periods.Count < 2)
        {
            return Task.FromResult<IReadOnlyList<PeriodTransition>>(transitions);
        }

        for (var i = 0; i < periods.Count - 1; i++)
        {
            var from = periods[i];
            var to = periods[i + 1];
            var boundary = FindClosestBoundary(boundaries, to.StartAt);

            var (transitionType, summary, isResolved, confidence, gapId) = BuildTransitionInterpretation(boundary);
            var evidenceRefs = boundary == null
                ? []
                : new[] { new EvidenceRef { Type = "boundary_signal", Id = boundary.BoundaryAt.ToString("O"), Note = boundary.ReasonSummary } };

            transitions.Add(new PeriodTransition
            {
                Id = Guid.NewGuid(),
                FromPeriodId = from.Id,
                ToPeriodId = to.Id,
                TransitionType = transitionType,
                Summary = summary,
                IsResolved = isResolved,
                Confidence = confidence,
                GapId = gapId,
                EvidenceRefsJson = JsonSerializer.Serialize(evidenceRefs, JsonOptions),
                SourceType = request.SourceType,
                SourceId = request.SourceId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        return Task.FromResult<IReadOnlyList<PeriodTransition>>(transitions);
    }

    private static PeriodBoundaryCandidate? FindClosestBoundary(IReadOnlyList<PeriodBoundaryCandidate> boundaries, DateTime anchor)
    {
        return boundaries
            .Where(x => Math.Abs((x.BoundaryAt - anchor).TotalHours) <= 24)
            .OrderBy(x => Math.Abs((x.BoundaryAt - anchor).TotalHours))
            .FirstOrDefault();
    }

    private static (string TransitionType, string Summary, bool IsResolved, float Confidence, Guid? GapId) BuildTransitionInterpretation(PeriodBoundaryCandidate? boundary)
    {
        if (boundary == null)
        {
            return ("unresolved_gap", "No clear transition cause found from canonical evidence.", false, 0.35f, Guid.NewGuid());
        }

        if (boundary.HasKeyEvent && boundary.HasDynamicShift)
        {
            var confidence = (float)Math.Clamp(0.65 + (boundary.EventScore * 0.2f) + (boundary.DynamicShiftScore * 0.1f), 0.65, 0.95);
            return ("event_dynamic_shift", "Transition likely driven by a key event with interaction dynamic change.", true, confidence, null);
        }

        if (boundary.HasKeyEvent)
        {
            var confidence = (float)Math.Clamp(0.6 + (boundary.EventScore * 0.3f), 0.6, 0.92);
            return ("event_shift", "Transition likely linked to a key event.", true, confidence, null);
        }

        if (boundary.HasDynamicShift)
        {
            var confidence = (float)Math.Clamp(0.55 + (boundary.DynamicShiftScore * 0.3f), 0.55, 0.9);
            return ("dynamic_shift", "Transition likely linked to interaction dynamic shift.", true, confidence, null);
        }

        if (boundary.HasLongPause)
        {
            return (
                "unresolved_gap",
                "Long pause detected but transition cause remains unclear; unresolved gap created for review.",
                false,
                (float)Math.Clamp(0.35 + (boundary.PauseScore * 0.2f), 0.35, 0.6),
                Guid.NewGuid());
        }

        return ("unresolved_gap", "Transition cause is unclear; unresolved gap created.", false, 0.35f, Guid.NewGuid());
    }
}

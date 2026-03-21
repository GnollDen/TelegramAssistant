using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface ICurrentStateEngine
{
    Task<CurrentStateResult> ComputeAsync(CurrentStateRequest request, CancellationToken ct = default);
}

public interface IStateScoreCalculator
{
    Task<StateScoreResult> CalculateAsync(CurrentStateContext context, CancellationToken ct = default);
}

public interface IDynamicLabelMapper
{
    string Map(
        StateScoreResult scores,
        CurrentStateContext context,
        StateConfidenceResult confidence,
        StateSnapshot? previousSnapshot);
}

public interface IRelationshipStatusMapper
{
    (string Primary, string? Alternative) Map(
        StateScoreResult scores,
        CurrentStateContext context,
        StateConfidenceResult confidence,
        StateSnapshot? previousSnapshot);
}

public interface IStateConfidenceEvaluator
{
    StateConfidenceResult Evaluate(StateScoreResult scores, CurrentStateContext context);
}

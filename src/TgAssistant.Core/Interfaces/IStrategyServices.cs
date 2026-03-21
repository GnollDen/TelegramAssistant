using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IStrategyEngine
{
    Task<StrategyEngineResult> RunAsync(StrategyEngineRequest request, CancellationToken ct = default);
}

public interface IStrategyOptionGenerator
{
    Task<IReadOnlyList<StrategyCandidateOption>> GenerateAsync(
        StrategyEvaluationContext context,
        CancellationToken ct = default);
}

public interface IStrategyRanker
{
    IReadOnlyList<StrategyCandidateOption> Rank(StrategyEvaluationContext context, IReadOnlyList<StrategyCandidateOption> candidates);
}

public interface IStrategyRiskEvaluator
{
    StrategyRiskAssessment Evaluate(StrategyEvaluationContext context, StrategyCandidateOption option);
}

public interface IStrategyConfidenceEvaluator
{
    StrategyConfidenceAssessment Evaluate(
        StrategyEvaluationContext context,
        IReadOnlyList<StrategyCandidateOption> rankedOptions);
}

public interface IMicroStepPlanner
{
    (string MicroStep, IReadOnlyList<string> Horizon) Plan(
        StrategyEvaluationContext context,
        StrategyCandidateOption primaryOption,
        StrategyConfidenceAssessment confidence);
}

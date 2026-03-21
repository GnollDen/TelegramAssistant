using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IDraftEngine
{
    Task<DraftEngineResult> RunAsync(DraftEngineRequest request, CancellationToken ct = default);
}

public interface IDraftGenerator
{
    Task<DraftContentSet> GenerateAsync(DraftGenerationContext context, CancellationToken ct = default);
}

public interface IDraftStyleAdapter
{
    DraftStyledContent ApplyStyle(DraftGenerationContext context, DraftContentSet content);
}

public interface IDraftStrategyChecker
{
    DraftConflictAssessment Evaluate(DraftGenerationContext context, DraftStyledContent styled);
}

public interface IDraftPackagingService
{
    Task<DraftRecord> PersistAsync(
        DraftGenerationContext context,
        DraftStyledContent styled,
        DraftConflictAssessment conflict,
        CancellationToken ct = default);
}

public interface IDraftReviewEngine
{
    Task<DraftReviewResult> RunAsync(DraftReviewRequest request, CancellationToken ct = default);
}

public interface IDraftRiskAssessor
{
    DraftRiskAssessment Assess(DraftReviewContext context, DraftStrategyFitResult strategyFit);
}

public interface IDraftStrategyFitChecker
{
    DraftStrategyFitResult Evaluate(DraftReviewContext context);
}

public interface ISaferRewriteGenerator
{
    string Generate(DraftReviewContext context, DraftRiskAssessment assessment, DraftStrategyFitResult strategyFit);
}

public interface INaturalRewriteGenerator
{
    string Generate(DraftReviewContext context, DraftRiskAssessment assessment);
}

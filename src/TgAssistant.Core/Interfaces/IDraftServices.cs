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

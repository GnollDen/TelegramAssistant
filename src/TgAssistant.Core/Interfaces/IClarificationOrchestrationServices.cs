using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IClarificationOrchestrator
{
    Task<IReadOnlyList<ClarificationQuestion>> EnqueueQuestionsAsync(
        long caseId,
        IReadOnlyCollection<ClarificationQuestionDraft> drafts,
        string actor,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClarificationQueueItem>> BuildQueueAsync(long caseId, CancellationToken ct = default);

    Task<ClarificationApplyResult> ApplyAnswerAsync(ClarificationApplyRequest request, CancellationToken ct = default);
}

public interface IClarificationAnswerApplier
{
    Task<(ClarificationQuestion Question, ClarificationAnswer Answer, List<ConflictRecord> Conflicts)> ApplyAsync(
        ClarificationApplyRequest request,
        CancellationToken ct = default);
}

public interface IClarificationDependencyResolver
{
    Task<List<ClarificationDependencyUpdate>> ResolveAfterParentAnswerAsync(
        ClarificationQuestion parentQuestion,
        ClarificationAnswer answer,
        string actor,
        string? reason,
        CancellationToken ct = default);
}

public interface IRecomputeTargetPlanner
{
    Task<RecomputeTargetPlan> BuildPlanAsync(
        ClarificationQuestion question,
        ClarificationAnswer answer,
        IReadOnlyCollection<ClarificationDependencyUpdate> dependencyUpdates,
        IReadOnlyCollection<ConflictRecord> conflicts,
        CancellationToken ct = default);
}

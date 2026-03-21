using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IPeriodizationService
{
    Task<PeriodizationRunResult> RunAsync(PeriodizationRunRequest request, CancellationToken ct = default);
}

public interface IPeriodBoundaryDetector
{
    Task<IReadOnlyList<PeriodBoundaryCandidate>> DetectAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Message> messages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<OfflineEvent> offlineEvents,
        IReadOnlyList<ClarificationQuestion> clarificationQuestions,
        CancellationToken ct = default);
}

public interface ITimelineAssembler
{
    Task<IReadOnlyList<Period>> AssembleAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Message> messages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<PeriodBoundaryCandidate> boundaries,
        CancellationToken ct = default);
}

public interface ITransitionBuilder
{
    Task<IReadOnlyList<PeriodTransition>> BuildAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Period> periods,
        IReadOnlyList<PeriodBoundaryCandidate> boundaries,
        CancellationToken ct = default);
}

public interface IPeriodEvidenceAssembler
{
    Task<PeriodEvidencePack> BuildEvidenceAsync(
        PeriodizationRunRequest request,
        Period period,
        IReadOnlyList<Message> periodMessages,
        IReadOnlyList<ChatSession> sessions,
        IReadOnlyList<OfflineEvent> offlineEvents,
        IReadOnlyList<ClarificationQuestion> clarificationQuestions,
        IReadOnlyList<ClarificationAnswer> clarificationAnswers,
        CancellationToken ct = default);
}

public interface IPeriodProposalService
{
    Task<IReadOnlyList<PeriodProposalRecord>> BuildAndPersistProposalsAsync(
        PeriodizationRunRequest request,
        IReadOnlyList<Period> periods,
        IReadOnlyList<PeriodTransition> transitions,
        IReadOnlyList<ConflictRecord> conflicts,
        CancellationToken ct = default);
}

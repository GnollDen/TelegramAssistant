using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IOutcomeService
{
    Task<OutcomeRecordResult> RecordAsync(OutcomeRecordRequest request, CancellationToken ct = default);
}

public interface IDraftActionMatcher
{
    Task<DraftActionMatchResult> MatchAsync(
        DraftRecord draft,
        long chatId,
        long? explicitMessageId,
        string? explicitActionText,
        CancellationToken ct = default);
}

public interface IObservedOutcomeRecorder
{
    ObservedOutcomeAssessment Assess(string? followUpText, string? explicitUserLabel);
}

public interface ILearningSignalBuilder
{
    List<LearningSignal> Build(
        StrategyRecord strategyRecord,
        StrategyOption? primaryOption,
        DraftRecord draftRecord,
        DraftActionMatchResult match,
        ObservedOutcomeAssessment observed,
        string? userLabel);
}

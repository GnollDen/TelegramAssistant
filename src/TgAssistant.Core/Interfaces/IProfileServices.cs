using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface IProfileEngine
{
    Task<ProfileEngineResult> RunAsync(ProfileEngineRequest request, CancellationToken ct = default);
}

public interface IProfileTraitExtractor
{
    Task<IReadOnlyList<ProfileTraitDraft>> ExtractAsync(
        string subjectType,
        string subjectId,
        ProfileEvidenceContext context,
        Period? period,
        CancellationToken ct = default);
}

public interface IProfileConfidenceEvaluator
{
    ProfileAssessment Evaluate(
        string subjectType,
        IReadOnlyList<ProfileTraitDraft> traits,
        ProfileEvidenceContext context,
        Period? period);
}

public interface IPairProfileSynthesizer
{
    Task<IReadOnlyList<ProfileTraitDraft>> SynthesizeAsync(
        ProfileEvidenceContext context,
        Period? period,
        CancellationToken ct = default);
}

public interface IPatternSynthesisService
{
    Task<IReadOnlyList<ProfilePatternRecord>> BuildPatternsAsync(
        string subjectType,
        string subjectId,
        ProfileEvidenceContext context,
        Period? period,
        CancellationToken ct = default);
}

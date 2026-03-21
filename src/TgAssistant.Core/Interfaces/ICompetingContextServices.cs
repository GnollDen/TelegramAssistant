using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface ICompetingContextInterpretationService
{
    CompetingContextImportValidationResult Validate(CompetingContextInterpretationRequest request);
    Task<CompetingContextInterpretationResult> InterpretAsync(CompetingContextInterpretationRequest request, CancellationToken ct = default);
}

public interface ICompetingContextRuntimeService
{
    Task<CompetingContextRuntimeResult> RunAsync(CompetingContextRuntimeRequest request, CancellationToken ct = default);
}

using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface ILlmContractNormalizer
{
    Task<LlmContractNormalizationResult> NormalizeAsync(LlmContractNormalizationRequest request, CancellationToken ct = default);
}

public interface ILlmContractSchemaProvider
{
    LlmContractSchemaDescriptor? GetSchema(LlmContractKind kind);
}

public interface ILlmContractValidator
{
    LlmContractValidationResult Validate(LlmContractKind kind, string normalizedPayloadJson);
}

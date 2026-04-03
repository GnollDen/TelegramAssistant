using TgAssistant.Core.Models;

namespace TgAssistant.Core.Interfaces;

public interface ILlmGateway
{
    Task<LlmGatewayResponse> ExecuteAsync(LlmGatewayRequest request, CancellationToken ct = default);
}

public interface ILlmProviderClient
{
    string ProviderId { get; }
    bool Supports(LlmModality modality);
    Task<LlmProviderResult> ExecuteAsync(LlmProviderRequest request, CancellationToken ct = default);
}

public interface ILlmRoutingPolicy
{
    LlmRoutingDecision Resolve(LlmGatewayRequest request);
}

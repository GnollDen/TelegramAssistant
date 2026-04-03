using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public class LlmGatewayService : ILlmGateway
{
    private readonly IReadOnlyDictionary<string, ILlmProviderClient> _providers;
    private readonly ILlmRoutingPolicy _routingPolicy;
    private readonly LlmGatewaySettings _settings;
    private readonly ILogger<LlmGatewayService> _logger;

    public LlmGatewayService(
        IEnumerable<ILlmProviderClient> providers,
        ILlmRoutingPolicy routingPolicy,
        IOptions<LlmGatewaySettings> settings,
        ILogger<LlmGatewayService> logger)
    {
        _providers = providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
        _routingPolicy = routingPolicy;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<LlmGatewayResponse> ExecuteAsync(LlmGatewayRequest request, CancellationToken ct = default)
    {
        request.Validate();
        var routingDecision = _routingPolicy.Resolve(request);
        var providerOrder = new List<string> { routingDecision.PrimaryProvider };
        providerOrder.AddRange(routingDecision.FallbackProviders);

        LlmGatewayException? lastFailure = null;
        for (var index = 0; index < providerOrder.Count; index++)
        {
            var providerId = providerOrder[index];
            var providerClient = ResolveProvider(providerId, request.Modality);
            var providerRequest = new LlmProviderRequest
            {
                ProviderId = providerId,
                Model = ResolveModel(providerId, request, routingDecision, isPrimaryAttempt: index == 0),
                Request = request,
                IsFallbackAttempt = index > 0,
                FallbackFromProvider = index > 0 ? routingDecision.PrimaryProvider : null
            };

            try
            {
                var providerResult = await providerClient.ExecuteAsync(providerRequest, ct);
                return new LlmGatewayResponse
                {
                    Provider = providerResult.ProviderId,
                    Model = providerResult.Model,
                    RequestId = providerResult.RequestId,
                    LatencyMs = providerResult.LatencyMs,
                    Output = providerResult.Output,
                    Usage = providerResult.Usage,
                    FallbackApplied = index > 0,
                    FallbackFromProvider = index > 0 ? routingDecision.PrimaryProvider : null,
                    Experiment = routingDecision.Experiment is null
                        ? null
                        : new LlmGatewayExperimentResult
                        {
                            Label = routingDecision.Experiment.Label,
                            Branch = routingDecision.Experiment.Branch,
                            StickyRoutingKey = routingDecision.Experiment.StickyRoutingKey,
                            SelectedProvider = providerResult.ProviderId,
                            SelectedModel = providerResult.Model
                        },
                    RawProviderPayloadJson = _settings.LogRawProviderPayloadJson ? providerResult.RawProviderPayloadJson : null
                };
            }
            catch (LlmGatewayException ex)
            {
                lastFailure = ex;
                if (!ex.Retryable || index == providerOrder.Count - 1)
                {
                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "Gateway fallback triggered for modality={Modality} task={TaskKey}. from={FromProvider} to={ToProvider} category={Category}",
                    request.Modality,
                    request.TaskKey,
                    providerId,
                    providerOrder[index + 1],
                    ex.Category);
            }
        }

        throw lastFailure ?? new LlmGatewayException("Gateway routing exhausted without producing a provider result.")
        {
            Category = LlmGatewayErrorCategory.Unknown,
            Modality = request.Modality,
            Retryable = false
        };
    }

    private ILlmProviderClient ResolveProvider(string providerId, LlmModality modality)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
        {
            throw new LlmGatewayException($"Gateway provider '{providerId}' is not registered in DI.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Provider = providerId,
                Modality = modality,
                Retryable = false
            };
        }

        if (!provider.Supports(modality))
        {
            throw new LlmGatewayException($"Gateway provider '{providerId}' does not support modality '{modality}'.")
            {
                Category = LlmGatewayErrorCategory.UnsupportedModality,
                Provider = providerId,
                Modality = modality,
                Retryable = false
            };
        }

        return provider;
    }

    private string ResolveModel(
        string providerId,
        LlmGatewayRequest request,
        LlmRoutingDecision routingDecision,
        bool isPrimaryAttempt)
    {
        var providerSettings = _settings.GetProvider(providerId);
        var requestedModel = string.IsNullOrWhiteSpace(request.ModelHint) ? null : request.ModelHint.Trim();
        routingDecision.ProviderModelHints.TryGetValue(providerId, out var routedModel);

        var resolved = isPrimaryAttempt
            ? requestedModel ?? routedModel ?? providerSettings?.DefaultModel
            : routedModel ?? requestedModel ?? providerSettings?.DefaultModel;

        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved.Trim();
        }

        throw new LlmGatewayException($"Gateway route for provider '{providerId}' does not resolve a model.")
        {
            Category = LlmGatewayErrorCategory.Validation,
            Provider = providerId,
            Modality = request.Modality,
            Retryable = false
        };
    }
}

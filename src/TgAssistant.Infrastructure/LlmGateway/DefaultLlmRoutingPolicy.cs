using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public class DefaultLlmRoutingPolicy : ILlmRoutingPolicy
{
    private readonly LlmGatewaySettings _settings;

    public DefaultLlmRoutingPolicy(IOptions<LlmGatewaySettings> settings)
    {
        _settings = settings.Value;
    }

    public LlmRoutingDecision Resolve(LlmGatewayRequest request)
    {
        var route = _settings.GetRoute(request.Modality);
        if (route is null)
        {
            throw new LlmGatewayException(
                $"Gateway route is not configured for modality '{LlmGatewaySettings.ToRouteKey(request.Modality)}'.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Modality = request.Modality,
                Retryable = false
            };
        }

        if (!route.Enabled)
        {
            throw new LlmGatewayException(
                $"Gateway route for modality '{LlmGatewaySettings.ToRouteKey(request.Modality)}' is disabled.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Modality = request.Modality,
                Retryable = false
            };
        }

        if (string.IsNullOrWhiteSpace(route.PrimaryProvider))
        {
            throw new LlmGatewayException(
                $"Gateway route for modality '{LlmGatewaySettings.ToRouteKey(request.Modality)}' is missing PrimaryProvider.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Modality = request.Modality,
                Retryable = false
            };
        }

        var decision = new LlmRoutingDecision
        {
            PrimaryProvider = route.PrimaryProvider.Trim(),
            RetryPolicyClass = string.IsNullOrWhiteSpace(route.RetryPolicyClass) ? "default" : route.RetryPolicyClass.Trim(),
            TimeoutBudgetClass = string.IsNullOrWhiteSpace(route.TimeoutBudgetClass) ? "default" : route.TimeoutBudgetClass.Trim()
        };

        if (!string.IsNullOrWhiteSpace(route.PrimaryModel))
        {
            decision.ProviderModelHints[decision.PrimaryProvider] = route.PrimaryModel.Trim();
        }

        foreach (var fallback in route.FallbackProviders.Where(target => !string.IsNullOrWhiteSpace(target.Provider)))
        {
            var providerId = fallback.Provider.Trim();
            decision.FallbackProviders.Add(providerId);

            if (!string.IsNullOrWhiteSpace(fallback.Model))
            {
                decision.ProviderModelHints[providerId] = fallback.Model.Trim();
            }
        }

        return decision;
    }
}

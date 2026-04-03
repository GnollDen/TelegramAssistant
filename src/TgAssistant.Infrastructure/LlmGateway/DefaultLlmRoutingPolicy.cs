using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
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
        var route = ResolveConfiguredRoute(request);

        if (request.RouteOverride is null && !string.IsNullOrWhiteSpace(request.ModelHint))
        {
            throw new LlmGatewayException(
                $"Gateway request for modality '{LlmGatewaySettings.ToRouteKey(request.Modality)}' cannot set ModelHint without RouteOverride.")
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

        ApplyProviderTargets(decision, route.PrimaryProvider, route.PrimaryModel, route.FallbackProviders);

        if (request.RouteOverride is not null && !string.IsNullOrWhiteSpace(request.RouteOverride.PrimaryProvider))
        {
            ApplyBoundedRouteOverride(request, decision);
        }
        else
        {
            ApplyExperimentOverride(request, decision);
        }

        return decision;
    }

    private static void ApplyBoundedRouteOverride(LlmGatewayRequest request, LlmRoutingDecision decision)
    {
        var routeOverride = request.RouteOverride!;
        var primaryProvider = routeOverride.PrimaryProvider.Trim();
        var allowedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { decision.PrimaryProvider };
        foreach (var fallbackProvider in decision.FallbackProviders)
        {
            allowedProviders.Add(fallbackProvider);
        }

        if (!allowedProviders.Contains(primaryProvider))
        {
            throw new LlmGatewayException(
                $"Gateway RouteOverride primary provider '{primaryProvider}' is outside configured provider bounds for modality '{request.Modality}'.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Modality = request.Modality,
                Retryable = false
            };
        }

        var boundedFallbackProviders = new List<string>();
        foreach (var fallback in routeOverride.FallbackProviders.Where(target => !string.IsNullOrWhiteSpace(target.Provider)))
        {
            var providerId = fallback.Provider.Trim();
            if (!allowedProviders.Contains(providerId))
            {
                throw new LlmGatewayException(
                    $"Gateway RouteOverride fallback provider '{providerId}' is outside configured provider bounds for modality '{request.Modality}'.")
                {
                    Category = LlmGatewayErrorCategory.Validation,
                    Modality = request.Modality,
                    Retryable = false
                };
            }

            if (string.Equals(providerId, primaryProvider, StringComparison.OrdinalIgnoreCase)
                || boundedFallbackProviders.Contains(providerId, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            boundedFallbackProviders.Add(providerId);
        }

        decision.PrimaryProvider = primaryProvider;
        decision.FallbackProviders = boundedFallbackProviders;
        decision.ProviderModelHints.Clear();

        foreach (var hint in routeOverride.ProviderModelHints)
        {
            if (string.IsNullOrWhiteSpace(hint.Key) || string.IsNullOrWhiteSpace(hint.Value))
            {
                continue;
            }

            var providerId = hint.Key.Trim();
            if (!string.Equals(providerId, decision.PrimaryProvider, StringComparison.OrdinalIgnoreCase)
                && !decision.FallbackProviders.Contains(providerId, StringComparer.OrdinalIgnoreCase))
            {
                throw new LlmGatewayException(
                    $"Gateway RouteOverride model hint provider '{providerId}' is outside configured provider bounds for modality '{request.Modality}'.")
                {
                    Category = LlmGatewayErrorCategory.Validation,
                    Modality = request.Modality,
                    Retryable = false
                };
            }

            decision.ProviderModelHints[providerId] = hint.Value.Trim();
        }

        foreach (var fallback in routeOverride.FallbackProviders.Where(target => !string.IsNullOrWhiteSpace(target.Provider)))
        {
            var providerId = fallback.Provider.Trim();
            if (!decision.FallbackProviders.Contains(providerId, StringComparer.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(fallback.Model))
            {
                continue;
            }

            decision.ProviderModelHints[providerId] = fallback.Model.Trim();
        }
    }

    private LlmGatewayRouteSettings ResolveConfiguredRoute(LlmGatewayRequest request)
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

        return route;
    }

    private void ApplyExperimentOverride(LlmGatewayRequest request, LlmRoutingDecision decision)
    {
        var label = request.Experiment.Label?.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var experiment = _settings.GetExperiment(label);
        if (experiment is null || !experiment.Enabled)
        {
            return;
        }

        var branches = experiment.Branches
            .Where(branch => !string.IsNullOrWhiteSpace(branch.Branch)
                && !string.IsNullOrWhiteSpace(branch.Provider)
                && branch.WeightPercent > 0)
            .ToList();
        if (branches.Count == 0)
        {
            throw new LlmGatewayException($"Gateway experiment '{label}' is enabled but does not define any usable branches.")
            {
                Category = LlmGatewayErrorCategory.Validation,
                Modality = request.Modality,
                Retryable = false
            };
        }

        var stickyRoutingKey = ResolveStickyRoutingKey(request);
        var selectedBranch = SelectBranch(branches, stickyRoutingKey);

        decision.PrimaryProvider = selectedBranch.Provider.Trim();
        decision.FallbackProviders.Clear();
        decision.ProviderModelHints.Clear();
        ApplyProviderTargets(decision, selectedBranch.Provider, selectedBranch.Model, selectedBranch.FallbackProviders);
        decision.Experiment = new LlmRoutingExperimentDecision
        {
            Label = label,
            Branch = selectedBranch.Branch.Trim(),
            StickyRoutingKey = stickyRoutingKey
        };
    }

    private static void ApplyProviderTargets(
        LlmRoutingDecision decision,
        string primaryProvider,
        string? primaryModel,
        IEnumerable<LlmGatewayProviderTargetSettings> fallbackProviders)
    {
        if (!string.IsNullOrWhiteSpace(primaryModel))
        {
            decision.ProviderModelHints[primaryProvider.Trim()] = primaryModel.Trim();
        }

        foreach (var fallback in fallbackProviders.Where(target => !string.IsNullOrWhiteSpace(target.Provider)))
        {
            var providerId = fallback.Provider.Trim();
            decision.FallbackProviders.Add(providerId);

            if (!string.IsNullOrWhiteSpace(fallback.Model))
            {
                decision.ProviderModelHints[providerId] = fallback.Model.Trim();
            }
        }
    }

    private static string ResolveStickyRoutingKey(LlmGatewayRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Experiment.StickyRoutingKey))
        {
            return request.Experiment.StickyRoutingKey.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Trace.RequestId))
        {
            return request.Trace.RequestId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Trace.PathKey))
        {
            return request.Trace.PathKey.Trim();
        }

        return request.TaskKey.Trim();
    }

    private static LlmGatewayExperimentBranchSettings SelectBranch(
        IReadOnlyList<LlmGatewayExperimentBranchSettings> branches,
        string stickyRoutingKey)
    {
        var totalWeight = branches.Sum(branch => branch.WeightPercent);
        if (totalWeight <= 0)
        {
            return branches[0];
        }

        var selectionBucket = ComputeDeterministicBucket(stickyRoutingKey, totalWeight);
        var runningTotal = 0;
        foreach (var branch in branches)
        {
            runningTotal += branch.WeightPercent;
            if (selectionBucket < runningTotal)
            {
                return branch;
            }
        }

        return branches[^1];
    }

    private static int ComputeDeterministicBucket(string stickyRoutingKey, int totalWeight)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(stickyRoutingKey));
        var value = BitConverter.ToUInt32(bytes, 0);
        return (int)(value % totalWeight);
    }
}

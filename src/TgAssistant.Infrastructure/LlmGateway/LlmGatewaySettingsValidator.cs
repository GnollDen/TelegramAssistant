using TgAssistant.Core.Configuration;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public static class LlmGatewaySettingsValidator
{
    public static void ValidateOrThrow(LlmGatewaySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            return;
        }

        var errors = new List<string>();
        var routes = settings.Routing ?? new Dictionary<string, LlmGatewayRouteSettings>(StringComparer.OrdinalIgnoreCase);
        var providers = settings.Providers ?? new Dictionary<string, LlmGatewayProviderSettings>(StringComparer.OrdinalIgnoreCase);
        var experiments = settings.Experiments ?? new Dictionary<string, LlmGatewayExperimentSettings>(StringComparer.OrdinalIgnoreCase);
        var knownRouteKeys = Enum
            .GetValues<LlmModality>()
            .Where(modality => modality != LlmModality.Unspecified)
            .Select(LlmGatewaySettings.ToRouteKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var activeRouteEntries = routes
            .Where(entry => entry.Value?.Enabled ?? false)
            .ToList();

        if (activeRouteEntries.Count == 0)
        {
            errors.Add("LlmGateway:Enabled=true requires at least one enabled route.");
        }

        var activeProviderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providersNeedingChatPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var providersNeedingEmbeddingsPath = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in activeRouteEntries)
        {
            var routeKey = (entry.Key ?? string.Empty).Trim();
            var route = entry.Value!;

            if (!knownRouteKeys.Contains(routeKey))
            {
                errors.Add($"LlmGateway:Routing:{routeKey}: route key is not a supported modality route.");
            }

            var primaryProvider = (route.PrimaryProvider ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(primaryProvider))
            {
                errors.Add($"LlmGateway:Routing:{routeKey}:PrimaryProvider is required for enabled routes.");
                continue;
            }

            var retryPolicy = (route.RetryPolicyClass ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(retryPolicy))
            {
                errors.Add($"LlmGateway:Routing:{routeKey}:RetryPolicyClass is required for enabled routes.");
            }

            var timeoutBudget = (route.TimeoutBudgetClass ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(timeoutBudget))
            {
                errors.Add($"LlmGateway:Routing:{routeKey}:TimeoutBudgetClass is required for enabled routes.");
            }

            var fallbackProviders = (route.FallbackProviders ?? new List<LlmGatewayProviderTargetSettings>())
                .Where(target => !string.IsNullOrWhiteSpace(target.Provider))
                .Select(target => (target.Provider ?? string.Empty).Trim())
                .ToList();

            if (fallbackProviders.Any(id => string.Equals(id, primaryProvider, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"LlmGateway:Routing:{routeKey}:FallbackProviders cannot include PrimaryProvider '{primaryProvider}'.");
            }

            var duplicateFallback = fallbackProviders
                .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateFallback is not null)
            {
                errors.Add($"LlmGateway:Routing:{routeKey}:FallbackProviders contains duplicate provider '{duplicateFallback.Key}'.");
            }

            var routeProviderIds = new List<string> { primaryProvider };
            routeProviderIds.AddRange(fallbackProviders);
            foreach (var providerId in routeProviderIds)
            {
                activeProviderIds.Add(providerId);

                if (!providers.TryGetValue(providerId, out var providerSettings))
                {
                    errors.Add($"LlmGateway:Routing:{routeKey}: provider '{providerId}' is not defined in LlmGateway:Providers.");
                    continue;
                }

                if (!(providerSettings?.Enabled ?? false))
                {
                    errors.Add($"LlmGateway:Routing:{routeKey}: provider '{providerId}' is disabled but referenced by an enabled route.");
                }
            }

            if (string.Equals(routeKey, LlmGatewaySettings.ToRouteKey(LlmModality.Embeddings), StringComparison.OrdinalIgnoreCase))
            {
                providersNeedingEmbeddingsPath.Add(primaryProvider);
                foreach (var fallbackProvider in fallbackProviders)
                {
                    providersNeedingEmbeddingsPath.Add(fallbackProvider);
                }
            }
            else
            {
                providersNeedingChatPath.Add(primaryProvider);
                foreach (var fallbackProvider in fallbackProviders)
                {
                    providersNeedingChatPath.Add(fallbackProvider);
                }
            }

            ValidateRouteModelCompleteness(
                routeKey,
                primaryProvider,
                route.PrimaryModel,
                providers,
                "PrimaryProvider",
                errors);

            foreach (var fallback in route.FallbackProviders ?? new List<LlmGatewayProviderTargetSettings>())
            {
                if (string.IsNullOrWhiteSpace(fallback.Provider))
                {
                    continue;
                }

                var fallbackProvider = fallback.Provider.Trim();
                ValidateRouteModelCompleteness(
                    routeKey,
                    fallbackProvider,
                    fallback.Model,
                    providers,
                    "FallbackProviders",
                    errors);
            }
        }

        foreach (var experimentEntry in experiments.Where(entry => entry.Value?.Enabled ?? false))
        {
            var experimentLabel = (experimentEntry.Key ?? string.Empty).Trim();
            var experiment = experimentEntry.Value!;
            var branches = experiment.Branches ?? new List<LlmGatewayExperimentBranchSettings>();
            var usableBranches = branches
                .Where(branch => !string.IsNullOrWhiteSpace(branch.Branch)
                    && !string.IsNullOrWhiteSpace(branch.Provider)
                    && branch.WeightPercent > 0)
                .ToList();

            if (usableBranches.Count == 0)
            {
                errors.Add($"LlmGateway:Experiments:{experimentLabel}: enabled experiment must define at least one usable branch.");
                continue;
            }

            foreach (var branch in usableBranches)
            {
                var branchProvider = (branch.Provider ?? string.Empty).Trim();
                activeProviderIds.Add(branchProvider);
                providersNeedingChatPath.Add(branchProvider);

                if (!providers.TryGetValue(branchProvider, out var providerSettings))
                {
                    errors.Add($"LlmGateway:Experiments:{experimentLabel}: branch '{branch.Branch}' provider '{branchProvider}' is not defined in LlmGateway:Providers.");
                }
                else if (!(providerSettings?.Enabled ?? false))
                {
                    errors.Add($"LlmGateway:Experiments:{experimentLabel}: branch '{branch.Branch}' provider '{branchProvider}' is disabled.");
                }

                ValidateRouteModelCompleteness(
                    $"experiments:{experimentLabel}:branches:{branch.Branch}",
                    branchProvider,
                    branch.Model,
                    providers,
                    "Provider",
                    errors);

                var fallbackProviders = (branch.FallbackProviders ?? new List<LlmGatewayProviderTargetSettings>())
                    .Where(target => !string.IsNullOrWhiteSpace(target.Provider))
                    .Select(target => (target.Provider ?? string.Empty).Trim())
                    .ToList();

                foreach (var fallbackProvider in fallbackProviders)
                {
                    activeProviderIds.Add(fallbackProvider);
                    providersNeedingChatPath.Add(fallbackProvider);

                    if (!providers.TryGetValue(fallbackProvider, out var fallbackSettings))
                    {
                        errors.Add($"LlmGateway:Experiments:{experimentLabel}: branch '{branch.Branch}' fallback provider '{fallbackProvider}' is not defined in LlmGateway:Providers.");
                    }
                    else if (!(fallbackSettings?.Enabled ?? false))
                    {
                        errors.Add($"LlmGateway:Experiments:{experimentLabel}: branch '{branch.Branch}' fallback provider '{fallbackProvider}' is disabled.");
                    }
                }
            }
        }

        foreach (var providerId in activeProviderIds)
        {
            if (!providers.TryGetValue(providerId, out var provider) || provider is null)
            {
                continue;
            }

            var baseUrl = (provider.BaseUrl ?? string.Empty).Trim();
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            {
                errors.Add($"LlmGateway:Providers:{providerId}:BaseUrl must be a valid absolute URI for active providers.");
            }

            if (provider.TimeoutSeconds <= 0)
            {
                errors.Add($"LlmGateway:Providers:{providerId}:TimeoutSeconds must be greater than zero for active providers.");
            }

            if (provider.UseAuthorizationHeader)
            {
                var apiKey = (provider.ApiKey ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    errors.Add($"LlmGateway:Providers:{providerId}:ApiKey must be configured when UseAuthorizationHeader=true.");
                }
                else if (LooksLikePlaceholderSecret(apiKey))
                {
                    errors.Add($"LlmGateway:Providers:{providerId}:ApiKey appears to be placeholder secret material.");
                }
            }

            if (providersNeedingChatPath.Contains(providerId))
            {
                var chatPath = (provider.ChatCompletionsPath ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(chatPath))
                {
                    errors.Add($"LlmGateway:Providers:{providerId}:ChatCompletionsPath is required for active chat/tool/audio routes.");
                }
                else if (!chatPath.StartsWith("/", StringComparison.Ordinal))
                {
                    errors.Add($"LlmGateway:Providers:{providerId}:ChatCompletionsPath must start with '/'.");
                }
            }

            if (providersNeedingEmbeddingsPath.Contains(providerId))
            {
                var embeddingsPath = (provider.EmbeddingsPath ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(embeddingsPath))
                {
                    errors.Add($"LlmGateway:Providers:{providerId}:EmbeddingsPath is required for active embeddings routes.");
                }
                else if (!embeddingsPath.StartsWith("/", StringComparison.Ordinal))
                {
                    errors.Add($"LlmGateway:Providers:{providerId}:EmbeddingsPath must start with '/'.");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Gateway configuration validation failed:{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", errors)}");
        }
    }

    private static void ValidateRouteModelCompleteness(
        string routeKey,
        string providerId,
        string? routeModel,
        IReadOnlyDictionary<string, LlmGatewayProviderSettings> providers,
        string providerField,
        List<string> errors)
    {
        var model = (routeModel ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(model))
        {
            return;
        }

        if (providers.TryGetValue(providerId, out var provider)
            && !string.IsNullOrWhiteSpace(provider?.DefaultModel))
        {
            return;
        }

        errors.Add($"LlmGateway:Routing:{routeKey}:{providerField} '{providerId}' does not resolve a model (route model and provider default model are both missing).");
    }

    private static bool LooksLikePlaceholderSecret(string value)
    {
        return value.Contains("changeme", StringComparison.OrdinalIgnoreCase)
            || value.Contains("your_strong_password_here", StringComparison.OrdinalIgnoreCase)
            || value.Contains("__required", StringComparison.OrdinalIgnoreCase)
            || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);
    }
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using System.Diagnostics;

namespace TgAssistant.Infrastructure.LlmGateway;

public class LlmGatewayService : ILlmGateway
{
    private readonly IReadOnlyDictionary<string, ILlmProviderClient> _providers;
    private readonly ILlmRoutingPolicy _routingPolicy;
    private readonly LlmGatewaySettings _settings;
    private readonly ILogger<LlmGatewayService> _logger;
    private readonly LlmGatewayMetrics _metrics;

    public LlmGatewayService(
        IEnumerable<ILlmProviderClient> providers,
        ILlmRoutingPolicy routingPolicy,
        IOptions<LlmGatewaySettings> settings,
        ILogger<LlmGatewayService> logger,
        LlmGatewayMetrics metrics)
    {
        _providers = providers.ToDictionary(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase);
        _routingPolicy = routingPolicy;
        _settings = settings.Value;
        LlmGatewaySettingsValidator.ValidateOrThrow(_settings);
        _logger = logger;
        _metrics = metrics;
    }

    public async Task<LlmGatewayResponse> ExecuteAsync(LlmGatewayRequest request, CancellationToken ct = default)
    {
        var startedAt = Stopwatch.StartNew();
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
                var response = new LlmGatewayResponse
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

                _metrics.RecordSuccess(
                    request,
                    response.Provider,
                    response.Model,
                    response.FallbackApplied,
                    response.FallbackFromProvider,
                    providerResult.LatencyMs,
                    Math.Max(0, (int)startedAt.ElapsedMilliseconds),
                    response.Usage);

                var (spendUsd, spendSource) = ResolveSpendUsd(providerResult.ProviderId, providerResult.Usage);
                if (spendUsd.HasValue)
                {
                    _metrics.RecordSpend(
                        request,
                        response.Provider,
                        response.Model,
                        response.FallbackApplied,
                        response.FallbackFromProvider,
                        spendUsd.Value,
                        spendSource);
                }

                return response;
            }
            catch (LlmGatewayException ex)
            {
                lastFailure = ex;

                _metrics.RecordFailure(
                    request,
                    providerId,
                    providerRequest.Model,
                    index > 0,
                    index > 0 ? routingDecision.PrimaryProvider : null,
                    ex.Category.ToString(),
                    Math.Max(0, (int)startedAt.ElapsedMilliseconds));

                if (!ex.Retryable || index == providerOrder.Count - 1)
                {
                    throw;
                }

                _metrics.RecordFallback(
                    request,
                    providerId,
                    providerOrder[index + 1],
                    ex.Category.ToString());

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

        var exhausted = lastFailure ?? new LlmGatewayException("Gateway routing exhausted without producing a provider result.")
        {
            Category = LlmGatewayErrorCategory.Unknown,
            Modality = request.Modality,
            Retryable = false
        };

        _metrics.RecordFailure(
            request,
            exhausted.Provider ?? "unknown",
            request.ModelHint ?? "unknown",
            fallbackApplied: providerOrder.Count > 1,
            fallbackFromProvider: routingDecision.PrimaryProvider,
            errorCategory: exhausted.Category.ToString(),
            endToEndLatencyMs: Math.Max(0, (int)startedAt.ElapsedMilliseconds));

        throw exhausted;
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

        var allowRequestModelHint = isPrimaryAttempt && request.RouteOverride is not null;
        var resolved = isPrimaryAttempt
            ? routedModel ?? (allowRequestModelHint ? requestedModel : null) ?? providerSettings?.DefaultModel
            : routedModel ?? providerSettings?.DefaultModel;

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

    private (decimal? SpendUsd, string Source) ResolveSpendUsd(string providerId, LlmUsageInfo usage)
    {
        if (!string.Equals(providerId, OpenRouterProviderClient.ProviderIdValue, StringComparison.OrdinalIgnoreCase))
        {
            return (null, string.Empty);
        }

        if (usage.CostUsd is > 0m)
        {
            return (usage.CostUsd.Value, "provider");
        }

        var providerSettings = _settings.GetProvider(providerId);
        if (providerSettings is null)
        {
            return (null, string.Empty);
        }

        var promptTokens = usage.PromptTokens ?? 0;
        var completionTokens = usage.CompletionTokens ?? 0;
        var totalTokens = usage.TotalTokens ?? (promptTokens + completionTokens);
        if (totalTokens <= 0)
        {
            return (null, string.Empty);
        }

        decimal? derivedSpend = null;
        if (providerSettings.PromptCostUsdPer1kTokens is > 0m || providerSettings.CompletionCostUsdPer1kTokens is > 0m)
        {
            var promptPrice = providerSettings.PromptCostUsdPer1kTokens ?? 0m;
            var completionPrice = providerSettings.CompletionCostUsdPer1kTokens ?? providerSettings.PromptCostUsdPer1kTokens ?? 0m;
            derivedSpend = ((promptTokens * promptPrice) + (completionTokens * completionPrice)) / 1000m;
        }
        else if (providerSettings.TotalCostUsdPer1kTokens is > 0m)
        {
            derivedSpend = (totalTokens * providerSettings.TotalCostUsdPer1kTokens.Value) / 1000m;
        }

        return derivedSpend is > 0m
            ? (derivedSpend.Value, "derived")
            : (null, string.Empty);
    }
}

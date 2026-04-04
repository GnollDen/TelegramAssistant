using System.Diagnostics;
using System.Diagnostics.Metrics;
using TgAssistant.Core.Models;

namespace TgAssistant.Infrastructure.LlmGateway;

public sealed class LlmGatewayMetrics
{
    public const string MeterName = "TgAssistant.LlmGateway";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private readonly Counter<long> _requestsTotal = Meter.CreateCounter<long>("llm_gateway_requests_total");
    private readonly Counter<long> _failuresTotal = Meter.CreateCounter<long>("llm_gateway_failures_total");
    private readonly Counter<long> _tokensTotal = Meter.CreateCounter<long>("llm_gateway_tokens_total");
    private readonly Counter<long> _promptTokensTotal = Meter.CreateCounter<long>("llm_gateway_prompt_tokens_total");
    private readonly Counter<long> _completionTokensTotal = Meter.CreateCounter<long>("llm_gateway_completion_tokens_total");
    private readonly Counter<long> _fallbackTotal = Meter.CreateCounter<long>("llm_gateway_fallback_total");
    private readonly Counter<double> _spendUsdTotal = Meter.CreateCounter<double>("llm_gateway_spend_usd_total");
    private readonly Histogram<double> _providerLatencyMs = Meter.CreateHistogram<double>("llm_gateway_provider_latency_ms");
    private readonly Histogram<double> _endToEndLatencyMs = Meter.CreateHistogram<double>("llm_gateway_end_to_end_latency_ms");
    private readonly Histogram<double> _tokensPerSecond = Meter.CreateHistogram<double>("llm_gateway_tokens_per_second");
    private readonly Histogram<double> _requestSpendUsd = Meter.CreateHistogram<double>("llm_gateway_request_spend_usd");

    public void RecordSuccess(
        LlmGatewayRequest request,
        string provider,
        string model,
        bool fallbackApplied,
        string? fallbackFromProvider,
        int providerLatencyMs,
        int endToEndLatencyMs,
        LlmUsageInfo usage)
    {
        var tags = BuildCommonTags(request, provider, model, "success", fallbackApplied, fallbackFromProvider, null);
        _requestsTotal.Add(1, tags);
        _providerLatencyMs.Record(providerLatencyMs, tags);
        _endToEndLatencyMs.Record(endToEndLatencyMs, tags);

        AddTokenMetrics(tags, usage, providerLatencyMs);
    }

    public void RecordFailure(
        LlmGatewayRequest request,
        string provider,
        string model,
        bool fallbackApplied,
        string? fallbackFromProvider,
        string? errorCategory,
        int endToEndLatencyMs)
    {
        var tags = BuildCommonTags(request, provider, model, "error", fallbackApplied, fallbackFromProvider, errorCategory);
        _requestsTotal.Add(1, tags);
        _failuresTotal.Add(1, tags);
        _endToEndLatencyMs.Record(endToEndLatencyMs, tags);
    }

    public void RecordFallback(
        LlmGatewayRequest request,
        string fromProvider,
        string toProvider,
        string reason)
    {
        var tags = new TagList
        {
            { "from_provider", Normalize(fromProvider) },
            { "to_provider", Normalize(toProvider) },
            { "reason", Normalize(reason) },
            { "modality", request.Modality.ToString().ToLowerInvariant() }
        };

        if (!string.IsNullOrWhiteSpace(request.Trace.PathKey))
        {
            tags.Add("route_key", request.Trace.PathKey);
        }

        _fallbackTotal.Add(1, tags);
    }

    public void RecordSpend(
        LlmGatewayRequest request,
        string provider,
        string model,
        bool fallbackApplied,
        string? fallbackFromProvider,
        decimal spendUsd,
        string spendSource)
    {
        if (spendUsd <= 0m)
        {
            return;
        }

        var tags = BuildCommonTags(request, provider, model, "success", fallbackApplied, fallbackFromProvider, null);
        tags.Add("spend_source", Normalize(spendSource));
        var spendValue = (double)spendUsd;
        _spendUsdTotal.Add(spendValue, tags);
        _requestSpendUsd.Record(spendValue, tags);
    }

    private void AddTokenMetrics(TagList tags, LlmUsageInfo usage, int providerLatencyMs)
    {
        var promptTokens = usage.PromptTokens ?? 0;
        var completionTokens = usage.CompletionTokens ?? 0;
        var totalTokens = usage.TotalTokens ?? (promptTokens + completionTokens);

        if (promptTokens > 0)
        {
            _promptTokensTotal.Add(promptTokens, tags);
        }

        if (completionTokens > 0)
        {
            _completionTokensTotal.Add(completionTokens, tags);
        }

        if (totalTokens > 0)
        {
            _tokensTotal.Add(totalTokens, tags);
            if (providerLatencyMs > 0)
            {
                var tps = totalTokens / (providerLatencyMs / 1000d);
                _tokensPerSecond.Record(tps, tags);
            }
        }
    }

    private static TagList BuildCommonTags(
        LlmGatewayRequest request,
        string provider,
        string model,
        string status,
        bool fallbackApplied,
        string? fallbackFromProvider,
        string? errorCategory)
    {
        var tags = new TagList
        {
            { "provider", Normalize(provider) },
            { "model", Normalize(model) },
            { "modality", request.Modality.ToString().ToLowerInvariant() },
            { "status", Normalize(status) },
            { "route_kind", fallbackApplied ? "fallback" : "primary" },
            { "fallback_applied", fallbackApplied ? "true" : "false" }
        };

        if (!string.IsNullOrWhiteSpace(request.Trace.PathKey))
        {
            tags.Add("route_key", request.Trace.PathKey);
        }

        if (!string.IsNullOrWhiteSpace(fallbackFromProvider))
        {
            tags.Add("fallback_from_provider", Normalize(fallbackFromProvider));
        }

        if (!string.IsNullOrWhiteSpace(errorCategory))
        {
            tags.Add("error_category", Normalize(errorCategory));
        }

        return tags;
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }
}

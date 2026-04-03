using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class OpenRouterEmbeddingService : ITextEmbeddingGenerator
{
    private readonly HttpClient _http;
    private readonly ILlmGateway _gateway;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<OpenRouterEmbeddingService> _logger;

    public OpenRouterEmbeddingService(
        HttpClient http,
        ILlmGateway gateway,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<OpenRouterEmbeddingService> logger)
    {
        _http = http;
        _gateway = gateway;
        _usageRepository = usageRepository;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
        _ = _http;
    }

    public async Task<float[]> GenerateAsync(string model, string input, CancellationToken ct = default)
    {
        var budgetDecision = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = "embedding_generation",
            Modality = BudgetModalities.Embeddings,
            IsImportScope = false,
            IsOptionalPath = true
        }, ct);
        if (budgetDecision.ShouldPausePath || budgetDecision.ShouldDegradeOptionalPath)
        {
            _logger.LogWarning(
                "Embedding generation skipped by budget guardrail. state={State}, reason={Reason}",
                budgetDecision.State,
                budgetDecision.Reason);
            return Array.Empty<float>();
        }

        var req = new LlmGatewayRequest
        {
            Modality = LlmModality.Embeddings,
            TaskKey = "embedding_generation",
            ResponseMode = LlmResponseMode.EmbeddingVector,
            RouteOverride = BuildOpenRouterRouteOverride(model),
            EmbeddingInputs = [input],
            Limits = new LlmExecutionLimits
            {
                TimeoutMs = 60000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "embedding_generation",
                IsImportScope = false,
                IsOptionalPath = true,
                ScopeTags = ["stage5", "embedding"]
            }
        };

        try
        {
            var response = await _gateway.ExecuteAsync(req, ct);
            var vector = response.Output.Embeddings.FirstOrDefault() ?? Array.Empty<float>();
            if (vector.Length == 0)
            {
                _logger.LogDebug("Gateway embedding response missing vector for model={Model}", model);
                return Array.Empty<float>();
            }

            await TryLogUsageAsync(response.Usage, response.Model, response.LatencyMs, ct);
            return vector;
        }
        catch (LlmGatewayException ex)
        {
            if (ex.Category == LlmGatewayErrorCategory.Quota)
            {
                await _budgetGuardrailService.RegisterQuotaBlockedAsync(
                    pathKey: "embedding_generation",
                    modality: BudgetModalities.Embeddings,
                    reason: "quota_like_provider_failure",
                    isImportScope: false,
                    isOptionalPath: true,
                    ct: ct);
            }

            throw new HttpRequestException(
                $"Gateway embedding error provider={ex.Provider ?? "unknown"} category={ex.Category} status={ex.HttpStatus}; {ex.Message}",
                ex,
                ex.HttpStatus);
        }
    }

    private async Task TryLogUsageAsync(LlmUsageInfo usage, string model, int latencyMs, CancellationToken ct)
    {
        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = "embedding",
            Model = model,
            PromptTokens = usage.PromptTokens ?? 0,
            CompletionTokens = usage.CompletionTokens ?? 0,
            TotalTokens = usage.TotalTokens ?? 0,
            CostUsd = usage.CostUsd ?? 0m,
            LatencyMs = Math.Max(0, latencyMs),
            CreatedAt = DateTime.UtcNow
        }, ct);

        _logger.LogDebug("Embedding usage logged model={Model} tokens={Tokens}", model, usage.TotalTokens ?? 0);
    }

    private static LlmGatewayRouteOverride BuildOpenRouterRouteOverride(string model)
    {
        var routeOverride = new LlmGatewayRouteOverride
        {
            PrimaryProvider = "openrouter"
        };
        if (!string.IsNullOrWhiteSpace(model))
        {
            routeOverride.ProviderModelHints["openrouter"] = model.Trim();
        }

        return routeOverride;
    }
}

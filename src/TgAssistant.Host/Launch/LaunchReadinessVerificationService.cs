using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage6;
using TgAssistant.Intelligence.Stage6.Control;
using TgAssistant.Intelligence.Stage6.Outcome;
using TgAssistant.Web.Read;

namespace TgAssistant.Host.Launch;

public class LaunchReadinessVerificationService
{
    private readonly FoundationDomainVerificationService _foundationVerificationService;
    private readonly WebReadVerificationService _webReadVerificationService;
    private readonly WebReviewVerificationService _webReviewVerificationService;
    private readonly WebSearchVerificationService _webSearchVerificationService;
    private readonly WebOpsVerificationService _webOpsVerificationService;
    private readonly OutcomeVerificationService _outcomeVerificationService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly EvalVerificationService _evalVerificationService;
    private readonly ILogger<LaunchReadinessVerificationService> _logger;

    public LaunchReadinessVerificationService(
        FoundationDomainVerificationService foundationVerificationService,
        WebReadVerificationService webReadVerificationService,
        WebReviewVerificationService webReviewVerificationService,
        WebSearchVerificationService webSearchVerificationService,
        WebOpsVerificationService webOpsVerificationService,
        OutcomeVerificationService outcomeVerificationService,
        IBudgetGuardrailService budgetGuardrailService,
        EvalVerificationService evalVerificationService,
        ILogger<LaunchReadinessVerificationService> logger)
    {
        _foundationVerificationService = foundationVerificationService;
        _webReadVerificationService = webReadVerificationService;
        _webReviewVerificationService = webReviewVerificationService;
        _webSearchVerificationService = webSearchVerificationService;
        _webOpsVerificationService = webOpsVerificationService;
        _outcomeVerificationService = outcomeVerificationService;
        _budgetGuardrailService = budgetGuardrailService;
        _evalVerificationService = evalVerificationService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await RunStepAsync("foundation", token => _foundationVerificationService.RunAsync(token), ct);
        await RunStepAsync("web-read", token => _webReadVerificationService.RunAsync(token), ct);
        await RunStepAsync("web-review", token => _webReviewVerificationService.RunAsync(token), ct);
        await RunStepAsync("web-search", token => _webSearchVerificationService.RunAsync(token), ct);
        await RunStepAsync("ops-web", token => _webOpsVerificationService.RunAsync(token), ct);
        await RunStepAsync("outcome", token => _outcomeVerificationService.RunAsync(token), ct);
        await RunStepAsync("budget-visibility", RunBudgetVisibilityStepAsync, ct);
        await RunStepAsync("eval", token => _evalVerificationService.RunAsync(token), ct);

        _logger.LogInformation(
            "Launch readiness bundle passed. Verified: foundation, web-read, web-review, web-search, ops-web, outcome, budget-visibility, eval.");
    }

    private async Task RunStepAsync(string stepName, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await action(ct);
            _logger.LogInformation("Launch readiness step passed: {StepName}", stepName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Launch smoke failed at step '{stepName}': {ex.Message}", ex);
        }
    }

    private async Task RunBudgetVisibilityStepAsync(CancellationToken ct)
    {
        var pathKey = $"launch_smoke_budget_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var decision = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = pathKey,
            Modality = BudgetModalities.TextAnalysis,
            IsImportScope = false,
            IsOptionalPath = true
        }, ct);

        if (string.IsNullOrWhiteSpace(decision.State))
        {
            throw new InvalidOperationException("Budget guardrail decision did not contain state.");
        }

        var states = await _budgetGuardrailService.GetOperationalStatesAsync(ct);
        if (!states.Any(x => string.Equals(x.PathKey, pathKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Budget guardrail visibility failed: evaluated path was not persisted.");
        }
    }
}

using Microsoft.Extensions.Logging;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Host.Launch;

public class LaunchReadinessVerificationService
{
    private readonly FoundationDomainVerificationService _foundationVerificationService;
    private readonly Stage5VerificationService _stage5VerificationService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<LaunchReadinessVerificationService> _logger;

    public LaunchReadinessVerificationService(
        FoundationDomainVerificationService foundationVerificationService,
        Stage5VerificationService stage5VerificationService,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<LaunchReadinessVerificationService> logger)
    {
        _foundationVerificationService = foundationVerificationService;
        _stage5VerificationService = stage5VerificationService;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await RunStepAsync("foundation", token => _foundationVerificationService.RunAsync(token), ct);
        await RunStepAsync("stage5", token => _stage5VerificationService.RunAsync(token), ct);
        await RunStepAsync("budget-visibility", RunBudgetVisibilityStepAsync, ct);

        _logger.LogInformation(
            "Launch readiness verification bundle passed. Verified: foundation, stage5, budget-visibility.");
    }

    private async Task RunStepAsync(string stepName, Func<CancellationToken, Task> action, CancellationToken ct)
    {
        try
        {
            await action(ct);
            _logger.LogInformation("Launch readiness verification step passed: {StepName}", stepName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Launch verification failed at step '{stepName}': {ex.Message}", ex);
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

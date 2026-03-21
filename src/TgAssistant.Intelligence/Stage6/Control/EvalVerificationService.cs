using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Control;

public class EvalVerificationService
{
    private readonly IEvalHarnessService _evalHarnessService;
    private readonly IEvalRepository _evalRepository;
    private readonly ILogger<EvalVerificationService> _logger;

    public EvalVerificationService(
        IEvalHarnessService evalHarnessService,
        IEvalRepository evalRepository,
        ILogger<EvalVerificationService> logger)
    {
        _evalHarnessService = evalHarnessService;
        _evalRepository = evalRepository;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        var runName = "eval_smoke";
        var run = await _evalHarnessService.RunAsync(new EvalRunRequest
        {
            RunName = runName,
            Scenarios = ["budget_visibility", "stage5_config"],
            Actor = "eval_smoke"
        }, ct);

        if (run.RunId == Guid.Empty)
        {
            throw new InvalidOperationException("Eval smoke failed: run id was not generated.");
        }

        if (run.Scenarios.Count < 2)
        {
            throw new InvalidOperationException("Eval smoke failed: expected at least two recorded scenarios.");
        }

        var persistedRun = await _evalRepository.GetRunByIdAsync(run.RunId, ct)
            ?? throw new InvalidOperationException("Eval smoke failed: persisted run not found.");
        var persistedScenarios = await _evalRepository.GetScenarioResultsAsync(run.RunId, ct);
        if (persistedScenarios.Count != run.Scenarios.Count)
        {
            throw new InvalidOperationException("Eval smoke failed: scenario result persistence mismatch.");
        }

        if (string.IsNullOrWhiteSpace(persistedRun.MetricsJson))
        {
            throw new InvalidOperationException("Eval smoke failed: run metrics are empty.");
        }

        using var metricsDoc = JsonDocument.Parse(persistedRun.MetricsJson);
        if (!metricsDoc.RootElement.TryGetProperty("scenario_count", out _))
        {
            throw new InvalidOperationException("Eval smoke failed: run metrics do not contain scenario_count.");
        }

        _logger.LogInformation(
            "Eval smoke passed. run_id={RunId}, run_name={RunName}, passed={Passed}, scenarios={ScenarioCount}",
            run.RunId,
            run.RunName,
            run.Passed,
            run.Scenarios.Count);
    }
}

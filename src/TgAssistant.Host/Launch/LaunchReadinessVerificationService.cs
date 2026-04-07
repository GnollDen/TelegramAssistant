using Microsoft.Extensions.Logging;
using System.Text.Json;
using TgAssistant.Core.Models;
using TgAssistant.Infrastructure.Database;
using TgAssistant.Intelligence.Stage5;
using TgAssistant.Core.Interfaces;

namespace TgAssistant.Host.Launch;

public class LaunchReadinessVerificationService
{
    private static readonly string[] RequiredPhaseBGateSteps =
    [
        "phase-b-stage-semantic-contract-proof",
        "phase-b-temporal-person-state-proof",
        "phase-b-person-history-proof",
        "phase-b-current-world-proof",
        "phase-b-conditional-modeling-proof",
        "phase-b-iterative-reintegration-proof",
        "phase-b-ai-conflict-session-v1-proof",
        "phase-b-stage7-dossier-profile-smoke",
        "phase-b-stage7-timeline-smoke",
        "phase-b-stage8-recompute-smoke"
    ];

    private readonly IServiceProvider _services;
    private readonly FoundationDomainVerificationService _foundationVerificationService;
    private readonly Stage5VerificationService _stage5VerificationService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<LaunchReadinessVerificationService> _logger;

    public LaunchReadinessVerificationService(
        IServiceProvider services,
        FoundationDomainVerificationService foundationVerificationService,
        Stage5VerificationService stage5VerificationService,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<LaunchReadinessVerificationService> logger)
    {
        _services = services;
        _foundationVerificationService = foundationVerificationService;
        _stage5VerificationService = stage5VerificationService;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        InvalidatePhaseBMarker();
        await RunStepAsync("foundation", token => _foundationVerificationService.RunAsync(token), ct);
        await RunStepAsync("stage5", token => _stage5VerificationService.RunAsync(token), ct);
        await RunStepAsync("budget-visibility", RunBudgetVisibilityStepAsync, ct);
        await RunStepAsync("phase-b-stage-semantic-contract-proof", RunPhaseBStageSemanticContractProofAsync, ct);
        await RunStepAsync("phase-b-temporal-person-state-proof", RunPhaseBTemporalPersonStateProofAsync, ct);
        await RunStepAsync("phase-b-person-history-proof", RunPhaseBPersonHistoryProofAsync, ct);
        await RunStepAsync("phase-b-current-world-proof", RunPhaseBCurrentWorldApproximationProofAsync, ct);
        await RunStepAsync("phase-b-conditional-modeling-proof", RunPhaseBConditionalModelingProofAsync, ct);
        await RunStepAsync("phase-b-iterative-reintegration-proof", RunPhaseBIterativeReintegrationProofAsync, ct);
        await RunStepAsync("phase-b-ai-conflict-session-v1-proof", RunPhaseBAiConflictSessionV1ProofAsync, ct);
        await RunStepAsync("phase-b-stage7-dossier-profile-smoke", RunPhaseBStage7DossierProfileSmokeAsync, ct);
        await RunStepAsync("phase-b-stage7-timeline-smoke", RunPhaseBStage7TimelineSmokeAsync, ct);
        await RunStepAsync("phase-b-stage8-recompute-smoke", RunPhaseBStage8RecomputeSmokeAsync, ct);
        await WritePhaseBMarkerAsync(ct);

        _logger.LogInformation(
            "Launch readiness verification bundle passed. Verified: foundation, stage5, budget-visibility, and phase-b proofs.");
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

    private async Task RunPhaseBTemporalPersonStateProofAsync(CancellationToken ct)
        => _ = await TemporalPersonStateProofRunner.RunAsync(_services, BuildPhaseBOutputPath("temporal-person-state-proof.json"), ct);

    private async Task RunPhaseBPersonHistoryProofAsync(CancellationToken ct)
        => _ = await PersonHistoryProofRunner.RunAsync(_services, BuildPhaseBOutputPath("person-history-proof.json"), ct);

    private async Task RunPhaseBCurrentWorldApproximationProofAsync(CancellationToken ct)
        => _ = await CurrentWorldApproximationProofRunner.RunAsync(_services, BuildPhaseBOutputPath("current-world-approximation-proof.json"), ct);

    private async Task RunPhaseBConditionalModelingProofAsync(CancellationToken ct)
        => _ = await ConditionalModelingProofRunner.RunAsync(_services, BuildPhaseBOutputPath("conditional-modeling-proof.json"), ct);

    private async Task RunPhaseBIterativeReintegrationProofAsync(CancellationToken ct)
        => _ = await IterativeReintegrationProofRunner.RunAsync(_services, BuildPhaseBOutputPath("iterative-reintegration-proof.json"), ct);

    private async Task RunPhaseBAiConflictSessionV1ProofAsync(CancellationToken ct)
        => _ = await AiConflictResolutionSessionV1ProofRunner.RunAsync(_services, BuildPhaseBOutputPath("ai-conflict-resolution-session-v1-proof.json"), ct);

    private async Task RunPhaseBStageSemanticContractProofAsync(CancellationToken ct)
        => _ = await StageSemanticContractProofRunner.RunAsync(BuildPhaseBOutputPath("stage-semantic-contract-proof.json"), ct);

    private static Task RunPhaseBStage7DossierProfileSmokeAsync(CancellationToken ct)
        => Stage7DossierProfileSmokeRunner.RunAsync(ct);

    private static Task RunPhaseBStage7TimelineSmokeAsync(CancellationToken ct)
        => Stage7TimelineSmokeRunner.RunAsync(ct);

    private static Task RunPhaseBStage8RecomputeSmokeAsync(CancellationToken ct)
        => Stage8RecomputeQueueSmokeRunner.RunAsync(ct);

    private static string BuildPhaseBOutputPath(string fileName)
    {
        var root = Path.Combine(HostArtifactsPathResolver.ResolveHostArtifactsRoot(), "phase-b", "launch-smoke");
        Directory.CreateDirectory(root);
        return Path.Combine(root, fileName);
    }

    private static async Task WritePhaseBMarkerAsync(CancellationToken ct)
    {
        var markerPath = BuildPhaseBOutputPath("phase-b-launch-gate.marker.json");
        var payload = new
        {
            passed = true,
            generatedAtUtc = DateTime.UtcNow,
            source = "LaunchReadinessVerificationService",
            requiredPhaseBGateSteps = RequiredPhaseBGateSteps
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(markerPath, json, ct);
    }

    private static void InvalidatePhaseBMarker()
    {
        var markerPath = BuildPhaseBOutputPath("phase-b-launch-gate.marker.json");
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
    }
}

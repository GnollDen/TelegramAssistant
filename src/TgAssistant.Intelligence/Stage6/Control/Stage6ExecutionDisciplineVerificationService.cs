// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage6;

namespace TgAssistant.Intelligence.Stage6.Control;

public class Stage6ExecutionDisciplineVerificationService
{
    private readonly IBotChatService _botChatService;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly BudgetVerificationService _budgetVerificationService;
    private readonly ILogger<Stage6ExecutionDisciplineVerificationService> _logger;

    public Stage6ExecutionDisciplineVerificationService(
        IBotChatService botChatService,
        IBudgetGuardrailService budgetGuardrailService,
        BudgetVerificationService budgetVerificationService,
        ILogger<Stage6ExecutionDisciplineVerificationService> logger)
    {
        _botChatService = botChatService;
        _budgetGuardrailService = budgetGuardrailService;
        _budgetVerificationService = budgetVerificationService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await VerifyCancellationStopsStage6ChatPathAsync();
        await VerifyPersistedQuotaCooldownStateAsync(ct);
        await _budgetVerificationService.RunAsync(ct);

        _logger.LogInformation("Stage6 execution discipline smoke passed.");
    }

    private async Task VerifyCancellationStopsStage6ChatPathAsync()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        try
        {
            _ = await _botChatService.GenerateReplyWithDiagnosticsAsync(
                userMessage: "stage6_execution_cancel_probe",
                transportChatId: 1,
                sourceMessageId: 1,
                senderId: 1,
                ct: canceled.Token);
            throw new InvalidOperationException("Stage6 execution smoke failed: canceled chat request unexpectedly completed.");
        }
        catch (OperationCanceledException)
        {
            // Expected: cancellation must stop Stage6 paid path before expensive downstream work starts.
        }
    }

    private async Task VerifyPersistedQuotaCooldownStateAsync(CancellationToken ct)
    {
        var pathKey = $"smoke_stage6_exec_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        var quotaDecision = await _budgetGuardrailService.RegisterQuotaBlockedAsync(
            pathKey: pathKey,
            modality: BudgetModalities.TextAnalysis,
            reason: "stage6_execution_smoke",
            isImportScope: false,
            isOptionalPath: false,
            ct: ct);

        if (!quotaDecision.ShouldPausePath
            || !string.Equals(quotaDecision.State, BudgetPathStates.QuotaBlocked, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Stage6 execution smoke failed: quota-blocked state not applied.");
        }

        var followupDecision = await _budgetGuardrailService.EvaluatePathAsync(new BudgetPathCheckRequest
        {
            PathKey = pathKey,
            Modality = BudgetModalities.TextAnalysis,
            IsOptionalPath = false,
            IsImportScope = false
        }, ct);
        if (!followupDecision.ShouldPausePath
            || !string.Equals(followupDecision.State, BudgetPathStates.QuotaBlocked, StringComparison.OrdinalIgnoreCase)
            || !followupDecision.BlockedUntilUtc.HasValue)
        {
            throw new InvalidOperationException("Stage6 execution smoke failed: persisted quota cooldown not honored on re-evaluation.");
        }

        var state = (await _budgetGuardrailService.GetOperationalStatesAsync(ct))
            .FirstOrDefault(x => string.Equals(x.PathKey, pathKey, StringComparison.Ordinal));
        if (state == null)
        {
            throw new InvalidOperationException("Stage6 execution smoke failed: quota cooldown state not visible operationally.");
        }

        using var detailsDoc = JsonDocument.Parse(state.DetailsJson);
        if (!detailsDoc.RootElement.TryGetProperty("blocked_until_utc", out var blockedUntilNode)
            || blockedUntilNode.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(blockedUntilNode.GetString()))
        {
            throw new InvalidOperationException("Stage6 execution smoke failed: persisted quota cooldown is missing blocked_until_utc.");
        }
    }
}

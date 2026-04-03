using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage5;

public class EditDiffTextCompletionService
{
    private readonly AnalysisSettings _analysisSettings;
    private readonly LlmGatewaySettings _gatewaySettings;
    private readonly OpenRouterAnalysisService _legacyService;
    private readonly ILlmGateway _gateway;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetGuardrailService _budgetGuardrailService;
    private readonly ILogger<EditDiffTextCompletionService> _logger;

    public EditDiffTextCompletionService(
        IOptions<AnalysisSettings> analysisSettings,
        IOptions<LlmGatewaySettings> gatewaySettings,
        OpenRouterAnalysisService legacyService,
        ILlmGateway gateway,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILogger<EditDiffTextCompletionService> logger)
    {
        _analysisSettings = analysisSettings.Value;
        _gatewaySettings = gatewaySettings.Value;
        _legacyService = legacyService;
        _gateway = gateway;
        _usageRepository = usageRepository;
        _budgetGuardrailService = budgetGuardrailService;
        _logger = logger;
    }

    public bool UsesGatewayPilot => _analysisSettings.EditDiffGatewayEnabled && _gatewaySettings.Enabled;

    public async Task<EditDiffTextCompletionResult> CompleteAsync(
        EditDiffCandidate candidate,
        string legacyModel,
        CancellationToken ct)
    {
        if (!UsesGatewayPilot)
        {
            if (_analysisSettings.EditDiffGatewayEnabled && !_gatewaySettings.Enabled)
            {
                _logger.LogWarning(
                    "Edit-diff gateway pilot flag is enabled but LlmGateway:Enabled=false. Falling back to legacy OpenRouter path.");
            }

            var raw = await _legacyService.CompleteTextAsync(
                legacyModel,
                EditDiffPromptBuilder.BuildSystemPrompt(),
                EditDiffPromptBuilder.BuildUserPrompt(candidate),
                Math.Clamp(_analysisSettings.EditDiffMaxTokens, 128, 800),
                "edit_diff",
                ct);

            return new EditDiffTextCompletionResult
            {
                RawText = NormalizeCompletionPayload(raw),
                Transport = "legacy_openrouter",
                Provider = "openrouter",
                Model = legacyModel
            };
        }

        var request = new LlmGatewayRequest
        {
            Modality = LlmModality.TextChat,
            TaskKey = "edit_diff",
            ResponseMode = LlmResponseMode.Text,
            Limits = new LlmExecutionLimits
            {
                MaxTokens = Math.Clamp(_analysisSettings.EditDiffMaxTokens, 128, 800),
                Temperature = 0.2f,
                TimeoutMs = Math.Max(1, _analysisSettings.HttpTimeoutSeconds) * 1000
            },
            Trace = new LlmTraceContext
            {
                PathKey = "stage5_edit_diff",
                RequestId = $"edit-diff-{candidate.MessageId}-{candidate.EditedAtUtc:yyyyMMddHHmmss}",
                IsImportScope = false,
                IsOptionalPath = true,
                ScopeTags =
                [
                    "stage5",
                    "edit_diff"
                ]
            },
            Messages =
            [
                LlmGatewayMessage.FromText(LlmMessageRole.System, EditDiffPromptBuilder.BuildSystemPrompt()),
                LlmGatewayMessage.FromText(LlmMessageRole.User, EditDiffPromptBuilder.BuildUserPrompt(candidate))
            ]
        };

        try
        {
            var response = await _gateway.ExecuteAsync(request, ct);
            await LogUsageAsync(response, ct);

            _logger.LogInformation(
                "Edit-diff gateway completion succeeded. provider={Provider}, model={Model}, request_id={RequestId}, latency_ms={LatencyMs}, fallback_applied={FallbackApplied}, fallback_from={FallbackFromProvider}",
                response.Provider,
                response.Model,
                response.RequestId ?? "n/a",
                response.LatencyMs,
                response.FallbackApplied,
                response.FallbackFromProvider ?? "n/a");

            return new EditDiffTextCompletionResult
            {
                RawText = NormalizeCompletionPayload(response.Output.StructuredPayloadJson ?? response.Output.Text),
                Transport = "llm_gateway",
                Provider = response.Provider,
                Model = response.Model,
                RequestId = response.RequestId,
                LatencyMs = response.LatencyMs,
                FallbackApplied = response.FallbackApplied,
                FallbackFromProvider = response.FallbackFromProvider
            };
        }
        catch (LlmGatewayException ex)
        {
            if (ex.Category == LlmGatewayErrorCategory.Quota)
            {
                await _budgetGuardrailService.RegisterQuotaBlockedAsync(
                    pathKey: "stage5_edit_diff",
                    modality: BudgetModalities.TextAnalysis,
                    reason: "quota_like_provider_failure",
                    isImportScope: false,
                    isOptionalPath: true,
                    ct: ct);
            }

            _logger.LogWarning(
                ex,
                "Edit-diff gateway completion failed. category={Category}, provider={Provider}, retryable={Retryable}, status={StatusCode}, reason_code={RawReasonCode}",
                ex.Category,
                ex.Provider ?? "n/a",
                ex.Retryable,
                ex.HttpStatus is null ? "n/a" : ((int)ex.HttpStatus.Value).ToString(),
                ex.RawReasonCode ?? "n/a");
            throw;
        }
    }

    private async Task LogUsageAsync(LlmGatewayResponse response, CancellationToken ct)
    {
        if (response.Usage.TotalTokens is null
            && response.Usage.PromptTokens is null
            && response.Usage.CompletionTokens is null
            && response.Usage.CostUsd is null)
        {
            return;
        }

        await _usageRepository.LogAsync(new AnalysisUsageEvent
        {
            Phase = "edit_diff",
            Model = response.Model,
            PromptTokens = response.Usage.PromptTokens ?? 0,
            CompletionTokens = response.Usage.CompletionTokens ?? 0,
            TotalTokens = response.Usage.TotalTokens ?? 0,
            CostUsd = response.Usage.CostUsd ?? 0m,
            LatencyMs = Math.Max(0, response.LatencyMs),
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private static string NormalizeCompletionPayload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed
            .Split('\n', StringSplitOptions.TrimEntries)
            .ToList();

        if (lines.Count == 0)
        {
            return string.Empty;
        }

        if (lines[0].StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].StartsWith("```", StringComparison.Ordinal))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines).Trim();
    }
}

public static class EditDiffPromptBuilder
{
    public static string BuildSystemPrompt()
    {
        return """
You analyze Telegram message edits for memory impact.
Return ONLY JSON with fields:
- classification: typo | formatting | minor_rephrase | meaning_changed | important_added | important_removed | message_deleted | unknown
- summary: concise Russian summary of what changed (max 220 chars)
- should_affect_memory: boolean
- added_important: boolean
- removed_important: boolean
- confidence: number 0..1
Rules:
- Pure typo/punctuation/casing fixes => should_affect_memory=false
- If meaningful facts/time/place/person/commitments changed => should_affect_memory=true
- If deletion removed meaningful content => removed_important=true, should_affect_memory=true
""";
    }

    public static string BuildUserPrompt(EditDiffCandidate candidate)
    {
        return $"""
chat_id: {candidate.ChatId}
message_id: {candidate.MessageId}
edited_at_utc: {candidate.EditedAtUtc:O}

BEFORE:
{candidate.BeforeText}

AFTER:
{candidate.AfterText}
""";
    }

    public static string ResolveLegacyModel(AnalysisSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CheapModel))
        {
            return settings.CheapModel.Trim();
        }

        return !string.IsNullOrWhiteSpace(settings.CheapBaselineModel)
            ? settings.CheapBaselineModel.Trim()
            : "openai/gpt-4o-mini";
    }
}

public class EditDiffTextCompletionResult
{
    public string RawText { get; set; } = string.Empty;
    public string Transport { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int? LatencyMs { get; set; }
    public bool FallbackApplied { get; set; }
    public string? FallbackFromProvider { get; set; }
}

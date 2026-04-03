using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Host.Launch;

public static class EditDiffPilotSmokeRunner
{
    public static async Task<EditDiffPilotSmokeResult> RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var analysisSettings = services.GetRequiredService<IOptions<AnalysisSettings>>().Value;
        var legacyService = services.GetRequiredService<OpenRouterAnalysisService>();
        var gateway = services.GetRequiredService<TgAssistant.Core.Interfaces.ILlmGateway>();
        var contractNormalizer = services.GetRequiredService<ILlmContractNormalizer>();
        var usageRepository = services.GetRequiredService<TgAssistant.Core.Interfaces.IAnalysisUsageRepository>();
        var budgetGuardrailService = services.GetRequiredService<TgAssistant.Core.Interfaces.IBudgetGuardrailService>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        var candidate = new EditDiffCandidate
        {
            ChatId = 885574984,
            MessageId = 40401,
            EditedAtUtc = DateTime.UtcNow,
            BeforeText = "Встреча завтра в 19:00 у метро.",
            AfterText = "Встреча завтра в 19:00 у метро!"
        };
        var legacyModel = EditDiffPromptBuilder.ResolveLegacyModel(analysisSettings);

        var legacyPath = new EditDiffTextCompletionService(
            Options.Create(new AnalysisSettings
            {
                EditDiffGatewayEnabled = false,
                EditDiffMaxTokens = analysisSettings.EditDiffMaxTokens,
                HttpTimeoutSeconds = analysisSettings.HttpTimeoutSeconds
            }),
            Options.Create(new LlmGatewaySettings { Enabled = false }),
            legacyService,
            gateway,
            contractNormalizer,
            usageRepository,
            budgetGuardrailService,
            loggerFactory.CreateLogger<EditDiffTextCompletionService>());

        var gatewayPath = new EditDiffTextCompletionService(
            Options.Create(new AnalysisSettings
            {
                EditDiffGatewayEnabled = true,
                EditDiffMaxTokens = analysisSettings.EditDiffMaxTokens,
                HttpTimeoutSeconds = analysisSettings.HttpTimeoutSeconds
            }),
            Options.Create(new LlmGatewaySettings { Enabled = true }),
            legacyService,
            gateway,
            contractNormalizer,
            usageRepository,
            budgetGuardrailService,
            loggerFactory.CreateLogger<EditDiffTextCompletionService>());

        var legacyResult = await legacyPath.CompleteAsync(candidate, legacyModel, ct);
        var gatewayResult = await gatewayPath.CompleteAsync(candidate, legacyModel, ct);

        AssertValidJsonResult(legacyResult, expectedTransport: "legacy_openrouter");
        AssertValidJsonResult(gatewayResult, expectedTransport: "llm_gateway");

        return new EditDiffPilotSmokeResult
        {
            Legacy = legacyResult,
            Gateway = gatewayResult
        };
    }

    private static void AssertValidJsonResult(EditDiffTextCompletionResult result, string expectedTransport)
    {
        if (!string.Equals(result.Transport, expectedTransport, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Edit-diff pilot smoke failed: expected transport '{expectedTransport}', got '{result.Transport}'.");
        }

        if (string.IsNullOrWhiteSpace(result.Provider) || string.IsNullOrWhiteSpace(result.Model))
        {
            throw new InvalidOperationException("Edit-diff pilot smoke failed: provider/model metadata is missing.");
        }

        if (string.IsNullOrWhiteSpace(result.RawText))
        {
            throw new InvalidOperationException("Edit-diff pilot smoke failed: completion text is empty.");
        }

        using var document = JsonDocument.Parse(result.RawText);
        var root = document.RootElement;
        if (!root.TryGetProperty("classification", out var classification) || classification.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Edit-diff pilot smoke failed: classification is missing from JSON output.");
        }

        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("Edit-diff pilot smoke failed: summary is missing from JSON output.");
        }

        if (!root.TryGetProperty("should_affect_memory", out var shouldAffect)
            || shouldAffect.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException("Edit-diff pilot smoke failed: should_affect_memory is missing from JSON output.");
        }
    }
}

public sealed class EditDiffPilotSmokeResult
{
    public EditDiffTextCompletionResult Legacy { get; set; } = new();
    public EditDiffTextCompletionResult Gateway { get; set; } = new();
}

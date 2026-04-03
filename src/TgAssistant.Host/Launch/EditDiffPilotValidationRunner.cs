using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Intelligence.Stage5;

namespace TgAssistant.Host.Launch;

public static class EditDiffPilotValidationRunner
{
    public static async Task<EditDiffPilotValidationReport> RunAsync(
        IServiceProvider services,
        string? outputPath = null,
        CancellationToken ct = default)
    {
        var analysisSettings = services.GetRequiredService<IOptions<AnalysisSettings>>().Value;
        var legacyService = services.GetRequiredService<OpenRouterAnalysisService>();
        var gateway = services.GetRequiredService<ILlmGateway>();
        var contractNormalizer = services.GetRequiredService<ILlmContractNormalizer>();
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();

        var legacyUsageRepository = new RecordingAnalysisUsageRepository();
        var legacyBudgetService = new RecordingBudgetGuardrailService();
        var gatewayUsageRepository = new RecordingAnalysisUsageRepository();
        var gatewayBudgetService = new RecordingBudgetGuardrailService();

        var legacyPath = BuildCompletionService(
            analysisSettings,
            legacyService,
            gateway,
            contractNormalizer,
            legacyUsageRepository,
            legacyBudgetService,
            loggerFactory,
            gatewayPilotEnabled: false,
            gatewayEnabled: false);
        var gatewayPath = BuildCompletionService(
            analysisSettings,
            legacyService,
            gateway,
            contractNormalizer,
            gatewayUsageRepository,
            gatewayBudgetService,
            loggerFactory,
            gatewayPilotEnabled: true,
            gatewayEnabled: true);

        var replayCases = BuildReplayCases(EditDiffPromptBuilder.ResolveLegacyModel(analysisSettings));
        var caseComparisons = new List<EditDiffPilotCaseComparison>(replayCases.Count);
        foreach (var replayCase in replayCases)
        {
            var legacy = await ExecuteBranchAsync(legacyPath, replayCase, legacyUsageRepository, isGatewayBranch: false, ct);
            var gatewayResult = await ExecuteBranchAsync(gatewayPath, replayCase, gatewayUsageRepository, isGatewayBranch: true, ct);
            caseComparisons.Add(BuildCaseComparison(replayCase, legacy, gatewayResult));
        }

        var budgetCompatibility = await RunQuotaBudgetProbeAsync(
            analysisSettings,
            legacyService,
            contractNormalizer,
            loggerFactory,
            ct);

        var comparison = BuildComparisonSummary(caseComparisons);
        var baselineSummary = BuildBranchSummary("legacy", caseComparisons.Select(x => x.Legacy));
        var gatewaySummary = BuildBranchSummary("gateway", caseComparisons.Select(x => x.Gateway));
        var risks = BuildRolloutRisks(gatewaySummary, comparison, budgetCompatibility);
        var recommendation = risks.Count == 0
            ? "pilot_ready_for_rollout_gate_review"
            : "hold_broad_rollout_pending_gateway_gate";

        var resolvedOutputPath = ResolveOutputPath(outputPath);
        var report = new EditDiffPilotValidationReport
        {
            GeneratedAtUtc = DateTime.UtcNow,
            SafePathKey = "stage5_edit_diff",
            OutputPath = resolvedOutputPath,
            Cases = caseComparisons,
            BaselineSummary = baselineSummary,
            GatewaySummary = gatewaySummary,
            Comparison = comparison,
            BudgetCompatibility = budgetCompatibility,
            RolloutRecommendation = recommendation,
            RolloutRisks = risks
        };

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(resolvedOutputPath, json, ct);
        return report;
    }

    private static EditDiffTextCompletionService BuildCompletionService(
        AnalysisSettings analysisSettings,
        OpenRouterAnalysisService legacyService,
        ILlmGateway gateway,
        ILlmContractNormalizer contractNormalizer,
        IAnalysisUsageRepository usageRepository,
        IBudgetGuardrailService budgetGuardrailService,
        ILoggerFactory loggerFactory,
        bool gatewayPilotEnabled,
        bool gatewayEnabled)
    {
        return new EditDiffTextCompletionService(
            Options.Create(new AnalysisSettings
            {
                EditDiffGatewayEnabled = gatewayPilotEnabled,
                EditDiffMaxTokens = analysisSettings.EditDiffMaxTokens,
                HttpTimeoutSeconds = analysisSettings.HttpTimeoutSeconds
            }),
            Options.Create(new LlmGatewaySettings { Enabled = gatewayEnabled }),
            legacyService,
            gateway,
            contractNormalizer,
            usageRepository,
            budgetGuardrailService,
            loggerFactory.CreateLogger<EditDiffTextCompletionService>());
    }

    private static async Task<EditDiffPilotBranchResult> ExecuteBranchAsync(
        EditDiffTextCompletionService completionService,
        EditDiffReplayCase replayCase,
        RecordingAnalysisUsageRepository usageRepository,
        bool isGatewayBranch,
        CancellationToken ct)
    {
        var usageBefore = usageRepository.Events.Count;

        try
        {
            var result = await completionService.CompleteAsync(
                new EditDiffCandidate
                {
                    ChatId = 885574984,
                    MessageId = replayCase.MessageId,
                    EditedAtUtc = replayCase.EditedAtUtc,
                    BeforeText = replayCase.BeforeText,
                    AfterText = replayCase.AfterText
                },
                replayCase.LegacyModel,
                ct);

            var usageEvent = usageRepository.Events.Count > usageBefore
                ? usageRepository.Events[^1]
                : null;
            var normalized = TryNormalizeEditDiff(result.RawText, out var schemaError);

            return new EditDiffPilotBranchResult
            {
                Transport = result.Transport,
                Provider = result.Provider,
                Model = result.Model,
                RequestId = result.RequestId,
                LatencyMs = result.LatencyMs,
                FallbackApplied = result.FallbackApplied,
                FallbackFromProvider = result.FallbackFromProvider,
                SchemaValid = normalized is not null,
                SchemaError = schemaError,
                NormalizedOutput = normalized,
                MatchesExpectedBehavior = normalized is not null && MatchesExpected(replayCase.Expected, normalized),
                UsageLogged = usageEvent is not null,
                UsageEvent = usageEvent,
                TelemetryVisible = BuildTelemetryVisibility(result, usageEvent, isGatewayBranch),
                RawText = result.RawText
            };
        }
        catch (LlmGatewayException ex)
        {
            return new EditDiffPilotBranchResult
            {
                Transport = "llm_gateway",
                Provider = ex.Provider,
                Model = string.Empty,
                SchemaValid = false,
                SchemaError = null,
                MatchesExpectedBehavior = false,
                UsageLogged = false,
                TelemetryVisible = false,
                ErrorCategory = ex.Category.ToString(),
                ErrorRetryable = ex.Retryable,
                HttpStatusCode = ex.HttpStatus is null ? null : (int)ex.HttpStatus.Value,
                RawReasonCode = ex.RawReasonCode
            };
        }
    }

    private static bool BuildTelemetryVisibility(
        EditDiffTextCompletionResult result,
        AnalysisUsageEvent? usageEvent,
        bool isGatewayBranch)
    {
        if (string.IsNullOrWhiteSpace(result.Provider) || string.IsNullOrWhiteSpace(result.Model))
        {
            return false;
        }

        if (!isGatewayBranch)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(result.RequestId) || !result.LatencyMs.HasValue)
        {
            return false;
        }

        if (result.FallbackApplied && string.IsNullOrWhiteSpace(result.FallbackFromProvider))
        {
            return false;
        }

        return usageEvent is not null
            && string.Equals(usageEvent.Phase, "edit_diff", StringComparison.Ordinal)
            && string.Equals(usageEvent.Model, result.Model, StringComparison.Ordinal);
    }

    private static EditDiffPilotCaseComparison BuildCaseComparison(
        EditDiffReplayCase replayCase,
        EditDiffPilotBranchResult legacy,
        EditDiffPilotBranchResult gateway)
    {
        return new EditDiffPilotCaseComparison
        {
            CaseId = replayCase.CaseId,
            BeforeText = replayCase.BeforeText,
            AfterText = replayCase.AfterText,
            Expected = replayCase.Expected,
            Legacy = legacy,
            Gateway = gateway,
            NormalizedOutputComparison = BuildNormalizedOutputComparison(legacy, gateway)
        };
    }

    private static string BuildNormalizedOutputComparison(
        EditDiffPilotBranchResult legacy,
        EditDiffPilotBranchResult gateway)
    {
        if (!string.IsNullOrWhiteSpace(legacy.ErrorCategory) || !string.IsNullOrWhiteSpace(gateway.ErrorCategory))
        {
            return "error";
        }

        if (!legacy.SchemaValid || !gateway.SchemaValid)
        {
            return "schema_mismatch";
        }

        if (legacy.NormalizedOutput is null || gateway.NormalizedOutput is null)
        {
            return "unavailable";
        }

        return AreEquivalent(legacy.NormalizedOutput, gateway.NormalizedOutput)
            ? "parity"
            : "diverged";
    }

    private static EditDiffPilotBranchSummary BuildBranchSummary(
        string branch,
        IEnumerable<EditDiffPilotBranchResult> results)
    {
        var materialized = results.ToList();
        var totalCases = materialized.Count;
        var successCount = materialized.Count(x => string.IsNullOrWhiteSpace(x.ErrorCategory));
        var errorCount = totalCases - successCount;
        var schemaValidCount = materialized.Count(x => x.SchemaValid);
        var telemetryVisibleCount = materialized.Count(x => x.TelemetryVisible);
        var expectedMatchCount = materialized.Count(x => x.MatchesExpectedBehavior);
        var usageLoggedCount = materialized.Count(x => x.UsageLogged);
        var fallbackCount = materialized.Count(x => x.FallbackApplied);
        var latencyValues = materialized.Where(x => x.LatencyMs.HasValue).Select(x => x.LatencyMs!.Value).ToList();

        return new EditDiffPilotBranchSummary
        {
            Branch = branch,
            TotalCases = totalCases,
            SuccessCount = successCount,
            ErrorCount = errorCount,
            ErrorRate = ComputeRate(errorCount, totalCases),
            SchemaValidCount = schemaValidCount,
            SchemaValidRate = ComputeRate(schemaValidCount, totalCases),
            TelemetryVisibleCount = telemetryVisibleCount,
            TelemetryVisibleRate = ComputeRate(telemetryVisibleCount, totalCases),
            ExpectedBehaviorMatchCount = expectedMatchCount,
            ExpectedBehaviorMatchRate = ComputeRate(expectedMatchCount, totalCases),
            UsageLoggedCount = usageLoggedCount,
            UsageLoggedRate = ComputeRate(usageLoggedCount, totalCases),
            FallbackCount = fallbackCount,
            FallbackRate = ComputeRate(fallbackCount, totalCases),
            AverageLatencyMs = latencyValues.Count == 0 ? null : Math.Round(latencyValues.Average(), 2),
            Providers = materialized
                .Select(x => x.Provider)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static EditDiffPilotComparisonSummary BuildComparisonSummary(IReadOnlyList<EditDiffPilotCaseComparison> comparisons)
    {
        var parityCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "parity", StringComparison.Ordinal));
        var divergedCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "diverged", StringComparison.Ordinal));
        var schemaMismatchCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "schema_mismatch", StringComparison.Ordinal));
        var errorCount = comparisons.Count(x => string.Equals(x.NormalizedOutputComparison, "error", StringComparison.Ordinal));

        return new EditDiffPilotComparisonSummary
        {
            TotalCases = comparisons.Count,
            ParityCount = parityCount,
            DivergedCount = divergedCount,
            SchemaMismatchCount = schemaMismatchCount,
            ErrorCount = errorCount,
            ParityRate = ComputeRate(parityCount, comparisons.Count)
        };
    }

    private static async Task<EditDiffPilotBudgetCompatibility> RunQuotaBudgetProbeAsync(
        AnalysisSettings analysisSettings,
        OpenRouterAnalysisService legacyService,
        ILlmContractNormalizer contractNormalizer,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var budgetService = new RecordingBudgetGuardrailService();
        var usageRepository = new RecordingAnalysisUsageRepository();
        var quotaGateway = new ThrowingQuotaGateway();
        var probeService = BuildCompletionService(
            analysisSettings,
            legacyService,
            quotaGateway,
            contractNormalizer,
            usageRepository,
            budgetService,
            loggerFactory,
            gatewayPilotEnabled: true,
            gatewayEnabled: true);

        try
        {
            await probeService.CompleteAsync(
                new EditDiffCandidate
                {
                    ChatId = 885574984,
                    MessageId = 49901,
                    EditedAtUtc = DateTime.UtcNow,
                    BeforeText = "Я приеду в 18:00.",
                    AfterText = "Я приеду в 20:00."
                },
                EditDiffPromptBuilder.ResolveLegacyModel(analysisSettings),
                ct);

            throw new InvalidOperationException("Edit-diff pilot validation expected a quota-like gateway failure probe to throw.");
        }
        catch (LlmGatewayException ex) when (ex.Category == LlmGatewayErrorCategory.Quota)
        {
            var quotaRegistration = budgetService.QuotaRegistrations.SingleOrDefault();
            return new EditDiffPilotBudgetCompatibility
            {
                FailureCategory = ex.Category.ToString(),
                FailureProvider = ex.Provider,
                FailureRetryable = ex.Retryable,
                FailureStatusCode = ex.HttpStatus is null ? null : (int)ex.HttpStatus.Value,
                FailureReasonCode = ex.RawReasonCode,
                QuotaRegistrationCompatible = quotaRegistration is not null
                    && string.Equals(quotaRegistration.PathKey, "stage5_edit_diff", StringComparison.Ordinal)
                    && string.Equals(quotaRegistration.Modality, BudgetModalities.TextAnalysis, StringComparison.Ordinal)
                    && string.Equals(quotaRegistration.Reason, "quota_like_provider_failure", StringComparison.Ordinal)
                    && !quotaRegistration.IsImportScope
                    && quotaRegistration.IsOptionalPath,
                RegisteredCount = budgetService.QuotaRegistrations.Count,
                RegisteredPathKey = quotaRegistration?.PathKey,
                RegisteredModality = quotaRegistration?.Modality,
                RegisteredReason = quotaRegistration?.Reason,
                RegisteredIsImportScope = quotaRegistration?.IsImportScope,
                RegisteredIsOptionalPath = quotaRegistration?.IsOptionalPath
            };
        }
    }

    private static List<string> BuildRolloutRisks(
        EditDiffPilotBranchSummary gatewaySummary,
        EditDiffPilotComparisonSummary comparison,
        EditDiffPilotBudgetCompatibility budgetCompatibility)
    {
        var risks = new List<string>();

        if (!budgetCompatibility.QuotaRegistrationCompatible)
        {
            risks.Add("budget_quota_registration_incompatible");
        }

        if (gatewaySummary.ErrorCount > 0)
        {
            risks.Add("gateway_pilot_errors_present");
        }

        if (gatewaySummary.FallbackCount > 0)
        {
            risks.Add("gateway_primary_provider_degraded_or_unstable");
        }

        if (gatewaySummary.TelemetryVisibleCount < gatewaySummary.TotalCases)
        {
            risks.Add("gateway_telemetry_incomplete_on_some_cases");
        }

        if (comparison.ParityCount < comparison.TotalCases)
        {
            risks.Add("normalized_output_parity_below_full_match");
        }

        return risks;
    }

    private static bool MatchesExpected(EditDiffReplayExpected expected, EditDiffReplayNormalizedOutput actual)
    {
        return string.Equals(expected.Classification, actual.Classification, StringComparison.Ordinal)
            && expected.ShouldAffectMemory == actual.ShouldAffectMemory
            && expected.AddedImportant == actual.AddedImportant
            && expected.RemovedImportant == actual.RemovedImportant
            && !string.IsNullOrWhiteSpace(actual.Summary)
            && actual.Confidence >= 0f
            && actual.Confidence <= 1f;
    }

    private static bool AreEquivalent(EditDiffReplayNormalizedOutput left, EditDiffReplayNormalizedOutput right)
    {
        return string.Equals(left.Classification, right.Classification, StringComparison.Ordinal)
            && left.ShouldAffectMemory == right.ShouldAffectMemory
            && left.AddedImportant == right.AddedImportant
            && left.RemovedImportant == right.RemovedImportant;
    }

    private static EditDiffReplayNormalizedOutput? TryNormalizeEditDiff(string? json, out string? schemaError)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            schemaError = "empty_json";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                schemaError = "root_not_object";
                return null;
            }

            if (!TryGetRequiredString(root, "classification", out var classification)
                || !TryGetRequiredString(root, "summary", out var summary)
                || !TryGetRequiredBoolean(root, "should_affect_memory", out var shouldAffectMemory)
                || !TryGetRequiredBoolean(root, "added_important", out var addedImportant)
                || !TryGetRequiredBoolean(root, "removed_important", out var removedImportant)
                || !TryGetRequiredSingle(root, "confidence", out var confidence))
            {
                schemaError = "missing_or_invalid_field";
                return null;
            }

            schemaError = null;
            return new EditDiffReplayNormalizedOutput
            {
                Classification = classification,
                Summary = summary,
                ShouldAffectMemory = shouldAffectMemory,
                AddedImportant = addedImportant,
                RemovedImportant = removedImportant,
                Confidence = confidence
            };
        }
        catch (JsonException)
        {
            schemaError = "json_parse_error";
            return null;
        }
    }

    private static bool TryGetRequiredString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetRequiredBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetRequiredSingle(JsonElement root, string name, out float value)
    {
        value = 0f;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        value = (float)property.GetDouble();
        return true;
    }

    private static decimal ComputeRate(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            return 0m;
        }

        return Math.Round((decimal)numerator / denominator, 4);
    }

    private static string ResolveOutputPath(string? outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.GetFullPath(outputPath);
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "artifacts",
            "llm-gateway",
            "edit_diff_pilot_validation_report.json"));
    }

    private static List<EditDiffReplayCase> BuildReplayCases(string legacyModel)
    {
        return
        [
            new EditDiffReplayCase
            {
                CaseId = "typo_fix",
                MessageId = 40401,
                EditedAtUtc = DateTime.UtcNow.AddMinutes(-4),
                BeforeText = "Встреча завтра в 19:00 у метро.",
                AfterText = "Встреча завтра в 19:00 у метро!",
                LegacyModel = legacyModel,
                Expected = new EditDiffReplayExpected
                {
                    Classification = "formatting",
                    Summary = "Косметическая правка пунктуации без изменения смысла.",
                    ShouldAffectMemory = false,
                    AddedImportant = false,
                    RemovedImportant = false,
                    Confidence = 0.93f
                }
            },
            new EditDiffReplayCase
            {
                CaseId = "meaning_change",
                MessageId = 40402,
                EditedAtUtc = DateTime.UtcNow.AddMinutes(-3),
                BeforeText = "Я смогу приехать в пятницу.",
                AfterText = "Я не смогу приехать в пятницу.",
                LegacyModel = legacyModel,
                Expected = new EditDiffReplayExpected
                {
                    Classification = "meaning_changed",
                    Summary = "Правка изменила смысл сообщения о возможности приезда.",
                    ShouldAffectMemory = true,
                    AddedImportant = false,
                    RemovedImportant = false,
                    Confidence = 0.95f
                }
            },
            new EditDiffReplayCase
            {
                CaseId = "deleted_message",
                MessageId = 40403,
                EditedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                BeforeText = "Билеты уже куплены, выезжаю утром.",
                AfterText = "[DELETED]",
                LegacyModel = legacyModel,
                Expected = new EditDiffReplayExpected
                {
                    Classification = "message_deleted",
                    Summary = "Удалено сообщение с потенциально значимой логистикой.",
                    ShouldAffectMemory = true,
                    AddedImportant = false,
                    RemovedImportant = true,
                    Confidence = 0.91f
                }
            },
            new EditDiffReplayCase
            {
                CaseId = "time_added",
                MessageId = 40404,
                EditedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                BeforeText = "Созвонимся вечером.",
                AfterText = "Созвонимся сегодня в 21:30.",
                LegacyModel = legacyModel,
                Expected = new EditDiffReplayExpected
                {
                    Classification = "important_added",
                    Summary = "В правке появилось конкретное время звонка.",
                    ShouldAffectMemory = true,
                    AddedImportant = true,
                    RemovedImportant = false,
                    Confidence = 0.94f
                }
            }
        ];
    }

    private sealed class RecordingAnalysisUsageRepository : IAnalysisUsageRepository
    {
        public List<AnalysisUsageEvent> Events { get; } = new();

        public Task LogAsync(AnalysisUsageEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task<decimal> GetCostUsdSinceAsync(string phase, DateTime sinceUtc, CancellationToken ct = default)
        {
            var total = Events
                .Where(x => string.Equals(x.Phase, phase, StringComparison.OrdinalIgnoreCase) && x.CreatedAt >= sinceUtc)
                .Sum(x => x.CostUsd);
            return Task.FromResult(total);
        }

        public Task<Dictionary<string, decimal>> GetCostUsdByPhaseSinceAsync(DateTime sinceUtc, CancellationToken ct = default)
        {
            var result = Events
                .Where(x => x.CreatedAt >= sinceUtc)
                .GroupBy(x => x.Phase, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.Sum(y => y.CostUsd), StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }

        public Task<AnalysisUsageWindowSummary> SummarizeWindowAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        {
            var rows = Events.Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc).ToList();
            return Task.FromResult(new AnalysisUsageWindowSummary
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                TotalRows = rows.Count,
                TotalCostUsd = rows.Sum(x => x.CostUsd),
                TotalPromptTokens = rows.Sum(x => x.PromptTokens),
                TotalCompletionTokens = rows.Sum(x => x.CompletionTokens),
                TotalTokens = rows.Sum(x => x.TotalTokens),
                AverageLatencyMs = rows.Count == 0
                    ? 0
                    : rows.Where(x => x.LatencyMs.HasValue).DefaultIfEmpty().Average(x => x?.LatencyMs ?? 0)
            });
        }
    }

    private sealed class RecordingBudgetGuardrailService : IBudgetGuardrailService
    {
        public List<BudgetQuotaRegistrationRecord> QuotaRegistrations { get; } = new();

        public Task<BudgetPathDecision> EvaluatePathAsync(BudgetPathCheckRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new BudgetPathDecision
            {
                PathKey = request.PathKey,
                Modality = request.Modality,
                State = BudgetPathStates.Active,
                Reason = "budget_ok"
            });
        }

        public Task<BudgetPathDecision> RegisterQuotaBlockedAsync(
            string pathKey,
            string modality,
            string reason,
            bool isImportScope,
            bool isOptionalPath,
            CancellationToken ct = default)
        {
            QuotaRegistrations.Add(new BudgetQuotaRegistrationRecord
            {
                PathKey = pathKey,
                Modality = modality,
                Reason = reason,
                IsImportScope = isImportScope,
                IsOptionalPath = isOptionalPath
            });

            return Task.FromResult(new BudgetPathDecision
            {
                PathKey = pathKey,
                Modality = modality,
                State = BudgetPathStates.QuotaBlocked,
                Reason = reason,
                ShouldPausePath = true,
                ShouldDegradeOptionalPath = isOptionalPath
            });
        }

        public Task<List<BudgetOperationalState>> GetOperationalStatesAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new List<BudgetOperationalState>());
        }
    }

    private sealed class ThrowingQuotaGateway : ILlmGateway
    {
        public Task<LlmGatewayResponse> ExecuteAsync(LlmGatewayRequest request, CancellationToken ct = default)
        {
            throw new LlmGatewayException("Quota-like provider failure for pilot governance probe.")
            {
                Category = LlmGatewayErrorCategory.Quota,
                Provider = "codex-lb",
                Modality = request.Modality,
                HttpStatus = HttpStatusCode.TooManyRequests,
                Retryable = false,
                RawReasonCode = "quota_probe_429"
            };
        }
    }

    private sealed class BudgetQuotaRegistrationRecord
    {
        public string PathKey { get; set; } = string.Empty;
        public string Modality { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public bool IsImportScope { get; set; }
        public bool IsOptionalPath { get; set; }
    }

    private sealed class EditDiffReplayCase
    {
        public string CaseId { get; set; } = string.Empty;
        public long MessageId { get; set; }
        public DateTime EditedAtUtc { get; set; }
        public string BeforeText { get; set; } = string.Empty;
        public string AfterText { get; set; } = string.Empty;
        public string LegacyModel { get; set; } = string.Empty;
        public EditDiffReplayExpected Expected { get; set; } = new();
    }
}

public sealed class EditDiffPilotValidationReport
{
    public DateTime GeneratedAtUtc { get; set; }
    public string SafePathKey { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<EditDiffPilotCaseComparison> Cases { get; set; } = new();
    public EditDiffPilotBranchSummary BaselineSummary { get; set; } = new();
    public EditDiffPilotBranchSummary GatewaySummary { get; set; } = new();
    public EditDiffPilotComparisonSummary Comparison { get; set; } = new();
    public EditDiffPilotBudgetCompatibility BudgetCompatibility { get; set; } = new();
    public string RolloutRecommendation { get; set; } = string.Empty;
    public List<string> RolloutRisks { get; set; } = new();
}

public sealed class EditDiffPilotCaseComparison
{
    public string CaseId { get; set; } = string.Empty;
    public string BeforeText { get; set; } = string.Empty;
    public string AfterText { get; set; } = string.Empty;
    public EditDiffReplayExpected Expected { get; set; } = new();
    public EditDiffPilotBranchResult Legacy { get; set; } = new();
    public EditDiffPilotBranchResult Gateway { get; set; } = new();
    public string NormalizedOutputComparison { get; set; } = string.Empty;
}

public sealed class EditDiffPilotBranchResult
{
    public string Transport { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string Model { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public int? LatencyMs { get; set; }
    public bool FallbackApplied { get; set; }
    public string? FallbackFromProvider { get; set; }
    public bool SchemaValid { get; set; }
    public string? SchemaError { get; set; }
    public EditDiffReplayNormalizedOutput? NormalizedOutput { get; set; }
    public bool MatchesExpectedBehavior { get; set; }
    public bool UsageLogged { get; set; }
    public AnalysisUsageEvent? UsageEvent { get; set; }
    public bool TelemetryVisible { get; set; }
    public string? ErrorCategory { get; set; }
    public bool? ErrorRetryable { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? RawReasonCode { get; set; }
    public string RawText { get; set; } = string.Empty;
}

public sealed class EditDiffPilotBranchSummary
{
    public string Branch { get; set; } = string.Empty;
    public int TotalCases { get; set; }
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public decimal ErrorRate { get; set; }
    public int SchemaValidCount { get; set; }
    public decimal SchemaValidRate { get; set; }
    public int TelemetryVisibleCount { get; set; }
    public decimal TelemetryVisibleRate { get; set; }
    public int ExpectedBehaviorMatchCount { get; set; }
    public decimal ExpectedBehaviorMatchRate { get; set; }
    public int UsageLoggedCount { get; set; }
    public decimal UsageLoggedRate { get; set; }
    public int FallbackCount { get; set; }
    public decimal FallbackRate { get; set; }
    public double? AverageLatencyMs { get; set; }
    public List<string> Providers { get; set; } = new();
}

public sealed class EditDiffPilotComparisonSummary
{
    public int TotalCases { get; set; }
    public int ParityCount { get; set; }
    public int DivergedCount { get; set; }
    public int SchemaMismatchCount { get; set; }
    public int ErrorCount { get; set; }
    public decimal ParityRate { get; set; }
}

public sealed class EditDiffPilotBudgetCompatibility
{
    public string FailureCategory { get; set; } = string.Empty;
    public string? FailureProvider { get; set; }
    public bool FailureRetryable { get; set; }
    public int? FailureStatusCode { get; set; }
    public string? FailureReasonCode { get; set; }
    public bool QuotaRegistrationCompatible { get; set; }
    public int RegisteredCount { get; set; }
    public string? RegisteredPathKey { get; set; }
    public string? RegisteredModality { get; set; }
    public string? RegisteredReason { get; set; }
    public bool? RegisteredIsImportScope { get; set; }
    public bool? RegisteredIsOptionalPath { get; set; }
}

public sealed class EditDiffReplayExpected
{
    public string Classification { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool ShouldAffectMemory { get; set; }
    public bool AddedImportant { get; set; }
    public bool RemovedImportant { get; set; }
    public float Confidence { get; set; }
}

public sealed class EditDiffReplayNormalizedOutput
{
    public string Classification { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public bool ShouldAffectMemory { get; set; }
    public bool AddedImportant { get; set; }
    public bool RemovedImportant { get; set; }
    public float Confidence { get; set; }
}

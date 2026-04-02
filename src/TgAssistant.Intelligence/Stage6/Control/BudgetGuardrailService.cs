// Legacy Stage6 surface retained for reference and diagnostics only. Not part of the PRD target architecture.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Intelligence.Stage6.Control;

public class BudgetGuardrailService : IBudgetGuardrailService
{
    private static readonly string[] TextAnalysisPhases = ["cheap", "cheap_probe", "expensive", "summary", "chat", "edit_diff", "daily_crystallization"];
    private static readonly string[] EmbeddingsPhases = ["embedding"];
    private static readonly string[] VisionPhases = ["vision", "import_vision"];
    private static readonly string[] AudioPhases = ["audio_transcription", "import_audio_transcription", "audio_paralinguistics", "import_audio_paralinguistics"];

    private readonly BudgetGuardrailSettings _settings;
    private readonly IAnalysisUsageRepository _usageRepository;
    private readonly IBudgetOpsRepository _budgetOpsRepository;
    private readonly ILogger<BudgetGuardrailService> _logger;

    public BudgetGuardrailService(
        IOptions<BudgetGuardrailSettings> settings,
        IAnalysisUsageRepository usageRepository,
        IBudgetOpsRepository budgetOpsRepository,
        ILogger<BudgetGuardrailService> logger)
    {
        _settings = settings.Value;
        _usageRepository = usageRepository;
        _budgetOpsRepository = budgetOpsRepository;
        _logger = logger;
    }

    public async Task<BudgetPathDecision> EvaluatePathAsync(BudgetPathCheckRequest request, CancellationToken ct = default)
    {
        var normalized = NormalizeRequest(request);
        if (!_settings.Enabled)
        {
            return await SaveAndReturnAsync(BuildDecision(normalized, BudgetPathStates.Active, "guardrails_disabled", 0m, 0m, 0m, 0m, 0m, 0m), ct);
        }

        var now = DateTime.UtcNow;
        var existing = await _budgetOpsRepository.GetBudgetOperationalStateAsync(normalized.PathKey, ct);
        if (existing != null && string.Equals(existing.State, BudgetPathStates.QuotaBlocked, StringComparison.OrdinalIgnoreCase))
        {
            var blockedUntilUtc = TryReadBlockedUntil(existing.DetailsJson);
            if (!blockedUntilUtc.HasValue || blockedUntilUtc.Value > now)
            {
                var decision = BuildDecision(
                    normalized,
                    BudgetPathStates.QuotaBlocked,
                    "quota_blocked",
                    0m,
                    _settings.DailyBudgetUsd,
                    0m,
                    ResolveStageBudget(normalized.Modality),
                    0m,
                    _settings.ImportBudgetUsd,
                    blockedUntilUtc);
                return await SaveAndReturnAsync(decision, ct);
            }
        }

        var sinceUtc = now.AddDays(-1);
        var costsByPhase = await _usageRepository.GetCostUsdByPhaseSinceAsync(sinceUtc, ct);
        var dailySpent = costsByPhase.Values.Sum();
        var modalitySpent = ResolveModalitySpent(costsByPhase, normalized.Modality);
        var importSpent = costsByPhase
            .Where(x => x.Key.StartsWith("import_", StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.Value);

        var dailyBudget = Math.Max(0m, _settings.DailyBudgetUsd);
        var stageBudget = Math.Max(0m, ResolveStageBudget(normalized.Modality));
        var importBudget = Math.Max(0m, _settings.ImportBudgetUsd);
        var softRatio = ClampRatio(_settings.SoftLimitRatio, 0.50m, 0.99m);
        var hardRatio = Math.Max(softRatio, ClampRatio(_settings.HardLimitRatio, 0.80m, 1.20m));

        var hardByDaily = dailyBudget > 0 && dailySpent >= dailyBudget * hardRatio;
        var hardByStage = stageBudget > 0 && modalitySpent >= stageBudget * hardRatio;
        var hardByImport = normalized.IsImportScope && importBudget > 0 && importSpent >= importBudget * hardRatio;
        if (hardByDaily || hardByStage || hardByImport)
        {
            var reason = hardByImport ? "hard_limit_import" : hardByStage ? "hard_limit_stage" : "hard_limit_daily";
            var decision = BuildDecision(
                normalized,
                BudgetPathStates.HardPaused,
                reason,
                dailySpent,
                dailyBudget,
                modalitySpent,
                stageBudget,
                importSpent,
                importBudget);
            return await SaveAndReturnAsync(decision, ct);
        }

        var softByDaily = dailyBudget > 0 && dailySpent >= dailyBudget * softRatio;
        var softByStage = stageBudget > 0 && modalitySpent >= stageBudget * softRatio;
        var softByImport = normalized.IsImportScope && importBudget > 0 && importSpent >= importBudget * softRatio;
        if (normalized.IsOptionalPath && (softByDaily || softByStage || softByImport))
        {
            var reason = softByImport ? "soft_limit_import" : softByStage ? "soft_limit_stage" : "soft_limit_daily";
            var decision = BuildDecision(
                normalized,
                BudgetPathStates.SoftLimited,
                reason,
                dailySpent,
                dailyBudget,
                modalitySpent,
                stageBudget,
                importSpent,
                importBudget);
            return await SaveAndReturnAsync(decision, ct);
        }

        return await SaveAndReturnAsync(
            BuildDecision(
                normalized,
                BudgetPathStates.Active,
                "budget_ok",
                dailySpent,
                dailyBudget,
                modalitySpent,
                stageBudget,
                importSpent,
                importBudget),
            ct);
    }

    public async Task<BudgetPathDecision> RegisterQuotaBlockedAsync(
        string pathKey,
        string modality,
        string reason,
        bool isImportScope,
        bool isOptionalPath,
        CancellationToken ct = default)
    {
        var normalized = NormalizeRequest(new BudgetPathCheckRequest
        {
            PathKey = pathKey,
            Modality = modality,
            IsImportScope = isImportScope,
            IsOptionalPath = isOptionalPath
        });

        var blockedUntilUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _settings.QuotaBlockMinutes));
        var details = JsonSerializer.Serialize(new
        {
            blocked_until_utc = blockedUntilUtc,
            reason = string.IsNullOrWhiteSpace(reason) ? "quota_like_provider_failure" : reason
        });

        var state = new BudgetOperationalState
        {
            PathKey = normalized.PathKey,
            Modality = normalized.Modality,
            State = BudgetPathStates.QuotaBlocked,
            Reason = string.IsNullOrWhiteSpace(reason) ? "quota_like_provider_failure" : reason,
            DetailsJson = details,
            UpdatedAt = DateTime.UtcNow
        };

        await _budgetOpsRepository.UpsertBudgetOperationalStateAsync(state, ct);

        _logger.LogWarning(
            "Budget guardrail quota block set. path={Path}, modality={Modality}, blocked_until_utc={BlockedUntilUtc}, reason={Reason}",
            normalized.PathKey,
            normalized.Modality,
            blockedUntilUtc,
            state.Reason);

        return await EvaluatePathAsync(normalized, ct);
    }

    public Task<List<BudgetOperationalState>> GetOperationalStatesAsync(CancellationToken ct = default)
    {
        return _budgetOpsRepository.GetBudgetOperationalStatesAsync(ct);
    }

    private async Task<BudgetPathDecision> SaveAndReturnAsync(BudgetPathDecision decision, CancellationToken ct)
    {
        var details = JsonSerializer.Serialize(new
        {
            decision.DailySpentUsd,
            decision.DailyBudgetUsd,
            decision.StageSpentUsd,
            decision.StageBudgetUsd,
            decision.ImportSpentUsd,
            decision.ImportBudgetUsd,
            blocked_until_utc = decision.BlockedUntilUtc,
            decision.EvaluatedAt
        });

        await _budgetOpsRepository.UpsertBudgetOperationalStateAsync(new BudgetOperationalState
        {
            PathKey = decision.PathKey,
            Modality = decision.Modality,
            State = decision.State,
            Reason = decision.Reason,
            DetailsJson = details,
            UpdatedAt = decision.EvaluatedAt
        }, ct);

        if (!string.Equals(decision.State, BudgetPathStates.Active, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Budget guardrail decision: path={Path}, modality={Modality}, state={State}, reason={Reason}, daily={DailySpent:0.0000}/{DailyBudget:0.0000}, stage={StageSpent:0.0000}/{StageBudget:0.0000}, import={ImportSpent:0.0000}/{ImportBudget:0.0000}",
                decision.PathKey,
                decision.Modality,
                decision.State,
                decision.Reason,
                decision.DailySpentUsd,
                decision.DailyBudgetUsd,
                decision.StageSpentUsd,
                decision.StageBudgetUsd,
                decision.ImportSpentUsd,
                decision.ImportBudgetUsd);
        }

        return decision;
    }

    private static DateTime? TryReadBlockedUntil(string detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(detailsJson);
            if (!doc.RootElement.TryGetProperty("blocked_until_utc", out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var parsed)
                ? parsed.ToUniversalTime()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private BudgetPathDecision BuildDecision(
        BudgetPathCheckRequest request,
        string state,
        string reason,
        decimal dailySpent,
        decimal dailyBudget,
        decimal stageSpent,
        decimal stageBudget,
        decimal importSpent,
        decimal importBudget,
        DateTime? blockedUntilUtc = null)
    {
        var pause = string.Equals(state, BudgetPathStates.HardPaused, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(state, BudgetPathStates.QuotaBlocked, StringComparison.OrdinalIgnoreCase);
        var degrade = request.IsOptionalPath && string.Equals(state, BudgetPathStates.SoftLimited, StringComparison.OrdinalIgnoreCase);
        return new BudgetPathDecision
        {
            PathKey = request.PathKey,
            Modality = request.Modality,
            State = state,
            Reason = reason,
            ShouldPausePath = pause,
            ShouldDegradeOptionalPath = degrade,
            DailySpentUsd = dailySpent,
            DailyBudgetUsd = dailyBudget,
            StageSpentUsd = stageSpent,
            StageBudgetUsd = stageBudget,
            ImportSpentUsd = importSpent,
            ImportBudgetUsd = importBudget,
            BlockedUntilUtc = blockedUntilUtc,
            EvaluatedAt = DateTime.UtcNow
        };
    }

    private static BudgetPathCheckRequest NormalizeRequest(BudgetPathCheckRequest request)
    {
        return new BudgetPathCheckRequest
        {
            PathKey = string.IsNullOrWhiteSpace(request.PathKey) ? "unknown_path" : request.PathKey.Trim(),
            Modality = NormalizeModality(request.Modality),
            IsImportScope = request.IsImportScope,
            IsOptionalPath = request.IsOptionalPath
        };
    }

    private static string NormalizeModality(string modality)
    {
        var normalized = modality?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            BudgetModalities.TextAnalysis => BudgetModalities.TextAnalysis,
            BudgetModalities.Embeddings => BudgetModalities.Embeddings,
            BudgetModalities.Vision => BudgetModalities.Vision,
            BudgetModalities.Audio => BudgetModalities.Audio,
            _ => BudgetModalities.TextAnalysis
        };
    }

    private decimal ResolveStageBudget(string modality)
    {
        return modality switch
        {
            BudgetModalities.Embeddings => _settings.StageEmbeddingsBudgetUsd,
            BudgetModalities.Vision => _settings.StageVisionBudgetUsd,
            BudgetModalities.Audio => _settings.StageAudioBudgetUsd,
            _ => _settings.StageTextAnalysisBudgetUsd
        };
    }

    private static decimal ClampRatio(decimal value, decimal min, decimal max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private static decimal ResolveModalitySpent(IReadOnlyDictionary<string, decimal> costsByPhase, string modality)
    {
        var phases = modality switch
        {
            BudgetModalities.Embeddings => EmbeddingsPhases,
            BudgetModalities.Vision => VisionPhases,
            BudgetModalities.Audio => AudioPhases,
            _ => TextAnalysisPhases
        };

        var spent = 0m;
        foreach (var phase in phases)
        {
            spent += costsByPhase.TryGetValue(phase, out var value) ? value : 0m;
        }

        return spent;
    }
}

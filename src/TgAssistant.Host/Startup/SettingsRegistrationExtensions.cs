using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Host.Startup;

public static partial class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTelegramAssistantSettings(
        this IServiceCollection services,
        IConfiguration config,
        bool includeLegacyStage6ClusterDiagnostics = false)
    {
        services.Configure<TelegramSettings>(config.GetSection(TelegramSettings.Section));
        services.Configure<RedisSettings>(config.GetSection(RedisSettings.Section));
        services.Configure<DatabaseSettings>(config.GetSection(DatabaseSettings.Section));
        services.Configure<GeminiSettings>(config.GetSection(GeminiSettings.Section));
        services.Configure<ClaudeSettings>(config.GetSection(ClaudeSettings.Section));
        services.Configure<LlmGatewaySettings>(config.GetSection(LlmGatewaySettings.Section));
        services.Configure<BatchWorkerSettings>(config.GetSection(BatchWorkerSettings.Section));
        services.Configure<MediaSettings>(config.GetSection(MediaSettings.Section));
        services.Configure<VoiceParalinguisticsSettings>(config.GetSection(VoiceParalinguisticsSettings.Section));
        services.Configure<ArchiveImportSettings>(config.GetSection(ArchiveImportSettings.Section));
        services.Configure<BackfillSettings>(config.GetSection(BackfillSettings.Section));
        services.Configure<ChatCoordinationSettings>(config.GetSection(ChatCoordinationSettings.Section));
        services.Configure<RiskyOperationSafetySettings>(config.GetSection(RiskyOperationSafetySettings.Section));
        services.Configure<AnalysisSettings>(config.GetSection(AnalysisSettings.Section));
        services.Configure<ResolutionInterpretationLoopSettings>(config.GetSection(ResolutionInterpretationLoopSettings.Section));
        services.Configure<AggregationSettings>(config.GetSection(AggregationSettings.Section));
        services.Configure<MergeSettings>(config.GetSection(MergeSettings.Section));
        services.Configure<MonitoringSettings>(config.GetSection(MonitoringSettings.Section));
        services.Configure<MaintenanceSettings>(config.GetSection(MaintenanceSettings.Section));
        services.Configure<Neo4jSettings>(config.GetSection(Neo4jSettings.Section));
        services.Configure<EmbeddingSettings>(config.GetSection(EmbeddingSettings.Section));
        services.Configure<BudgetGuardrailSettings>(config.GetSection(BudgetGuardrailSettings.Section));
        services.Configure<EvalHarnessSettings>(config.GetSection(EvalHarnessSettings.Section));
        var legacyDiagnosticsSection = config.GetSection("LegacyDiagnostics");
        var webSection = legacyDiagnosticsSection.GetSection(WebSettings.Section);
        services.Configure<WebSettings>(webSection.Exists() ? webSection : config.GetSection(WebSettings.Section));

        if (includeLegacyStage6ClusterDiagnostics)
        {
            var botChatSection = legacyDiagnosticsSection.GetSection(BotChatSettings.Section);
            var stage6AutoCaseSection = legacyDiagnosticsSection.GetSection(Stage6AutoCaseGenerationSettings.Section);

            services.Configure<BotChatSettings>(botChatSection.Exists() ? botChatSection : config.GetSection(BotChatSettings.Section));
            services.Configure<Stage6AutoCaseGenerationSettings>(
                stage6AutoCaseSection.Exists() ? stage6AutoCaseSection : config.GetSection(Stage6AutoCaseGenerationSettings.Section));
        }

        services.PostConfigure<TelegramSettings>(s =>
        {
            if (string.IsNullOrWhiteSpace(s.MonitoredChats))
            {
                return;
            }

            var needsParse = s.MonitoredChatIds.Count == 0 || s.MonitoredChatIds.All(id => id <= 0);
            if (!needsParse)
            {
                s.MonitoredChatIds = s.MonitoredChatIds.Where(id => id > 0).Distinct().ToList();
                return;
            }

            s.MonitoredChatIds = s.MonitoredChats
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(raw => long.TryParse(raw.Trim(), out var id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
        });

        services.PostConfigure<BackfillSettings>(s =>
        {
            var parsed = s.ChatIds
                .Where(id => id > 0)
                .ToList();

            var raw = config.GetSection(BackfillSettings.Section).GetValue<string>("ChatIds");
            if (!string.IsNullOrWhiteSpace(raw))
            {
                parsed.AddRange(raw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => long.TryParse(value.Trim(), out var id) ? id : 0)
                    .Where(id => id > 0));
            }

            s.ChatIds = parsed.Distinct().ToList();
        });

        services.PostConfigure<ChatCoordinationSettings>(s =>
        {
            s.HandoverPendingExtractionThreshold = Math.Max(0, s.HandoverPendingExtractionThreshold);
            s.ListenerEligibilityRefreshSeconds = Math.Max(5, s.ListenerEligibilityRefreshSeconds);
            s.DowntimeCatchupThresholdMinutes = Math.Max(1, s.DowntimeCatchupThresholdMinutes);
            s.TailReopenMaxSessionLag = Math.Max(1, s.TailReopenMaxSessionLag);
            s.TailReopenMaxWindowHours = Math.Max(1, s.TailReopenMaxWindowHours);
        });

        services.PostConfigure<RiskyOperationSafetySettings>(s =>
        {
            s.BackupFreshnessHours = Math.Max(1, s.BackupFreshnessHours);
            s.IntegrityWriteVolumeWarningThreshold = Math.Max(1, s.IntegrityWriteVolumeWarningThreshold);
            s.IntegrityWriteVolumeUnsafeThreshold = Math.Max(
                s.IntegrityWriteVolumeWarningThreshold,
                s.IntegrityWriteVolumeUnsafeThreshold);
        });

        services.PostConfigure<LlmGatewaySettings>(s =>
        {
            s.Routing = new Dictionary<string, LlmGatewayRouteSettings>(
                s.Routing ?? new Dictionary<string, LlmGatewayRouteSettings>(),
                StringComparer.OrdinalIgnoreCase);
            s.Providers = new Dictionary<string, LlmGatewayProviderSettings>(
                s.Providers ?? new Dictionary<string, LlmGatewayProviderSettings>(),
                StringComparer.OrdinalIgnoreCase);
            s.Experiments = new Dictionary<string, LlmGatewayExperimentSettings>(
                s.Experiments ?? new Dictionary<string, LlmGatewayExperimentSettings>(),
                StringComparer.OrdinalIgnoreCase);

            foreach (var route in s.Routing.Values)
            {
                route.PrimaryProvider = route.PrimaryProvider?.Trim() ?? string.Empty;
                route.PrimaryModel = string.IsNullOrWhiteSpace(route.PrimaryModel) ? null : route.PrimaryModel.Trim();
                route.RetryPolicyClass = string.IsNullOrWhiteSpace(route.RetryPolicyClass) ? "default" : route.RetryPolicyClass.Trim();
                route.TimeoutBudgetClass = string.IsNullOrWhiteSpace(route.TimeoutBudgetClass) ? "default" : route.TimeoutBudgetClass.Trim();
                route.FallbackProviders ??= new List<LlmGatewayProviderTargetSettings>();
                foreach (var fallback in route.FallbackProviders)
                {
                    fallback.Provider = fallback.Provider?.Trim() ?? string.Empty;
                    fallback.Model = string.IsNullOrWhiteSpace(fallback.Model) ? null : fallback.Model.Trim();
                }
            }

            foreach (var provider in s.Providers.Values)
            {
                provider.BaseUrl = provider.BaseUrl?.Trim() ?? string.Empty;
                provider.ApiKey = provider.ApiKey?.Trim() ?? string.Empty;
                provider.DefaultModel = string.IsNullOrWhiteSpace(provider.DefaultModel) ? null : provider.DefaultModel.Trim();
                provider.ChatCompletionsPath = string.IsNullOrWhiteSpace(provider.ChatCompletionsPath)
                    ? "/v1/chat/completions"
                    : provider.ChatCompletionsPath.Trim();
                provider.EmbeddingsPath = string.IsNullOrWhiteSpace(provider.EmbeddingsPath)
                    ? "/v1/embeddings"
                    : provider.EmbeddingsPath.Trim();
                provider.TimeoutSeconds = Math.Max(1, provider.TimeoutSeconds);
            }

            foreach (var experiment in s.Experiments.Values)
            {
                experiment.Branches ??= new List<LlmGatewayExperimentBranchSettings>();
                foreach (var branch in experiment.Branches)
                {
                    branch.Branch = branch.Branch?.Trim() ?? string.Empty;
                    branch.Provider = branch.Provider?.Trim() ?? string.Empty;
                    branch.Model = string.IsNullOrWhiteSpace(branch.Model) ? null : branch.Model.Trim();
                    branch.WeightPercent = Math.Max(0, branch.WeightPercent);
                    branch.FallbackProviders ??= new List<LlmGatewayProviderTargetSettings>();
                    foreach (var fallback in branch.FallbackProviders)
                    {
                        fallback.Provider = fallback.Provider?.Trim() ?? string.Empty;
                        fallback.Model = string.IsNullOrWhiteSpace(fallback.Model) ? null : fallback.Model.Trim();
                    }
                }
            }
        });

        if (includeLegacyStage6ClusterDiagnostics)
        {
            services.PostConfigure<Stage6AutoCaseGenerationSettings>(s =>
            {
                s.PollIntervalSeconds = Math.Max(10, s.PollIntervalSeconds);
                s.ScopeLookbackHours = Math.Max(1, s.ScopeLookbackHours);
                s.CaseUpdateCooldownMinutes = Math.Max(1, s.CaseUpdateCooldownMinutes);
                s.StaleAfterNoSignalHours = Math.Max(1, s.StaleAfterNoSignalHours);
                s.MinMessagesForStateRefresh = Math.Max(1, s.MinMessagesForStateRefresh);
                s.MinMessagesForDossierCandidate = Math.Max(1, s.MinMessagesForDossierCandidate);
                s.MinMessagesForDraftCandidate = Math.Max(1, s.MinMessagesForDraftCandidate);
                s.StateRefreshMinAgeHours = Math.Max(1, s.StateRefreshMinAgeHours);
                s.DossierCandidateMinAgeHours = Math.Max(1, s.DossierCandidateMinAgeHours);
                s.DraftCandidatePendingHours = Math.Max(1, s.DraftCandidatePendingHours);
                s.RiskBlockingThreshold = Math.Clamp(s.RiskBlockingThreshold, 0f, 1f);
                s.RiskImportantThreshold = Math.Clamp(s.RiskImportantThreshold, 0f, 1f);
                s.NeedsReviewMaxConfidenceThreshold = Math.Clamp(s.NeedsReviewMaxConfidenceThreshold, 0f, 1f);
                s.AmbiguityClarificationThreshold = Math.Clamp(s.AmbiguityClarificationThreshold, 0f, 1f);
                s.EvidenceConflictConfidenceThreshold = Math.Clamp(s.EvidenceConflictConfidenceThreshold, 0f, 1f);
                s.NextStepBlockedConfidenceThreshold = Math.Clamp(s.NextStepBlockedConfidenceThreshold, 0f, 1f);
                s.NeedsInputFallbackConfidenceThreshold = Math.Clamp(s.NeedsInputFallbackConfidenceThreshold, 0f, 1f);
            });
        }

        return services;
    }
}

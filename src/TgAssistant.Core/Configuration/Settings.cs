using TgAssistant.Core.Models;

namespace TgAssistant.Core.Configuration;

public class TelegramSettings
{
    public const string Section = "Telegram";
    public int ApiId { get; set; }
    public string ApiHash { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string BotToken { get; set; } = string.Empty;
    public long OwnerUserId { get; set; }
    public List<long> MonitoredChatIds { get; set; } = new();
    public string MonitoredChats { get; set; } = string.Empty;
}

public class RedisSettings
{
    public const string Section = "Redis";
    public string ConnectionString { get; set; } = string.Empty;
    public string StreamName { get; set; } = "tg-messages";
    public string DeadLetterStreamName { get; set; } = "tg-messages-dlq";
    public string ConsumerGroup { get; set; } = "batch-workers";
    public string ConsumerName { get; set; } = "worker";
    public bool EnablePendingReclaim { get; set; } = true;
    public int PendingReclaimIntervalSeconds { get; set; } = 30;
    public int PendingMinIdleSeconds { get; set; } = 60;
    public int PendingReclaimBatchSize { get; set; } = 100;
    public int PendingMetricsLogIntervalSeconds { get; set; } = 60;
    public int PendingMetricsSampleSize { get; set; } = 100;
}

public class DatabaseSettings
{
    public const string Section = "Database";
    public string ConnectionString { get; set; } = string.Empty;
}

public class GeminiSettings
{
    public const string Section = "Gemini";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "google/gemini-2.0-flash-lite";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public int MaxParallelRequests { get; set; } = 5;
}

public class ClaudeSettings
{
    public const string Section = "Claude";
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "anthropic/claude-3.5-sonnet";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
}

public class LlmGatewaySettings
{
    public const string Section = "LlmGateway";
    public bool Enabled { get; set; }
    public bool LogRawProviderPayloadJson { get; set; }
    public Dictionary<string, LlmGatewayRouteSettings> Routing { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LlmGatewayProviderSettings> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, LlmGatewayExperimentSettings> Experiments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public LlmGatewayRouteSettings? GetRoute(LlmModality modality)
    {
        return TryGetValue(Routing, ToRouteKey(modality));
    }

    public LlmGatewayProviderSettings? GetProvider(string providerId)
    {
        return TryGetValue(Providers, providerId);
    }

    public LlmGatewayExperimentSettings? GetExperiment(string label)
    {
        return TryGetValue(Experiments, label);
    }

    public static string ToRouteKey(LlmModality modality)
    {
        return modality switch
        {
            LlmModality.TextChat => "text_chat",
            LlmModality.Tools => "tools",
            LlmModality.Embeddings => "embeddings",
            LlmModality.Vision => "vision",
            LlmModality.AudioTranscription => "audio_transcription",
            LlmModality.AudioParalinguistics => "audio_paralinguistics",
            _ => "unspecified"
        };
    }

    private static TValue? TryGetValue<TValue>(IReadOnlyDictionary<string, TValue> values, string key)
        where TValue : class
    {
        if (values.TryGetValue(key, out var exact))
        {
            return exact;
        }

        var match = values.FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(match.Key) ? null : match.Value;
    }
}

public class LlmGatewayRouteSettings
{
    public bool Enabled { get; set; } = true;
    public string PrimaryProvider { get; set; } = string.Empty;
    public string? PrimaryModel { get; set; }
    public List<LlmGatewayProviderTargetSettings> FallbackProviders { get; set; } = new();
    public string RetryPolicyClass { get; set; } = "default";
    public string TimeoutBudgetClass { get; set; } = "default";
}

public class LlmGatewayProviderTargetSettings
{
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
}

public class LlmGatewayProviderSettings
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool UseAuthorizationHeader { get; set; } = true;
    public string? DefaultModel { get; set; }
    public string ChatCompletionsPath { get; set; } = "/v1/chat/completions";
    public string EmbeddingsPath { get; set; } = "/v1/embeddings";
    public int TimeoutSeconds { get; set; } = 120;
    public decimal? TotalCostUsdPer1kTokens { get; set; }
    public decimal? PromptCostUsdPer1kTokens { get; set; }
    public decimal? CompletionCostUsdPer1kTokens { get; set; }
}

public class LlmGatewayExperimentSettings
{
    public bool Enabled { get; set; } = true;
    public List<LlmGatewayExperimentBranchSettings> Branches { get; set; } = new();
}

public class LlmGatewayExperimentBranchSettings
{
    public string Branch { get; set; } = string.Empty;
    public int WeightPercent { get; set; } = 100;
    public string Provider { get; set; } = string.Empty;
    public string? Model { get; set; }
    public List<LlmGatewayProviderTargetSettings> FallbackProviders { get; set; } = new();
}

public class BatchWorkerSettings
{
    public const string Section = "BatchWorker";
    public int MaxBatchTimeSeconds { get; set; } = 10;
    public int MaxBatchSize { get; set; } = 50;
    public int SilenceTimeoutSeconds { get; set; } = 3;
}

public class MediaSettings
{
    public const string Section = "Media";
    public string StoragePath { get; set; } = "/data/media";
    public int MaxProcessFileSizeMb { get; set; } = 25;
    public int MaxImageLongSide { get; set; } = 1280;
    public int JpegQuality { get; set; } = 80;
    public bool EnablePhotoBurstGuard { get; set; } = true;
    public int PhotoBurstThreshold { get; set; } = 10;
    public int PhotoBurstKeepCount { get; set; } = 3;
    public int PhotoBurstWindowSeconds { get; set; } = 120;
    public string VisionModel { get; set; } = "qwen/qwen2.5-vl-72b-instruct";
    public string ArchiveVisionModel { get; set; } = "qwen/qwen2.5-vl-72b-instruct";
    public string AudioModel { get; set; } = "openai/gpt-audio-mini";
    public int VisionMaxTokens { get; set; } = 220;
    public int ArchiveVisionMaxTokens { get; set; } = 120;
}

public class VoiceParalinguisticsSettings
{
    public const string Section = "VoiceParalinguistics";
    public bool Enabled { get; set; } = true;
    public bool RealtimeOnlyNewMessages { get; set; } = true;
    public int RealtimeOnlyLookbackSeconds { get; set; } = 30;
    public bool TranscriptionEnabled { get; set; } = true;
    public bool DeleteSourceAudioAfterProcessing { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 30;
    public int MaxParallel { get; set; } = 2;
    public string Model { get; set; } = "openai/gpt-audio-mini";
    public int MaxTokens { get; set; } = 300;
    public int RetryCount { get; set; } = 2;
    public int RetryBaseDelaySeconds { get; set; } = 2;
    public int TransientBackoffBaseSeconds { get; set; } = 60;
    public int TransientBackoffMaxSeconds { get; set; } = 900;
}

public class ArchiveImportSettings
{
    public const string Section = "ArchiveImport";
    public bool Enabled { get; set; }
    public bool MediaProcessingEnabled { get; set; } = true;
    public bool RequireCostConfirmation { get; set; } = true;
    public bool ConfirmProcessing { get; set; }
    public string SourcePath { get; set; } = "/data/archive/result.json";
    public string MediaBasePath { get; set; } = "/data/archive";
    public int BatchSize { get; set; } = 500;
    public int MaxParallelMedia { get; set; } = 10;
    public int RequestsPerMinute { get; set; } = 300;
    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxMediaFileSizeMb { get; set; } = 50;
}

public class BackfillSettings
{
    public const string Section = "Backfill";
    public bool Enabled { get; set; }
    public string SinceDate { get; set; } = "2026-03-07";
    public List<long> ChatIds { get; set; } = new();
}

public class ChatCoordinationSettings
{
    public const string Section = "ChatCoordination";
    public bool Enabled { get; set; } = true;
    public bool PhaseGuardsEnabled { get; set; } = true;
    public int PhaseGuardLeaseTtlMinutes { get; set; } = 30;
    public int HandoverPendingExtractionThreshold { get; set; } = 100;
    public int ListenerEligibilityRefreshSeconds { get; set; } = 60;
    public bool AutoRecoveryCatchupEnabled { get; set; } = true;
    public int DowntimeCatchupThresholdMinutes { get; set; } = 30;
    public bool EnforceGlobalBackfillExclusivity { get; set; } = false;
    public int TailReopenMaxSessionLag { get; set; } = 3;
    public int TailReopenMaxWindowHours { get; set; } = 24;
}

public class RiskyOperationSafetySettings
{
    public const string Section = "RiskyOperationSafety";
    public bool Enabled { get; set; } = true;
    public bool RequireBackupEvidenceForRepairApply { get; set; } = true;
    public int BackupFreshnessHours { get; set; } = 6;
    public int IntegrityWriteVolumeUnsafeThreshold { get; set; } = 5000;
    public int IntegrityWriteVolumeWarningThreshold { get; set; } = 1500;
    public bool AllowWarningApplyWithoutOverride { get; set; } = false;
}

public class AnalysisSettings
{
    public const string Section = "Analysis";
    public bool Enabled { get; set; } = false;
    public bool ArchiveOnlyMode { get; set; } = false;
    public string ArchiveCutoffUtc { get; set; } = string.Empty;
    public string CheapModel { get; set; } = "deepseek/deepseek-v3.2";
    public string CheapPromptId { get; set; } = "stage5_cheap_extract_v10";
    public bool CheapModelAbEnabled { get; set; } = false;
    public string CheapBaselineModel { get; set; } = "openai/gpt-4o-mini";
    public string CheapCandidateModel { get; set; } = "deepseek/deepseek-v3.2";
    public int CheapAbCandidatePercent { get; set; } = 50;
    public string ExpensiveModel { get; set; } = "anthropic/claude-3.5-sonnet";
    public string ExpensiveFallbackModel { get; set; } = "openai/gpt-4o";
    public int BatchSize { get; set; } = 8;
    public int CheapBatchWorkers { get; set; } = 1;
    public int CheapLlmParallelism { get; set; } = 4;
    public string CheapProviderOrder { get; set; } = string.Empty;
    public bool CheapProviderAllowFallbacks { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 2;
    public bool ExpensivePassEnabled { get; set; } = false;
    public int MaxExpensivePerBatch { get; set; } = 10;
    public int FactReviewBatchSize { get; set; } = 100;
    public int CheapMaxTokens { get; set; } = 2000;
    public int ExpensiveMaxTokens { get; set; } = 3000;
    public int HttpTimeoutSeconds { get; set; } = 120;
    public int ExpensiveCooldownMinutes { get; set; } = 10;
    public int ExpensiveFailureBackoffBaseSeconds { get; set; } = 30;
    public int ExpensiveFailureBackoffMaxMinutes { get; set; } = 30;
    public int ExpensiveRetryBaseSeconds { get; set; } = 30;
    public int MaxExpensiveRetryCount { get; set; } = 5;
    public decimal ExpensiveDailyBudgetUsd { get; set; } = 0m;
    public int ExpensiveContextMaxFacts { get; set; } = 60;
    public int ExpensiveContextMaxChars { get; set; } = 12000;
    public int FactReviewReenqueueHours { get; set; } = 24;
    public float CheapConfidenceThreshold { get; set; } = 0.8f;
    public float MinFactConfidence { get; set; } = 0.55f;
    public float MinSensitiveFactConfidence { get; set; } = 0.75f;
    public float AutoConfirmFactConfidence { get; set; } = 0.95f;
    public float MinRelationshipConfidence { get; set; } = 0.6f;
    public int TemporalFactSupersedeTtlHours { get; set; } = 12;
    public int LocalBurstContextMessages { get; set; } = 6;
    public int LocalBurstContextWindowMinutes { get; set; } = 120;
    public int SessionContextLookbackMessages { get; set; } = 80;
    public int SessionContextGapMinutes { get; set; } = 180;
    public int SessionStartContextMessages { get; set; } = 4;
    public int HistoricalSummaryContextItems { get; set; } = 6;
    public bool SummaryEnabled { get; set; } = true;
    public bool SummaryWorkerEnabled { get; set; } = false;
    public bool SummaryContractNormalizationEnabled { get; set; } = false;
    public string SummaryModel { get; set; } = "openai/gpt-4o-mini";
    public int SummaryPollIntervalSeconds { get; set; } = 30;
    public int SummaryBatchSize { get; set; } = 100;
    public int SummaryDayMaxMessages { get; set; } = 500;
    public int SummarySessionMaxMessages { get; set; } = 120;
    public int SummarySessionGapMinutes { get; set; } = 180;
    public int SummaryMinMessages { get; set; } = 4;
    public int SummaryMaxTokens { get; set; } = 800;
    public bool SummaryHistoricalHintsEnabled { get; set; } = true;
    public int SummaryHistoricalHintsTopK { get; set; } = 3;
    public int SummaryHistoricalHintsCandidatePool { get; set; } = 12;
    public int SummaryHistoricalHintsTimeoutMs { get; set; } = 2000;
    public int SummaryHistoricalHintsQueryMaxChars { get; set; } = 2000;
    public int SummaryHistoricalHintsMaxCharsPerItem { get; set; } = 320;
    public float SummaryHistoricalHintsMinSimilarity { get; set; } = 0.72f;
    public bool EditDiffEnabled { get; set; } = true;
    public bool EditDiffGatewayEnabled { get; set; } = false;
    public int EditDiffBatchSize { get; set; } = 20;
    public int EditDiffPollIntervalSeconds { get; set; } = 5;
    public int EditDiffMaxTokens { get; set; } = 320;
    public int HotSessionGapMinutes { get; set; } = 30;
    public int EpisodicSessionGapMinutes { get; set; } = 120;
    public int EpisodicMaxSessionsPerChat { get; set; } = 0;
    public bool EnableTestModeSessionCap { get; set; } = false;
    public int EpisodicShortSessionMergeThreshold { get; set; } = 10;
    public int EpisodicShortSessionMaxBridgeGapMinutes { get; set; } = 1440;
    public int TestModeMaxSessionsPerChat { get; set; } = 23;
    public int SessionChunkSize { get; set; } = 40;
    public int SessionAnalysisBatchSize { get; set; } = 20;
    public int SessionAnalysisMinIdleMinutes { get; set; } = 15;
    public int SessionChunkTargetChars { get; set; } = 6000;
    public int SessionChunkMaxChars { get; set; } = 9000;
    public int SessionChunkMinMessages { get; set; } = 12;
    public int SessionChunkHardMaxMessages { get; set; } = 80;
    public int SessionChunkPauseGapMinutes { get; set; } = 25;
    public int SessionChunkParallelism { get; set; } = 2;
    public int CheapChunkTargetChars { get; set; } = 12000;
    public int CheapChunkMaxChars { get; set; } = 15000;
    public int CheapChunkMinMessages { get; set; } = 6;
    public int CheapChunkPauseGapMinutes { get; set; } = 20;
}

public class AggregationSettings
{
    public const string Section = "Aggregation";
    public int MinIdleAgeHours { get; set; } = 6;
    public int ArchiveThresholdHours { get; set; } = 24;
}

public class MergeSettings
{
    public const string Section = "Merge";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int MaxCandidatesPerRun { get; set; } = 500;
    public int CommandBatchSize { get; set; } = 100;
    public float AutoRejectScoreThreshold { get; set; } = 0.2f;
    public int AutoRejectAliasLengthMax { get; set; } = 3;
    public bool SemanticGateEnabled { get; set; } = true;
    public float SemanticRejectSimilarityThreshold { get; set; } = 0.55f;
    public float SemanticAutoMergeSimilarityThreshold { get; set; } = 0.92f;
    public int SemanticAutoMergeMinEvidence { get; set; } = 2;
}

public class MonitoringSettings
{
    public const string Section = "Monitoring";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
}

public class MaintenanceSettings
{
    public const string Section = "Maintenance";
    public bool Enabled { get; set; } = true;
    public bool FactDecayEnabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = 60;
    public int ExtractionErrorsRetentionDays { get; set; } = 14;
    public int Stage5MetricsRetentionDays { get; set; } = 30;
    public int MergeDecisionsRetentionDays { get; set; } = 90;
    public int FactReviewCommandsRetentionDays { get; set; } = 30;
    public int FactReviewPendingTimeoutDays { get; set; } = 7;
}

public class Neo4jSettings
{
    public const string Section = "Neo4j";
    public bool Enabled { get; set; } = false;
    public string BaseUrl { get; set; } = "http://localhost:7474";
    public string Username { get; set; } = "neo4j";
    public string Password { get; set; } = string.Empty;
    public string Database { get; set; } = "neo4j";
    public int PollIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 200;
}

public class EmbeddingSettings
{
    public const string Section = "Embedding";
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "text-embedding-3-small";
    public int PollIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 100;
}

public class BotChatSettings
{
    public const string Section = "BotChat";
    public long OwnerId { get; set; }
    public long DefaultCaseId { get; set; }
    public long DefaultChatId { get; set; }
}

public class WebSettings
{
    public const string Section = "Web";
    public string Url { get; set; } = "http://127.0.0.1:5078";
    public bool RequireOperatorAccessToken { get; set; } = true;
    public bool AllowSyntheticScopes { get; set; } = false;
    public string OperatorAccessToken { get; set; } = string.Empty;
    public string AccessHeaderName { get; set; } = "X-Tga-Operator-Key";
    public string AccessCookieName { get; set; } = "tga_operator_key";
    public string OperatorIdentity { get; set; } = "web-operator";
    public long DefaultCaseId { get; set; }
    public long DefaultChatId { get; set; }
}

public class ContinuousRefinementSettings
{
    public const string Section = "ContinuousRefinement";
    public bool Enabled { get; set; } = false;
    public int PollIntervalSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 10;
    public int MinMessageLength { get; set; } = 280;
    public int StaleAfterHours { get; set; } = 168;
    public int MinDelaySeconds { get; set; } = 60;
    public int MaxDelaySeconds { get; set; } = 120;
    public float LowConfidenceThreshold { get; set; } = 0.7f;
}

public class Stage6AutoCaseGenerationSettings
{
    public const string Section = "Stage6AutoCaseGeneration";
    public bool Enabled { get; set; } = false;
    public int PollIntervalSeconds { get; set; } = 120;
    public int ScopeLookbackHours { get; set; } = 72;
    public int CaseUpdateCooldownMinutes { get; set; } = 30;
    public int StaleAfterNoSignalHours { get; set; } = 24;
    public float RiskBlockingThreshold { get; set; } = 0.75f;
    public float RiskImportantThreshold { get; set; } = 0.6f;
    public float NeedsReviewMaxConfidenceThreshold { get; set; } = 0.55f;
    public float AmbiguityClarificationThreshold { get; set; } = 0.65f;
    public float EvidenceConflictConfidenceThreshold { get; set; } = 0.5f;
    public float NextStepBlockedConfidenceThreshold { get; set; } = 0.55f;
    public float NeedsInputFallbackConfidenceThreshold { get; set; } = 0.5f;
    public int MinMessagesForStateRefresh { get; set; } = 4;
    public int MinMessagesForDossierCandidate { get; set; } = 8;
    public int MinMessagesForDraftCandidate { get; set; } = 2;
    public int StateRefreshMinAgeHours { get; set; } = 6;
    public int DossierCandidateMinAgeHours { get; set; } = 24;
    public int DraftCandidatePendingHours { get; set; } = 12;
}

public class BudgetGuardrailSettings
{
    public const string Section = "BudgetGuardrails";
    public bool Enabled { get; set; } = true;
    public decimal DailyBudgetUsd { get; set; } = 0m;
    public decimal ImportBudgetUsd { get; set; } = 0m;
    public decimal SoftLimitRatio { get; set; } = 0.85m;
    public decimal HardLimitRatio { get; set; } = 1.00m;
    public decimal StageTextAnalysisBudgetUsd { get; set; } = 0m;
    public decimal StageEmbeddingsBudgetUsd { get; set; } = 0m;
    public decimal StageVisionBudgetUsd { get; set; } = 0m;
    public decimal StageAudioBudgetUsd { get; set; } = 0m;
    public int QuotaBlockMinutes { get; set; } = 20;
}

public class EvalHarnessSettings
{
    public const string Section = "EvalHarness";
    public bool Enabled { get; set; } = true;
    public string DefaultRunName { get; set; } = "default";
}

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
    public string ConnectionString { get; set; } = "localhost:6379";
    public string StreamName { get; set; } = "tg-messages";
    public string ConsumerGroup { get; set; } = "batch-workers";
    public string ConsumerName { get; set; } = "worker-1";
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
    public int PollIntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 30;
    public int MaxParallel { get; set; } = 2;
    public string Model { get; set; } = "openai/gpt-audio-mini";
    public int MaxTokens { get; set; } = 300;
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

public class AnalysisSettings
{
    public const string Section = "Analysis";
    public bool Enabled { get; set; } = false;
    public string CheapModel { get; set; } = "deepseek/deepseek-chat";
    public bool CheapModelAbEnabled { get; set; } = false;
    public string CheapBaselineModel { get; set; } = "openai/gpt-4o-mini";
    public string CheapCandidateModel { get; set; } = "deepseek/deepseek-chat";
    public int CheapAbCandidatePercent { get; set; } = 50;
    public string ExpensiveModel { get; set; } = "anthropic/claude-3.5-sonnet";
    public string ExpensiveFallbackModel { get; set; } = "openai/gpt-4o";
    public int BatchSize { get; set; } = 50;
    public int PollIntervalSeconds { get; set; } = 10;
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

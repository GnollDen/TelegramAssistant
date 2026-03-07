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
    public string Model { get; set; } = "anthropic/claude-sonnet-4";
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
    public string VisionModel { get; set; } = "openai/gpt-4o-mini";
    public string ArchiveVisionModel { get; set; } = "meta-llama/llama-4-scout";
    public string AudioModel { get; set; } = "openai/gpt-audio-mini";
    public int VisionMaxTokens { get; set; } = 220;
    public int ArchiveVisionMaxTokens { get; set; } = 120;
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
    public int MaxParallelMedia { get; set; } = 2;
    public int RequestsPerMinute { get; set; } = 30;
    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxMediaFileSizeMb { get; set; } = 50;
}

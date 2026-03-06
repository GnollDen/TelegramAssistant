namespace TgAssistant.Core.Configuration;

public class TelegramSettings
{
    public const string Section = "Telegram";
    
    /// <summary>
    /// Telegram API ID from https://my.telegram.org
    /// </summary>
    public int ApiId { get; set; }
    
    /// <summary>
    /// Telegram API Hash from https://my.telegram.org
    /// </summary>
    public string ApiHash { get; set; } = string.Empty;
    
    /// <summary>
    /// Phone number for userbot authentication.
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;
    
    /// <summary>
    /// Bot token for Telegram Bot API (interaction channel).
    /// </summary>
    public string BotToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Owner's Telegram user ID (only this user can interact with the bot).
    /// </summary>
    public long OwnerUserId { get; set; }
    
    /// <summary>
    /// Chat IDs to monitor (hardcoded for MVP).
    /// </summary>
    public List<long> MonitoredChatIds { get; set; } = new();
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
    public string Model { get; set; } = "gemini-2.0-flash-lite";
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
    
    /// <summary>
    /// Max seconds to wait before flushing a batch.
    /// </summary>
    public int MaxBatchTimeSeconds { get; set; } = 10;
    
    /// <summary>
    /// Max messages in a single batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 50;
    
    /// <summary>
    /// Seconds of silence after last message to trigger flush.
    /// </summary>
    public int SilenceTimeoutSeconds { get; set; } = 3;
}

public class MediaSettings
{
    public const string Section = "Media";
    
    /// <summary>
    /// Base path for storing downloaded media files.
    /// </summary>
    public string StoragePath { get; set; } = "/data/media";
}

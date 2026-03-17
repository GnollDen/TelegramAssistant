using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TgAssistant.Core.Configuration;
using TgAssistant.Intelligence.Stage6;

namespace TgAssistant.Telegram.Bot;

public class TelegramBotHostedService : BackgroundService
{
    private const int MaxTelegramMessageChars = 4000;
    private readonly TelegramSettings _telegramSettings;
    private readonly BotChatSettings _settings;
    private readonly IBotChatService _botChatService;
    private readonly ILogger<TelegramBotHostedService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TelegramBotHostedService"/> class.
    /// </summary>
    public TelegramBotHostedService(
        IOptions<TelegramSettings> telegramSettings,
        IOptions<BotChatSettings> settings,
        IBotChatService botChatService,
        ILogger<TelegramBotHostedService> logger)
    {
        _telegramSettings = telegramSettings.Value;
        _settings = settings.Value;
        _botChatService = botChatService;
        _logger = logger;

        if (_settings.OwnerId <= 0 && _telegramSettings.OwnerUserId > 0)
        {
            _settings.OwnerId = _telegramSettings.OwnerUserId;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_telegramSettings.BotToken))
        {
            _logger.LogWarning("Telegram bot service is disabled because Telegram:BotToken is empty.");
            return;
        }

        if (_settings.OwnerId <= 0)
        {
            _logger.LogWarning("Telegram bot service has no configured owner id. Unauthorized gate will deny all messages.");
        }

        var botClient = new TelegramBotClient(_telegramSettings.BotToken);
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>()
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        _logger.LogInformation("Telegram bot receiver started.");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Telegram bot receiver stopping.");
        }
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        try
        {
            var message = update.Message;
            if (message == null)
            {
                return;
            }

            if (message.From == null || message.From.Id != _settings.OwnerId)
            {
                _logger.LogWarning(
                    "Dropped unauthorized bot message. ChatId={ChatId}, SenderId={SenderId}, ExpectedOwnerId={OwnerId}",
                    message.Chat.Id,
                    message.From?.Id,
                    _settings.OwnerId);
                return;
            }

            if (string.IsNullOrWhiteSpace(message.Text))
            {
                return;
            }

            var normalizedText = message.Text.Trim();
            string reply;
            if (TryParseResummaryCommand(normalizedText, out var chatId, out var sessionIndex))
            {
                reply = await _botChatService.TriggerSessionResummaryAsync(chatId, sessionIndex, ct);
            }
            else if (normalizedText.StartsWith("/resummary", StringComparison.OrdinalIgnoreCase))
            {
                reply = "Usage: /resummary <chat_id> <session_index>";
            }
            else
            {
                reply = await _botChatService.GenerateReplyAsync(normalizedText);
            }

            var chunks = SplitReplyToChunks(reply, MaxTelegramMessageChars);
#pragma warning disable CS0618 // Required by current phase requirement to call SendTextMessageAsync.
            foreach (var chunk in chunks)
            {
                await botClient.SendTextMessageAsync(message.Chat.Id, chunk, cancellationToken: ct);
            }
#pragma warning restore CS0618
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling Telegram bot update.");
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram bot polling error.");
        return Task.CompletedTask;
    }

    private static List<string> SplitReplyToChunks(string reply, int maxLength)
    {
        var normalized = (reply ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return ["I cannot provide a response right now."];
        }

        if (normalized.Length <= maxLength)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        var remaining = normalized;
        while (remaining.Length > maxLength)
        {
            var candidate = remaining[..maxLength];
            var splitIndex = candidate.LastIndexOf("\n\n", StringComparison.Ordinal);
            if (splitIndex < maxLength / 3)
            {
                splitIndex = candidate.LastIndexOf('\n');
            }

            if (splitIndex < maxLength / 3)
            {
                splitIndex = candidate.LastIndexOf(' ');
            }

            if (splitIndex <= 0)
            {
                splitIndex = maxLength;
            }

            var chunk = remaining[..splitIndex].Trim();
            if (chunk.Length == 0)
            {
                chunk = remaining[..Math.Min(maxLength, remaining.Length)];
                splitIndex = chunk.Length;
            }

            chunks.Add(chunk);
            remaining = remaining[splitIndex..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            chunks.Add(remaining);
        }

        return chunks;
    }

    private static bool TryParseResummaryCommand(string text, out long chatId, out int sessionIndex)
    {
        chatId = 0;
        sessionIndex = 0;
        if (!text.StartsWith("/resummary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
        {
            return false;
        }

        return long.TryParse(parts[1], out chatId) && int.TryParse(parts[2], out sessionIndex);
    }
}

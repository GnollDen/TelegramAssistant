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

            var reply = await _botChatService.GenerateReplyAsync(message.Text);
#pragma warning disable CS0618 // Required by current phase requirement to call SendTextMessageAsync.
            await botClient.SendTextMessageAsync(message.Chat.Id, reply, cancellationToken: ct);
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
}

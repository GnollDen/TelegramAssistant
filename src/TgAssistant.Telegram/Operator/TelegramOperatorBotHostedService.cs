using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramOperatorBotHostedService : BackgroundService
{
    private readonly TelegramSettings _settings;
    private readonly TelegramBotApiClient _botApiClient;
    private readonly TelegramOperatorWorkflowService _workflowService;
    private readonly ILogger<TelegramOperatorBotHostedService> _logger;

    public TelegramOperatorBotHostedService(
        IOptions<TelegramSettings> settings,
        TelegramBotApiClient botApiClient,
        TelegramOperatorWorkflowService workflowService,
        ILogger<TelegramOperatorBotHostedService> logger)
    {
        _settings = settings.Value;
        _botApiClient = botApiClient;
        _workflowService = workflowService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_botApiClient.IsConfigured)
        {
            _logger.LogInformation("Telegram operator bot skipped because Telegram:BotToken is not configured.");
            return;
        }

        if (_settings.OwnerUserId <= 0)
        {
            _logger.LogWarning("Telegram operator bot skipped because Telegram:OwnerUserId is not configured.");
            return;
        }

        _logger.LogInformation("Telegram operator bot starting for owner user {OwnerUserId}", _settings.OwnerUserId);

        long? offset = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _botApiClient.GetUpdatesAsync(offset, timeoutSeconds: 25, stoppingToken);
                foreach (var update in updates.OrderBy(x => x.UpdateId))
                {
                    offset = update.UpdateId + 1;
                    var interaction = Translate(update);
                    if (interaction == null)
                    {
                        continue;
                    }

                    var response = await _workflowService.HandleInteractionAsync(interaction, stoppingToken);
                    if (!string.IsNullOrWhiteSpace(interaction.CallbackQueryId))
                    {
                        await _botApiClient.AnswerCallbackQueryAsync(
                            interaction.CallbackQueryId!,
                            response.CallbackNotificationText,
                            stoppingToken);
                    }

                    foreach (var message in response.Messages)
                    {
                        await _botApiClient.SendMessageAsync(interaction.ChatId, message, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Telegram operator bot polling loop failed. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private static TelegramOperatorInteraction? Translate(TelegramBotUpdate update)
    {
        if (update.CallbackQuery?.Message?.Chat != null)
        {
            return new TelegramOperatorInteraction
            {
                ChatId = update.CallbackQuery.Message.Chat.Id,
                UserId = update.CallbackQuery.From.Id,
                IsPrivateChat = string.Equals(update.CallbackQuery.Message.Chat.Type, "private", StringComparison.OrdinalIgnoreCase),
                UserDisplayName = BuildUserDisplayName(update.CallbackQuery.From),
                CallbackData = update.CallbackQuery.Data,
                CallbackQueryId = update.CallbackQuery.Id
            };
        }

        if (update.Message?.From == null || update.Message.From.IsBot)
        {
            return null;
        }

        return new TelegramOperatorInteraction
        {
            ChatId = update.Message.Chat.Id,
            UserId = update.Message.From.Id,
            IsPrivateChat = string.Equals(update.Message.Chat.Type, "private", StringComparison.OrdinalIgnoreCase),
            UserDisplayName = BuildUserDisplayName(update.Message.From),
            MessageText = update.Message.Text
        };
    }

    private static string BuildUserDisplayName(TelegramBotUser user)
    {
        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName)
            ? string.IsNullOrWhiteSpace(user.Username) ? $"Telegram User {user.Id}" : user.Username.Trim()
            : displayName;
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramOperatorBotHostedService : BackgroundService
{
    private readonly TelegramSettings _settings;
    private readonly TelegramBotApiClient _botApiClient;
    private readonly TelegramOperatorSessionStore _sessionStore;
    private readonly TelegramOperatorWorkflowService _workflowService;
    private readonly ILogger<TelegramOperatorBotHostedService> _logger;

    public TelegramOperatorBotHostedService(
        IOptions<TelegramSettings> settings,
        TelegramBotApiClient botApiClient,
        TelegramOperatorSessionStore sessionStore,
        TelegramOperatorWorkflowService workflowService,
        ILogger<TelegramOperatorBotHostedService> logger)
    {
        _settings = settings.Value;
        _botApiClient = botApiClient;
        _sessionStore = sessionStore;
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

                    if (!string.IsNullOrWhiteSpace(response.TrackMessagesForSurfaceMode))
                    {
                        foreach (var messageId in _sessionStore.GetTrackedMessageIds(interaction.ChatId, response.TrackMessagesForSurfaceMode!))
                        {
                            await _botApiClient.ClearMessageButtonsAsync(interaction.ChatId, messageId, stoppingToken);
                        }
                    }

                    foreach (var edit in response.MessageEdits)
                    {
                        await _botApiClient.EditMessageAsync(
                            interaction.ChatId,
                            edit.MessageId,
                            new TelegramOperatorMessage
                            {
                                Text = edit.Text,
                                Buttons = edit.Buttons
                            },
                            stoppingToken);
                    }

                    var startIndex = 0;
                    var trackedMessageIds = new List<long>();
                    if (response.MessageEdits.Count == 0
                        && !string.IsNullOrWhiteSpace(interaction.CallbackQueryId)
                        && interaction.SourceMessageId.HasValue
                        && response.Messages.Count > 0)
                    {
                        var edited = await _botApiClient.EditMessageAsync(
                            interaction.ChatId,
                            interaction.SourceMessageId.Value,
                            response.Messages[0],
                            stoppingToken);
                        if (edited)
                        {
                            startIndex = 1;
                            trackedMessageIds.Add(interaction.SourceMessageId.Value);
                        }
                    }

                    foreach (var message in response.Messages.Skip(startIndex))
                    {
                        var messageId = await _botApiClient.SendMessageAsync(interaction.ChatId, message, stoppingToken);
                        if (messageId.HasValue)
                        {
                            trackedMessageIds.Add(messageId.Value);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(response.TrackMessagesForSurfaceMode))
                    {
                        _sessionStore.ReplaceTrackedMessageIds(
                            interaction.ChatId,
                            response.TrackMessagesForSurfaceMode!,
                            trackedMessageIds);
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
                SourceMessageId = update.CallbackQuery.Message.MessageId,
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
            SourceMessageId = update.Message.MessageId,
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

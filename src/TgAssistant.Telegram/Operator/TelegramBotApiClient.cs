using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramBotApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly TelegramSettings _settings;
    private readonly ILogger<TelegramBotApiClient> _logger;

    public TelegramBotApiClient(
        HttpClient httpClient,
        IOptions<TelegramSettings> settings,
        ILogger<TelegramBotApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.BotToken);

    public async Task<IReadOnlyList<TelegramBotUpdate>> GetUpdatesAsync(
        long? offset,
        int timeoutSeconds,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var response = await _httpClient.PostAsJsonAsync(
            BuildUrl("getUpdates"),
            new TelegramGetUpdatesRequest
            {
                Offset = offset,
                Timeout = timeoutSeconds,
                AllowedUpdates =
                [
                    "message",
                    "callback_query"
                ]
            },
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TelegramBotApiResponse<List<TelegramBotUpdate>>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Telegram getUpdates returned an empty payload.");
        if (!payload.Ok)
        {
            throw new InvalidOperationException($"Telegram getUpdates failed: {payload.Description ?? "unknown error"}");
        }

        return payload.Result ?? [];
    }

    public async Task SendMessageAsync(
        long chatId,
        TelegramOperatorMessage message,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return;
        }

        var response = await _httpClient.PostAsJsonAsync(
            BuildUrl("sendMessage"),
            new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = message.Text,
                DisableNotification = true,
                ReplyMarkup = message.Buttons.Count == 0
                    ? null
                    : new TelegramInlineKeyboardMarkup
                    {
                        InlineKeyboard = message.Buttons
                            .Select(row => row.Select(button => new TelegramInlineKeyboardButton
                            {
                                Text = button.Text,
                                CallbackData = button.CallbackData
                            }).ToList())
                            .ToList()
                    }
            },
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task AnswerCallbackQueryAsync(
        string callbackQueryId,
        string? text,
        CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(callbackQueryId))
        {
            return;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                BuildUrl("answerCallbackQuery"),
                new TelegramAnswerCallbackQueryRequest
                {
                    CallbackQueryId = callbackQueryId,
                    Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
                    ShowAlert = false
                },
                JsonOptions,
                ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram answerCallbackQuery failed for callback {CallbackQueryId}", callbackQueryId);
        }
    }

    private string BuildUrl(string method)
        => $"https://api.telegram.org/bot{_settings.BotToken.Trim()}/{method}";
}

public sealed class TelegramBotUpdate
{
    public long UpdateId { get; set; }
    public TelegramBotMessage? Message { get; set; }
    public TelegramBotCallbackQuery? CallbackQuery { get; set; }
}

public sealed class TelegramBotMessage
{
    public long MessageId { get; set; }
    public TelegramBotChat Chat { get; set; } = new();
    public TelegramBotUser? From { get; set; }
    public string? Text { get; set; }
}

public sealed class TelegramBotCallbackQuery
{
    public string Id { get; set; } = string.Empty;
    public TelegramBotUser From { get; set; } = new();
    public TelegramBotMessage? Message { get; set; }
    public string? Data { get; set; }
}

public sealed class TelegramBotChat
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
}

public sealed class TelegramBotUser
{
    public long Id { get; set; }
    public bool IsBot { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

internal sealed class TelegramBotApiResponse<TResult>
{
    public bool Ok { get; set; }
    public TResult? Result { get; set; }
    public string? Description { get; set; }
}

internal sealed class TelegramGetUpdatesRequest
{
    public long? Offset { get; set; }
    public int Timeout { get; set; }
    public List<string> AllowedUpdates { get; set; } = [];
}

internal sealed class TelegramSendMessageRequest
{
    public long ChatId { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool DisableNotification { get; set; }
    public TelegramInlineKeyboardMarkup? ReplyMarkup { get; set; }
}

internal sealed class TelegramInlineKeyboardMarkup
{
    public List<List<TelegramInlineKeyboardButton>> InlineKeyboard { get; set; } = [];
}

internal sealed class TelegramInlineKeyboardButton
{
    public string Text { get; set; } = string.Empty;
    public string CallbackData { get; set; } = string.Empty;
}

internal sealed class TelegramAnswerCallbackQueryRequest
{
    public string CallbackQueryId { get; set; } = string.Empty;
    public string? Text { get; set; }
    public bool ShowAlert { get; set; }
}

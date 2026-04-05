using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramBotApiClient
{
    private static readonly string[] ForbiddenVisiblePayloadMarkers =
    [
        "Open Web Handoff",
        "tracked_person_id",
        "scope_item_key",
        "operator_session_id",
        "target_api",
        "handoff_token"
    ];

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

    public async Task<long?> SendMessageAsync(
        long chatId,
        TelegramOperatorMessage message,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var keyboardRows = BuildInlineKeyboard(message);
        var text = SanitizeVisibleText(message.Text);

        var response = await _httpClient.PostAsJsonAsync(
            BuildUrl("sendMessage"),
            new TelegramSendMessageRequest
            {
                ChatId = chatId,
                Text = text,
                DisableNotification = true,
                ReplyMarkup = keyboardRows.Count == 0
                    ? null
                    : new TelegramInlineKeyboardMarkup
                    {
                        InlineKeyboard = keyboardRows
                    }
            },
            JsonOptions,
            ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<TelegramBotApiResponse<TelegramBotMessage>>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Telegram sendMessage returned an empty payload.");
        if (!payload.Ok)
        {
            throw new InvalidOperationException($"Telegram sendMessage failed: {payload.Description ?? "unknown error"}");
        }

        return payload.Result?.MessageId;
    }

    public async Task<bool> EditMessageAsync(
        long chatId,
        long messageId,
        TelegramOperatorMessage message,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            var keyboardRows = BuildInlineKeyboard(message);
            var text = SanitizeVisibleText(message.Text);
            var response = await _httpClient.PostAsJsonAsync(
                BuildUrl("editMessageText"),
                new TelegramEditMessageTextRequest
                {
                    ChatId = chatId,
                    MessageId = messageId,
                    Text = text,
                    ReplyMarkup = keyboardRows.Count == 0
                        ? null
                        : new TelegramInlineKeyboardMarkup
                        {
                            InlineKeyboard = keyboardRows
                        }
                },
                JsonOptions,
                ct);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var payload = await response.Content.ReadFromJsonAsync<TelegramBotApiResponse<object>>(JsonOptions, ct);
            var description = payload?.Description?.Trim() ?? string.Empty;
            if ((int)response.StatusCode == 400 && IsIgnorableEditError(description))
            {
                _logger.LogInformation(
                    "Telegram editMessageText skipped. chat_id={ChatId}, message_id={MessageId}, description={Description}",
                    chatId,
                    messageId,
                    description);
                return false;
            }

            _logger.LogWarning(
                "Telegram editMessageText returned non-success status. chat_id={ChatId}, message_id={MessageId}, status_code={StatusCode}, description={Description}",
                chatId,
                messageId,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(description) ? "<empty>" : description);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Telegram editMessageText failed. chat_id={ChatId}, message_id={MessageId}",
                chatId,
                messageId);
            return false;
        }
    }

    public async Task<bool> ClearMessageButtonsAsync(
        long chatId,
        long messageId,
        CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                BuildUrl("editMessageReplyMarkup"),
                new TelegramEditMessageReplyMarkupRequest
                {
                    ChatId = chatId,
                    MessageId = messageId,
                    ReplyMarkup = new TelegramInlineKeyboardMarkup
                    {
                        InlineKeyboard = []
                    }
                },
                JsonOptions,
                ct);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var payload = await response.Content.ReadFromJsonAsync<TelegramBotApiResponse<object>>(JsonOptions, ct);
            var description = payload?.Description?.Trim() ?? string.Empty;
            if ((int)response.StatusCode == 400 && IsIgnorableEditError(description))
            {
                _logger.LogInformation(
                    "Telegram editMessageReplyMarkup skipped. chat_id={ChatId}, message_id={MessageId}, description={Description}",
                    chatId,
                    messageId,
                    description);
                return false;
            }

            _logger.LogWarning(
                "Telegram editMessageReplyMarkup returned non-success status. chat_id={ChatId}, message_id={MessageId}, status_code={StatusCode}, description={Description}",
                chatId,
                messageId,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(description) ? "<empty>" : description);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Telegram editMessageReplyMarkup failed. chat_id={ChatId}, message_id={MessageId}",
                chatId,
                messageId);
            return false;
        }
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
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var payload = await response.Content.ReadFromJsonAsync<TelegramBotApiResponse<object>>(JsonOptions, ct);
            var description = payload?.Description?.Trim() ?? string.Empty;
            if ((int)response.StatusCode == 400 && IsIgnorableCallbackError(description))
            {
                _logger.LogInformation(
                    "Telegram callback acknowledgment skipped: callback is stale or invalid. callback_id={CallbackQueryId}, description={Description}",
                    callbackQueryId,
                    description);
                return;
            }

            _logger.LogWarning(
                "Telegram answerCallbackQuery returned non-success status. callback_id={CallbackQueryId}, status_code={StatusCode}, description={Description}",
                callbackQueryId,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(description) ? "<empty>" : description);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Telegram answerCallbackQuery failed for callback {CallbackQueryId}", callbackQueryId);
        }
    }

    private string BuildUrl(string method)
        => $"https://api.telegram.org/bot{_settings.BotToken.Trim()}/{method}";

    private static bool IsIgnorableCallbackError(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        return description.Contains("query is too old", StringComparison.OrdinalIgnoreCase)
            || description.Contains("query ID is invalid", StringComparison.OrdinalIgnoreCase)
            || description.Contains("query_id_invalid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnorableEditError(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        return description.Contains("message is not modified", StringComparison.OrdinalIgnoreCase)
            || description.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase)
            || description.Contains("message can't be edited", StringComparison.OrdinalIgnoreCase);
    }

    private static List<List<TelegramInlineKeyboardButton>> BuildInlineKeyboard(TelegramOperatorMessage message)
    {
        return message.Buttons
            .Select(row => row
                .Where(button => !string.IsNullOrWhiteSpace(button.CallbackData) || !string.IsNullOrWhiteSpace(button.Url))
                .Select(button => new TelegramInlineKeyboardButton
                {
                    Text = button.Text,
                    CallbackData = string.IsNullOrWhiteSpace(button.CallbackData) ? null : button.CallbackData.Trim(),
                    Url = string.IsNullOrWhiteSpace(button.Url) ? null : button.Url.Trim()
                })
                .ToList())
            .Where(row => row.Count > 0)
            .ToList();
    }

    private static string SanitizeVisibleText(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.ReplaceLineEndings("\n");
        if (normalized.Length == 0)
        {
            return "Действие выполнено.";
        }

        var lines = normalized
            .Split('\n', StringSplitOptions.None)
            .Where(line => !ContainsForbiddenPayloadMarker(line))
            .ToList();
        if (lines.Count == 0)
        {
            return "Открытие в веб доступно кнопкой ниже.";
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static bool ContainsForbiddenPayloadMarker(string value)
        => ForbiddenVisiblePayloadMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
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

internal sealed class TelegramEditMessageTextRequest
{
    public long ChatId { get; set; }
    public long MessageId { get; set; }
    public string Text { get; set; } = string.Empty;
    public TelegramInlineKeyboardMarkup? ReplyMarkup { get; set; }
}

internal sealed class TelegramEditMessageReplyMarkupRequest
{
    public long ChatId { get; set; }
    public long MessageId { get; set; }
    public TelegramInlineKeyboardMarkup? ReplyMarkup { get; set; }
}

internal sealed class TelegramInlineKeyboardMarkup
{
    public List<List<TelegramInlineKeyboardButton>> InlineKeyboard { get; set; } = [];
}

internal sealed class TelegramInlineKeyboardButton
{
    public string Text { get; set; } = string.Empty;
    public string? CallbackData { get; set; }
    public string? Url { get; set; }
}

internal sealed class TelegramAnswerCallbackQueryRequest
{
    public string CallbackQueryId { get; set; } = string.Empty;
    public string? Text { get; set; }
    public bool ShowAlert { get; set; }
}

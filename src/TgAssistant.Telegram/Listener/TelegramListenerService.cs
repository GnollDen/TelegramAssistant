using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TL;
using WTelegram;

namespace TgAssistant.Telegram.Listener;

/// <summary>
/// Listens to Telegram chats via MTProto (userbot) and pushes messages to Redis.
/// Thin layer: no business logic, just capture and forward.
/// </summary>
public class TelegramListenerService : BackgroundService
{
    private readonly TelegramSettings _settings;
    private readonly MediaSettings _mediaSettings;
    private readonly IMessageQueue _queue;
    private readonly ILogger<TelegramListenerService> _logger;
    private Client? _client;

    public TelegramListenerService(
        IOptions<TelegramSettings> settings,
        IOptions<MediaSettings> mediaSettings,
        IMessageQueue queue,
        ILogger<TelegramListenerService> logger)
    {
        _settings = settings.Value;
        _mediaSettings = mediaSettings.Value;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram listener starting...");

        _client = new Client(ConfigProvider);
        _client.OnUpdates += OnUpdates;

        // Login (will prompt for verification code on first run)
        var user = await _client.LoginUserIfNeeded();
        _logger.LogInformation("Logged in as {Name} (ID: {Id})", user.first_name, user.id);

        // Keep alive until cancellation
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private string? ConfigProvider(string what)
    {
        return what switch
        {
            "api_id" => _settings.ApiId.ToString(),
            "api_hash" => _settings.ApiHash,
            "phone_number" => _settings.PhoneNumber,
            "verification_code" => GetVerificationCode(),
            "session_pathname" => "data/telegram.session",
            _ => null
        };
    }

    private string GetVerificationCode()
    {
        // For first run: read from console or implement bot-based code entry
        // TODO: implement code delivery via bot for headless server
        Console.Write("Enter Telegram verification code: ");
        return Console.ReadLine() ?? "";
    }

    private async Task OnUpdates(UpdatesBase updates)
    {
        foreach (var update in updates.UpdateList)
        {
            if (update is UpdateNewMessage { message: Message msg })
            {
                await HandleMessage(msg);
            }
            else if (update is UpdateEditMessage { message: Message editMsg })
            {
                await HandleMessage(editMsg, isEdit: true);
            }
        }
    }

    private async Task HandleMessage(MessageBase messageBase, bool isEdit = false)
    {
        if (messageBase is not Message message)
            return;

        var chatId = message.peer_id switch
        {
            PeerUser pu => pu.user_id,
            PeerChat pc => pc.chat_id,
            PeerChannel ch => ch.channel_id,
            _ => 0L
        };

        // Filter: only monitored chats
        if (!_settings.MonitoredChatIds.Contains(chatId))
            return;

        _logger.LogDebug("Received message {Id} from chat {ChatId}", message.id, chatId);

        // Download media if present
        string? mediaPath = null;
        var mediaType = Core.Models.MediaType.None;

        if (message.media != null)
        {
            (mediaPath, mediaType) = await DownloadMedia(message, chatId);
        }

        var raw = new RawTelegramMessage
        {
            MessageId = message.id,
            ChatId = chatId,
            SenderId = message.from_id is PeerUser fromUser ? fromUser.user_id : 0,
            SenderName = "", // Resolved later from cached user info
            Timestamp = message.date,
            Text = message.message,
            MediaType = mediaType,
            MediaPath = mediaPath,
            ReplyToMessageId = message.reply_to is MessageReplyHeader reply ? reply.reply_to_msg_id : null,
            EditTimestamp = isEdit ? message.edit_date : null,
            ReactionsJson = null // TODO: handle reactions from UpdateMessageReactions
        };

        await _queue.EnqueueAsync(raw);
    }

    private async Task<(string? path, Core.Models.MediaType type)> DownloadMedia(
        Message message, long chatId)
    {
        var type = message.media switch
        {
            MessageMediaPhoto => Core.Models.MediaType.Photo,
            MessageMediaDocument { document: Document doc } => doc.mime_type switch
            {
                var m when m?.StartsWith("audio/") == true => Core.Models.MediaType.Voice,
                var m when m?.StartsWith("video/") == true => Core.Models.MediaType.Video,
                _ => Core.Models.MediaType.Document
            },
            _ => Core.Models.MediaType.None
        };

        if (type == Core.Models.MediaType.None)
            return (null, type);

        try
        {
            var date = message.date.ToString("yyyy-MM-dd");
            var dir = Path.Combine(_mediaSettings.StoragePath, chatId.ToString(), date);
            Directory.CreateDirectory(dir);

            var ext = type switch
            {
                Core.Models.MediaType.Voice => ".ogg",
                Core.Models.MediaType.Photo => ".jpg",
                Core.Models.MediaType.Video => ".mp4",
                _ => ".bin"
            };

            var filePath = Path.Combine(dir, $"{message.id}{ext}");

            await using var fs = File.Create(filePath);
            
            // WTelegramClient download
            if (message.media is MessageMediaPhoto { photo: Photo photo })
            {
                await _client!.DownloadFileAsync(photo, fs);
            }
            else if (message.media is MessageMediaDocument { document: Document doc })
            {
                await _client!.DownloadFileAsync(doc, fs);
            }

            _logger.LogDebug("Downloaded media {Type} to {Path}", type, filePath);
            return (filePath, type);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download media from message {Id}", message.id);
            return (null, type);
        }
    }

    public override void Dispose()
    {
        _client?.Dispose();
        base.Dispose();
    }
}

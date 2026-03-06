using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TL;
using WTelegram;

namespace TgAssistant.Telegram.Listener;

public class TelegramListenerService : BackgroundService
{
    private readonly TelegramSettings _settings;
    private readonly MediaSettings _mediaSettings;
    private readonly IMessageQueue _queue;
    private readonly ILogger<TelegramListenerService> _logger;
    private Client? _client;
    private readonly Dictionary<long, string> _userNameCache = new();

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
        _logger.LogInformation("Monitoring {Count} chats: {Chats}",
            _settings.MonitoredChatIds.Count,
            string.Join(", ", _settings.MonitoredChatIds));

        _client = new Client(ConfigProvider);
        _client.OnUpdates += OnUpdates;

        var user = await _client.LoginUserIfNeeded();
        _logger.LogInformation("Logged in as {Name} (ID: {Id})", user.first_name, user.id);

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
        Console.Write("Enter Telegram verification code: ");
        return Console.ReadLine() ?? "";
    }

    private async Task OnUpdates(UpdatesBase updates)
    {
        // Cache user names
        if (updates.Users != null)
        {
            foreach (var (id, user) in updates.Users)
            {
                if (user is User u)
                    _userNameCache[id] = $"{u.first_name} {u.last_name}".Trim();
            }
        }

        foreach (var update in updates.UpdateList)
        {
            try
            {
                await HandleUpdate(update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling update {Type}", update.GetType().Name);
            }
        }
    }

    private async Task HandleUpdate(Update update)
    {
        switch (update)
        {
            case UpdateNewMessage { message: TL.Message msg }:
                await HandleMessage(msg, isEdit: false);
                break;
            case UpdateEditMessage { message: TL.Message editMsg }:
                await HandleMessage(editMsg, isEdit: true);
                break;
            case UpdateDeleteMessages del:
                await HandleDelete(del.messages, chatId: 0);
                break;
            case UpdateMessageReactions react:
                await HandleReactions(react);
                break;
        }
    }

    private async Task HandleMessage(TL.Message message, bool isEdit)
    {
        var chatId = message.peer_id switch
        {
            PeerUser pu => pu.user_id,
            PeerChat pc => pc.chat_id,
            PeerChannel ch => ch.channel_id,
            _ => 0L
        };

        if (_settings.MonitoredChatIds.Count > 0 && !_settings.MonitoredChatIds.Contains(chatId))
            return;

        var senderId = message.from_id is PeerUser fromPeer ? fromPeer.user_id : 0;
        var senderName = ResolveSenderName(senderId);
        var mediaType = DetectMediaType(message);

        string? mediaPath = null;
        if (mediaType != Core.Models.MediaType.None && message.media != null)
            mediaPath = await DownloadMedia(message, chatId, mediaType);

        string? reactionsJson = null;
        if (message.reactions?.results != null)
        {
            var reactions = message.reactions.results
                .Select(r => new { emoji = r.reaction is ReactionEmoji re ? re.emoticon : "custom", count = r.count })
                .ToList();
            reactionsJson = JsonSerializer.Serialize(reactions);
        }

        string? forwardJson = null;
        if (message.fwd_from != null)
        {
            var fwd = new
            {
                from_id = message.fwd_from.from_id switch
                {
                    PeerUser pu => pu.user_id,
                    PeerChannel pc => pc.channel_id,
                    _ => 0L
                },
                from_name = message.fwd_from.from_name,
                date = message.fwd_from.date
            };
            forwardJson = JsonSerializer.Serialize(fwd);
        }

        var raw = new RawTelegramMessage
        {
            MessageId = message.id,
            ChatId = chatId,
            SenderId = senderId,
            SenderName = senderName,
            Timestamp = message.date,
            Text = message.message,
            MediaType = mediaType,
            MediaPath = mediaPath,
            ReplyToMessageId = message.reply_to is MessageReplyHeader reply ? reply.reply_to_msg_id : null,
            EditTimestamp = isEdit ? message.edit_date : null,
            ReactionsJson = reactionsJson,
            ForwardJson = forwardJson
        };

        await _queue.EnqueueAsync(raw);

        _logger.LogInformation("{Action} in chat {ChatId} from {Sender} ({SenderId}): {Preview}",
            isEdit ? "Edited" : "Message", chatId, senderName, senderId,
            message.message?.Length > 80 ? message.message[..80] + "..." : message.message ?? $"[{mediaType}]");
    }

    private async Task HandleDelete(int[] messageIds, long chatId)
    {
        if (chatId != 0 && _settings.MonitoredChatIds.Count > 0 && !_settings.MonitoredChatIds.Contains(chatId))
            return;

        foreach (var msgId in messageIds)
        {
            _logger.LogInformation("Deleted message {Id} in chat {ChatId}", msgId, chatId);
            var raw = new RawTelegramMessage
            {
                MessageId = msgId, ChatId = chatId, SenderId = 0, SenderName = "",
                Timestamp = DateTime.UtcNow, Text = "[DELETED]", MediaType = Core.Models.MediaType.None
            };
            await _queue.EnqueueAsync(raw);
        }
    }

    private async Task HandleReactions(UpdateMessageReactions react)
    {
        var chatId = react.peer switch
        {
            PeerUser pu => pu.user_id,
            PeerChat pc => pc.chat_id,
            PeerChannel ch => ch.channel_id,
            _ => 0L
        };

        if (_settings.MonitoredChatIds.Count > 0 && !_settings.MonitoredChatIds.Contains(chatId))
            return;

        var reactions = react.reactions?.results?
            .Select(r => new { emoji = r.reaction is ReactionEmoji re ? re.emoticon : "custom", count = r.count })
            .ToList();
        if (reactions == null || reactions.Count == 0) return;

        _logger.LogInformation("Reactions on msg {Id} in chat {ChatId}: {R}",
            react.msg_id, chatId, JsonSerializer.Serialize(reactions));

        var raw = new RawTelegramMessage
        {
            MessageId = react.msg_id, ChatId = chatId, SenderId = 0, SenderName = "",
            Timestamp = DateTime.UtcNow, Text = "[REACTION_UPDATE]",
            MediaType = Core.Models.MediaType.None,
            ReactionsJson = JsonSerializer.Serialize(reactions)
        };
        await _queue.EnqueueAsync(raw);
    }

    private Core.Models.MediaType DetectMediaType(TL.Message message)
    {
        return message.media switch
        {
            MessageMediaPhoto => Core.Models.MediaType.Photo,
            MessageMediaDocument { document: Document doc } => DetectDocumentType(doc),
            _ => Core.Models.MediaType.None
        };
    }

    private Core.Models.MediaType DetectDocumentType(Document doc)
    {
        foreach (var attr in doc.attributes)
        {
            switch (attr)
            {
                case DocumentAttributeSticker: return Core.Models.MediaType.Sticker;
                case DocumentAttributeAnimated: return Core.Models.MediaType.Animation;
                case DocumentAttributeVideo dav when dav.flags.HasFlag(DocumentAttributeVideo.Flags.round_message):
                    return Core.Models.MediaType.VideoNote;
                case DocumentAttributeVideo: return Core.Models.MediaType.Video;
                case DocumentAttributeAudio daa when daa.flags.HasFlag(DocumentAttributeAudio.Flags.voice):
                    return Core.Models.MediaType.Voice;
                case DocumentAttributeAudio: return Core.Models.MediaType.Voice;
            }
        }
        return doc.mime_type switch
        {
            var m when m?.StartsWith("audio/") == true => Core.Models.MediaType.Voice,
            var m when m?.StartsWith("video/") == true => Core.Models.MediaType.Video,
            var m when m?.StartsWith("image/") == true => Core.Models.MediaType.Photo,
            _ => Core.Models.MediaType.Document
        };
    }

    private async Task<string?> DownloadMedia(TL.Message message, long chatId, Core.Models.MediaType type)
    {
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
                Core.Models.MediaType.VideoNote => ".mp4",
                Core.Models.MediaType.Sticker => ".webp",
                Core.Models.MediaType.Animation => ".gif",
                _ => ".bin"
            };
            var filePath = Path.Combine(dir, $"{message.id}{ext}");
            await using var fs = File.Create(filePath);
            if (message.media is MessageMediaPhoto { photo: Photo photo })
                await _client!.DownloadFileAsync(photo, fs);
            else if (message.media is MessageMediaDocument { document: Document doc })
                await _client!.DownloadFileAsync(doc, fs);
            _logger.LogInformation("Downloaded {Type} to {Path}", type, filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download media from message {Id}", message.id);
            return null;
        }
    }

    private string ResolveSenderName(long senderId)
    {
        if (senderId == 0) return "";
        return _userNameCache.TryGetValue(senderId, out var name) ? name : "";
    }

    public override void Dispose()
    {
        _client?.Dispose();
        base.Dispose();
    }
}

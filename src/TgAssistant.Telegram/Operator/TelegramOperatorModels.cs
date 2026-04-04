using TgAssistant.Core.Models;

namespace TgAssistant.Telegram.Operator;

public static class TelegramOperatorSurfaceModes
{
    public const string None = "none";
    public const string Resolution = "resolution";
}

public sealed class TelegramOperatorInteraction
{
    public long ChatId { get; set; }
    public long UserId { get; set; }
    public bool IsPrivateChat { get; set; }
    public string? UserDisplayName { get; set; }
    public string? MessageText { get; set; }
    public string? CallbackData { get; set; }
    public string? CallbackQueryId { get; set; }
}

public sealed class TelegramOperatorButton
{
    public string Text { get; set; } = string.Empty;
    public string CallbackData { get; set; } = string.Empty;
}

public sealed class TelegramOperatorMessage
{
    public string Text { get; set; } = string.Empty;
    public List<List<TelegramOperatorButton>> Buttons { get; set; } = [];
}

public sealed class TelegramOperatorResponse
{
    public string? CallbackNotificationText { get; set; }
    public List<TelegramOperatorMessage> Messages { get; set; } = [];
}

public sealed class TelegramOperatorSessionSnapshot
{
    public string SurfaceMode { get; set; } = TelegramOperatorSurfaceModes.None;
    public string OperatorSessionId { get; set; } = string.Empty;
    public Guid ActiveTrackedPersonId { get; set; }
    public string ActiveMode { get; set; } = string.Empty;
    public string? ActiveTrackedPersonDisplayName { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

internal sealed class TelegramOperatorConversationState
{
    public long ChatId { get; set; }
    public long UserId { get; set; }
    public string SurfaceMode { get; set; } = TelegramOperatorSurfaceModes.None;
    public string? ActiveTrackedPersonDisplayName { get; set; }
    public int ResolutionCardGeneration { get; set; }
    public Dictionary<string, TelegramResolutionCardBinding> ResolutionCardBindings { get; set; } = new(StringComparer.Ordinal);
    public OperatorSessionContext Session { get; set; } = new();

    public TelegramOperatorSessionSnapshot ToSnapshot()
    {
        return new TelegramOperatorSessionSnapshot
        {
            SurfaceMode = SurfaceMode,
            OperatorSessionId = Session.OperatorSessionId,
            ActiveTrackedPersonId = Session.ActiveTrackedPersonId,
            ActiveMode = Session.ActiveMode,
            ActiveTrackedPersonDisplayName = ActiveTrackedPersonDisplayName,
            ExpiresAtUtc = Session.ExpiresAtUtc
        };
    }
}

internal sealed class TelegramResolutionCardBinding
{
    public string Token { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<string> AvailableActions { get; set; } = [];
}

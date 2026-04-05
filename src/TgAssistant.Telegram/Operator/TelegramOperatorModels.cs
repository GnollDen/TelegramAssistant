using TgAssistant.Core.Models;

namespace TgAssistant.Telegram.Operator;

public static class TelegramOperatorSurfaceModes
{
    public const string None = "none";
    public const string Assistant = "assistant";
    public const string Alerts = "alerts";
    public const string OfflineEvent = "offline_event";
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
    public string? CallbackData { get; set; }
    public string? Url { get; set; }
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
    public string? ActiveTrackedPersonScopeKey { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}

internal sealed class TelegramOperatorConversationState
{
    public long ChatId { get; set; }
    public long UserId { get; set; }
    public string SurfaceMode { get; set; } = TelegramOperatorSurfaceModes.None;
    public string? ActiveTrackedPersonDisplayName { get; set; }
    public string? ActiveTrackedPersonScopeKey { get; set; }
    public int ResolutionCardGeneration { get; set; }
    public Dictionary<string, TelegramResolutionCardBinding> ResolutionCardBindings { get; set; } = new(StringComparer.Ordinal);
    public int AlertCardGeneration { get; set; }
    public Dictionary<string, TelegramAlertCardBinding> AlertCardBindings { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> AcknowledgedAlertScopeItemKeys { get; set; } = new(StringComparer.Ordinal);
    public TelegramPendingResolutionInput? PendingResolutionInput { get; set; }
    public TelegramOfflineEventDraft? OfflineEventDraft { get; set; }
    public TelegramPendingOfflineEventInput? PendingOfflineEventInput { get; set; }
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
            ActiveTrackedPersonScopeKey = ActiveTrackedPersonScopeKey,
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
    public string? OpenWebUrl { get; set; }
}

internal sealed class TelegramAlertCardBinding
{
    public string Token { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AlertRuleId { get; set; } = string.Empty;
    public string AlertReason { get; set; } = string.Empty;
    public string? OpenWebUrl { get; set; }
}

internal sealed class TelegramPendingResolutionInput
{
    public string ActionType { get; set; } = string.Empty;
    public string ScopeItemKey { get; set; } = string.Empty;
    public string ItemType { get; set; } = string.Empty;
    public string ItemTitle { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public Guid BoundTrackedPersonId { get; set; }
}

internal sealed class TelegramOfflineEventDraft
{
    public string? Summary { get; set; }
    public string? RecordingReference { get; set; }
    public OfflineEventClarificationState? ClarificationState { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public Guid BoundTrackedPersonId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
}

internal sealed class TelegramPendingOfflineEventInput
{
    public string InputKind { get; set; } = string.Empty;
    public string? ClarificationQuestionKey { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public Guid BoundTrackedPersonId { get; set; }
}

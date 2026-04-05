using System.Collections.Concurrent;
using TgAssistant.Core.Models;

namespace TgAssistant.Telegram.Operator;

public sealed class TelegramOperatorSessionStore
{
    private readonly ConcurrentDictionary<long, TelegramOperatorConversationState> _states = new();

    internal TelegramOperatorConversationState GetOrAdd(long chatId, Func<TelegramOperatorConversationState> factory)
        => _states.GetOrAdd(chatId, _ => factory());

    internal void Set(TelegramOperatorConversationState state)
        => _states[state.ChatId] = state;

    public TelegramOperatorSessionSnapshot? GetSnapshot(long chatId)
    {
        return _states.TryGetValue(chatId, out var state)
            ? state.ToSnapshot()
            : null;
    }

    internal IReadOnlyList<long> GetTrackedMessageIds(long chatId, string surfaceMode)
    {
        if (!_states.TryGetValue(chatId, out var state))
        {
            return [];
        }

        return surfaceMode switch
        {
            TelegramOperatorSurfaceModes.Resolution => [.. state.ActiveResolutionMessageIds],
            TelegramOperatorSurfaceModes.Alerts => [.. state.ActiveAlertMessageIds],
            _ => []
        };
    }

    internal void ReplaceTrackedMessageIds(long chatId, string surfaceMode, IEnumerable<long> messageIds)
    {
        if (!_states.TryGetValue(chatId, out var state))
        {
            return;
        }

        var normalizedIds = messageIds
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        switch (surfaceMode)
        {
            case TelegramOperatorSurfaceModes.Resolution:
                state.ActiveResolutionMessageIds = normalizedIds;
                break;
            case TelegramOperatorSurfaceModes.Alerts:
                state.ActiveAlertMessageIds = normalizedIds;
                break;
        }

        _states[chatId] = state;
    }

    internal static void ClearResolutionContext(TelegramOperatorConversationState state)
    {
        state.ActiveTrackedPersonDisplayName = null;
        state.ActiveTrackedPersonScopeKey = null;
        state.SurfaceMode = TelegramOperatorSurfaceModes.None;
        state.ResolutionCardBindings.Clear();
        state.AlertCardBindings.Clear();
        state.AcknowledgedAlertScopeItemKeys.Clear();
        state.ActiveResolutionMessageIds.Clear();
        state.ActiveAlertMessageIds.Clear();
        state.PendingResolutionInput = null;
        state.OfflineEventDraft = null;
        state.PendingOfflineEventInput = null;
        state.Session.ActiveTrackedPersonId = Guid.Empty;
        state.Session.ActiveScopeItemKey = null;
        state.Session.ActiveMode = string.Empty;
        state.Session.UnfinishedStep = null;
    }
}

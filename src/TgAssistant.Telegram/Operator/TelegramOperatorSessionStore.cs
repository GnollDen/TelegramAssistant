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

    internal static void ClearResolutionContext(TelegramOperatorConversationState state)
    {
        state.ActiveTrackedPersonDisplayName = null;
        state.SurfaceMode = TelegramOperatorSurfaceModes.None;
        state.ResolutionCardBindings.Clear();
        state.Session.ActiveTrackedPersonId = Guid.Empty;
        state.Session.ActiveScopeItemKey = null;
        state.Session.ActiveMode = string.Empty;
        state.Session.UnfinishedStep = null;
    }
}

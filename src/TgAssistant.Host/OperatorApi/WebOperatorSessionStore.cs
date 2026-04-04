using System.Collections.Concurrent;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.OperatorApi;

public sealed class WebOperatorSessionStore
{
    private readonly ConcurrentDictionary<string, OperatorSessionContext> _sessions = new(StringComparer.Ordinal);

    public bool TryGet(string sessionId, out OperatorSessionContext session)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            session = CloneSession(existing);
            return true;
        }

        session = new OperatorSessionContext();
        return false;
    }

    public void Upsert(OperatorSessionContext session)
    {
        if (session == null || string.IsNullOrWhiteSpace(session.OperatorSessionId))
        {
            return;
        }

        _sessions[session.OperatorSessionId.Trim()] = CloneSession(session);
    }

    public void Remove(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        _sessions.TryRemove(sessionId.Trim(), out _);
    }

    private static OperatorSessionContext CloneSession(OperatorSessionContext session)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = session.OperatorSessionId,
            Surface = session.Surface,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            LastSeenAtUtc = session.LastSeenAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            ActiveTrackedPersonId = session.ActiveTrackedPersonId,
            ActiveScopeItemKey = session.ActiveScopeItemKey,
            ActiveMode = session.ActiveMode,
            UnfinishedStep = session.UnfinishedStep == null
                ? null
                : new OperatorWorkflowStepContext
                {
                    StepKind = session.UnfinishedStep.StepKind,
                    StepState = session.UnfinishedStep.StepState,
                    StartedAtUtc = session.UnfinishedStep.StartedAtUtc,
                    BoundTrackedPersonId = session.UnfinishedStep.BoundTrackedPersonId,
                    BoundScopeItemKey = session.UnfinishedStep.BoundScopeItemKey
                }
        };
    }
}

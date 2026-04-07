using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.OperatorApi;

public sealed class OperatorResolutionHandoffConsumeService
{
    private static readonly ConcurrentDictionary<string, DateTime> ConsumedTokenHashes = new(StringComparer.Ordinal);

    private readonly WebOperatorAuthSessionResolver _webAuthResolver;
    private readonly WebSettings _webSettings;
    private readonly IOperatorResolutionApplicationService _service;

    public OperatorResolutionHandoffConsumeService(
        WebOperatorAuthSessionResolver webAuthResolver,
        IOptions<WebSettings> webSettings,
        IOperatorResolutionApplicationService service)
    {
        _webAuthResolver = webAuthResolver;
        _webSettings = webSettings.Value ?? new WebSettings();
        _service = service;
    }

    public async Task<OperatorResolutionHandoffConsumeExecutionResult> ConsumeAsync(
        HttpContext httpContext,
        OperatorResolutionHandoffConsumeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(request);

        var requestedMode = OperatorModeTypes.Normalize(request.ActiveMode);
        var trackedPersonId = request.TrackedPersonId;
        var scopeItemKey = NormalizeOptional(request.ScopeItemKey);
        var sourceSessionId = NormalizeOptional(request.OperatorSessionId);
        var handoffToken = NormalizeOptional(request.HandoffToken);
        var signingSecret = OperatorHandoffTokenCodec.ResolveSigningSecret(_webSettings);
        var ttlMinutes = Math.Clamp(_webSettings.HandoffTokenTtlMinutes, 1, 24 * 60);
        if (trackedPersonId == Guid.Empty)
        {
            return CreateResult(
                StatusCodes.Status400BadRequest,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "tracked_person_id_required",
                    Session = new OperatorSessionContext(),
                    ActiveMode = requestedMode
                });
        }

        if (string.IsNullOrWhiteSpace(scopeItemKey))
        {
            return CreateResult(
                StatusCodes.Status400BadRequest,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "scope_item_key_required",
                    Session = new OperatorSessionContext(),
                    ActiveMode = requestedMode
                });
        }

        if (string.IsNullOrWhiteSpace(sourceSessionId))
        {
            return CreateResult(
                StatusCodes.Status400BadRequest,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "operator_session_id_required",
                    Session = new OperatorSessionContext(),
                    ActiveMode = requestedMode
                });
        }

        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            return CreateResult(
                StatusCodes.Status503ServiceUnavailable,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "handoff_signing_secret_missing",
                    Session = new OperatorSessionContext(),
                    ActiveMode = requestedMode
                });
        }

        var isValidTelegramToken = OperatorHandoffTokenCodec.TryValidateToken(
            handoffToken,
            OperatorHandoffTokenCodec.TelegramResolutionContext,
            trackedPersonId,
            scopeItemKey,
            sourceSessionId,
            signingSecret,
            ttlMinutes);
        var isValidAssistantToken = !isValidTelegramToken && OperatorHandoffTokenCodec.TryValidateToken(
            handoffToken,
            OperatorHandoffTokenCodec.AssistantResolutionContext,
            trackedPersonId,
            scopeItemKey,
            sourceSessionId,
            signingSecret,
            ttlMinutes);
        if (!isValidTelegramToken && !isValidAssistantToken)
        {
            return CreateResult(
                StatusCodes.Status403Forbidden,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "handoff_token_invalid",
                    Session = new OperatorSessionContext(),
                    ActiveMode = requestedMode
                });
        }

        if (IsHandoffTokenReplayed(handoffToken))
        {
            return CreateResult(
                StatusCodes.Status403Forbidden,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "handoff_token_replayed",
                    Session = new OperatorSessionContext(),
                    ActiveMode = requestedMode
                });
        }

        var auth = await _webAuthResolver.ResolveForHandoffAsync(httpContext, requestedMode, ct);
        if (!auth.Accepted)
        {
            return CreateResult(
                auth.StatusCode,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = auth.FailureReason,
                    Session = auth.Session,
                    ActiveMode = requestedMode
                });
        }

        var currentSession = auth.Session ?? new OperatorSessionContext();
        if (currentSession.ActiveTrackedPersonId != Guid.Empty
            && currentSession.ActiveTrackedPersonId != trackedPersonId)
        {
            return CreateResult(
                StatusCodes.Status403Forbidden,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "session_active_tracked_person_mismatch",
                    Session = currentSession,
                    ActiveMode = requestedMode
                });
        }

        if (!string.IsNullOrWhiteSpace(currentSession.ActiveScopeItemKey)
            && !string.Equals(currentSession.ActiveScopeItemKey.Trim(), scopeItemKey, StringComparison.Ordinal))
        {
            return CreateResult(
                StatusCodes.Status403Forbidden,
                new OperatorResolutionHandoffConsumeResult
                {
                    Accepted = false,
                    FailureReason = "session_scope_item_mismatch",
                    Session = currentSession,
                    ActiveMode = requestedMode
                });
        }

        var session = currentSession;
        if (session.ActiveTrackedPersonId == Guid.Empty)
        {
            var selection = await _service.SelectTrackedPersonAsync(
                new OperatorTrackedPersonSelectionRequest
                {
                    TrackedPersonId = trackedPersonId,
                    RequestedAtUtc = DateTime.UtcNow,
                    OperatorIdentity = auth.OperatorIdentity,
                    Session = auth.Session ?? new OperatorSessionContext()
                },
                ct);
            if (!selection.Accepted)
            {
                return CreateResult(
                    ToStatusCode(selection.FailureReason),
                    new OperatorResolutionHandoffConsumeResult
                    {
                        Accepted = false,
                        FailureReason = selection.FailureReason,
                        Session = selection.Session,
                        ActiveMode = requestedMode
                    });
            }

            session = selection.Session ?? currentSession;
        }

        session.ActiveTrackedPersonId = trackedPersonId;
        session.ActiveScopeItemKey = scopeItemKey;
        session.ActiveMode = OperatorModeTypes.IsSupported(requestedMode)
            ? requestedMode
            : OperatorModeTypes.ResolutionDetail;

        var handoffResult = new OperatorResolutionHandoffConsumeResult
        {
            Accepted = true,
            Session = session,
            ActiveTrackedPersonId = session.ActiveTrackedPersonId,
            ActiveScopeItemKey = session.ActiveScopeItemKey,
            ActiveMode = session.ActiveMode
        };

        _webAuthResolver.PersistSession(httpContext, handoffResult.Session, handoffResult.ActiveMode);
        MarkHandoffTokenConsumed(handoffToken, ttlMinutes);
        return CreateResult(StatusCodes.Status200OK, handoffResult);
    }

    private static OperatorResolutionHandoffConsumeExecutionResult CreateResult(
        int statusCode,
        OperatorResolutionHandoffConsumeResult payload)
        => new()
        {
            StatusCode = statusCode,
            Payload = payload
        };

    private static int ToStatusCode(string? failureReason)
        => string.Equals(failureReason, "tracked_person_not_found_or_inactive", StringComparison.Ordinal)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool IsHandoffTokenReplayed(string handoffToken)
    {
        var tokenHash = HashToken(handoffToken);
        var nowUtc = DateTime.UtcNow;

        foreach (var pair in ConsumedTokenHashes)
        {
            if (pair.Value <= nowUtc)
            {
                ConsumedTokenHashes.TryRemove(pair.Key, out _);
            }
        }

        if (ConsumedTokenHashes.TryGetValue(tokenHash, out var existingExpiry) && existingExpiry > nowUtc)
        {
            return true;
        }

        return false;
    }

    private static void MarkHandoffTokenConsumed(string handoffToken, int ttlMinutes)
    {
        var tokenHash = HashToken(handoffToken);
        ConsumedTokenHashes[tokenHash] = DateTime.UtcNow.AddMinutes(Math.Clamp(ttlMinutes, 1, 24 * 60));
    }

    private static string HashToken(string handoffToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(handoffToken));
        return Convert.ToHexString(bytes);
    }
}

public sealed class OperatorResolutionHandoffConsumeExecutionResult
{
    public int StatusCode { get; init; }
    public OperatorResolutionHandoffConsumeResult Payload { get; init; } = new();
}

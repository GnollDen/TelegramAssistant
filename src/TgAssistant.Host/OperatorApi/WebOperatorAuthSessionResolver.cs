using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;

namespace TgAssistant.Host.OperatorApi;

public sealed class WebOperatorAuthSessionResolver
{
    private const string SessionHeaderName = "X-Tga-Operator-Session";
    private const string SessionCookieName = "tga_operator_session";
    private const string AuthDeniedSessionEventType = "auth_denied";
    private const string SessionExpiredEventType = "session_expired";
    private const string SessionDeniedEventType = "session_denied";
    private const string SessionAuthenticatedEventType = "session_authenticated";
    private const string SessionRestoredEventType = "session_restored";
    private const string WebAuthSource = "web_access_token";
    private const string WebHandoffSource = "web_handoff_token";
    private const int DefaultSessionTtlMinutes = 120;

    private readonly WebOperatorSessionStore _sessionStore;
    private readonly IOperatorSessionAuditService _auditService;
    private readonly ILogger<WebOperatorAuthSessionResolver> _logger;
    private readonly WebSettings _settings;

    public WebOperatorAuthSessionResolver(
        WebOperatorSessionStore sessionStore,
        IOperatorSessionAuditService auditService,
        IOptions<WebSettings> settings,
        ILogger<WebOperatorAuthSessionResolver> logger)
    {
        _sessionStore = sessionStore;
        _auditService = auditService;
        _logger = logger;
        _settings = settings.Value ?? new WebSettings();
    }

    public async Task<WebOperatorAuthResult> ResolveAsync(
        HttpContext httpContext,
        string requestedMode,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var normalizedMode = NormalizeMode(requestedMode);
        var suppliedSessionId = ResolveSuppliedSessionId(httpContext.Request);
        var subject = ResolveSurfaceSubject(httpContext);
        var operatorId = ResolveOperatorId();
        var restoredSessionAttempt = await TryRestoreSessionAsync(
            httpContext,
            normalizedMode,
            suppliedSessionId,
            operatorId,
            subject,
            nowUtc,
            allowBootstrapOnMissingOrExpired: false,
            ct);
        if (restoredSessionAttempt.Failure != null)
        {
            return restoredSessionAttempt.Failure;
        }

        if (restoredSessionAttempt.Session != null)
        {
            var restoredIdentity = BuildIdentity(operatorId, subject, WebAuthSource, restoredSessionAttempt.Session.AuthenticatedAtUtc);
            if (restoredSessionAttempt.ShouldAuditRestored)
            {
                await RecordAcceptedSessionEventAsync(httpContext, restoredIdentity, restoredSessionAttempt.Session, SessionRestoredEventType, nowUtc, ct);
            }

            return WebOperatorAuthResult.Success(restoredIdentity, restoredSessionAttempt.Session);
        }

        var token = ResolveAccessToken(httpContext.Request);

        if (_settings.RequireOperatorAccessToken && string.IsNullOrWhiteSpace(_settings.OperatorAccessToken))
        {
            return await DenyAsync(
                httpContext,
                suppliedSessionId,
                operatorId,
                subject,
                SessionDeniedEventType,
                "operator_access_token_not_configured",
                StatusCodes.Status401Unauthorized,
                nowUtc,
                ct);
        }

        if (_settings.RequireOperatorAccessToken
            && !string.Equals(_settings.OperatorAccessToken.Trim(), token, StringComparison.Ordinal))
        {
            return await DenyAsync(
                httpContext,
                suppliedSessionId,
                operatorId,
                subject,
                AuthDeniedSessionEventType,
                "auth_denied",
                StatusCodes.Status401Unauthorized,
                nowUtc,
                ct);
        }

        var session = CreateNewSession(normalizedMode, nowUtc);
        var identity = BuildIdentity(operatorId, subject, WebAuthSource, session.AuthenticatedAtUtc);
        _sessionStore.Upsert(session);
        WriteSession(httpContext, session);
        await RecordAcceptedSessionEventAsync(httpContext, identity, session, SessionAuthenticatedEventType, nowUtc, ct);
        return WebOperatorAuthResult.Success(identity, session);
    }

    public async Task<WebOperatorAuthResult> ResolveForHandoffAsync(
        HttpContext httpContext,
        string requestedMode,
        CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;
        var normalizedMode = NormalizeMode(requestedMode);
        var suppliedSessionId = ResolveSuppliedSessionId(httpContext.Request);
        var subject = ResolveSurfaceSubject(httpContext);
        var operatorId = ResolveOperatorId();
        var restoredSessionAttempt = await TryRestoreSessionAsync(
            httpContext,
            normalizedMode,
            suppliedSessionId,
            operatorId,
            subject,
            nowUtc,
            allowBootstrapOnMissingOrExpired: true,
            ct);
        if (restoredSessionAttempt.Failure != null)
        {
            return restoredSessionAttempt.Failure;
        }

        if (restoredSessionAttempt.Session != null)
        {
            var restoredIdentity = BuildIdentity(operatorId, subject, WebAuthSource, restoredSessionAttempt.Session.AuthenticatedAtUtc);
            if (restoredSessionAttempt.ShouldAuditRestored)
            {
                await RecordAcceptedSessionEventAsync(httpContext, restoredIdentity, restoredSessionAttempt.Session, SessionRestoredEventType, nowUtc, ct);
            }

            return WebOperatorAuthResult.Success(restoredIdentity, restoredSessionAttempt.Session);
        }

        var session = CreateNewSession(normalizedMode, nowUtc);
        var identity = BuildIdentity(operatorId, subject, WebHandoffSource, session.AuthenticatedAtUtc);
        _sessionStore.Upsert(session);
        WriteSession(httpContext, session);
        await RecordAcceptedSessionEventAsync(httpContext, identity, session, SessionAuthenticatedEventType, nowUtc, ct);
        return WebOperatorAuthResult.Success(identity, session);
    }

    public void PersistSession(HttpContext httpContext, OperatorSessionContext? session, string fallbackMode)
    {
        if (session == null)
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(session.OperatorSessionId))
        {
            session.OperatorSessionId = $"web:{Guid.NewGuid():N}";
        }

        session.Surface = OperatorSurfaceTypes.Web;
        session.ActiveMode = NormalizeMode(string.IsNullOrWhiteSpace(session.ActiveMode) ? fallbackMode : session.ActiveMode);
        session.AuthenticatedAtUtc = session.AuthenticatedAtUtc == default ? nowUtc : session.AuthenticatedAtUtc;
        session.LastSeenAtUtc = nowUtc;
        session.ExpiresAtUtc = nowUtc.AddMinutes(DefaultSessionTtlMinutes);

        _sessionStore.Upsert(session);
        WriteSession(httpContext, session);
    }

    private async Task<WebOperatorAuthResult> DenyAsync(
        HttpContext httpContext,
        string? suppliedSessionId,
        string operatorId,
        string subject,
        string sessionEventType,
        string failureReason,
        int statusCode,
        DateTime eventTimeUtc,
        CancellationToken ct)
    {
        var deniedSessionId = suppliedSessionId ?? $"web-denied:{Guid.NewGuid():N}";
        var deniedSession = new OperatorSessionContext
        {
            OperatorSessionId = deniedSessionId,
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = eventTimeUtc,
            LastSeenAtUtc = eventTimeUtc,
            ExpiresAtUtc = eventTimeUtc
        };
        var identity = new OperatorIdentityContext
        {
            OperatorId = operatorId,
            OperatorDisplay = operatorId,
            SurfaceSubject = subject,
            AuthSource = WebAuthSource,
            AuthTimeUtc = eventTimeUtc
        };

        Guid? auditEventId = null;
        try
        {
            auditEventId = await _auditService.RecordSessionEventAsync(
                new OperatorSessionAuditRequest
                {
                    RequestId = $"web-auth:{Guid.NewGuid():N}",
                    SessionEventType = sessionEventType,
                    DecisionOutcome = OperatorAuditDecisionOutcomes.Denied,
                    FailureReason = failureReason,
                    OperatorIdentity = identity,
                    Session = deniedSession,
                    EventTimeUtc = eventTimeUtc,
                    Details = new Dictionary<string, object?>
                    {
                        ["surface"] = OperatorSurfaceTypes.Web,
                        ["path"] = httpContext.Request.Path.Value,
                        ["method"] = httpContext.Request.Method
                    }
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web operator auth/session denial audit write failed: reason={FailureReason}", failureReason);
        }

        return WebOperatorAuthResult.Failure(
            statusCode,
            failureReason,
            auditEventId,
            deniedSession,
            identity);
    }

    private async Task RecordAcceptedSessionEventAsync(
        HttpContext httpContext,
        OperatorIdentityContext identity,
        OperatorSessionContext session,
        string sessionEventType,
        DateTime eventTimeUtc,
        CancellationToken ct)
    {
        try
        {
            await _auditService.RecordSessionEventAsync(
                new OperatorSessionAuditRequest
                {
                    RequestId = $"web-auth:{Guid.NewGuid():N}",
                    SessionEventType = sessionEventType,
                    DecisionOutcome = OperatorAuditDecisionOutcomes.Accepted,
                    FailureReason = null,
                    OperatorIdentity = identity,
                    Session = session,
                    EventTimeUtc = eventTimeUtc,
                    Details = new Dictionary<string, object?>
                    {
                        ["surface"] = OperatorSurfaceTypes.Web,
                        ["path"] = httpContext.Request.Path.Value,
                        ["method"] = httpContext.Request.Method
                    }
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web operator auth/session accepted audit write failed: event={SessionEventType}", sessionEventType);
        }
    }

    private async Task<SessionRestoreAttempt> TryRestoreSessionAsync(
        HttpContext httpContext,
        string normalizedMode,
        string suppliedSessionId,
        string operatorId,
        string subject,
        DateTime nowUtc,
        bool allowBootstrapOnMissingOrExpired,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(suppliedSessionId))
        {
            return SessionRestoreAttempt.Empty;
        }

        if (!_sessionStore.TryGet(suppliedSessionId, out var session))
        {
            if (allowBootstrapOnMissingOrExpired)
            {
                return SessionRestoreAttempt.Empty;
            }

            return new SessionRestoreAttempt
            {
                Failure = await DenyAsync(
                    httpContext,
                    suppliedSessionId,
                    operatorId,
                    subject,
                    SessionDeniedEventType,
                    "session_not_found",
                    StatusCodes.Status401Unauthorized,
                    nowUtc,
                    ct)
            };
        }

        if (session.ExpiresAtUtc.HasValue && session.ExpiresAtUtc.Value <= nowUtc)
        {
            _sessionStore.Remove(suppliedSessionId);
            if (allowBootstrapOnMissingOrExpired)
            {
                return SessionRestoreAttempt.Empty;
            }

            return new SessionRestoreAttempt
            {
                Failure = await DenyAsync(
                    httpContext,
                    suppliedSessionId,
                    operatorId,
                    subject,
                    SessionExpiredEventType,
                    "session_expired",
                    StatusCodes.Status401Unauthorized,
                    nowUtc,
                    ct)
            };
        }

        var shouldAuditRestored = session.LastSeenAtUtc <= session.AuthenticatedAtUtc;
        session.LastSeenAtUtc = nowUtc;
        session.ExpiresAtUtc = nowUtc.AddMinutes(DefaultSessionTtlMinutes);
        session.ActiveMode = normalizedMode;
        _sessionStore.Upsert(session);
        WriteSession(httpContext, session);
        return new SessionRestoreAttempt
        {
            Session = session,
            ShouldAuditRestored = shouldAuditRestored
        };
    }

    private void WriteSession(HttpContext httpContext, OperatorSessionContext session)
    {
        httpContext.Response.Headers[SessionHeaderName] = session.OperatorSessionId;
        httpContext.Response.Cookies.Append(
            SessionCookieName,
            session.OperatorSessionId,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps,
                Expires = session.ExpiresAtUtc
            });
    }

    private static string ResolveSuppliedSessionId(HttpRequest request)
    {
        var headerSessionId = NormalizeOptional(request.Headers[SessionHeaderName].FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(headerSessionId))
        {
            return headerSessionId;
        }

        if (request.Cookies.TryGetValue(SessionCookieName, out var cookieSessionId))
        {
            return NormalizeOptional(cookieSessionId);
        }

        return string.Empty;
    }

    private static string NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static OperatorSessionContext CreateNewSession(string normalizedMode, DateTime nowUtc)
    {
        return new OperatorSessionContext
        {
            OperatorSessionId = $"web:{Guid.NewGuid():N}",
            Surface = OperatorSurfaceTypes.Web,
            AuthenticatedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddMinutes(DefaultSessionTtlMinutes),
            ActiveMode = normalizedMode
        };
    }

    private static OperatorIdentityContext BuildIdentity(string operatorId, string subject, string authSource, DateTime authTimeUtc)
    {
        return new OperatorIdentityContext
        {
            OperatorId = operatorId,
            OperatorDisplay = operatorId,
            SurfaceSubject = subject,
            AuthSource = authSource,
            AuthTimeUtc = authTimeUtc
        };
    }

    private static string NormalizeMode(string? mode)
    {
        var normalized = OperatorModeTypes.Normalize(mode);
        return OperatorModeTypes.IsSupported(normalized)
            ? normalized
            : OperatorModeTypes.ResolutionQueue;
    }

    private string ResolveAccessToken(HttpRequest request)
    {
        var headerName = string.IsNullOrWhiteSpace(_settings.AccessHeaderName)
            ? "X-Tga-Operator-Key"
            : _settings.AccessHeaderName.Trim();
        if (request.Headers.TryGetValue(headerName, out var headerValues))
        {
            var headerToken = NormalizeOptional(headerValues.FirstOrDefault());
            if (!string.IsNullOrWhiteSpace(headerToken))
            {
                return headerToken;
            }
        }

        var cookieName = string.IsNullOrWhiteSpace(_settings.AccessCookieName)
            ? "tga_operator_key"
            : _settings.AccessCookieName.Trim();
        if (request.Cookies.TryGetValue(cookieName, out var cookieToken))
        {
            return NormalizeOptional(cookieToken);
        }

        return string.Empty;
    }

    private string ResolveOperatorId()
    {
        var configured = NormalizeOptional(_settings.OperatorIdentity);
        return string.IsNullOrWhiteSpace(configured) ? "web-operator" : configured;
    }

    private static string ResolveSurfaceSubject(HttpContext httpContext)
    {
        var explicitSubject = NormalizeOptional(httpContext.Request.Headers["X-Tga-Operator-Subject"].FirstOrDefault());
        if (!string.IsNullOrWhiteSpace(explicitSubject))
        {
            return explicitSubject;
        }

        var remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"web:{remoteIp}";
    }

    private sealed class SessionRestoreAttempt
    {
        public static SessionRestoreAttempt Empty { get; } = new();

        public OperatorSessionContext? Session { get; init; }
        public bool ShouldAuditRestored { get; init; }
        public WebOperatorAuthResult? Failure { get; init; }
    }
}

public sealed class WebOperatorAuthResult
{
    private WebOperatorAuthResult()
    {
    }

    public bool Accepted { get; private set; }
    public int StatusCode { get; private set; }
    public string FailureReason { get; private set; } = string.Empty;
    public Guid? AuditEventId { get; private set; }
    public OperatorSessionContext Session { get; private set; } = new();
    public OperatorIdentityContext OperatorIdentity { get; private set; } = new();

    public static WebOperatorAuthResult Success(
        OperatorIdentityContext operatorIdentity,
        OperatorSessionContext session)
    {
        return new WebOperatorAuthResult
        {
            Accepted = true,
            StatusCode = StatusCodes.Status200OK,
            OperatorIdentity = operatorIdentity,
            Session = session
        };
    }

    public static WebOperatorAuthResult Failure(
        int statusCode,
        string failureReason,
        Guid? auditEventId,
        OperatorSessionContext session,
        OperatorIdentityContext operatorIdentity)
    {
        return new WebOperatorAuthResult
        {
            Accepted = false,
            StatusCode = statusCode,
            FailureReason = failureReason,
            AuditEventId = auditEventId,
            Session = session,
            OperatorIdentity = operatorIdentity
        };
    }
}

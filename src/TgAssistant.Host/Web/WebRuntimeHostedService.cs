using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TgAssistant.Core.Configuration;
using TgAssistant.Core.Interfaces;
using TgAssistant.Core.Models;
using TgAssistant.Host.Startup;
using TgAssistant.Web.Read;

namespace TgAssistant.Host.Web;

public sealed class WebRuntimeHostedService : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly IOptions<WebSettings> _webSettings;
    private readonly RuntimeRoleSelection _runtimeRoleSelection;
    private readonly ILogger<WebRuntimeHostedService> _logger;
    private IHost? _webHost;

    public WebRuntimeHostedService(
        IServiceProvider services,
        IOptions<WebSettings> webSettings,
        RuntimeRoleSelection runtimeRoleSelection,
        ILogger<WebRuntimeHostedService> logger)
    {
        _services = services;
        _webSettings = webSettings;
        _runtimeRoleSelection = runtimeRoleSelection;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = _webSettings.Value;
        var listenUrl = settings.Url.Trim();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(listenUrl);
        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (IsPublicPath(context.Request.Path))
            {
                await next();
                return;
            }

            if (!settings.RequireOperatorAccessToken)
            {
                await next();
                return;
            }

            if (TryAuthenticateOperator(context, settings))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(RenderUnauthorizedPage(settings), cancellationToken);
        });

        app.MapGet("/health/live", () => Results.Json(new { status = "live", role = _runtimeRoleSelection.Roles.ToString() }));
        app.MapGet("/health/ready", () => Results.Json(new { status = "ready", role = _runtimeRoleSelection.Roles.ToString(), web = true }));
        app.MapGet("/routes", () => Results.Json(new
        {
            shell = new[] { "/", "/queue", "/case-detail", "/artifact-detail" },
            renderer = WebRouteRenderer.DefaultRoutes
        }));

        app.MapGet("/", async (HttpContext context, CancellationToken ct) =>
        {
            var scope = await ResolveScopeAsync(context.Request, settings, ct);
            if (!scope.IsResolved)
            {
                var onboardingBody = RenderScopeOnboardingHtml("/queue", context.Request, scope);
                var html = WrapShellHtml("Нужен рабочий контекст", "/", onboardingBody, context.Request, scope);
                return Results.Content(html, "text/html; charset=utf-8");
            }

            var request = CreateReadRequest(context.Request, settings, scope);
            var body = "<p>Предпросмотр очереди временно недоступен.</p>";
            try
            {
                var queueResult = await GetRenderer().RenderAsync("/queue" + context.Request.QueryString, request, ct);
                if (queueResult is not null)
                {
                    body = queueResult.Html;
                }
            }
            catch (Exception ex)
            {
                body = $"<h2>Не удалось открыть очередь</h2><p>{WebUtility.HtmlEncode(ex.Message)}</p><p>Попробуйте перейти на <a href='/queue'>/queue</a>.</p>";
            }

            var htmlReady = WrapShellHtml("Операторская панель", "/", body, context.Request, scope);
            return Results.Content(htmlReady, "text/html; charset=utf-8");
        });

        app.MapGet("/queue", async (HttpContext context, CancellationToken ct) =>
            await RenderRouteAsync(context, settings, "/queue", "Очередь", ct));

        app.MapGet("/case-detail", async (HttpContext context, CancellationToken ct) =>
            await RenderRouteAsync(context, settings, "/case-detail", "Детали кейса", ct));

        app.MapGet("/artifact-detail", async (HttpContext context, CancellationToken ct) =>
            await RenderRouteAsync(context, settings, "/artifact-detail", "Детали артефакта", ct));

        app.MapGet("/{**route}", async (HttpContext context, string? route, CancellationToken ct) =>
        {
            var normalizedRoute = NormalizeCatchAllRoute(route);
            if (normalizedRoute is null)
            {
                return Results.NotFound();
            }

            return await RenderRouteAsync(context, settings, normalizedRoute, null, ct);
        });

        _webHost = app;
        await _webHost.StartAsync(cancellationToken);
        _logger.LogInformation("Web runtime host started at {Url}. role={Role}", listenUrl, _runtimeRoleSelection.Roles);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_webHost is null)
        {
            return;
        }

        await _webHost.StopAsync(cancellationToken);
        _logger.LogInformation("Web runtime host stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_webHost is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
    }

    private async Task<IResult> RenderRouteAsync(
        HttpContext context,
        WebSettings settings,
        string route,
        string? fallbackTitle,
        CancellationToken ct)
    {
        try
        {
            var scope = await ResolveScopeAsync(context.Request, settings, ct);
            if (!scope.IsResolved)
            {
                var onboardingHtml = RenderScopeOnboardingHtml(route, context.Request, scope);
                var onboardingPage = WrapShellHtml("Нужен рабочий контекст", route, onboardingHtml, context.Request, scope);
                return Results.Content(onboardingPage, "text/html; charset=utf-8");
            }

            var request = CreateReadRequest(context.Request, settings, scope);
            var routeWithQuery = $"{route}{context.Request.QueryString}";
            var result = await GetRenderer().RenderAsync(routeWithQuery, request, ct);
            if (result is null)
            {
                return Results.NotFound(WrapShellHtml(
                    "Маршрут не найден",
                    route,
                    $"<h2>Страница недоступна</h2><p>Маршрут <code>{WebUtility.HtmlEncode(route)}</code> не поддерживается.</p><p>Перейдите в <a href='/queue'>очередь</a> или на <a href='/dashboard'>панель</a>.</p>",
                    context.Request,
                    scope));
            }

            var title = string.IsNullOrWhiteSpace(result.Title) ? fallbackTitle ?? "Операторская панель" : result.Title;
            var html = WrapShellHtml(title, route, result.Html, context.Request, scope);
            return Results.Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web route render failed for {Route}", route);
            var safeRoute = WebUtility.HtmlEncode(route);
            var safeError = WebUtility.HtmlEncode(ex.Message);
            var errorHtml = $"<h2>Результат временно недоступен</h2><p>Не удалось открыть страницу <code>{safeRoute}</code>.</p><p>{safeError}</p><p><a href='/queue'>Открыть очередь</a></p>";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            return Results.Content(
                WrapShellHtml("Временная ошибка", route, errorHtml, context.Request, ScopeResolution.Unresolved("error")),
                "text/html; charset=utf-8");
        }
    }

    private IWebRouteRenderer GetRenderer() => _services.GetRequiredService<IWebRouteRenderer>();

    private async Task<ScopeResolution> ResolveScopeAsync(HttpRequest request, WebSettings settings, CancellationToken ct)
    {
        var hasExplicitCaseParam = request.Query.ContainsKey("caseScopeId");
        var hasExplicitChatParam = request.Query.ContainsKey("chatId");
        var hasExplicitScopeInput = hasExplicitCaseParam || hasExplicitChatParam;

        var hasValidExplicitCase = TryReadPositiveLong(request.Query, "caseScopeId", out var explicitCaseId);
        var hasValidExplicitChat = TryReadPositiveLong(request.Query, "chatId", out var explicitChatId);
        if (hasValidExplicitCase && hasValidExplicitChat)
        {
            return ScopeResolution.Resolved(explicitCaseId, explicitChatId, "explicit");
        }

        if (hasExplicitScopeInput)
        {
            var invalidCandidates = await GetScopeCandidatesAsync(ct);
            return ScopeResolution.Unresolved(
                "Переданы неполные или невалидные параметры scope. Для web-нужен pair caseScopeId + chatId > 0.",
                invalidCandidates);
        }

        if (settings.DefaultCaseId > 0 && settings.DefaultChatId > 0)
        {
            if (ScopeVisibilityPolicy.IsOperatorVisibleScope(settings.DefaultCaseId, settings.DefaultChatId))
            {
                return ScopeResolution.Resolved(settings.DefaultCaseId, settings.DefaultChatId, "configured_default");
            }

            _logger.LogWarning(
                "Configured web default scope is synthetic/smoke and blocked in operator-safe mode. case_id={CaseId}, chat_id={ChatId}",
                settings.DefaultCaseId,
                settings.DefaultChatId);
        }

        var candidates = await GetScopeCandidatesAsync(ct);
        if (candidates.Count == 0)
        {
            return ScopeResolution.Unresolved(
                "Рабочий контекст пока не найден: нет сконфигурированного default scope и нет stage6-кейсов для авто-выбора.",
                candidates);
        }

        if (candidates.Count == 1)
        {
            var single = candidates[0];
            return ScopeResolution.Resolved(single.ScopeCaseId, single.ChatId, "inferred_single", candidates);
        }

        var top = candidates[0];
        var second = candidates[1];
        if (top.ActiveCaseCount > second.ActiveCaseCount)
        {
            return ScopeResolution.Resolved(top.ScopeCaseId, top.ChatId, "inferred_top_active", candidates);
        }

        return ScopeResolution.Unresolved(
            "Найдено несколько конкурирующих рабочих контекстов с одинаковым приоритетом. Автовыбор отключен, чтобы не открыть неверный scope.",
            candidates);
    }

    private async Task<List<Stage6ScopeCandidate>> GetScopeCandidatesAsync(CancellationToken ct)
    {
        var repository = _services.GetRequiredService<IStage6CaseRepository>();
        return await repository.GetScopeCandidatesAsync(limit: 8, ct);
    }

    private static WebReadRequest CreateReadRequest(HttpRequest request, WebSettings settings, ScopeResolution scope)
    {
        var readRequest = new WebReadRequest
        {
            Actor = settings.OperatorIdentity,
            CaseId = scope.CaseId,
            ChatId = scope.ChatId
        };

        if (DateTime.TryParse(
                request.Query["asOfUtc"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var asOfUtc))
        {
            readRequest.AsOfUtc = DateTime.SpecifyKind(asOfUtc, DateTimeKind.Utc);
        }

        return readRequest;
    }

    private static bool IsPublicPath(PathString path)
    {
        return path.Equals("/health/live", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/health/ready", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryAuthenticateOperator(HttpContext context, WebSettings settings)
    {
        var expectedToken = settings.OperatorAccessToken.Trim();
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        var token = ExtractToken(context, settings);
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!string.Equals(token, expectedToken, StringComparison.Ordinal))
        {
            return false;
        }

        context.Response.Cookies.Append(settings.AccessCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = false,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(12)
        });

        return true;
    }

    private static string? ExtractToken(HttpContext context, WebSettings settings)
    {
        if (context.Request.Headers.TryGetValue(settings.AccessHeaderName, out var tokenHeader))
        {
            var headerToken = tokenHeader.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(headerToken))
            {
                return headerToken;
            }
        }

        if (context.Request.Query.TryGetValue("access_token", out var tokenQuery))
        {
            var queryToken = tokenQuery.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(queryToken))
            {
                return queryToken;
            }
        }

        if (context.Request.Cookies.TryGetValue(settings.AccessCookieName, out var cookieToken))
        {
            var value = cookieToken.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string RenderUnauthorizedPage(WebSettings settings)
    {
        var encodedHeader = WebUtility.HtmlEncode(settings.AccessHeaderName);
        return
            $"<html><body><h1>Нужен доступ оператора</h1><p>Передайте токен через заголовок <code>{encodedHeader}</code> или query-параметр <code>access_token</code>.</p></body></html>";
    }

    private static string WrapShellHtml(string title, string activeRoute, string bodyHtml, HttpRequest request, ScopeResolution scope)
    {
        var queuePath = BuildNavigationPath("/queue", request, scope);
        var casePath = BuildNavigationPath("/case-detail", request, scope);
        var artifactPath = BuildNavigationPath("/artifact-detail", request, scope);
        var shellPath = BuildNavigationPath("/", request, scope);
        var escapedTitle = WebUtility.HtmlEncode(title);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{escapedTitle}</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<style>");
        sb.Append("body{font-family:ui-sans-serif,system-ui,sans-serif;margin:0;background:#f3f6fb;color:#14243f;line-height:1.45;}");
        sb.Append("header{background:#1e314f;color:#fff;padding:14px 18px;}");
        sb.Append("header h1{margin:0;font-size:1.05rem;}nav{display:flex;gap:9px;flex-wrap:wrap;margin-top:10px;}");
        sb.Append("nav a{color:#dce8ff;text-decoration:none;border:1px solid #4a6288;padding:6px 10px;border-radius:7px;background:rgba(255,255,255,.04);}");
        sb.Append("nav a.active{background:#6289ca;color:#fff;border-color:#6289ca;}");
        sb.Append("main{max-width:1120px;margin:16px auto;padding:0 14px 24px;}section{background:#fff;padding:12px 14px;border-radius:10px;border:1px solid #d9e3f1;}");
        sb.Append("</style></head><body>");
        sb.Append("<header><h1>Telegram Assistant — оператор</h1><nav>");
        sb.Append(RenderNavLink(shellPath, "Панель", activeRoute == "/"));
        sb.Append(RenderNavLink(queuePath, "Очередь", activeRoute == "/queue" || activeRoute == "/inbox"));
        sb.Append(RenderNavLink(casePath, "Кейс", activeRoute == "/case-detail"));
        sb.Append(RenderNavLink(artifactPath, "Артефакт", activeRoute == "/artifact-detail"));
        sb.Append("</nav></header><main><section>");
        sb.Append(bodyHtml);
        sb.Append("</section></main></body></html>");
        return sb.ToString();
    }

    private static string BuildNavigationPath(string route, HttpRequest request, ScopeResolution scope)
    {
        var query = new List<string>();
        if (request.Query.TryGetValue("caseId", out var caseId) && !string.IsNullOrWhiteSpace(caseId))
        {
            query.Add($"caseId={WebUtility.UrlEncode(caseId)}");
        }

        if (request.Query.TryGetValue("artifactType", out var artifactType) && !string.IsNullOrWhiteSpace(artifactType))
        {
            query.Add($"artifactType={WebUtility.UrlEncode(artifactType)}");
        }

        if (TryReadPositiveLong(request.Query, "caseScopeId", out var explicitCaseId))
        {
            query.Add($"caseScopeId={explicitCaseId}");
        }
        else if (scope.IsResolved)
        {
            query.Add($"caseScopeId={scope.CaseId}");
        }

        if (TryReadPositiveLong(request.Query, "chatId", out var explicitChatId))
        {
            query.Add($"chatId={explicitChatId}");
        }
        else if (scope.IsResolved)
        {
            query.Add($"chatId={scope.ChatId}");
        }

        return query.Count == 0 ? route : $"{route}?{string.Join("&", query)}";
    }

    private static string RenderScopeOnboardingHtml(string route, HttpRequest request, ScopeResolution scope)
    {
        var sb = new StringBuilder();
        sb.Append("<h2>Нужен рабочий контекст</h2>");
        sb.Append("<p>Система не открывает web в техническом scope <code>case=0, chat=0</code>. Сначала выберите рабочую область.</p>");
        sb.Append($"<p>{WebUtility.HtmlEncode(scope.Message)}</p>");

        if (scope.Candidates.Count > 0)
        {
            sb.Append("<h3>Доступные рабочие контексты</h3><ul>");
            foreach (var candidate in scope.Candidates)
            {
                var link = BuildScopedRoute(route, request, candidate.ScopeCaseId, candidate.ChatId);
                sb.Append($"<li><a href='{WebUtility.HtmlEncode(link)}'>case={candidate.ScopeCaseId}, chat={candidate.ChatId}</a> — active={candidate.ActiveCaseCount}, total={candidate.TotalCaseCount}, updated={candidate.LastCaseUpdatedAtUtc:yyyy-MM-dd HH:mm} UTC</li>");
            }

            sb.Append("</ul>");
        }

        sb.Append("<p>Что сделать дальше: задайте <code>Web:DefaultCaseId</code> и <code>Web:DefaultChatId</code> или откройте один из доступных контекстов выше.</p>");
        sb.Append("<details><summary>Технические детали</summary>");
        sb.Append($"<p>route={WebUtility.HtmlEncode(route)}, resolution={WebUtility.HtmlEncode(scope.Source)}</p>");
        sb.Append("</details>");
        return sb.ToString();
    }

    private static string BuildScopedRoute(string route, HttpRequest request, long caseId, long chatId)
    {
        var segments = new List<string>
        {
            $"caseScopeId={caseId}",
            $"chatId={chatId}"
        };

        foreach (var pair in request.Query)
        {
            if (pair.Key.Equals("caseScopeId", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("chatId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = pair.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            segments.Add($"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(value)}");
        }

        return segments.Count == 0 ? route : $"{route}?{string.Join("&", segments)}";
    }

    private static string RenderNavLink(string href, string label, bool active)
    {
        var css = active ? "active" : string.Empty;
        return $"<a class=\"{css}\" href=\"{WebUtility.HtmlEncode(href)}\">{WebUtility.HtmlEncode(label)}</a>";
    }

    private static string? NormalizeCatchAllRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return null;
        }

        var normalized = route.StartsWith('/') ? route : $"/{route}";
        if (normalized.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    private static bool TryReadPositiveLong(IQueryCollection query, string key, out long value)
    {
        value = 0;
        return query.TryGetValue(key, out var raw)
               && long.TryParse(raw, out value)
               && value > 0;
    }

    private sealed class ScopeResolution
    {
        public bool IsResolved { get; init; }
        public long CaseId { get; init; }
        public long ChatId { get; init; }
        public string Source { get; init; } = "none";
        public string Message { get; init; } = string.Empty;
        public IReadOnlyList<Stage6ScopeCandidate> Candidates { get; init; } = [];

        public static ScopeResolution Resolved(long caseId, long chatId, string source, IReadOnlyList<Stage6ScopeCandidate>? candidates = null)
        {
            return new ScopeResolution
            {
                IsResolved = true,
                CaseId = caseId,
                ChatId = chatId,
                Source = source,
                Message = "Рабочий контекст успешно определен.",
                Candidates = candidates ?? []
            };
        }

        public static ScopeResolution Unresolved(string message, IReadOnlyList<Stage6ScopeCandidate>? candidates = null)
        {
            return new ScopeResolution
            {
                IsResolved = false,
                Source = "unresolved",
                Message = message,
                Candidates = candidates ?? []
            };
        }
    }
}

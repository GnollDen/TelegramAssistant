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
            var request = CreateReadRequest(context.Request, settings);
            var queueResult = await GetRenderer().RenderAsync("/queue" + context.Request.QueryString, request, ct);
            var body = queueResult is null
                ? "<p>Queue preview is unavailable.</p>"
                : queueResult.Html;
            var html = WrapShellHtml("Operator Shell", "/", body, context.Request, settings);
            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapGet("/queue", async (HttpContext context, CancellationToken ct) =>
            await RenderRouteAsync(context, settings, "/queue", "Queue", ct));

        app.MapGet("/case-detail", async (HttpContext context, CancellationToken ct) =>
            await RenderRouteAsync(context, settings, "/case-detail", "Case Detail", ct));

        app.MapGet("/artifact-detail", async (HttpContext context, CancellationToken ct) =>
            await RenderRouteAsync(context, settings, "/artifact-detail", "Artifact Detail", ct));

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
        var request = CreateReadRequest(context.Request, settings);
        var routeWithQuery = $"{route}{context.Request.QueryString}";
        var result = await GetRenderer().RenderAsync(routeWithQuery, request, ct);
        if (result is null)
        {
            return Results.NotFound(WrapShellHtml(
                "Route Not Found",
                route,
                $"<p>Route <code>{WebUtility.HtmlEncode(route)}</code> is not available.</p>",
                context.Request,
                settings));
        }

        var title = string.IsNullOrWhiteSpace(result.Title) ? fallbackTitle ?? "Operator Shell" : result.Title;
        var html = WrapShellHtml(title, route, result.Html, context.Request, settings);
        return Results.Content(html, "text/html; charset=utf-8");
    }

    private IWebRouteRenderer GetRenderer() => _services.GetRequiredService<IWebRouteRenderer>();

    private static WebReadRequest CreateReadRequest(HttpRequest request, WebSettings settings)
    {
        var readRequest = new WebReadRequest
        {
            Actor = settings.OperatorIdentity,
            CaseId = settings.DefaultCaseId,
            ChatId = settings.DefaultChatId
        };

        if (long.TryParse(request.Query["caseScopeId"], out var scopeCaseId))
        {
            readRequest.CaseId = scopeCaseId;
        }

        if (long.TryParse(request.Query["chatId"], out var chatId))
        {
            readRequest.ChatId = chatId;
        }

        if (DateTime.TryParse(request.Query["asOfUtc"], out var asOfUtc))
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
            $"<html><body><h1>Operator Access Required</h1><p>Provide token via header <code>{encodedHeader}</code> or query <code>access_token</code>.</p></body></html>";
    }

    private static string WrapShellHtml(string title, string activeRoute, string bodyHtml, HttpRequest request, WebSettings settings)
    {
        var queuePath = BuildNavigationPath("/queue", request, settings);
        var casePath = BuildNavigationPath("/case-detail", request, settings);
        var artifactPath = BuildNavigationPath("/artifact-detail", request, settings);
        var shellPath = BuildNavigationPath("/", request, settings);
        var escapedTitle = WebUtility.HtmlEncode(title);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append($"<title>{escapedTitle}</title>");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<style>");
        sb.Append("body{font-family:ui-sans-serif,system-ui,sans-serif;margin:0;background:#f5f7fb;color:#13203a;}");
        sb.Append("header{background:#13203a;color:#fff;padding:14px 18px;}");
        sb.Append("header h1{margin:0;font-size:1.05rem;}nav{display:flex;gap:10px;flex-wrap:wrap;margin-top:10px;}");
        sb.Append("nav a{color:#d8e4ff;text-decoration:none;border:1px solid #365480;padding:6px 10px;border-radius:7px;}");
        sb.Append("nav a.active{background:#3f74d1;color:#fff;border-color:#3f74d1;}");
        sb.Append("main{max-width:1100px;margin:16px auto;padding:0 14px 24px;}section{background:#fff;padding:12px 14px;border-radius:8px;border:1px solid #d9e2ef;}");
        sb.Append("</style></head><body>");
        sb.Append("<header><h1>Telegram Assistant Operator Shell</h1><nav>");
        sb.Append(RenderNavLink(shellPath, "Shell", activeRoute == "/"));
        sb.Append(RenderNavLink(queuePath, "Queue", activeRoute == "/queue" || activeRoute == "/inbox"));
        sb.Append(RenderNavLink(casePath, "Case Detail", activeRoute == "/case-detail"));
        sb.Append(RenderNavLink(artifactPath, "Artifact Detail", activeRoute == "/artifact-detail"));
        sb.Append("</nav></header><main><section>");
        sb.Append(bodyHtml);
        sb.Append("</section></main></body></html>");
        return sb.ToString();
    }

    private static string BuildNavigationPath(string route, HttpRequest request, WebSettings settings)
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

        if (request.Query.TryGetValue("chatId", out var chatId) && !string.IsNullOrWhiteSpace(chatId))
        {
            query.Add($"chatId={WebUtility.UrlEncode(chatId)}");
        }
        else if (settings.DefaultChatId > 0)
        {
            query.Add($"chatId={settings.DefaultChatId}");
        }

        if (settings.DefaultCaseId > 0)
        {
            query.Add($"caseScopeId={settings.DefaultCaseId}");
        }

        return query.Count == 0 ? route : $"{route}?{string.Join("&", query)}";
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
}

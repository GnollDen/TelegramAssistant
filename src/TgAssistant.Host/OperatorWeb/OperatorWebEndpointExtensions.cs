using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TgAssistant.Host.Startup;

namespace TgAssistant.Host.OperatorWeb;

public static class OperatorWebEndpointExtensions
{
    public static IEndpointRouteBuilder MapOperatorWebShell(this IEndpointRouteBuilder endpoints)
    {
        var runtimeRoleSelection = endpoints.ServiceProvider.GetRequiredService<RuntimeRoleSelection>();
        if (!runtimeRoleSelection.Has(RuntimeWorkloadRole.Ops))
        {
            return endpoints;
        }

        endpoints.MapGet("/", () => Results.Redirect("/operator"));
        endpoints.MapGet("/operator", () => Results.Content(OperatorHomeHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/operator/resolution", () => Results.Content(OperatorResolutionHtml, "text/html; charset=utf-8"));

        endpoints.MapGet("/operator/resolution/bootstrap", (HttpRequest request) =>
        {
            if (string.Equals(request.Query["simulate"], "error", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(
                    new
                    {
                        state = "error",
                        reason = "bootstrap_simulated_error",
                        message = "Resolution bootstrap simulation returned an error."
                    },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                state = "empty",
                queueCount = 0,
                message = "No unresolved items are currently projected for the active tracked person."
            });
        });

        return endpoints;
    }

    private const string OperatorHomeHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Operator Web</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f5f7fb;
      --card: #ffffff;
      --ink: #152033;
      --muted: #5b6b88;
      --line: #d7deea;
      --brand: #0d3b66;
      --brand-ink: #ffffff;
      --critical: #a11212;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      background: radial-gradient(circle at top right, #dde9ff, var(--bg) 45%);
      color: var(--ink);
    }
    main {
      max-width: 980px;
      margin: 48px auto;
      padding: 0 20px;
    }
    .card {
      background: var(--card);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 20px;
      margin-bottom: 20px;
      box-shadow: 0 8px 24px rgba(18, 34, 61, 0.08);
    }
    .nav-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
      gap: 12px;
    }
    .nav-item {
      display: block;
      text-decoration: none;
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 12px 14px;
      color: var(--ink);
      background: #f9fbff;
    }
    .nav-item strong { display: block; margin-bottom: 4px; }
    .nav-item small { color: var(--muted); }
    .nav-item.primary {
      background: var(--brand);
      color: var(--brand-ink);
      border-color: var(--brand);
    }
    .badge {
      display: inline-block;
      margin-left: 6px;
      padding: 1px 7px;
      border-radius: 999px;
      font-size: 12px;
      background: #eef2fb;
      color: #2d4269;
    }
    .critical {
      color: var(--critical);
      font-weight: 600;
    }
  </style>
</head>
<body>
  <main>
    <section class="card">
      <h1>Operator Web Home</h1>
      <p>Navigation-first shell for bounded operator workflows. Legacy Stage6 web/queue/case pages are not used.</p>
      <div class="nav-grid">
        <a class="nav-item primary" href="/operator/resolution">
          <strong>Resolution <span class="badge">P0</span></strong>
          <small>Enter the dedicated queue/detail workflow route.</small>
        </a>
        <span class="nav-item" aria-disabled="true">
          <strong>Persons</strong>
          <small>Planned in later OPINT slices.</small>
        </span>
        <span class="nav-item" aria-disabled="true">
          <strong>Alerts</strong>
          <small>Planned in later OPINT slices.</small>
        </span>
      </div>
    </section>
    <section class="card">
      <h2>Operational Snapshot</h2>
      <p>System status: <strong>normal</strong></p>
      <p class="critical">Critical unresolved items: pending OPINT-005-B queue integration.</p>
    </section>
  </main>
</body>
</html>
""";

    private const string OperatorResolutionHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Operator Resolution</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f4f7fb;
      --panel: #ffffff;
      --ink: #14243c;
      --muted: #5e6e89;
      --line: #d9e1ef;
      --accent: #0d4a7f;
      --warn: #9a1a1a;
      --ok: #0d6635;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      color: var(--ink);
      background: linear-gradient(180deg, #e7eefb, var(--bg) 240px);
    }
    main {
      max-width: 1000px;
      margin: 36px auto;
      padding: 0 20px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 18px;
      margin-bottom: 14px;
      box-shadow: 0 10px 22px rgba(20, 36, 60, 0.08);
    }
    .row {
      display: flex;
      gap: 10px;
      flex-wrap: wrap;
      align-items: center;
      margin-top: 8px;
    }
    a, button {
      border-radius: 8px;
      border: 1px solid var(--line);
      padding: 8px 12px;
      text-decoration: none;
      color: var(--ink);
      background: #f7faff;
      font: inherit;
      cursor: pointer;
    }
    button.primary {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
    }
    .state {
      border-left: 4px solid var(--accent);
      background: #f8fbff;
      padding: 12px;
      border-radius: 8px;
      margin-top: 10px;
    }
    .state.loading { border-left-color: var(--accent); }
    .state.empty { border-left-color: var(--ok); }
    .state.error { border-left-color: var(--warn); background: #fff6f6; }
    .muted { color: var(--muted); }
    code { background: #eef2fb; padding: 2px 6px; border-radius: 6px; }
  </style>
</head>
<body>
  <main>
    <section class="panel">
      <h1>Resolution Route</h1>
      <p class="muted">Dedicated P0 resolution entry. Queue/detail/action flows will be layered on this route in subsequent OPINT-005 slices.</p>
      <div class="row">
        <a href="/operator">Back to Home</a>
        <button id="retry" class="primary" type="button">Retry Bootstrap</button>
      </div>
      <div id="state" class="state loading">Loading resolution bootstrap...</div>
    </section>

    <section class="panel">
      <h2>Contract Boundary</h2>
      <p>This route targets clean operator contracts only: <code>/api/operator/resolution/*</code>. Legacy Stage6 web/queue/case pages are excluded.</p>
      <p class="muted">Use <code>?simulate=error</code> to verify explicit failure-state rendering.</p>
    </section>
  </main>

  <script>
    const stateNode = document.getElementById("state");
    const retryButton = document.getElementById("retry");

    function setState(kind, message) {
      stateNode.className = "state " + kind;
      stateNode.textContent = message;
    }

    async function loadBootstrap() {
      setState("loading", "Loading resolution bootstrap...");
      const search = new URLSearchParams(window.location.search);
      const simulate = search.get("simulate");
      const endpoint = simulate ? "/operator/resolution/bootstrap?simulate=" + encodeURIComponent(simulate) : "/operator/resolution/bootstrap";
      try {
        const response = await fetch(endpoint, { method: "GET", headers: { "accept": "application/json" } });
        if (!response.ok) {
          let reason = "request_failed";
          try {
            const body = await response.json();
            reason = body.reason || body.message || reason;
          } catch (_) {
          }
          throw new Error(reason);
        }

        const payload = await response.json();
        if (payload.state === "empty") {
          setState("empty", "Empty queue: " + (payload.message || "No unresolved items."));
          return;
        }

        setState("loading", "Bootstrap completed. Queue rendering will be enabled in OPINT-005-B.");
      } catch (error) {
        setState("error", "Failed to load resolution entry state: " + (error && error.message ? error.message : "unknown_error"));
      }
    }

    retryButton.addEventListener("click", loadBootstrap);
    loadBootstrap();
  </script>
</body>
</html>
""";
}

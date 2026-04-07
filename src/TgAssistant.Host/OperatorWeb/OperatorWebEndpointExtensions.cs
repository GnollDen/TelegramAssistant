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
        endpoints.MapGet("/operator/alerts", () => Results.Content(OperatorAlertsWebShell.Html, "text/html; charset=utf-8"));
        endpoints.MapGet("/operator/persons", () => Results.Content(OperatorPersonsHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/operator/person-workspace", () => Results.Content(OperatorPersonWorkspaceShellHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/operator/resolution", () => Results.Content(OperatorResolutionHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/operator/offline-events", () => Results.Content(OperatorOfflineEventsHtml, "text/html; charset=utf-8"));

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
                state = "ready",
                queueRoute = "/api/operator/resolution/queue/query",
                trackedPersonsRoute = "/api/operator/tracked-persons/query",
                trackedPersonSelectionRoute = "/api/operator/tracked-persons/select"
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
        <a class="nav-item" href="/operator/offline-events">
          <strong>Offline Events <span class="badge">P1</span></strong>
          <small>Inspect and refine captured offline events with trust and clarification history.</small>
        </a>
        <a class="nav-item" href="/operator/persons">
          <strong>Persons <span class="badge">P1</span></strong>
          <small>Browse tracked persons with unresolved and recency signals.</small>
        </a>
        <a class="nav-item" href="/operator/alerts">
          <strong>Alerts <span class="badge">P2</span></strong>
          <small>Grouped workflow-critical alerts linked to person and resolution context.</small>
        </a>
      </div>
    </section>
    <section class="card">
      <h2>Operational Snapshot</h2>
      <p>System status: <strong>normal</strong></p>
      <p class="critical">Critical unresolved items: resolution queue connected; detail/evidence/action slices remain.</p>
    </section>
  </main>
</body>
</html>
""";

    private const string OperatorPersonsHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Operator Persons</title>
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
      --chip: #eef2fb;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      color: var(--ink);
      background: linear-gradient(180deg, #e7eefb, var(--bg) 220px);
    }
    main {
      max-width: 1080px;
      margin: 28px auto;
      padding: 0 16px 40px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 14px;
      box-shadow: 0 10px 22px rgba(20, 36, 60, 0.08);
      margin-bottom: 14px;
    }
    .row {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      align-items: center;
    }
    .row > * {
      flex: 1 1 170px;
    }
    input, button, a {
      border-radius: 8px;
      border: 1px solid var(--line);
      padding: 8px 10px;
      font: inherit;
      color: inherit;
      background: #fff;
      text-decoration: none;
    }
    button { cursor: pointer; }
    button.primary {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
    }
    .state {
      border-radius: 8px;
      padding: 8px 10px;
      font-size: 13px;
      margin-top: 10px;
    }
    .state.loading { background: #eef5ff; color: #1d3f70; }
    .state.empty { background: #f5f7fb; color: var(--muted); }
    .state.error { background: #ffefef; color: var(--warn); }
    .counts {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 8px;
      color: var(--muted);
      font-size: 13px;
    }
    .counts span {
      border: 1px solid var(--line);
      border-radius: 999px;
      background: var(--chip);
      padding: 3px 9px;
    }
    #persons-list {
      display: grid;
      gap: 10px;
    }
    .person-card {
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 12px;
      display: grid;
      gap: 8px;
    }
    .person-card h3 {
      margin: 0;
      font-size: 17px;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      color: var(--muted);
      font-size: 12px;
    }
    .meta span {
      border: 1px solid var(--line);
      border-radius: 999px;
      background: #f8faff;
      padding: 2px 8px;
    }
    .badge {
      display: inline-block;
      border-radius: 999px;
      padding: 2px 9px;
      font-size: 12px;
      font-weight: 600;
      border: 1px solid;
    }
    .badge.unresolved {
      color: #6a1111;
      background: #ffeaea;
      border-color: #f2b4b4;
    }
    .badge.resolved {
      color: var(--ok);
      background: #ecf9f1;
      border-color: #b6e3c7;
    }
    .actions {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    .actions > * {
      flex: 1 1 200px;
      text-align: center;
    }
    .muted { color: var(--muted); }
  </style>
</head>
<body>
  <main>
    <section class="panel">
      <h1>Persons List</h1>
      <p class="muted">Tracked-person navigation for P1 workspace expansion. Search, unresolved badges, and recent update cues are shown from operator read contracts.</p>
      <div class="row">
        <label>
          Operator access token
          <input id="access-token" type="password" autocomplete="off" placeholder="X-Tga-Operator-Key">
        </label>
        <label>
          Search
          <input id="search-input" type="search" autocomplete="off" placeholder="Name or scope key">
        </label>
        <button class="primary" id="refresh-button" type="button">Refresh Persons</button>
        <a href="/operator">Back To Home</a>
      </div>
      <div id="state" class="state empty">Ready to load tracked persons.</div>
      <div id="counts" class="counts"></div>
    </section>
    <section class="panel">
      <h2>Tracked Persons</h2>
      <div id="persons-list">
        <p class="muted">No data loaded yet.</p>
      </div>
    </section>
  </main>

  <script>
    const tokenInput = document.getElementById("access-token");
    const searchInput = document.getElementById("search-input");
    const refreshButton = document.getElementById("refresh-button");
    const stateNode = document.getElementById("state");
    const countsNode = document.getElementById("counts");
    const personsListNode = document.getElementById("persons-list");

    function setState(kind, message) {
      stateNode.className = "state " + kind;
      stateNode.textContent = message;
    }

    function formatUtc(value) {
      if (!value) {
        return "n/a";
      }
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) {
        return "n/a";
      }
      return date.toISOString().replace("T", " ").replace(".000Z", "Z");
    }

    function readAccessToken() {
      return window.localStorage.getItem("operator_web_access_token") || "";
    }

    function writeAccessToken(token) {
      window.localStorage.setItem("operator_web_access_token", token);
      document.cookie = "tga_operator_key=" + encodeURIComponent(token) + "; path=/; SameSite=Lax";
    }

    async function operatorPostJson(path, body) {
      const token = readAccessToken();
      const headers = {
        "Content-Type": "application/json"
      };
      if (token) {
        headers["X-Tga-Operator-Key"] = token;
      }
      const response = await fetch(path, {
        method: "POST",
        headers: headers,
        body: JSON.stringify(body || {}),
        credentials: "include"
      });

      let payload = null;
      try {
        payload = await response.json();
      } catch (_error) {
        payload = null;
      }

      if (!response.ok) {
        const reason = payload && payload.failureReason ? payload.failureReason : "http_" + response.status;
        throw new Error(reason);
      }

      return payload || {};
    }

    function renderCounts(payload) {
      countsNode.innerHTML = "";
      const total = payload.totalCount || 0;
      const filtered = payload.filteredCount || 0;
      const unresolved = (payload.persons || []).filter(function(person) {
        return person.hasUnresolved;
      }).length;

      [
        "Total tracked: " + total,
        "Matched search: " + filtered,
        "With unresolved: " + unresolved
      ].forEach(function(text) {
        const tag = document.createElement("span");
        tag.textContent = text;
        countsNode.appendChild(tag);
      });
    }

    function buildWorkspaceLink(person) {
      const params = new URLSearchParams();
      params.set("trackedPersonId", person.trackedPersonId || "");
      params.set("displayName", person.displayName || "");
      params.set("scopeKey", person.scopeKey || "");
      return "/operator/person-workspace?" + params.toString();
    }

    async function applyTrackedPersonSelection(trackedPersonId) {
      const result = await operatorPostJson("/api/operator/tracked-persons/select", {
        trackedPersonId: trackedPersonId,
        requestedAtUtc: new Date().toISOString()
      });
      if (!result.accepted) {
        throw new Error(result.failureReason || "tracked_person_select_rejected");
      }
    }

    function renderPersons(payload) {
      const persons = Array.isArray(payload.persons) ? payload.persons : [];
      personsListNode.innerHTML = "";
      if (persons.length === 0) {
        personsListNode.innerHTML = "<p class='muted'>No tracked persons match this search.</p>";
        return;
      }

      persons.forEach(function(person) {
        const card = document.createElement("article");
        card.className = "person-card";

        const heading = document.createElement("h3");
        heading.textContent = person.displayName || person.trackedPersonId || "Unknown tracked person";
        card.appendChild(heading);

        const unresolved = Number(person.unresolvedCount || 0);
        const badge = document.createElement("span");
        badge.className = "badge " + (unresolved > 0 ? "unresolved" : "resolved");
        badge.textContent = unresolved > 0
          ? unresolved + " unresolved"
          : "no unresolved";
        card.appendChild(badge);

        const meta = document.createElement("div");
        meta.className = "meta";
        [
          "Scope " + (person.scopeKey || "n/a"),
          "Evidence " + Number(person.evidenceCount || 0),
          "Recent update " + formatUtc(person.recentUpdateAtUtc || person.updatedAtUtc),
          "Last unresolved " + formatUtc(person.lastUnresolvedAtUtc)
        ].forEach(function(text) {
          const tag = document.createElement("span");
          tag.textContent = text;
          meta.appendChild(tag);
        });
        card.appendChild(meta);

        const actions = document.createElement("div");
        actions.className = "actions";

        const selectButton = document.createElement("button");
        selectButton.type = "button";
        selectButton.className = "primary";
        selectButton.textContent = "Set Active Scope";
        selectButton.addEventListener("click", async function() {
          selectButton.disabled = true;
          try {
            await applyTrackedPersonSelection(person.trackedPersonId);
            setState("loading", "Active tracked person updated to " + (person.displayName || person.trackedPersonId) + ".");
          } catch (error) {
            setState("error", "Failed to update active scope: " + (error && error.message ? error.message : "unknown_error"));
          } finally {
            selectButton.disabled = false;
          }
        });
        actions.appendChild(selectButton);

        const workspaceLink = document.createElement("a");
        workspaceLink.href = buildWorkspaceLink(person);
        workspaceLink.textContent = "Open Person Workspace";
        workspaceLink.addEventListener("click", async function(event) {
          event.preventDefault();
          try {
            await applyTrackedPersonSelection(person.trackedPersonId);
            window.location.href = workspaceLink.href;
          } catch (error) {
            setState("error", "Workspace handoff failed: " + (error && error.message ? error.message : "unknown_error"));
          }
        });
        actions.appendChild(workspaceLink);

        card.appendChild(actions);
        personsListNode.appendChild(card);
      });
    }

    async function refreshPersons() {
      try {
        const token = tokenInput.value.trim();
        if (token) {
          writeAccessToken(token);
        }
        setState("loading", "Loading tracked persons...");
        const result = await operatorPostJson("/api/operator/persons/query", {
          search: searchInput.value.trim() || null,
          limit: 100
        });
        if (!result.accepted) {
          throw new Error(result.failureReason || "persons_query_rejected");
        }

        renderCounts(result);
        renderPersons(result);
        setState("loading", "Persons list loaded.");
      } catch (error) {
        setState("error", "Persons list load failed: " + (error && error.message ? error.message : "unknown_error"));
      }
    }

    tokenInput.value = readAccessToken();
    refreshButton.addEventListener("click", refreshPersons);
    searchInput.addEventListener("keydown", function(event) {
      if (event.key === "Enter") {
        refreshPersons();
      }
    });

    refreshPersons();
  </script>
</body>
</html>
""";

    private const string OperatorPersonWorkspaceShellHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Person Workspace</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f4f7fb;
      --panel: #ffffff;
      --ink: #14243c;
      --muted: #5e6e89;
      --line: #d9e1ef;
      --accent: #0d4a7f;
      --chip: #eef2fb;
      --warn: #9a1a1a;
      --ok: #0d6635;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      background: linear-gradient(180deg, #e7eefb, var(--bg) 220px);
      color: var(--ink);
    }
    main {
      max-width: 1120px;
      margin: 28px auto;
      padding: 0 16px 34px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 14px;
      box-shadow: 0 10px 22px rgba(20, 36, 60, 0.08);
      margin-bottom: 14px;
    }
    .row {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      align-items: center;
    }
    label {
      display: grid;
      gap: 6px;
      font-size: 13px;
      color: var(--muted);
    }
    input,
    button,
    a {
      border-radius: 8px;
      border: 1px solid var(--line);
      padding: 8px 10px;
      font: inherit;
      color: inherit;
      background: #fff;
      text-decoration: none;
    }
    button { cursor: pointer; background: #f7faff; }
    button.primary {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
    }
    .state {
      border-left: 4px solid var(--accent);
      background: #f8fbff;
      padding: 10px;
      border-radius: 8px;
      font-size: 14px;
    }
    .state.loading { border-left-color: var(--accent); }
    .state.success { border-left-color: var(--ok); background: #ecf8f0; }
    .state.empty { border-left-color: var(--ok); }
    .state.error { border-left-color: var(--warn); background: #fff6f6; }
    .muted { color: var(--muted); }
    .tabs {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
      gap: 8px;
    }
    .tab-btn {
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 8px 10px;
      background: #f7faff;
      color: var(--ink);
      text-align: center;
      cursor: pointer;
    }
    .tab-btn.active {
      background: #e8f2ff;
      border-color: var(--accent);
      box-shadow: inset 0 0 0 1px var(--accent);
    }
    .tab-btn.pending {
      color: var(--muted);
    }
    .tab-panel { display: none; }
    .tab-panel.active { display: block; }
    .chip-list {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-top: 8px;
    }
    .chip {
      background: var(--chip);
      border-radius: 999px;
      padding: 3px 8px;
      font-size: 12px;
      color: #2a4169;
    }
    .metrics {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
      gap: 8px;
      margin-bottom: 10px;
    }
    .metric {
      border: 1px solid var(--line);
      border-radius: 9px;
      padding: 10px;
      background: #fcfeff;
    }
    .metric strong {
      display: block;
      font-size: 20px;
    }
    .card-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
      gap: 8px;
    }
    .family-card {
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 10px;
      background: #fbfdff;
    }
    .family-card h3 {
      margin: 0 0 8px;
      font-size: 16px;
    }
    .family-card p {
      margin: 4px 0;
      font-size: 13px;
    }
    .provenance-list {
      display: grid;
      gap: 8px;
      margin-top: 10px;
    }
    .prov-item {
      border: 1px solid var(--line);
      border-radius: 8px;
      background: #fff;
      padding: 9px;
      font-size: 13px;
    }
    .snapshot-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
      gap: 8px;
      margin-bottom: 8px;
    }
    .snapshot-card {
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 10px;
      background: #fbfdff;
      font-size: 13px;
      display: grid;
      gap: 5px;
    }
    .snapshot-card strong {
      font-size: 15px;
      color: var(--ink);
    }
  </style>
</head>
<body>
  <main>
    <section class="panel">
      <h1>Person Workspace</h1>
      <p id="person-line">Loading tracked person context...</p>
      <div class="row">
        <label>
          Operator access token
          <input id="access-token" type="password" autocomplete="off" placeholder="X-Tga-Operator-Key">
        </label>
      </div>
      <div class="row">
        <button id="refresh" class="primary" type="button">Refresh Workspace</button>
        <a href="/operator/persons">Back to persons list</a>
      </div>
      <div id="state" class="state loading">Reading bounded person workspace summary...</div>
    </section>
    <section class="panel">
      <h2>Человек в фокусе</h2>
      <p class="muted">Короткий срез по человеку, вашему контексту и рабочей версии взаимодействия. Если данных мало, вывод не форсируем.</p>
      <div id="snapshot-content" class="state empty">Сводка появится после загрузки данных по человеку.</div>
    </section>
    <section class="panel">
      <h2>Sections</h2>
      <p class="muted">Person-scoped tab shell is stable; Summary, Dossier, Profile, Pair Dynamics, Timeline, Evidence, Revisions, and Resolution are live from bounded operator contracts.</p>
      <div id="tabs" class="tabs"></div>
    </section>
    <section id="tab-summary" class="panel tab-panel active">
      <h2>Summary</h2>
      <div id="summary-content" class="state empty">Summary is waiting for workspace data.</div>
    </section>
    <section id="tab-dossier" class="panel tab-panel">
      <h2>Dossier</h2>
      <div id="dossier-content" class="state empty">Dossier is waiting for workspace data.</div>
    </section>
    <section id="tab-profile" class="panel tab-panel">
      <h2>Profile</h2>
      <div id="profile-content" class="state empty">Profile is waiting for workspace data.</div>
    </section>
    <section id="tab-pair-dynamics" class="panel tab-panel">
      <h2>Pair Dynamics</h2>
      <div id="pair-dynamics-content" class="state empty">Pair Dynamics is waiting for workspace data.</div>
    </section>
    <section id="tab-timeline" class="panel tab-panel">
      <h2>Timeline</h2>
      <div id="timeline-content" class="state empty">Timeline is waiting for workspace data.</div>
    </section>
    <section id="tab-evidence" class="panel tab-panel">
      <h2>Evidence</h2>
      <div id="evidence-content" class="state empty">Evidence is waiting for workspace data.</div>
    </section>
    <section id="tab-revisions" class="panel tab-panel">
      <h2>Revisions</h2>
      <div id="revisions-content" class="state empty">Revision history is waiting for workspace data.</div>
    </section>
    <section id="tab-resolution" class="panel tab-panel">
      <h2>Resolution</h2>
      <div id="resolution-content" class="state empty">Resolution drilldown is waiting for workspace data.</div>
    </section>
    <section id="tab-placeholder" class="panel tab-panel">
      <h2 id="placeholder-title">Section</h2>
      <p id="placeholder-text" class="muted">This section is pending in later OPINT-008 slices.</p>
      <div id="placeholder-meta" class="chip-list"></div>
    </section>
  </main>
  <script>
    const tokenInput = document.getElementById("access-token");
    const refreshButton = document.getElementById("refresh");
    const personLine = document.getElementById("person-line");
    const stateNode = document.getElementById("state");
    const tabsNode = document.getElementById("tabs");
    const snapshotContentNode = document.getElementById("snapshot-content");
    const summaryContentNode = document.getElementById("summary-content");
    const dossierContentNode = document.getElementById("dossier-content");
    const profileContentNode = document.getElementById("profile-content");
    const pairDynamicsContentNode = document.getElementById("pair-dynamics-content");
    const timelineContentNode = document.getElementById("timeline-content");
    const evidenceContentNode = document.getElementById("evidence-content");
    const revisionsContentNode = document.getElementById("revisions-content");
    const resolutionContentNode = document.getElementById("resolution-content");
    const summaryPanel = document.getElementById("tab-summary");
    const dossierPanel = document.getElementById("tab-dossier");
    const profilePanel = document.getElementById("tab-profile");
    const pairDynamicsPanel = document.getElementById("tab-pair-dynamics");
    const timelinePanel = document.getElementById("tab-timeline");
    const evidencePanel = document.getElementById("tab-evidence");
    const revisionsPanel = document.getElementById("tab-revisions");
    const resolutionPanel = document.getElementById("tab-resolution");
    const placeholderPanel = document.getElementById("tab-placeholder");
    const placeholderTitleNode = document.getElementById("placeholder-title");
    const placeholderTextNode = document.getElementById("placeholder-text");
    const placeholderMetaNode = document.getElementById("placeholder-meta");

    const query = new URLSearchParams(window.location.search);
    const state = {
      trackedPersonId: query.get("trackedPersonId") || "",
      workspace: null,
      dossier: null,
      profile: null,
      pairDynamics: null,
      timeline: null,
      evidence: null,
      revisions: null,
      resolution: null,
      activeSection: "summary"
    };

    function setState(kind, message) {
      stateNode.className = "state " + kind;
      stateNode.textContent = message;
    }

    function formatUtc(value) {
      if (!value) {
        return "n/a";
      }

      const date = new Date(value);
      if (Number.isNaN(date.getTime())) {
        return value;
      }

      return date.toLocaleString();
    }

    function formatPercent(value) {
      const numeric = Number(value);
      if (Number.isNaN(numeric)) {
        return "n/a";
      }

      return Math.round(numeric * 100) + "%";
    }

    function titleize(value) {
      return (value || "").replaceAll("_", " ").replace(/\b\w/g, function(c) { return c.toUpperCase(); });
    }

    function hasSnapshotText(value) {
      if (typeof value !== "string") {
        return false;
      }

      const normalized = value.trim();
      if (normalized.length === 0) {
        return false;
      }

      const lowered = normalized.toLowerCase();
      return lowered !== "unknown"
        && lowered !== "неизвестен"
        && lowered !== "n/a"
        && lowered !== "null";
    }

    function formatSnapshotText(value, fallback) {
      return hasSnapshotText(value) ? value.trim() : fallback;
    }

    function appendSnapshotCard(parent, label, value, note) {
      const card = document.createElement("article");
      card.className = "snapshot-card";

      const labelNode = document.createElement("small");
      labelNode.textContent = label;
      card.appendChild(labelNode);

      const valueNode = document.createElement("strong");
      valueNode.textContent = value;
      card.appendChild(valueNode);

      const noteNode = document.createElement("span");
      noteNode.textContent = note;
      card.appendChild(noteNode);

      parent.appendChild(card);
    }

    function appendSnapshotChip(parent, text) {
      const chip = document.createElement("span");
      chip.className = "chip";
      chip.textContent = text;
      parent.appendChild(chip);
    }

    function hasBoundedSnapshotData(snapshot) {
      if (!snapshot) {
        return false;
      }

      const tracked = snapshot.trackedPerson || {};
      const operator = snapshot.operator || {};
      const pair = snapshot.pair || {};
      return hasSnapshotText(tracked.displayName)
        || hasSnapshotText(tracked.scopeKey)
        || tracked.trackedPersonId
        || hasSnapshotText(operator.operatorDisplay)
        || hasSnapshotText(operator.operatorSessionId)
        || hasSnapshotText(operator.surface)
        || hasSnapshotText(operator.activeMode)
        || !!pair.available;
    }

    function mapSurfaceLabel(surface) {
      const normalized = (surface || "").toString().trim().toLowerCase();
      switch (normalized) {
        case "web":
          return "веб";
        case "telegram":
          return "telegram";
        default:
          return "канал не определен";
      }
    }

    function mapModeLabel(mode) {
      const normalized = (mode || "").toString().trim().toLowerCase();
      switch (normalized) {
        case "resolution_queue":
          return "очередь решений";
        case "resolution_detail":
          return "разбор элемента";
        case "assistant":
          return "ассистент";
        case "offline_event":
          return "офлайн-события";
        case "alerts":
          return "алерты";
        default:
          return "режим не определен";
      }
    }

    function buildOperatorSessionLabel(operator) {
      if (operator.sessionExpiresAtUtc) {
        return "сессия активна до " + formatUtc(operator.sessionExpiresAtUtc);
      }
      if (operator.sessionAuthenticatedAtUtc) {
        return "сессия активна с " + formatUtc(operator.sessionAuthenticatedAtUtc);
      }
      return "время сессии не определено";
    }

    function buildPairAssessmentLabel(pair) {
      if (!pair.available) {
        return "Пока без версии";
      }
      if ((pair.contradictionCount || 0) > 0) {
        return "Есть спорные сигналы";
      }
      if (typeof pair.uncertainty === "number" && pair.uncertainty >= 0.55) {
        return "Версия требует проверки";
      }
      if (typeof pair.trust === "number"
          && typeof pair.uncertainty === "number"
          && pair.trust >= 0.70
          && pair.uncertainty <= 0.30) {
        return "Есть опора для рабочей версии";
      }
      return "Есть рабочая версия";
    }

    function buildPairAssessmentNote(pair) {
      if (!pair.available) {
        return "Отдельная версия взаимодействия пока не собрана";
      }

      const contradictions = Number(pair.contradictionCount || 0);
      if (contradictions > 0) {
        return "Есть спорные сигналы: " + contradictions;
      }
      if (typeof pair.uncertainty === "number" && pair.uncertainty >= 0.55) {
        return "Неопределенность высокая, нужна проверка";
      }
      return "Признаки противоречий: " + contradictions;
    }

    function buildResolutionDrilldownUrl(scopeItemKey) {
      const params = new URLSearchParams();
      params.set("trackedPersonId", state.trackedPersonId || "");
      if (scopeItemKey) {
        params.set("scopeItemKey", scopeItemKey);
      }

      return "/operator/resolution?" + params.toString();
    }

    function readAccessToken() {
      return window.localStorage.getItem("operator_web_access_token") || "";
    }

    function writeAccessToken(token) {
      window.localStorage.setItem("operator_web_access_token", token);
      document.cookie = "tga_operator_key=" + encodeURIComponent(token) + "; path=/; SameSite=Lax";
    }

    function resolveHeaders() {
      const token = readAccessToken();
      const headers = {
        "accept": "application/json",
        "content-type": "application/json"
      };
      if (token) {
        headers["X-Tga-Operator-Key"] = token;
      }

      return headers;
    }

    async function operatorPostJson(path, payload) {
      const response = await fetch(path, {
        method: "POST",
        credentials: "same-origin",
        headers: resolveHeaders(),
        body: JSON.stringify(payload || {})
      });

      let body = null;
      try {
        body = await response.json();
      } catch (_) {
        body = null;
      }

      if (!response.ok) {
        const reason = body && (body.failureReason || body.reason || body.message)
          ? (body.failureReason || body.reason || body.message)
          : "request_failed";
        const error = new Error(reason);
        error.status = response.status;
        throw error;
      }

      return body || {};
    }

    function renderPersonLine() {
      const trackedPerson = state.workspace && state.workspace.trackedPerson
        ? state.workspace.trackedPerson
        : null;
      if (!trackedPerson) {
        personLine.textContent = "Tracked person context is not loaded yet.";
        return;
      }

      personLine.textContent =
        "Active tracked person: " + trackedPerson.displayName + " (" + trackedPerson.trackedPersonId + ") | scope " + trackedPerson.scopeKey + ".";
    }

    function renderSnapshotBlock() {
      const summary = state.workspace && state.workspace.summary
        ? state.workspace.summary
        : null;
      const snapshot = summary && summary.snapshot ? summary.snapshot : null;
      if (!hasBoundedSnapshotData(snapshot)) {
        snapshotContentNode.className = "state empty";
        snapshotContentNode.textContent = "Сводка появится после загрузки данных по человеку.";
        return;
      }

      const tracked = snapshot.trackedPerson || {};
      const operator = snapshot.operator || {};
      const pair = snapshot.pair || {};
      const trackedName = formatSnapshotText(tracked.displayName, "Имя пока не определено");
      const operatorName = hasSnapshotText(operator.operatorDisplay)
        ? operator.operatorDisplay.trim()
        : formatSnapshotText(operator.operatorId, "Имя не задано");
      const operatorContext = mapSurfaceLabel(operator.surface) + " · " + mapModeLabel(operator.activeMode);
      const pairAssessment = buildPairAssessmentLabel(pair);
      const pairSummary = formatSnapshotText(pair.latestSummary, "пока без формулировки");
      const trackedUnresolved = tracked.unresolvedCount > 0 ? String(tracked.unresolvedCount) : "нет";

      snapshotContentNode.className = "";
      snapshotContentNode.innerHTML = "";

      const grid = document.createElement("div");
      grid.className = "snapshot-grid";
      appendSnapshotCard(
        grid,
        "Человек в фокусе",
        trackedName,
        tracked.unresolvedCount > 0 ? ("Открытые вопросы: " + tracked.unresolvedCount) : "Открытых вопросов сейчас нет");
      appendSnapshotCard(grid, "Ваш контекст", operatorName, operatorContext + " · " + buildOperatorSessionLabel(operator));
      appendSnapshotCard(grid, "Версия взаимодействия", pairAssessment, buildPairAssessmentNote(pair));
      snapshotContentNode.appendChild(grid);

      const chips = document.createElement("div");
      chips.className = "chip-list";
      appendSnapshotChip(chips, "Открытые вопросы: " + trackedUnresolved);
      appendSnapshotChip(chips, "Последнее обновление: " + formatUtc(tracked.recentUpdateAtUtc || pair.latestUpdatedAtUtc || null));
      appendSnapshotChip(chips, "Опора сигнала: " + formatPercent(pair.trust));
      appendSnapshotChip(chips, "Неопределенность: " + formatPercent(pair.uncertainty));
      appendSnapshotChip(chips, "Короткая версия: " + pairSummary);
      appendSnapshotChip(chips, "Вы вошли как: " + operatorName);
      snapshotContentNode.appendChild(chips);

      const note = document.createElement("p");
      note.className = "muted";
      note.textContent = "Это ограниченный срез из read/session-источников. Он помогает сориентироваться, но не заменяет полный разбор.";
      snapshotContentNode.appendChild(note);
    }

    function renderTabs() {
      tabsNode.innerHTML = "";
      const sections = state.workspace && Array.isArray(state.workspace.sections)
        ? state.workspace.sections
        : [];
      if (sections.length === 0) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "tab-btn active";
        button.textContent = "Summary";
        tabsNode.appendChild(button);
        return;
      }

      sections.forEach(function(section) {
        const key = section.sectionKey || "";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "tab-btn";
        if (!section.available) {
          button.classList.add("pending");
        }
        if (key === state.activeSection) {
          button.classList.add("active");
        }
        button.textContent = (section.label || titleize(key)) + " [" + titleize(section.status || "unknown") + "]";
        button.addEventListener("click", function() {
          state.activeSection = key || "summary";
          renderTabs();
          renderActiveSection();
        });
        tabsNode.appendChild(button);
      });
    }

    function renderSummarySection() {
      const summary = state.workspace && state.workspace.summary ? state.workspace.summary : null;
      if (!summary) {
        summaryContentNode.className = "state empty";
        summaryContentNode.textContent = "Summary data is unavailable.";
        return;
      }

      const families = Array.isArray(summary.families) ? summary.families : [];
      const truthLayerCounts = Array.isArray(summary.truthLayerCounts) ? summary.truthLayerCounts : [];
      const promotionCounts = Array.isArray(summary.promotionStateCounts) ? summary.promotionStateCounts : [];
      const provenance = Array.isArray(summary.provenance) ? summary.provenance : [];

      summaryContentNode.className = "";
      summaryContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(summary.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(summary.overallUncertainty) },
        { label: "Durable Objects", value: String(summary.durableObjectCount || 0) },
        { label: "Unresolved Items", value: String(summary.unresolvedCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      summaryContentNode.appendChild(metrics);

      const countChips = document.createElement("div");
      countChips.className = "chip-list";
      truthLayerCounts.forEach(function(entry) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = "Truth " + titleize(entry.key || "unknown") + ": " + (entry.count || 0);
        countChips.appendChild(chip);
      });
      promotionCounts.forEach(function(entry) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = "Promotion " + titleize(entry.key || "unknown") + ": " + (entry.count || 0);
        countChips.appendChild(chip);
      });
      if (!countChips.children.length) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = "No durable truth/promotion data yet.";
        countChips.appendChild(chip);
      }
      summaryContentNode.appendChild(countChips);

      const familyGrid = document.createElement("div");
      familyGrid.className = "card-grid";
      families.forEach(function(card) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + (card.label || titleize(card.family || "unknown")) + "</h3>" +
          "<p><strong>Trust:</strong> " + formatPercent(card.trust) + " | <strong>Uncertainty:</strong> " + formatPercent(card.uncertainty) + "</p>" +
          "<p><strong>Confidence:</strong> " + formatPercent(card.confidence) + " | <strong>Coverage:</strong> " + formatPercent(card.coverage) + "</p>" +
          "<p><strong>Freshness:</strong> " + formatPercent(card.freshness) + " | <strong>Stability:</strong> " + formatPercent(card.stability) + "</p>" +
          "<p><strong>Objects:</strong> " + (card.objectCount || 0) + " | <strong>Contradictions:</strong> " + (card.contradictionCount || 0) + "</p>" +
          "<p><strong>Provenance:</strong> evidence links " + (card.evidenceLinkCount || 0) + ", truth " + titleize(card.truthLayer || "unknown") + ", promotion " + titleize(card.promotionState || "unknown") + "</p>" +
          "<p><strong>Latest update:</strong> " + formatUtc(card.latestUpdatedAtUtc) + "</p>" +
          "<p><strong>Latest summary:</strong> " + (card.latestSummary || "n/a") + "</p>";
        familyGrid.appendChild(node);
      });
      if (!familyGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No durable summary objects are available for this person scope yet.";
        familyGrid.appendChild(empty);
      }
      summaryContentNode.appendChild(familyGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Provenance Drilldown Seeds";
      summaryContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Family:</strong> " + titleize(item.family || "unknown") + "</p>" +
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No provenance entries are available yet.";
        provList.appendChild(empty);
      }
      summaryContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(summary.generatedAtUtc) + " from bounded operator read models.";
      summaryContentNode.appendChild(updatedNote);
    }

    function renderDossierSection() {
      const dossier = state.dossier;
      if (!dossier) {
        dossierContentNode.className = "state empty";
        dossierContentNode.textContent = "Dossier data is unavailable.";
        return;
      }

      const facts = Array.isArray(dossier.facts) ? dossier.facts : [];
      const provenance = Array.isArray(dossier.provenance) ? dossier.provenance : [];

      dossierContentNode.className = "";
      dossierContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(dossier.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(dossier.overallUncertainty) },
        { label: "Durable Dossiers", value: String(dossier.durableDossierCount || 0) },
        { label: "Evidence Links", value: String(dossier.totalEvidenceLinkCount || 0) },
        { label: "Approved Fields", value: String(dossier.durableFieldCount || 0) },
        { label: "Proposal Fields", value: String(dossier.proposalOnlyFieldCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      dossierContentNode.appendChild(metrics);

      const factGrid = document.createElement("div");
      factGrid.className = "card-grid";
      facts.forEach(function(fact) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + titleize((fact.category || "unknown") + " / " + (fact.key || "unknown")) + "</h3>" +
          "<p><strong>Value:</strong> " + (fact.value || "n/a") + "</p>" +
          "<p><strong>Confidence:</strong> " + formatPercent(fact.confidence) + " | <strong>Approval:</strong> " + titleize(fact.approvalState || "unknown") + "</p>" +
          "<p><strong>Truth:</strong> " + titleize(fact.truthLayer || "unknown") + " | <strong>Promotion:</strong> " + titleize(fact.promotionState || "unknown") + "</p>" +
          "<p><strong>Evidence refs:</strong> " + (fact.evidenceRefCount || 0) + " | <strong>Revision:</strong> " + (fact.revisionNumber || 0) + "</p>" +
          "<p><strong>Drilldown seeds:</strong> metadata " + (fact.durableObjectMetadataId || "n/a") + ", dossier " + (fact.durableDossierId || "n/a") + ", model pass " + (fact.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(fact.updatedAtUtc) + "</p>";
        factGrid.appendChild(node);
      });
      if (!factGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No dossier facts are available for this person scope yet.";
        factGrid.appendChild(empty);
      }
      dossierContentNode.appendChild(factGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Dossier Provenance Seeds";
      dossierContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No dossier provenance entries are available yet.";
        provList.appendChild(empty);
      }
      dossierContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(dossier.generatedAtUtc) + " from bounded operator dossier read models.";
      dossierContentNode.appendChild(updatedNote);
    }

    function renderProfileSection() {
      const profile = state.profile;
      if (!profile) {
        profileContentNode.className = "state empty";
        profileContentNode.textContent = "Profile data is unavailable.";
        return;
      }

      const signals = Array.isArray(profile.signals) ? profile.signals : [];
      const provenance = Array.isArray(profile.provenance) ? profile.provenance : [];

      profileContentNode.className = "";
      profileContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(profile.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(profile.overallUncertainty) },
        { label: "Durable Profiles", value: String(profile.durableProfileCount || 0) },
        { label: "Inferences", value: String(profile.inferenceCount || 0) },
        { label: "Hypotheses", value: String(profile.hypothesisCount || 0) },
        { label: "Ambiguity", value: String(profile.ambiguityCount || 0) },
        { label: "Contradictions", value: String(profile.contradictionCount || 0) },
        { label: "Evidence Links", value: String(profile.totalEvidenceLinkCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      profileContentNode.appendChild(metrics);

      const signalGrid = document.createElement("div");
      signalGrid.className = "card-grid";
      signals.forEach(function(signal) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + titleize(signal.signalType || "signal") + " / " + titleize(signal.signalKey || "unknown") + "</h3>" +
          "<p><strong>Summary:</strong> " + (signal.summary || "n/a") + "</p>" +
          "<p><strong>Confidence:</strong> " + formatPercent(signal.confidence) + " | <strong>Profile scope:</strong> " + titleize(signal.profileScope || "unknown") + "</p>" +
          "<p><strong>Truth:</strong> " + titleize(signal.truthLayer || "unknown") + " | <strong>Promotion:</strong> " + titleize(signal.promotionState || "unknown") + "</p>" +
          "<p><strong>Evidence refs:</strong> " + (signal.evidenceRefCount || 0) + " | <strong>Revision:</strong> " + (signal.revisionNumber || 0) + "</p>" +
          "<p><strong>Drilldown seeds:</strong> metadata " + (signal.durableObjectMetadataId || "n/a") + ", profile " + (signal.durableProfileId || "n/a") + ", model pass " + (signal.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(signal.updatedAtUtc) + "</p>";
        signalGrid.appendChild(node);
      });
      if (!signalGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No profile signals are available for this person scope yet.";
        signalGrid.appendChild(empty);
      }
      profileContentNode.appendChild(signalGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Profile Provenance Seeds";
      profileContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No profile provenance entries are available yet.";
        provList.appendChild(empty);
      }
      profileContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(profile.generatedAtUtc) + " from bounded operator profile read models.";
      profileContentNode.appendChild(updatedNote);
    }

    function renderPairDynamicsSection() {
      const pairDynamics = state.pairDynamics;
      if (!pairDynamics) {
        pairDynamicsContentNode.className = "state empty";
        pairDynamicsContentNode.textContent = "Pair dynamics data is unavailable.";
        return;
      }

      const signals = Array.isArray(pairDynamics.signals) ? pairDynamics.signals : [];
      const provenance = Array.isArray(pairDynamics.provenance) ? pairDynamics.provenance : [];

      pairDynamicsContentNode.className = "";
      pairDynamicsContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(pairDynamics.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(pairDynamics.overallUncertainty) },
        { label: "Direction of Change", value: titleize(pairDynamics.directionOfChange || "steady") },
        { label: "Durable Pairs", value: String(pairDynamics.durablePairCount || 0) },
        { label: "Dimensions", value: String(pairDynamics.dimensionCount || 0) },
        { label: "Inferences", value: String(pairDynamics.inferenceCount || 0) },
        { label: "Hypotheses", value: String(pairDynamics.hypothesisCount || 0) },
        { label: "Conflicts", value: String(pairDynamics.conflictCount || 0) },
        { label: "Ambiguity", value: String(pairDynamics.ambiguityCount || 0) },
        { label: "Contradictions", value: String(pairDynamics.contradictionCount || 0) },
        { label: "Evidence Links", value: String(pairDynamics.totalEvidenceLinkCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      pairDynamicsContentNode.appendChild(metrics);

      const signalGrid = document.createElement("div");
      signalGrid.className = "card-grid";
      signals.forEach(function(signal) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + titleize(signal.signalType || "signal") + " / " + titleize(signal.signalKey || "unknown") + "</h3>" +
          "<p><strong>Summary:</strong> " + (signal.summary || "n/a") + "</p>" +
          "<p><strong>Value:</strong> " + (signal.signalValue || "n/a") + " | <strong>Direction:</strong> " + titleize(signal.directionOfChange || "steady") + "</p>" +
          "<p><strong>Confidence:</strong> " + formatPercent(signal.confidence) + " | <strong>Pair type:</strong> " + titleize(signal.pairDynamicsType || "unknown") + "</p>" +
          "<p><strong>Truth:</strong> " + titleize(signal.truthLayer || "unknown") + " | <strong>Promotion:</strong> " + titleize(signal.promotionState || "unknown") + "</p>" +
          "<p><strong>Evidence refs:</strong> " + (signal.evidenceRefCount || 0) + " | <strong>Revision:</strong> " + (signal.revisionNumber || 0) + "</p>" +
          "<p><strong>Drilldown seeds:</strong> metadata " + (signal.durableObjectMetadataId || "n/a") + ", pair " + (signal.durablePairDynamicsId || "n/a") + ", model pass " + (signal.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(signal.updatedAtUtc) + "</p>";
        signalGrid.appendChild(node);
      });
      if (!signalGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No pair-dynamics signals are available for this person scope yet.";
        signalGrid.appendChild(empty);
      }
      pairDynamicsContentNode.appendChild(signalGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Pair Dynamics Provenance Seeds";
      pairDynamicsContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No pair-dynamics provenance entries are available yet.";
        provList.appendChild(empty);
      }
      pairDynamicsContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(pairDynamics.generatedAtUtc) + " from bounded operator pair-dynamics read models.";
      pairDynamicsContentNode.appendChild(updatedNote);
    }

    function renderTimelineSection() {
      const timeline = state.timeline;
      if (!timeline) {
        timelineContentNode.className = "state empty";
        timelineContentNode.textContent = "Timeline data is unavailable.";
        return;
      }

      const shifts = Array.isArray(timeline.shifts) ? timeline.shifts : [];
      const provenance = Array.isArray(timeline.provenance) ? timeline.provenance : [];

      timelineContentNode.className = "";
      timelineContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(timeline.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(timeline.overallUncertainty) },
        { label: "Durable Episodes", value: String(timeline.durableEpisodeCount || 0) },
        { label: "Durable Story Arcs", value: String(timeline.durableStoryArcCount || 0) },
        { label: "Key Shifts", value: String(timeline.keyShiftCount || 0) },
        { label: "Open Arcs", value: String(timeline.openArcCount || 0) },
        { label: "Contradictions", value: String(timeline.contradictionCount || 0) },
        { label: "Evidence Links", value: String(timeline.totalEvidenceLinkCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      timelineContentNode.appendChild(metrics);

      const shiftGrid = document.createElement("div");
      shiftGrid.className = "card-grid";
      shifts.forEach(function(shift) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + titleize(shift.family || "timeline") + " / " + titleize(shift.shiftType || "unknown") + "</h3>" +
          "<p><strong>Summary:</strong> " + (shift.summary || "n/a") + "</p>" +
          "<p><strong>Window:</strong> " + formatUtc(shift.shiftStartedAtUtc) + " -> " + formatUtc(shift.shiftEndedAtUtc || shift.updatedAtUtc) + "</p>" +
          "<p><strong>Closure:</strong> " + titleize(shift.closureState || "unknown") + " | <strong>Confidence:</strong> " + formatPercent(shift.confidence) + "</p>" +
          "<p><strong>Truth:</strong> " + titleize(shift.truthLayer || "unknown") + " | <strong>Promotion:</strong> " + titleize(shift.promotionState || "unknown") + "</p>" +
          "<p><strong>Evidence refs:</strong> " + (shift.evidenceRefCount || 0) + " | <strong>Revision:</strong> " + (shift.revisionNumber || 0) + "</p>" +
          "<p><strong>Drilldown seeds:</strong> metadata " + (shift.durableObjectMetadataId || "n/a") + ", object " + (shift.durableObjectId || "n/a") + ", model pass " + (shift.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(shift.updatedAtUtc) + "</p>";
        shiftGrid.appendChild(node);
      });
      if (!shiftGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No timeline shifts are available for this person scope yet.";
        shiftGrid.appendChild(empty);
      }
      timelineContentNode.appendChild(shiftGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Timeline Provenance Seeds";
      timelineContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No timeline provenance entries are available yet.";
        provList.appendChild(empty);
      }
      timelineContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(timeline.generatedAtUtc) + " from bounded operator timeline read models.";
      timelineContentNode.appendChild(updatedNote);
    }

    function renderEvidenceSection() {
      const evidence = state.evidence;
      if (!evidence) {
        evidenceContentNode.className = "state empty";
        evidenceContentNode.textContent = "Evidence data is unavailable.";
        return;
      }

      const links = Array.isArray(evidence.links) ? evidence.links : [];
      const provenance = Array.isArray(evidence.provenance) ? evidence.provenance : [];

      evidenceContentNode.className = "";
      evidenceContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(evidence.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(evidence.overallUncertainty) },
        { label: "Durable Objects", value: String(evidence.durableObjectCount || 0) },
        { label: "Evidence Items", value: String(evidence.evidenceItemCount || 0) },
        { label: "Source Objects", value: String(evidence.sourceObjectCount || 0) },
        { label: "Evidence Links", value: String(evidence.totalEvidenceLinkCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      evidenceContentNode.appendChild(metrics);

      const linkGrid = document.createElement("div");
      linkGrid.className = "card-grid";
      links.forEach(function(link) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + titleize(link.durableObjectFamily || "durable") + " / " + (link.durableObjectKey || "n/a") + "</h3>" +
          "<p><strong>Why this durable object exists:</strong> " + (link.evidenceSummary || "Evidence-backed durable conclusion.") + "</p>" +
          "<p><strong>Link role:</strong> " + titleize(link.linkRole || "linked") + " | <strong>Evidence kind:</strong> " + titleize(link.evidenceKind || "unknown") + "</p>" +
          "<p><strong>Durable trust:</strong> " + formatPercent(link.durableConfidence) + " | <strong>Evidence confidence:</strong> " + formatPercent(link.evidenceConfidence) + "</p>" +
          "<p><strong>Durable truth:</strong> " + titleize(link.durableTruthLayer || "unknown") + " | <strong>Promotion:</strong> " + titleize(link.durablePromotionState || "unknown") + "</p>" +
          "<p><strong>Evidence truth:</strong> " + titleize(link.evidenceTruthLayer || "unknown") + " | <strong>Observed:</strong> " + formatUtc(link.observedAtUtc || link.sourceOccurredAtUtc) + "</p>" +
          "<p><strong>Drilldown seeds:</strong> metadata " + (link.durableObjectMetadataId || "n/a") + ", evidence " + (link.evidenceItemId || "n/a") + ", source " + (link.sourceObjectId || "n/a") + ", model pass " + (link.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Source:</strong> " + titleize(link.sourceKind || "unknown") + " / " + (link.sourceDisplayLabel || "n/a") + " / ref " + (link.sourceRef || "n/a") + "</p>" +
          "<p><strong>Linked:</strong> " + formatUtc(link.linkedAtUtc) + "</p>";
        linkGrid.appendChild(node);
      });
      if (!linkGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No durable-object evidence links are available for this person scope yet.";
        linkGrid.appendChild(empty);
      }
      evidenceContentNode.appendChild(linkGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Evidence Provenance Seeds";
      evidenceContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Family:</strong> " + titleize(item.family || "unknown") + "</p>" +
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No evidence provenance entries are available yet.";
        provList.appendChild(empty);
      }
      evidenceContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(evidence.generatedAtUtc) + " from bounded operator evidence/read-model contracts.";
      evidenceContentNode.appendChild(updatedNote);
    }

    function renderRevisionsSection() {
      const revisions = state.revisions;
      if (!revisions) {
        revisionsContentNode.className = "state empty";
        revisionsContentNode.textContent = "Revision history data is unavailable.";
        return;
      }

      const revisionItems = Array.isArray(revisions.revisions) ? revisions.revisions : [];
      const provenance = Array.isArray(revisions.provenance) ? revisions.provenance : [];

      revisionsContentNode.className = "";
      revisionsContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Overall Trust", value: formatPercent(revisions.overallTrust) },
        { label: "Overall Uncertainty", value: formatPercent(revisions.overallUncertainty) },
        { label: "Durable Objects", value: String(revisions.durableObjectCount || 0) },
        { label: "Revision Entries", value: String(revisions.revisionCount || 0) },
        { label: "Triggered Revisions", value: String(revisions.triggeredRevisionCount || 0) },
        { label: "Contradiction Revisions", value: String(revisions.contradictionRevisionCount || 0) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      revisionsContentNode.appendChild(metrics);

      const revisionGrid = document.createElement("div");
      revisionGrid.className = "card-grid";
      revisionItems.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "family-card";
        node.innerHTML =
          "<h3>" + titleize(item.family || "durable") + " / " + (item.objectKey || "n/a") + " / r" + (item.revisionNumber || 0) + "</h3>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>" +
          "<p><strong>Confidence:</strong> " + formatPercent(item.confidence) + " | <strong>Freshness:</strong> " + formatPercent(item.freshness) + " | <strong>Stability:</strong> " + formatPercent(item.stability) + "</p>" +
          "<p><strong>Truth:</strong> " + titleize(item.truthLayer || "unknown") + " | <strong>Promotion:</strong> " + titleize(item.promotionState || "unknown") + "</p>" +
          "<p><strong>Trigger:</strong> " + titleize(item.triggerKind || "none") + " / " + (item.triggerRef || "n/a") + " | <strong>Pass:</strong> " + titleize(item.passFamily || "unknown") + " / " + titleize(item.runKind || "unknown") + "</p>" +
          "<p><strong>Result:</strong> " + titleize(item.resultStatus || "unknown") + " | <strong>Target:</strong> " + titleize(item.targetType || "unknown") + " / " + (item.targetRef || "n/a") + "</p>" +
          "<p><strong>Contradictions:</strong> " + (item.contradictionCount || 0) + " | <strong>Evidence refs:</strong> " + (item.evidenceRefCount || 0) + "</p>" +
          "<p><strong>Drilldown seeds:</strong> metadata " + (item.durableObjectMetadataId || "n/a") + ", object " + (item.durableObjectId || "n/a") + ", revision hash " + (item.revisionHash || "n/a") + ", model pass " + (item.modelPassRunId || "n/a") + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.createdAtUtc) + "</p>";
        revisionGrid.appendChild(node);
      });
      if (!revisionGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No durable revision entries are available for this person scope yet.";
        revisionGrid.appendChild(empty);
      }
      revisionsContentNode.appendChild(revisionGrid);

      const provHeader = document.createElement("h3");
      provHeader.textContent = "Revision Provenance Seeds";
      revisionsContentNode.appendChild(provHeader);

      const provList = document.createElement("div");
      provList.className = "provenance-list";
      provenance.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "prov-item";
        node.innerHTML =
          "<p><strong>Family:</strong> " + titleize(item.family || "unknown") + "</p>" +
          "<p><strong>Object key:</strong> " + (item.objectKey || "n/a") + "</p>" +
          "<p><strong>Durable metadata:</strong> " + (item.durableObjectMetadataId || "n/a") + "</p>" +
          "<p><strong>Model pass run:</strong> " + (item.lastModelPassRunId || "n/a") + "</p>" +
          "<p><strong>Evidence links:</strong> " + (item.evidenceLinkCount || 0) + "</p>" +
          "<p><strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>";
        provList.appendChild(node);
      });
      if (!provList.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No revision provenance entries are available yet.";
        provList.appendChild(empty);
      }
      revisionsContentNode.appendChild(provList);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(revisions.generatedAtUtc) + " from bounded durable revision history contracts.";
      revisionsContentNode.appendChild(updatedNote);
    }

    function renderResolutionSection() {
      const resolution = state.resolution;
      if (!resolution) {
        resolutionContentNode.className = "state empty";
        resolutionContentNode.textContent = "Resolution drilldown data is unavailable.";
        return;
      }

      const items = Array.isArray(resolution.items) ? resolution.items : [];
      const statusCounts = Array.isArray(resolution.statusCounts) ? resolution.statusCounts : [];
      const priorityCounts = Array.isArray(resolution.priorityCounts) ? resolution.priorityCounts : [];

      resolutionContentNode.className = "";
      resolutionContentNode.innerHTML = "";

      const metrics = document.createElement("div");
      metrics.className = "metrics";
      [
        { label: "Unresolved", value: String(resolution.unresolvedCount || 0) },
        { label: "Resolved", value: String(resolution.resolvedCount || 0) },
        { label: "Resolved Actions", value: String(resolution.resolvedActionCount || 0) },
        { label: "Last Resolved At", value: formatUtc(resolution.lastResolvedAtUtc) }
      ].forEach(function(metric) {
        const card = document.createElement("article");
        card.className = "metric";
        card.innerHTML = "<small>" + metric.label + "</small><strong>" + metric.value + "</strong>";
        metrics.appendChild(card);
      });
      resolutionContentNode.appendChild(metrics);

      const chips = document.createElement("div");
      chips.className = "chip-list";
      statusCounts.forEach(function(entry) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = "Status " + titleize(entry.key || "unknown") + ": " + (entry.count || 0);
        chips.appendChild(chip);
      });
      priorityCounts.forEach(function(entry) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = "Priority " + titleize(entry.key || "unknown") + ": " + (entry.count || 0);
        chips.appendChild(chip);
      });
      if (!chips.children.length) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = "No resolution status counts are available yet.";
        chips.appendChild(chip);
      }
      resolutionContentNode.appendChild(chips);

      const itemGrid = document.createElement("div");
      itemGrid.className = "card-grid";
      items.forEach(function(item) {
        const node = document.createElement("article");
        node.className = "family-card";
        const trustLabel = formatItemTrustLabel(item);
        const runtimeEvidenceCaveat = formatRuntimeEvidenceCaveat(item);
        node.innerHTML =
          "<h3>" + (item.title || item.scopeItemKey || "Untitled item") + "</h3>" +
          "<p><strong>Status:</strong> " + titleize(item.status || "unknown") + " | <strong>Priority:</strong> " + titleize(item.priority || "unknown") + "</p>" +
          "<p><strong>Type:</strong> " + titleize(item.itemType || "unknown") + " | <strong>" + trustLabel + ":</strong> " + formatItemTrustValue(item) + "</p>" +
          "<p><strong>Summary:</strong> " + (item.summary || "n/a") + "</p>" +
          "<p><strong>Why it matters:</strong> " + (item.whyItMatters || "n/a") + "</p>" +
          "<p><strong>Affected:</strong> " + (item.affectedFamily || "n/a") + " / " + (item.affectedObjectRef || "n/a") + "</p>" +
          "<p><strong>Evidence count:</strong> " + (item.evidenceCount || 0) + " | <strong>Recommended action:</strong> " + titleize(item.recommendedNextAction || "none") + "</p>" +
          "<p><strong>Drilldown seed:</strong> scope item " + (item.scopeItemKey || "n/a") + " | <strong>Updated:</strong> " + formatUtc(item.updatedAtUtc) + "</p>" +
          (runtimeEvidenceCaveat ? "<p class='muted'>" + runtimeEvidenceCaveat + "</p>" : "");

        const actionRow = document.createElement("div");
        actionRow.className = "row";
        const openLink = document.createElement("a");
        openLink.href = buildResolutionDrilldownUrl(item.scopeItemKey || "");
        openLink.textContent = "Open Resolution Detail";
        actionRow.appendChild(openLink);
        node.appendChild(actionRow);
        itemGrid.appendChild(node);
      });
      if (!itemGrid.children.length) {
        const empty = document.createElement("div");
        empty.className = "state empty";
        empty.textContent = "No unresolved resolution items are active for this person scope.";
        itemGrid.appendChild(empty);
      }
      resolutionContentNode.appendChild(itemGrid);

      const updatedNote = document.createElement("p");
      updatedNote.className = "muted";
      updatedNote.textContent = "Generated at " + formatUtc(resolution.generatedAtUtc) + " from bounded resolution queue/action contracts.";
      resolutionContentNode.appendChild(updatedNote);
    }

    function renderPlaceholderSection() {
      const sections = state.workspace && Array.isArray(state.workspace.sections)
        ? state.workspace.sections
        : [];
      const active = sections.find(function(section) {
        return section.sectionKey === state.activeSection;
      });
      const label = active && active.label ? active.label : titleize(state.activeSection);
      placeholderTitleNode.textContent = label;
      placeholderTextNode.textContent = label + " is pending in later OPINT-008 slices.";
      placeholderMetaNode.innerHTML = "";

      [
        "Section: " + (active && active.sectionKey ? active.sectionKey : state.activeSection),
        "Status: " + titleize(active && active.status ? active.status : "pending"),
        "Availability: " + ((active && active.available) ? "ready" : "pending")
      ].forEach(function(text) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = text;
        placeholderMetaNode.appendChild(chip);
      });
    }

    async function ensureDossierLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.dossier) {
        return;
      }

      dossierContentNode.className = "state loading";
      dossierContentNode.textContent = "Loading bounded dossier view...";
      const result = await operatorPostJson("/api/operator/person-workspace/dossier/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.dossier = result.dossier || null;
      renderDossierSection();
    }

    async function ensureProfileLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.profile) {
        return;
      }

      profileContentNode.className = "state loading";
      profileContentNode.textContent = "Loading bounded profile view...";
      const result = await operatorPostJson("/api/operator/person-workspace/profile/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.profile = result.profile || null;
      renderProfileSection();
    }

    async function ensurePairDynamicsLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.pairDynamics) {
        return;
      }

      pairDynamicsContentNode.className = "state loading";
      pairDynamicsContentNode.textContent = "Loading bounded pair dynamics view...";
      const result = await operatorPostJson("/api/operator/person-workspace/pair-dynamics/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.pairDynamics = result.pairDynamics || null;
      renderPairDynamicsSection();
    }

    async function ensureTimelineLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.timeline) {
        return;
      }

      timelineContentNode.className = "state loading";
      timelineContentNode.textContent = "Loading bounded timeline view...";
      const result = await operatorPostJson("/api/operator/person-workspace/timeline/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.timeline = result.timeline || null;
      renderTimelineSection();
    }

    async function ensureEvidenceLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.evidence) {
        return;
      }

      evidenceContentNode.className = "state loading";
      evidenceContentNode.textContent = "Loading bounded evidence view...";
      const result = await operatorPostJson("/api/operator/person-workspace/evidence/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.evidence = result.evidence || null;
      renderEvidenceSection();
    }

    async function ensureRevisionsLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.revisions) {
        return;
      }

      revisionsContentNode.className = "state loading";
      revisionsContentNode.textContent = "Loading bounded revision history view...";
      const result = await operatorPostJson("/api/operator/person-workspace/revisions/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.revisions = result.revisions || null;
      renderRevisionsSection();
    }

    async function ensureResolutionLoaded(forceReload) {
      if (!state.trackedPersonId) {
        throw new Error("trackedPersonId query parameter is required.");
      }
      if (!forceReload && state.resolution) {
        return;
      }

      resolutionContentNode.className = "state loading";
      resolutionContentNode.textContent = "Loading bounded resolution drilldown view...";
      const result = await operatorPostJson("/api/operator/person-workspace/resolution/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.resolution = result.resolution || null;
      renderResolutionSection();
    }

    function renderActiveSection() {
      const showSummary = state.activeSection === "summary";
      const showDossier = state.activeSection === "dossier";
      const showProfile = state.activeSection === "profile";
      const showPairDynamics = state.activeSection === "pair_dynamics";
      const showTimeline = state.activeSection === "timeline";
      const showEvidence = state.activeSection === "evidence";
      const showRevisions = state.activeSection === "revisions";
      const showResolution = state.activeSection === "resolution";
      summaryPanel.classList.toggle("active", showSummary);
      dossierPanel.classList.toggle("active", showDossier);
      profilePanel.classList.toggle("active", showProfile);
      pairDynamicsPanel.classList.toggle("active", showPairDynamics);
      timelinePanel.classList.toggle("active", showTimeline);
      evidencePanel.classList.toggle("active", showEvidence);
      revisionsPanel.classList.toggle("active", showRevisions);
      resolutionPanel.classList.toggle("active", showResolution);
      placeholderPanel.classList.toggle("active", !showSummary && !showDossier && !showProfile && !showPairDynamics && !showTimeline && !showEvidence && !showRevisions && !showResolution);
      if (showSummary) {
        renderSummarySection();
      } else if (showDossier) {
        ensureDossierLoaded(false).catch(function(error) {
          dossierContentNode.className = "state error";
          dossierContentNode.textContent = "Dossier load failed: " + (error.message || "unknown_error");
        });
      } else if (showProfile) {
        ensureProfileLoaded(false).catch(function(error) {
          profileContentNode.className = "state error";
          profileContentNode.textContent = "Profile load failed: " + (error.message || "unknown_error");
        });
      } else if (showPairDynamics) {
        ensurePairDynamicsLoaded(false).catch(function(error) {
          pairDynamicsContentNode.className = "state error";
          pairDynamicsContentNode.textContent = "Pair dynamics load failed: " + (error.message || "unknown_error");
        });
      } else if (showTimeline) {
        ensureTimelineLoaded(false).catch(function(error) {
          timelineContentNode.className = "state error";
          timelineContentNode.textContent = "Timeline load failed: " + (error.message || "unknown_error");
        });
      } else if (showEvidence) {
        ensureEvidenceLoaded(false).catch(function(error) {
          evidenceContentNode.className = "state error";
          evidenceContentNode.textContent = "Evidence load failed: " + (error.message || "unknown_error");
        });
      } else if (showRevisions) {
        ensureRevisionsLoaded(false).catch(function(error) {
          revisionsContentNode.className = "state error";
          revisionsContentNode.textContent = "Revision history load failed: " + (error.message || "unknown_error");
        });
      } else if (showResolution) {
        ensureResolutionLoaded(false).catch(function(error) {
          resolutionContentNode.className = "state error";
          resolutionContentNode.textContent = "Resolution drilldown load failed: " + (error.message || "unknown_error");
        });
      } else {
        renderPlaceholderSection();
      }
    }

    async function loadWorkspaceSummary() {
      if (!state.trackedPersonId) {
        setState("error", "trackedPersonId query parameter is required.");
        return;
      }

      setState("loading", "Loading bounded workspace summary...");
      const result = await operatorPostJson("/api/operator/person-workspace/summary/query", {
        trackedPersonId: state.trackedPersonId
      });
      state.workspace = result.workspace || null;
      state.dossier = null;
      state.profile = null;
      state.pairDynamics = null;
      state.timeline = null;
      state.evidence = null;
      state.revisions = null;
      state.resolution = null;
      renderPersonLine();
      renderSnapshotBlock();
      renderTabs();
      renderActiveSection();
      setState("empty", "Workspace summary loaded from durable/read-model contracts.");
    }

    tokenInput.value = readAccessToken();
    tokenInput.addEventListener("change", function() {
      writeAccessToken(tokenInput.value.trim());
    });
    refreshButton.addEventListener("click", async function() {
      try {
        writeAccessToken(tokenInput.value.trim());
        await loadWorkspaceSummary();
      } catch (error) {
        setState("error", "Workspace summary request failed: " + (error.message || "unknown_error"));
      }
    });

    (async function init() {
      writeAccessToken(tokenInput.value.trim());
      try {
        await loadWorkspaceSummary();
      } catch (error) {
        setState("error", "Workspace summary request failed: " + (error.message || "unknown_error"));
      }
    })();
  </script>
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
      --chip: #eef2fb;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      color: var(--ink);
      background: linear-gradient(180deg, #e7eefb, var(--bg) 240px);
    }
    main {
      max-width: 1160px;
      margin: 28px auto;
      padding: 0 16px;
      display: grid;
      grid-template-columns: 300px 1fr;
      gap: 14px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 14px;
      box-shadow: 0 10px 22px rgba(20, 36, 60, 0.08);
    }
    .panel h1,
    .panel h2,
    .panel h3 {
      margin: 0 0 10px;
    }
    .stack {
      display: grid;
      gap: 10px;
    }
    label {
      display: grid;
      gap: 6px;
      font-size: 13px;
      color: var(--muted);
    }
    select,
    input,
    textarea,
    button,
    a {
      border-radius: 8px;
      border: 1px solid var(--line);
      padding: 8px 10px;
      font: inherit;
      color: inherit;
      background: #fff;
      text-decoration: none;
    }
    button,
    select,
    input {
      width: 100%;
    }
    button {
      cursor: pointer;
      background: #f7faff;
    }
    button.primary {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
    }
    .row {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      align-items: center;
    }
    .row > * {
      flex: 1 1 150px;
    }
    .chip-list {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .chip {
      background: var(--chip);
      border-radius: 999px;
      padding: 3px 8px;
      font-size: 12px;
      color: #2a4169;
    }
    .state {
      border-left: 4px solid var(--accent);
      background: #f8fbff;
      padding: 10px;
      border-radius: 8px;
      font-size: 14px;
    }
    .state.loading { border-left-color: var(--accent); }
    .state.success { border-left-color: var(--ok); background: #ecf8f0; }
    .state.empty { border-left-color: var(--ok); }
    .state.error { border-left-color: var(--warn); background: #fff6f6; }
    .muted { color: var(--muted); }
    .queue-list {
      display: grid;
      gap: 8px;
      max-height: 68vh;
      overflow: auto;
      padding-right: 2px;
    }
    .item {
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 11px;
      background: #fcfdff;
      cursor: pointer;
      transition: border-color 0.15s ease, box-shadow 0.15s ease, background 0.15s ease;
    }
    .item:hover {
      border-color: #b6c8e4;
      background: #f9fbff;
    }
    .item.active {
      border-color: var(--accent);
      box-shadow: inset 0 0 0 1px var(--accent);
      background: #f2f8ff;
    }
    .item-top {
      display: flex;
      justify-content: space-between;
      gap: 8px;
      align-items: baseline;
      margin-bottom: 6px;
    }
    .item h3 {
      margin: 0;
      font-size: 16px;
    }
    .item p {
      margin: 4px 0;
      font-size: 14px;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-top: 8px;
      font-size: 12px;
    }
    .meta span {
      background: var(--chip);
      padding: 3px 8px;
      border-radius: 999px;
    }
    .priority-critical { color: #8d0e0e; }
    .priority-high { color: #9d400f; }
    .priority-medium { color: #27538f; }
    .priority-low { color: #216333; }
    .filter-grid {
      display: grid;
      grid-template-columns: 1fr;
      gap: 8px;
    }
    .checklist {
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 8px;
      background: #fafcff;
    }
    .checklist strong {
      display: block;
      margin-bottom: 6px;
      font-size: 13px;
    }
    .checklist label {
      display: flex;
      align-items: center;
      gap: 8px;
      color: var(--ink);
      font-size: 13px;
      margin: 4px 0;
    }
    .checklist input[type="checkbox"] {
      width: auto;
      margin: 0;
    }
    .inline-note {
      font-size: 12px;
      color: var(--muted);
    }
    .workspace {
      display: grid;
      grid-template-columns: 1.2fr 1fr;
      gap: 12px;
      align-items: start;
    }
    .detail-panel {
      border: 1px solid var(--line);
      border-radius: 10px;
      background: #fbfdff;
      padding: 10px;
      display: grid;
      gap: 10px;
    }
    .detail-panel h3 {
      margin: 0;
    }
    .detail-content {
      min-height: 120px;
    }
    .detail-block {
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 10px;
      background: #fff;
    }
    .detail-block h4 {
      margin: 0 0 8px;
      font-size: 14px;
    }
    .detail-block p {
      margin: 6px 0;
      font-size: 14px;
    }
    .detail-list {
      margin: 0;
      padding-left: 18px;
    }
    .detail-list li {
      margin: 6px 0;
      font-size: 14px;
    }
    .action-panel {
      display: grid;
      gap: 8px;
    }
    .action-grid {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 8px;
    }
    .action-grid button {
      margin: 0;
    }
    .action-grid .approve {
      border-color: #7ec89f;
      background: #e9fff2;
    }
    .action-grid .reject {
      border-color: #d9a0a0;
      background: #fff1f1;
    }
    .action-grid .defer {
      border-color: #d7ca9f;
      background: #fff9ea;
    }
    .action-grid .clarify {
      border-color: #9fb7d7;
      background: #edf4ff;
    }
    .action-feedback {
      margin-top: 2px;
    }
    .evidence-inline {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
      align-items: center;
      margin-top: 8px;
    }
    .evidence-inline .chip {
      background: #e8f0ff;
      color: #1f3f72;
    }
    .drawer-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(15, 28, 48, 0.32);
      opacity: 0;
      visibility: hidden;
      transition: opacity 0.18s ease;
      z-index: 20;
    }
    .drawer-backdrop.open {
      opacity: 1;
      visibility: visible;
    }
    .drawer {
      position: fixed;
      top: 0;
      right: 0;
      width: min(560px, 96vw);
      height: 100vh;
      background: #f8fbff;
      border-left: 1px solid var(--line);
      box-shadow: -14px 0 28px rgba(20, 36, 60, 0.16);
      display: grid;
      grid-template-rows: auto auto 1fr auto;
      gap: 10px;
      padding: 14px;
      transform: translateX(100%);
      transition: transform 0.2s ease;
      z-index: 21;
    }
    .drawer.open {
      transform: translateX(0);
    }
    .drawer-head {
      display: flex;
      gap: 8px;
      align-items: center;
      justify-content: space-between;
    }
    .drawer-head h3 {
      margin: 0;
    }
    .drawer-tools {
      display: grid;
      grid-template-columns: repeat(3, minmax(110px, 1fr));
      gap: 8px;
      align-items: end;
    }
    .drawer-list {
      border: 1px solid var(--line);
      border-radius: 10px;
      background: #fff;
      padding: 8px;
      overflow: auto;
      max-height: 34vh;
      display: grid;
      gap: 8px;
    }
    .evidence-card {
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 8px;
      cursor: pointer;
      background: #fcfeff;
    }
    .evidence-card.active {
      border-color: var(--accent);
      box-shadow: inset 0 0 0 1px var(--accent);
      background: #f1f8ff;
    }
    .evidence-card p {
      margin: 4px 0;
      font-size: 13px;
    }
    .evidence-focus {
      border: 1px solid var(--line);
      border-radius: 10px;
      background: #fff;
      padding: 10px;
      display: grid;
      gap: 8px;
    }
    .evidence-focus p {
      margin: 4px 0;
      font-size: 14px;
    }
    .drawer-footer {
      display: flex;
      gap: 8px;
    }
    .drawer-footer button {
      flex: 1 1 0;
    }
    @media (max-width: 920px) {
      main {
        grid-template-columns: 1fr;
      }
      .queue-list {
        max-height: none;
      }
      .workspace {
        grid-template-columns: 1fr;
      }
    }
  </style>
</head>
<body>
  <main>
    <section class="panel stack">
      <div>
        <h1>Resolution Queue</h1>
        <p class="muted">Priority-first queue for bounded operator resolution contracts.</p>
      </div>

      <div class="stack">
        <label>
          Operator access token
          <input id="access-token" type="password" autocomplete="off" placeholder="X-Tga-Operator-Key">
        </label>
        <p class="inline-note">Stored in local browser storage for this route only.</p>
      </div>

      <div class="stack">
        <label>
          Tracked person
          <select id="tracked-person"></select>
        </label>
        <div class="row">
          <button id="refresh-people" type="button">Refresh scope</button>
          <button id="apply-person" type="button">Apply scope</button>
        </div>
      </div>

      <div class="stack">
        <label>
          Sort
          <select id="sort-by">
            <option value="priority">Priority then activity</option>
            <option value="updated_at">Activity recency</option>
          </select>
        </label>
        <label>
          Direction
          <select id="sort-direction">
            <option value="desc">Descending</option>
            <option value="asc">Ascending</option>
          </select>
        </label>
      </div>

      <div class="filter-grid">
        <div class="checklist" id="priority-filters"></div>
        <div class="checklist" id="status-filters"></div>
        <div class="checklist" id="type-filters"></div>
      </div>

      <div class="row">
        <button id="refresh-queue" class="primary" type="button">Refresh queue</button>
        <a href="/operator">Back to home</a>
      </div>

      <div id="state" class="state loading">Loading tracked person scope...</div>
      <div id="handoff-state" class="state empty">Handoff: none.</div>
    </section>

    <section class="panel stack">
      <h2>Queue + Detail</h2>
      <div class="workspace">
        <div class="stack">
          <div id="counts" class="chip-list"></div>
          <div id="queue-list" class="queue-list"></div>
          <p class="muted">This route uses <code>/api/operator/resolution/queue/query</code> and <code>/api/operator/resolution/detail/query</code> with no legacy Stage6 queue/case pages.</p>
        </div>
        <aside class="detail-panel">
          <h3>Selected Item Detail</h3>
          <div id="detail-state" class="state empty">Select a queue item to inspect detail.</div>
          <div id="detail-content" class="detail-content"></div>
        </aside>
      </div>
    </section>
  </main>

  <div id="evidence-backdrop" class="drawer-backdrop" aria-hidden="true"></div>
  <aside id="evidence-drawer" class="drawer" aria-hidden="true">
    <div class="drawer-head">
      <h3>Evidence Panel</h3>
      <button id="evidence-close" type="button">Close</button>
    </div>
    <div class="drawer-tools">
      <label>
        Sort
        <select id="evidence-sort-by">
          <option value="observed_at">Observed at</option>
          <option value="trust_factor">Trust factor</option>
        </select>
      </label>
      <label>
        Direction
        <select id="evidence-sort-direction">
          <option value="desc">Descending</option>
          <option value="asc">Ascending</option>
        </select>
      </label>
      <label>
        Limit
        <select id="evidence-limit">
          <option value="5">5</option>
          <option value="8" selected>8</option>
          <option value="12">12</option>
        </select>
      </label>
    </div>
    <div id="evidence-state" class="state empty">Open evidence from a selected detail item.</div>
    <div id="evidence-list" class="drawer-list"></div>
    <section id="evidence-focus" class="evidence-focus"></section>
    <div class="drawer-footer">
      <button id="evidence-prev" type="button">Previous</button>
      <button id="evidence-next" type="button">Next</button>
    </div>
  </aside>

  <div id="clarification-backdrop" class="drawer-backdrop" aria-hidden="true"></div>
  <aside id="clarification-drawer" class="drawer" aria-hidden="true">
    <div class="drawer-head">
      <h3>Clarification Panel</h3>
      <button id="clarification-close" type="button">Close</button>
    </div>
    <div class="stack">
      <p id="clarification-context" class="muted">Select a queue item detail before opening clarification.</p>
      <label>
        Clarification summary
        <input id="clarification-summary" type="text" maxlength="220" placeholder="Short summary for this follow-up">
      </label>
      <label>
        Explanation (required)
        <textarea id="clarification-explanation" rows="3" maxlength="1200" placeholder="Why clarification is needed for this item."></textarea>
      </label>
      <label>
        Follow-up question (required)
        <input id="clarification-question" type="text" maxlength="240" placeholder="What should be clarified next?">
      </label>
      <label>
        Follow-up answer (required)
        <textarea id="clarification-answer" rows="3" maxlength="1200" placeholder="Structured follow-up answer for recompute."></textarea>
      </label>
      <label>
        Answer kind
        <select id="clarification-answer-kind">
          <option value="free_text" selected>Free text</option>
          <option value="boolean">Boolean</option>
          <option value="enum">Enum</option>
          <option value="numeric">Numeric</option>
          <option value="date">Date</option>
        </select>
      </label>
      <label>
        Follow-up notes
        <textarea id="clarification-notes" rows="2" maxlength="400" placeholder="Optional bounded notes"></textarea>
      </label>
    </div>
    <div id="clarification-state" class="state empty">Clarify keeps queue/detail context and submits structured follow-up payload.</div>
    <div class="drawer-footer">
      <button id="clarification-cancel" type="button">Cancel</button>
      <button id="clarification-submit" class="primary" type="button">Submit Clarify</button>
    </div>
  </aside>

  <script>
    const stateNode = document.getElementById("state");
    const handoffStateNode = document.getElementById("handoff-state");
    const queueNode = document.getElementById("queue-list");
    const countsNode = document.getElementById("counts");
    const detailStateNode = document.getElementById("detail-state");
    const detailContentNode = document.getElementById("detail-content");
    const tokenInput = document.getElementById("access-token");
    const trackedPersonSelect = document.getElementById("tracked-person");
    const refreshPeopleButton = document.getElementById("refresh-people");
    const applyPersonButton = document.getElementById("apply-person");
    const refreshQueueButton = document.getElementById("refresh-queue");
    const sortBySelect = document.getElementById("sort-by");
    const sortDirectionSelect = document.getElementById("sort-direction");
    const evidenceBackdropNode = document.getElementById("evidence-backdrop");
    const evidenceDrawerNode = document.getElementById("evidence-drawer");
    const evidenceCloseButton = document.getElementById("evidence-close");
    const evidenceStateNode = document.getElementById("evidence-state");
    const evidenceListNode = document.getElementById("evidence-list");
    const evidenceFocusNode = document.getElementById("evidence-focus");
    const evidencePrevButton = document.getElementById("evidence-prev");
    const evidenceNextButton = document.getElementById("evidence-next");
    const evidenceSortBySelect = document.getElementById("evidence-sort-by");
    const evidenceSortDirectionSelect = document.getElementById("evidence-sort-direction");
    const evidenceLimitSelect = document.getElementById("evidence-limit");
    const clarificationBackdropNode = document.getElementById("clarification-backdrop");
    const clarificationDrawerNode = document.getElementById("clarification-drawer");
    const clarificationCloseButton = document.getElementById("clarification-close");
    const clarificationCancelButton = document.getElementById("clarification-cancel");
    const clarificationSubmitButton = document.getElementById("clarification-submit");
    const clarificationStateNode = document.getElementById("clarification-state");
    const clarificationContextNode = document.getElementById("clarification-context");
    const clarificationSummaryInput = document.getElementById("clarification-summary");
    const clarificationExplanationInput = document.getElementById("clarification-explanation");
    const clarificationQuestionInput = document.getElementById("clarification-question");
    const clarificationAnswerInput = document.getElementById("clarification-answer");
    const clarificationAnswerKindSelect = document.getElementById("clarification-answer-kind");
    const clarificationNotesInput = document.getElementById("clarification-notes");

    const priorityValues = ["critical", "high", "medium", "low"];
    const statusValues = ["open", "blocked", "queued", "running", "attention_required", "degraded"];
    const typeValues = ["clarification", "review", "contradiction", "missing_data", "blocked_branch"];
    const query = new URLSearchParams(window.location.search);
    const bootTrackedPersonId = query.get("trackedPersonId") || query.get("tracked_person_id") || "";
    const bootScopeItemKey = query.get("scopeItemKey") || query.get("scope_item_key") || "";
    const bootOperatorSessionId = query.get("operatorSessionId") || query.get("operator_session_id") || "";
    const bootActiveMode = query.get("activeMode") || query.get("active_mode") || "";
    const bootHandoffToken = query.get("handoffToken") || query.get("handoff_token") || "";

    const state = {
      trackedPersons: [],
      activeTrackedPersonId: bootTrackedPersonId || null,
      queue: null,
      selectedScopeItemKey: bootScopeItemKey || null,
      selectedDetailItem: null,
      activeConflictSession: null,
      evidenceIndex: -1,
      evidenceDrawerOpen: false,
      clarificationDrawerOpen: false,
      actionSubmitting: false,
      clarificationSubmitting: false,
      toggleDetailActionButtons: null,
      lastActionFeedback: null,
      bootTrackedPersonId: bootTrackedPersonId || null,
      bootTrackedPersonApplied: false,
      bootScopeItemKey: bootScopeItemKey || null,
      handoff: {
        trackedPersonId: bootTrackedPersonId || null,
        scopeItemKey: bootScopeItemKey || null,
        operatorSessionId: bootOperatorSessionId || null,
        activeMode: bootActiveMode || "resolution_detail",
        handoffToken: bootHandoffToken || null,
        consumed: false
      }
    };

    function setState(kind, message) {
      stateNode.className = "state " + kind;
      stateNode.textContent = message;
    }

    function setDetailState(kind, message) {
      detailStateNode.className = "state " + kind;
      detailStateNode.textContent = message;
    }

    function setHandoffState(kind, message) {
      if (!handoffStateNode) {
        return;
      }

      handoffStateNode.className = "state " + kind;
      handoffStateNode.textContent = message;
    }

    function clearConsumedHandoffQuery() {
      if (!window.history || typeof window.history.replaceState !== "function") {
        return;
      }

      const sanitized = new URL(window.location.href);
      sanitized.searchParams.delete("trackedPersonId");
      sanitized.searchParams.delete("tracked_person_id");
      sanitized.searchParams.delete("scopeItemKey");
      sanitized.searchParams.delete("scope_item_key");
      sanitized.searchParams.delete("operatorSessionId");
      sanitized.searchParams.delete("operator_session_id");
      sanitized.searchParams.delete("activeMode");
      sanitized.searchParams.delete("active_mode");
      sanitized.searchParams.delete("handoffToken");
      sanitized.searchParams.delete("handoff_token");
      sanitized.searchParams.delete("target_api");
      window.history.replaceState({}, "", sanitized.toString());
    }

    function titleize(value) {
      return value.replaceAll("_", " ").replace(/\b\w/g, function(c) { return c.toUpperCase(); });
    }

    function describeFailureReason(reason) {
      const normalized = (reason || "unknown_error").toLowerCase();
      switch (normalized) {
        case "auth_denied":
          return "Access token is invalid. Enter a valid operator token and retry.";
        case "session_expired":
          return "Session expired. Refresh and authenticate again.";
        case "handoff_token_invalid":
          return "Open in Web handoff is invalid or expired. Return to Telegram and open the link again.";
        case "session_active_tracked_person_mismatch":
          return "Handoff tracked person differs from current session scope. Use Apply scope explicitly.";
        case "session_scope_item_mismatch":
          return "Handoff item differs from current session scope. Reopen from Telegram or change scope manually.";
        case "tracked_person_not_found_or_inactive":
          return "Tracked person is unavailable in the current bounded scope.";
        case "scope_item_not_found":
          return "Selected item is unavailable after refresh.";
        default:
          return reason || "unknown_error";
      }
    }

    function formatUtc(value) {
      if (!value) {
        return "n/a";
      }

      const date = new Date(value);
      if (Number.isNaN(date.getTime())) {
        return value;
      }

      return date.toLocaleString();
    }

    function formatPercent(value) {
      const numeric = Number(value);
      if (Number.isNaN(numeric)) {
        return "n/a";
      }

      return Math.round(numeric * 100) + "%";
    }

    function formatDecisionLinkage(linkage) {
      if (!linkage || !linkage.linkTarget) {
        return "Связь с выводом не определена; evidence остается контекстным сигналом.";
      }

      const stance = (linkage.stance || "").toLowerCase();
      let verb = "оставляет неопределенность по";
      if (stance === "supports") {
        verb = "может поддерживать";
      } else if (stance === "challenges") {
        verb = "может оспаривать";
      }

      const linkType = (linkage.linkType || "").toLowerCase();
      const targetPrefix = linkType === "review_question"
        ? "вопросу"
        : linkType === "decision_unit"
          ? "решению"
          : "критерию";
      let text = verb + " " + targetPrefix + ": " + linkage.linkTarget + ".";
      if (linkage.reviewQuestion) {
        text += " Вопрос: " + linkage.reviewQuestion;
      }
      if (linkage.isHeuristic !== false) {
        const calibration = (linkage.heuristicCalibration || "").toLowerCase();
        const calibrationLabel = calibration === "medium" ? "средняя" : "низкая";
        text += " (эвристическая связь; надежность: " + calibrationLabel + ")";
      }

      return text;
    }

    function formatEvidenceTrustCue(entry) {
      const trust = formatPercent(entry && entry.trustFactor);
      if (!entry || !entry.decisionLinkage || entry.decisionLinkage.isHeuristic === false) {
        return trust;
      }

      const calibration = (entry.decisionLinkage.heuristicCalibration || "").toLowerCase();
      const calibrationLabel = calibration === "medium"
        ? "связь эвристическая, калибровка средняя"
        : "связь эвристическая, калибровка низкая";
      return trust + " (у источника; " + calibrationLabel + ")";
    }

    function isRuntimeReviewItem(item) {
      if (!item) {
        return false;
      }

      const itemType = (item.itemType || "").toLowerCase();
      const scopeItemKey = (item.scopeItemKey || "").toLowerCase();
      return itemType === "review" && scopeItemKey.indexOf("review:runtime_") === 0;
    }

    function formatItemTrustLabel(item) {
      return isRuntimeReviewItem(item)
        ? "Надежность проверки"
        : "Trust";
    }

    function formatItemTrustValue(item) {
      return formatPercent(item && item.trustFactor);
    }

    function formatRuntimeEvidenceCaveat(item) {
      if (!isRuntimeReviewItem(item)) {
        return "";
      }

      return "Семантика сообщений оценивается отдельно и остается эвристической.";
    }

    function snapshotQueueProjection(queue) {
      if (!queue || !Array.isArray(queue.items)) {
        return null;
      }

      return {
        totalOpenCount: Number(queue.totalOpenCount || 0),
        filteredCount: Number(queue.filteredCount || 0),
        scopeItemKeys: queue.items
          .map(function(item) { return item && item.scopeItemKey ? item.scopeItemKey : null; })
          .filter(function(scopeItemKey) { return !!scopeItemKey; })
      };
    }

    function computeProjectionDelta(beforeSnapshot, afterSnapshot) {
      if (!beforeSnapshot || !afterSnapshot) {
        return null;
      }

      const beforeSet = new Set(beforeSnapshot.scopeItemKeys || []);
      const afterSet = new Set(afterSnapshot.scopeItemKeys || []);
      let autoResolvedCount = 0;
      let remainingCount = 0;
      let newlyEmergedCount = 0;

      beforeSet.forEach(function(scopeItemKey) {
        if (afterSet.has(scopeItemKey)) {
          remainingCount += 1;
        } else {
          autoResolvedCount += 1;
        }
      });
      afterSet.forEach(function(scopeItemKey) {
        if (!beforeSet.has(scopeItemKey)) {
          newlyEmergedCount += 1;
        }
      });

      return {
        autoResolvedCount: autoResolvedCount,
        remainingCount: remainingCount,
        newlyEmergedCount: newlyEmergedCount
      };
    }

    function resolveFeedbackStateKind(lifecycleStatus, failureReason) {
      const normalizedLifecycle = (lifecycleStatus || "").toLowerCase();
      if (normalizedLifecycle === "failed" || failureReason) {
        return "error";
      }

      if (normalizedLifecycle === "running") {
        return "loading";
      }

      if (normalizedLifecycle === "clarification_blocked") {
        return "error";
      }

      if (normalizedLifecycle === "done") {
        return "success";
      }

      return "success";
    }

    function formatTargetLifecycleSummary(recompute) {
      const targets = recompute && Array.isArray(recompute.targets) ? recompute.targets : [];
      if (targets.length === 0) {
        return "no target lifecycle details";
      }

      const counts = {};
      targets.forEach(function(target) {
        const status = (target && target.lifecycleStatus ? target.lifecycleStatus : "unknown").toLowerCase();
        counts[status] = (counts[status] || 0) + 1;
      });

      return Object.keys(counts)
        .sort()
        .map(function(status) {
          return titleize(status) + " " + counts[status];
        })
        .join(", ");
    }

    function buildActionFeedback(actionType, action, beforeSnapshot, afterSnapshot) {
      const recompute = action && action.recompute ? action.recompute : null;
      const lifecycleStatus = recompute && recompute.lifecycleStatus ? recompute.lifecycleStatus : "unknown";
      const lastResultStatus = recompute && recompute.lastResultStatus ? recompute.lastResultStatus : null;
      const failureReason = recompute && recompute.failureReason ? recompute.failureReason : null;
      const targetSummary = formatTargetLifecycleSummary(recompute);
      const projectionDelta = computeProjectionDelta(beforeSnapshot, afterSnapshot);
      const actionId = action && action.actionId ? action.actionId : "n/a";
      const auditEventId = action && action.auditEventId ? action.auditEventId : "n/a";

      const parts = [
        titleize(actionType) + " accepted.",
        "Recompute: " + titleize(lifecycleStatus) + ".",
        "Targets: " + targetSummary + "."
      ];

      if (lastResultStatus) {
        parts.push("Last result: " + titleize(lastResultStatus) + ".");
      }
      if (failureReason) {
        parts.push("Failure: " + failureReason + ".");
      }

      if (projectionDelta) {
        parts.push(
          "Related conflicts (current projection) - Auto-resolved: " + projectionDelta.autoResolvedCount
            + ", Remaining: " + projectionDelta.remainingCount
            + ", Newly emerged: " + projectionDelta.newlyEmergedCount + "."
        );
      } else {
        parts.push("Related conflicts (current projection) are unavailable.");
      }

      parts.push("Action ID: " + actionId + ".");
      parts.push("Audit: " + auditEventId + ".");

      return {
        kind: resolveFeedbackStateKind(lifecycleStatus, failureReason),
        message: parts.join(" ")
      };
    }

    async function refreshQueueForActionFeedback(actionType, action, beforeSnapshot, scopeItemKey) {
      await loadQueue();
      const afterSnapshot = snapshotQueueProjection(state.queue);
      const feedback = buildActionFeedback(actionType, action, beforeSnapshot, afterSnapshot);
      state.lastActionFeedback = {
        scopeItemKey: scopeItemKey || null,
        kind: feedback.kind,
        message: feedback.message
      };
      return feedback;
    }

    function createActionRequestId(actionType) {
      if (window.crypto && typeof window.crypto.randomUUID === "function") {
        return "web:" + actionType + ":" + window.crypto.randomUUID();
      }

      const fallback = Date.now().toString(36) + "-" + Math.random().toString(36).slice(2, 10);
      return "web:" + actionType + ":" + fallback;
    }

    function createChecklist(containerId, title, values) {
      const container = document.getElementById(containerId);
      container.innerHTML = "";
      const titleNode = document.createElement("strong");
      titleNode.textContent = title;
      container.appendChild(titleNode);

      values.forEach(function(value) {
        const id = containerId + "-" + value;
        const label = document.createElement("label");
        label.setAttribute("for", id);

        const input = document.createElement("input");
        input.type = "checkbox";
        input.id = id;
        input.value = value;

        const text = document.createElement("span");
        text.textContent = titleize(value);

        label.appendChild(input);
        label.appendChild(text);
        container.appendChild(label);
      });
    }

    function selectedChecklistValues(containerId) {
      return Array.from(document.querySelectorAll("#" + containerId + " input[type='checkbox']:checked"))
        .map(function(node) { return node.value; });
    }

    function readAccessToken() {
      return window.localStorage.getItem("operator_web_access_token") || "";
    }

    function writeAccessToken(token) {
      window.localStorage.setItem("operator_web_access_token", token);
      document.cookie = "tga_operator_key=" + encodeURIComponent(token) + "; path=/; SameSite=Lax";
    }

    function resolveHeaders() {
      const token = readAccessToken();
      const headers = {
        "accept": "application/json",
        "content-type": "application/json"
      };

      if (token) {
        headers["X-Tga-Operator-Key"] = token;
      }

      return headers;
    }

    async function operatorPostJson(path, payload) {
      const response = await fetch(path, {
        method: "POST",
        credentials: "same-origin",
        headers: resolveHeaders(),
        body: JSON.stringify(payload || {})
      });

      let body = null;
      try {
        body = await response.json();
      } catch (_) {
        body = null;
      }

      if (!response.ok) {
        const reason = body && (body.failureReason || body.reason || body.message)
          ? (body.failureReason || body.reason || body.message)
          : "request_failed";
        const error = new Error(reason);
        error.status = response.status;
        throw error;
      }

      return body || {};
    }

    function renderTrackedPersons(queryResult) {
      const trackedPersons = Array.isArray(queryResult.trackedPersons) ? queryResult.trackedPersons : [];
      state.trackedPersons = trackedPersons;
      state.activeTrackedPersonId = queryResult.activeTrackedPersonId || null;

      trackedPersonSelect.innerHTML = "";
      if (trackedPersons.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = "No tracked persons available";
        trackedPersonSelect.appendChild(option);
        trackedPersonSelect.disabled = true;
        return;
      }

      trackedPersonSelect.disabled = false;
      trackedPersons.forEach(function(person) {
        const option = document.createElement("option");
        option.value = person.trackedPersonId;
        const suffix = person.scopeKey ? " | " + person.scopeKey : "";
        option.textContent = person.displayName + suffix;
        if (state.activeTrackedPersonId && person.trackedPersonId === state.activeTrackedPersonId) {
          option.selected = true;
        }
        trackedPersonSelect.appendChild(option);
      });

      if (bootTrackedPersonId) {
        const bootstrapOption = Array.from(trackedPersonSelect.options).find(function(option) {
          return option.value === bootTrackedPersonId;
        });
        if (bootstrapOption) {
          trackedPersonSelect.value = bootTrackedPersonId;
          state.activeTrackedPersonId = bootTrackedPersonId;
        }
      }

      if (!state.activeTrackedPersonId && trackedPersonSelect.options.length > 0) {
        trackedPersonSelect.selectedIndex = 0;
      }
    }

    function renderCounts(queue) {
      countsNode.innerHTML = "";

      const chips = [
        "Total open: " + (queue.totalOpenCount || 0),
        "Filtered: " + (queue.filteredCount || 0),
        "Scope: " + (queue.scopeKey || "n/a")
      ];

      const priorityCounts = Array.isArray(queue.priorityCounts) ? queue.priorityCounts : [];
      priorityCounts.forEach(function(entry) {
        chips.push(titleize(entry.key) + ": " + entry.count);
      });

      chips.forEach(function(text) {
        const chip = document.createElement("span");
        chip.className = "chip";
        chip.textContent = text;
        countsNode.appendChild(chip);
      });
    }

    function renderQueueItems(queue) {
      queueNode.innerHTML = "";
      const items = Array.isArray(queue.items) ? queue.items : [];
      if (items.length === 0) {
        setState("empty", "No unresolved items match current filters.");
        clearDetail("No unresolved item is available for detail.");
        return;
      }

      items.forEach(function(item) {
        const card = document.createElement("article");
        card.className = "item";
        card.setAttribute("role", "button");
        card.tabIndex = 0;
        card.dataset.scopeItemKey = item.scopeItemKey || "";

        const top = document.createElement("div");
        top.className = "item-top";

        const operatorTitle = item.humanShortTitle || item.title || item.scopeItemKey;
        const operatorSummary = item.whatHappened || item.summary || "No summary.";
        const operatorWhy = item.whyOperatorAnswerNeeded || item.whyItMatters || "Not provided.";

        const title = document.createElement("h3");
        title.textContent = operatorTitle;

        const priority = document.createElement("strong");
        const priorityValue = (item.priority || "unknown").toLowerCase();
        priority.className = "priority-" + priorityValue;
        priority.textContent = titleize(priorityValue);

        top.appendChild(title);
        top.appendChild(priority);
        card.appendChild(top);

        const summary = document.createElement("p");
        summary.textContent = operatorSummary;
        card.appendChild(summary);

        const why = document.createElement("p");
        why.className = "muted";
        why.textContent = "Почему нужен ответ оператора: " + operatorWhy;
        card.appendChild(why);

        const meta = document.createElement("div");
        meta.className = "meta";
        const trustLabel = formatItemTrustLabel(item);
        const runtimeEvidenceCaveat = formatRuntimeEvidenceCaveat(item);

        const metaValues = [
          "Type: " + titleize(item.itemType || "unknown"),
          "Status: " + titleize(item.status || "unknown"),
          trustLabel + ": " + formatItemTrustValue(item),
          "Evidence: " + (item.evidenceCount || 0),
          "Updated: " + formatUtc(item.updatedAtUtc),
          "Family: " + (item.affectedFamily || "n/a"),
          "Action: " + titleize(item.recommendedNextAction || "none")
        ];
        if (runtimeEvidenceCaveat) {
          metaValues.push("Оценка по эвристике");
        }

        metaValues.forEach(function(value) {
          const tag = document.createElement("span");
          tag.textContent = value;
          meta.appendChild(tag);
        });

        card.appendChild(meta);
        card.addEventListener("click", function() {
          if (!item.scopeItemKey) {
            return;
          }

          selectScopeItem(item.scopeItemKey);
        });
        card.addEventListener("keydown", function(event) {
          if (event.key !== "Enter" && event.key !== " ") {
            return;
          }

          event.preventDefault();
          if (!item.scopeItemKey) {
            return;
          }

          selectScopeItem(item.scopeItemKey);
        });
        queueNode.appendChild(card);
      });

      syncQueueSelection(items);
      setState("success", "Queue loaded from bounded operator projection.");
    }

    function clearDetail(message) {
      state.selectedScopeItemKey = null;
      state.selectedDetailItem = null;
      state.activeConflictSession = null;
      state.toggleDetailActionButtons = null;
      detailContentNode.innerHTML = "";
      resetEvidenceState();
      closeClarificationDrawer();
      setDetailState("empty", message || "Select a queue item to inspect detail.");
      updateQueueSelectionUi();
    }

    function updateQueueSelectionUi() {
      document.querySelectorAll("#queue-list .item").forEach(function(node) {
        const isActive = state.selectedScopeItemKey
          && node.dataset.scopeItemKey
          && node.dataset.scopeItemKey === state.selectedScopeItemKey;
        node.classList.toggle("active", !!isActive);
      });
    }

    function isConflictSessionEligible(item) {
      if (!item) {
        return false;
      }

      if ((item.itemType || "") === "contradiction") {
        return true;
      }

      return (item.itemType || "") === "review"
        && (item.sourceKind || "") === "durable_object_metadata";
    }

    function renderDetail(detail) {
      detailContentNode.innerHTML = "";
      const item = detail && detail.item ? detail.item : null;
      if (!item) {
        state.selectedDetailItem = null;
        resetEvidenceState();
        setDetailState("empty", "Detail payload is unavailable for this item.");
        return;
      }
      state.selectedDetailItem = item;

      const operatorTitle = item.humanShortTitle || item.title || item.scopeItemKey || "Untitled item";
      const operatorSummary = item.whatHappened || item.summary || "No summary.";
      const operatorWhy = item.whyOperatorAnswerNeeded || item.whyItMatters || "Not provided.";
      const operatorPrompt = item.whatToDoPrompt || "";
      const evidenceRationaleSummary = item.evidenceRationaleSummary || "Система показала эти сообщения как эвристический контекст для текущей карточки.";
      const autoResolutionGap = item.autoResolutionGap || "Автоматический контур остановился и требует ручного решения.";
      const operatorDecisionFocus = item.operatorDecisionFocus || operatorPrompt || "Нужно bounded-решение оператора по текущему item.";
      const rationaleIsHeuristic = item.rationaleIsHeuristic !== false;

      const summaryBlock = document.createElement("section");
      summaryBlock.className = "detail-block";
      summaryBlock.innerHTML =
        "<h4>" + operatorTitle + "</h4>" +
        "<p><strong>Что произошло:</strong> " + operatorSummary + "</p>" +
        "<p><strong>Почему нужен ответ оператора:</strong> " + operatorWhy + "</p>" +
        (operatorPrompt ? "<p><strong>Что сделать:</strong> " + operatorPrompt + "</p>" : "");
      detailContentNode.appendChild(summaryBlock);

      const rationaleBlock = document.createElement("section");
      rationaleBlock.className = "detail-block";
      rationaleBlock.innerHTML =
        "<h4>Что известно для решения</h4>" +
        "<p><strong>Почему сообщения показаны:</strong> " + evidenceRationaleSummary + "</p>" +
        "<p><strong>Что известно точно:</strong> " + autoResolutionGap + "</p>" +
        "<p><strong>Какое решение нужно сейчас:</strong> " + operatorDecisionFocus + "</p>" +
        (rationaleIsHeuristic ? "<p class='muted'>Сообщения ниже подобраны эвристически и не доказывают сбой сами по себе.</p>" : "");
      detailContentNode.appendChild(rationaleBlock);

      const statusBlock = document.createElement("section");
      statusBlock.className = "detail-block";
      const trustLabel = formatItemTrustLabel(item);
      const runtimeEvidenceCaveat = formatRuntimeEvidenceCaveat(item);
      const statusMeta = [
        "Type: " + titleize(item.itemType || "unknown"),
        "Status: " + titleize(item.status || "unknown"),
        "Priority: " + titleize(item.priority || "unknown"),
        trustLabel + ": " + formatItemTrustValue(item),
        "Evidence count: " + (item.evidenceCount || 0),
        "Updated: " + formatUtc(item.updatedAtUtc),
        "Family: " + (item.affectedFamily || "n/a"),
        "Object: " + (item.affectedObjectRef || "n/a"),
        "Scope item: " + (item.scopeItemKey || "n/a"),
        "Recommended action: " + titleize(item.recommendedNextAction || "none")
      ];
      if (runtimeEvidenceCaveat) {
        statusMeta.push("Оценка по эвристике");
      }
      const statusMetaNode = document.createElement("div");
      statusMetaNode.className = "meta";
      statusMeta.forEach(function(value) {
        const tag = document.createElement("span");
        tag.textContent = value;
        statusMetaNode.appendChild(tag);
      });
      statusBlock.appendChild(statusMetaNode);
      detailContentNode.appendChild(statusBlock);

      const evidenceBlock = document.createElement("section");
      evidenceBlock.className = "detail-block";
      evidenceBlock.innerHTML = "<h4>Evidence</h4>";
      const evidenceInline = document.createElement("div");
      evidenceInline.className = "evidence-inline";

      const evidenceChip = document.createElement("span");
      evidenceChip.className = "chip";
      evidenceChip.textContent = "Bounded summaries: " + ((Array.isArray(item.evidence) ? item.evidence.length : 0) || 0);
      evidenceInline.appendChild(evidenceChip);

      const evidenceSummary = document.createElement("p");
      evidenceSummary.className = "muted";
      evidenceSummary.textContent = "Почему система показала эти сообщения: " + evidenceRationaleSummary;
      evidenceBlock.appendChild(evidenceSummary);

      const evidenceButton = document.createElement("button");
      evidenceButton.type = "button";
      evidenceButton.textContent = "Open evidence panel";
      evidenceButton.addEventListener("click", openEvidenceDrawer);
      evidenceInline.appendChild(evidenceButton);

      evidenceBlock.appendChild(evidenceInline);
      detailContentNode.appendChild(evidenceBlock);

      const provenanceBlock = document.createElement("section");
      provenanceBlock.className = "detail-block";
      provenanceBlock.innerHTML =
        "<h4>Provenance</h4>" +
        "<p><strong>Source kind:</strong> " + (item.sourceKind || "n/a") + "</p>" +
        "<p><strong>Source ref:</strong> " + (item.sourceRef || "n/a") + "</p>" +
        "<p><strong>Required action:</strong> " + titleize(item.requiredAction || "none") + "</p>";
      detailContentNode.appendChild(provenanceBlock);

      const actionsBlock = document.createElement("section");
      actionsBlock.className = "detail-block action-panel";
      actionsBlock.innerHTML = "<h4>Bounded Actions</h4>";

      const explanationLabel = document.createElement("label");
      explanationLabel.textContent = "Explanation (required for reject/defer/clarify)";
      const explanationInput = document.createElement("textarea");
      explanationInput.rows = 3;
      explanationInput.placeholder = "Provide bounded rationale for reject/defer, or optional context for approve.";
      explanationLabel.appendChild(explanationInput);
      actionsBlock.appendChild(explanationLabel);

      const actionGrid = document.createElement("div");
      actionGrid.className = "action-grid";
      const actionFeedback = document.createElement("div");
      actionFeedback.className = "state empty action-feedback";
      actionFeedback.textContent = "Submit approve/reject/defer or open clarify without leaving detail context.";
      if (state.lastActionFeedback
        && state.lastActionFeedback.scopeItemKey
        && state.lastActionFeedback.scopeItemKey === item.scopeItemKey) {
        actionFeedback.className = "state " + state.lastActionFeedback.kind + " action-feedback";
        actionFeedback.textContent = state.lastActionFeedback.message;
      }

      function disableActionButtons(disabled) {
        state.actionSubmitting = disabled;
        actionGrid.querySelectorAll("button").forEach(function(button) {
          button.disabled = disabled;
        });
        explanationInput.disabled = disabled;
      }
      state.toggleDetailActionButtons = disableActionButtons;

      async function submitResolutionAction(actionType) {
        if (state.actionSubmitting) {
          return;
        }

        const trackedPersonId = state.activeTrackedPersonId || trackedPersonSelect.value;
        const scopeItemKey = item.scopeItemKey || state.selectedScopeItemKey;
        const explanation = explanationInput.value.trim();
        if (!trackedPersonId || !scopeItemKey) {
          actionFeedback.className = "state error action-feedback";
          actionFeedback.textContent = "Tracked person and selected item are required before action submit.";
          return;
        }

        if ((actionType === "reject" || actionType === "defer") && !explanation) {
          actionFeedback.className = "state error action-feedback";
          actionFeedback.textContent = titleize(actionType) + " requires explanation.";
          return;
        }

        disableActionButtons(true);
        actionFeedback.className = "state loading action-feedback";
        actionFeedback.textContent = "Submitting " + actionType + " decision...";
        try {
          const beforeSnapshot = snapshotQueueProjection(state.queue);
          const result = await operatorPostJson("/api/operator/resolution/actions", {
            requestId: createActionRequestId(actionType),
            trackedPersonId: trackedPersonId,
            scopeItemKey: scopeItemKey,
            actionType: actionType,
            explanation: explanation || null,
            submittedAtUtc: new Date().toISOString()
          });

          const action = result && result.action ? result.action : null;
          if (!result.accepted || !action || !action.accepted) {
            throw new Error(result.failureReason || (action && action.failureReason) || "action_submit_rejected");
          }

          const feedback = await refreshQueueForActionFeedback(actionType, action, beforeSnapshot, scopeItemKey);
          actionFeedback.className = "state " + feedback.kind + " action-feedback";
          actionFeedback.textContent = feedback.message;
          setState(feedback.kind, feedback.message);
        } catch (error) {
          actionFeedback.className = "state error action-feedback";
          actionFeedback.textContent = "Action submission failed: " + (error && error.message ? error.message : "unknown_error");
        } finally {
          disableActionButtons(false);
        }
      }

      [
        { type: "approve", label: "Approve", className: "approve" },
        { type: "reject", label: "Reject", className: "reject" },
        { type: "defer", label: "Defer", className: "defer" },
        { type: "clarify", label: "Clarify", className: "clarify" }
      ].forEach(function(definition) {
        const button = document.createElement("button");
        button.type = "button";
        button.textContent = definition.label;
        button.className = definition.className;
        button.addEventListener("click", function() {
          if (definition.type === "clarify") {
            openClarificationDrawer(explanationInput.value.trim());
            return;
          }

          submitResolutionAction(definition.type);
        });
        actionGrid.appendChild(button);
      });

      actionsBlock.appendChild(actionGrid);
      actionsBlock.appendChild(actionFeedback);
      detailContentNode.appendChild(actionsBlock);

      const conflictSessionBlock = document.createElement("section");
      conflictSessionBlock.className = "detail-block";
      conflictSessionBlock.innerHTML = "<h4>AI Conflict Session (V1)</h4>";

      const conflictSessionState = document.createElement("div");
      conflictSessionState.className = "state empty";
      conflictSessionState.textContent = "AI session is available for contradiction/review pilot items only.";
      conflictSessionBlock.appendChild(conflictSessionState);

      const conflictQuestionWrap = document.createElement("div");
      const conflictQuestionText = document.createElement("p");
      const conflictAnswerLabel = document.createElement("label");
      conflictAnswerLabel.textContent = "Operator answer";
      const conflictAnswerInput = document.createElement("textarea");
      conflictAnswerInput.rows = 2;
      conflictAnswerInput.placeholder = "Provide one bounded answer for AI follow-up.";
      conflictAnswerLabel.appendChild(conflictAnswerInput);
      const conflictAnswerButton = document.createElement("button");
      conflictAnswerButton.type = "button";
      conflictAnswerButton.textContent = "Submit Answer";
      conflictQuestionWrap.appendChild(conflictQuestionText);
      conflictQuestionWrap.appendChild(conflictAnswerLabel);
      conflictQuestionWrap.appendChild(conflictAnswerButton);
      conflictQuestionWrap.style.display = "none";
      conflictSessionBlock.appendChild(conflictQuestionWrap);

      const conflictVerdictNode = document.createElement("pre");
      conflictVerdictNode.className = "muted";
      conflictVerdictNode.style.whiteSpace = "pre-wrap";
      conflictVerdictNode.textContent = "";
      conflictSessionBlock.appendChild(conflictVerdictNode);

      const conflictApplyButton = document.createElement("button");
      conflictApplyButton.type = "button";
      conflictApplyButton.textContent = "Apply AI Proposal";
      conflictApplyButton.style.display = "none";
      conflictSessionBlock.appendChild(conflictApplyButton);
      detailContentNode.appendChild(conflictSessionBlock);

      function setConflictSessionState(kind, message) {
        conflictSessionState.className = "state " + kind;
        conflictSessionState.textContent = message;
      }

      function renderConflictSession(sessionPayload) {
        state.activeConflictSession = sessionPayload || null;
        conflictAnswerInput.value = "";
        conflictQuestionWrap.style.display = "none";
        conflictApplyButton.style.display = "none";
        conflictVerdictNode.textContent = "";
        if (!sessionPayload) {
          setConflictSessionState("empty", "No active AI conflict session.");
          return;
        }

        const sessionState = sessionPayload.state || "unknown";
        const verdict = sessionPayload.finalVerdict || null;
        if (sessionState === "awaiting_operator_answer" && sessionPayload.operatorQuestion) {
          conflictQuestionWrap.style.display = "";
          conflictQuestionText.innerHTML = "<strong>AI follow-up:</strong> " + (sessionPayload.operatorQuestion.questionText || "No question text.");
          setConflictSessionState("success", "AI asked one bounded follow-up question.");
          return;
        }

        if (verdict) {
          conflictVerdictNode.textContent = JSON.stringify(verdict, null, 2);
          if (sessionState === "ready_for_commit") {
            conflictApplyButton.style.display = "";
            setConflictSessionState("success", "Final AI verdict is ready for deterministic apply handoff.");
          } else if (sessionState === "needs_web_review" || sessionState === "fallback") {
            setConflictSessionState("empty", "AI returned unresolved/fallback verdict; continue with manual bounded action.");
          } else {
            setConflictSessionState("empty", "AI session completed with non-apply state: " + sessionState + ".");
          }
          return;
        }

        setConflictSessionState("loading", "AI session is running...");
      }

      async function startConflictSessionIfEligible() {
        if (!isConflictSessionEligible(item)) {
          setConflictSessionState("empty", "AI session is disabled for this item type/source.");
          return;
        }

        const trackedPersonId = state.activeTrackedPersonId || trackedPersonSelect.value;
        const scopeItemKey = item.scopeItemKey || state.selectedScopeItemKey;
        if (!trackedPersonId || !scopeItemKey) {
          setConflictSessionState("error", "Tracked person and scope item are required for AI session.");
          return;
        }

        setConflictSessionState("loading", "Starting AI conflict session...");
        try {
          const result = await operatorPostJson("/api/operator/resolution/conflict-session/start", {
            requestId: createActionRequestId("conflict-session-start"),
            trackedPersonId: trackedPersonId,
            scopeItemKey: scopeItemKey
          });
          if (!result.accepted || !result.conflictSession) {
            throw new Error(result.failureReason || "conflict_session_start_rejected");
          }

          renderConflictSession(result.conflictSession);
        } catch (error) {
          setConflictSessionState("error", "AI session start failed: " + (error && error.message ? error.message : "unknown_error"));
        }
      }

      async function submitConflictAnswer() {
        const active = state.activeConflictSession;
        if (!active || !active.conflictSessionId || !active.operatorQuestion) {
          setConflictSessionState("error", "No active AI question is available.");
          return;
        }

        const answerValue = conflictAnswerInput.value.trim();
        if (!answerValue) {
          setConflictSessionState("error", "One operator answer is required.");
          return;
        }

        setConflictSessionState("loading", "Submitting operator answer to AI session...");
        try {
          const result = await operatorPostJson("/api/operator/resolution/conflict-session/respond", {
            requestId: createActionRequestId("conflict-session-respond"),
            conflictSessionId: active.conflictSessionId,
            questionKey: active.operatorQuestion.questionKey || "q1",
            answerValue: answerValue,
            answerKind: active.operatorQuestion.answerKind || "free_text"
          });
          if (!result.accepted || !result.conflictSession) {
            throw new Error(result.failureReason || "conflict_session_respond_rejected");
          }

          renderConflictSession(result.conflictSession);
        } catch (error) {
          setConflictSessionState("error", "AI session answer submit failed: " + (error && error.message ? error.message : "unknown_error"));
        }
      }

      async function applyConflictProposal() {
        const active = state.activeConflictSession;
        if (!active || !active.finalVerdict || !active.finalVerdict.normalizationProposal) {
          setConflictSessionState("error", "No final AI proposal is available.");
          return;
        }

        const trackedPersonId = state.activeTrackedPersonId || trackedPersonSelect.value;
        const scopeItemKey = item.scopeItemKey || state.selectedScopeItemKey;
        const proposal = active.finalVerdict.normalizationProposal;
        const actionType = (proposal.recommendedAction || "").toLowerCase();
        if (!trackedPersonId || !scopeItemKey || !actionType) {
          setConflictSessionState("error", "Conflict proposal is incomplete.");
          return;
        }

        setConflictSessionState("loading", "Applying AI proposal through deterministic action path...");
        try {
          const beforeSnapshot = snapshotQueueProjection(state.queue);
          const result = await operatorPostJson("/api/operator/resolution/actions", {
            requestId: createActionRequestId("conflict-session-apply"),
            trackedPersonId: trackedPersonId,
            scopeItemKey: scopeItemKey,
            actionType: actionType,
            explanation: proposal.explanation || null,
            clarificationPayload: proposal.clarificationPayload || null,
            conflictResolutionSessionId: active.conflictSessionId,
            conflictVerdictRevision: active.revision,
            conflictVerdict: active.finalVerdict,
            submittedAtUtc: new Date().toISOString()
          });

          const action = result && result.action ? result.action : null;
          if (!result.accepted || !action || !action.accepted) {
            throw new Error(result.failureReason || (action && action.failureReason) || "action_submit_rejected");
          }

          const feedback = await refreshQueueForActionFeedback(actionType, action, beforeSnapshot, scopeItemKey);
          setConflictSessionState(feedback.kind, "AI proposal applied via deterministic path. " + feedback.message);
        } catch (error) {
          setConflictSessionState("error", "AI proposal apply failed: " + (error && error.message ? error.message : "unknown_error"));
        }
      }

      conflictAnswerButton.addEventListener("click", submitConflictAnswer);
      conflictApplyButton.addEventListener("click", applyConflictProposal);
      startConflictSessionIfEligible();

      const notes = Array.isArray(item.notes) ? item.notes : [];
      if (notes.length > 0) {
        const notesBlock = document.createElement("section");
        notesBlock.className = "detail-block";
        const notesHeader = document.createElement("h4");
        notesHeader.textContent = "Detail Notes";
        const notesList = document.createElement("ul");
        notesList.className = "detail-list";
        notes.forEach(function(note) {
          const li = document.createElement("li");
          const noteKind = note && note.kind ? titleize(note.kind) : "Note";
          const noteText = note && note.text ? note.text : "No details.";
          li.textContent = noteKind + ": " + noteText;
          notesList.appendChild(li);
        });
        notesBlock.appendChild(notesHeader);
        notesBlock.appendChild(notesList);
        detailContentNode.appendChild(notesBlock);
      }

      if (state.evidenceDrawerOpen) {
        renderEvidencePanelFromSelectedDetail();
      } else {
        resetEvidenceState();
      }

      setDetailState("success", "Detail loaded for selected queue item.");
    }

    function setClarificationState(kind, message) {
      clarificationStateNode.className = "state " + kind;
      clarificationStateNode.textContent = message;
    }

    function resetClarificationForm() {
      clarificationSummaryInput.value = "";
      clarificationExplanationInput.value = "";
      clarificationQuestionInput.value = "";
      clarificationAnswerInput.value = "";
      clarificationAnswerKindSelect.value = "free_text";
      clarificationNotesInput.value = "";
      setClarificationState("empty", "Clarify keeps queue/detail context and submits structured follow-up payload.");
    }

    function openClarificationDrawer(prefillExplanation) {
      const item = state.selectedDetailItem;
      if (!item) {
        setClarificationState("error", "Select a queue item detail before opening clarification.");
        return;
      }

      resetClarificationForm();
      clarificationContextNode.textContent =
        "Item: " + (item.title || item.scopeItemKey || "n/a")
        + " | Scope: " + (item.scopeItemKey || "n/a")
        + " | Type: " + titleize(item.itemType || "unknown");
      clarificationSummaryInput.value = "Clarification follow-up for " + (item.title || item.scopeItemKey || "selected item");
      clarificationExplanationInput.value = prefillExplanation || "";
      state.clarificationDrawerOpen = true;
      clarificationBackdropNode.classList.add("open");
      clarificationDrawerNode.classList.add("open");
      clarificationBackdropNode.setAttribute("aria-hidden", "false");
      clarificationDrawerNode.setAttribute("aria-hidden", "false");
    }

    function closeClarificationDrawer() {
      state.clarificationDrawerOpen = false;
      clarificationBackdropNode.classList.remove("open");
      clarificationDrawerNode.classList.remove("open");
      clarificationBackdropNode.setAttribute("aria-hidden", "true");
      clarificationDrawerNode.setAttribute("aria-hidden", "true");
    }

    function toggleClarificationForm(disabled) {
      state.clarificationSubmitting = disabled;
      clarificationSummaryInput.disabled = disabled;
      clarificationExplanationInput.disabled = disabled;
      clarificationQuestionInput.disabled = disabled;
      clarificationAnswerInput.disabled = disabled;
      clarificationAnswerKindSelect.disabled = disabled;
      clarificationNotesInput.disabled = disabled;
      clarificationSubmitButton.disabled = disabled;
      clarificationCancelButton.disabled = disabled;
      if (typeof state.toggleDetailActionButtons === "function") {
        state.toggleDetailActionButtons(disabled);
      }
    }

    async function submitClarifyAction() {
      if (state.clarificationSubmitting || state.actionSubmitting) {
        return;
      }

      const item = state.selectedDetailItem;
      const trackedPersonId = state.activeTrackedPersonId || trackedPersonSelect.value;
      const scopeItemKey = item && item.scopeItemKey ? item.scopeItemKey : state.selectedScopeItemKey;
      const explanation = clarificationExplanationInput.value.trim();
      const followUpQuestion = clarificationQuestionInput.value.trim();
      const followUpAnswer = clarificationAnswerInput.value.trim();
      const followUpNotes = clarificationNotesInput.value.trim();
      const answerKind = clarificationAnswerKindSelect.value || "free_text";
      const summary = clarificationSummaryInput.value.trim();
      if (!trackedPersonId || !scopeItemKey || !item) {
        setClarificationState("error", "Tracked person and selected item are required before clarify submit.");
        return;
      }

      if (!explanation) {
        setClarificationState("error", "Clarify requires explanation.");
        return;
      }

      if (!followUpQuestion) {
        setClarificationState("error", "Follow-up question is required.");
        return;
      }

      if (!followUpAnswer) {
        setClarificationState("error", "Follow-up answer is required.");
        return;
      }

      toggleClarificationForm(true);
      setClarificationState("loading", "Submitting clarify action...");
      try {
        const beforeSnapshot = snapshotQueueProjection(state.queue);
        const payload = {
          summary: summary || ("Clarification follow-up for " + (item.title || item.scopeItemKey || "selected item")),
          responses: [
            {
              questionKey: "operator_explanation",
              questionText: "Operator explanation for clarify",
              answerValue: explanation,
              answerKind: "free_text"
            },
            {
              questionKey: "follow_up_primary",
              questionText: followUpQuestion,
              answerValue: followUpAnswer,
              answerKind: answerKind,
              notes: followUpNotes || null
            }
          ],
          metadata: {
            surface: "web",
            tracked_person_id: trackedPersonId,
            scope_item_key: scopeItemKey,
            item_type: item.itemType || "unknown",
            action_type: "clarify",
            captured_at_utc: new Date().toISOString()
          }
        };
        const result = await operatorPostJson("/api/operator/resolution/actions", {
          requestId: createActionRequestId("clarify"),
          trackedPersonId: trackedPersonId,
          scopeItemKey: scopeItemKey,
          actionType: "clarify",
          explanation: explanation,
          clarificationPayload: payload,
          submittedAtUtc: new Date().toISOString()
        });

        const action = result && result.action ? result.action : null;
        if (!result.accepted || !action || !action.accepted) {
          throw new Error(result.failureReason || (action && action.failureReason) || "action_submit_rejected");
        }

        const feedback = await refreshQueueForActionFeedback("clarify", action, beforeSnapshot, scopeItemKey);
        setClarificationState(feedback.kind, feedback.message);
        setState(feedback.kind, feedback.message);
        closeClarificationDrawer();
      } catch (error) {
        setClarificationState("error", "Clarify submission failed: " + (error && error.message ? error.message : "unknown_error"));
      } finally {
        toggleClarificationForm(false);
      }
    }

    function syncQueueSelection(items) {
      const activeItems = Array.isArray(items) ? items : [];
      if (activeItems.length === 0) {
        clearDetail("No unresolved item is available for detail.");
        return;
      }

      if (!state.selectedScopeItemKey) {
        const firstKey = activeItems[0].scopeItemKey || null;
        if (firstKey) {
          selectScopeItem(firstKey);
          return;
        }
      }

      const selectedExists = activeItems.some(function(item) {
        return item.scopeItemKey === state.selectedScopeItemKey;
      });
      if (!selectedExists) {
        clearDetail("Previously selected item is no longer available in this queue projection.");
        return;
      }

      updateQueueSelectionUi();
      if (state.selectedScopeItemKey) {
        loadDetail(state.selectedScopeItemKey);
      }
    }

    async function selectScopeItem(scopeItemKey) {
      if (!scopeItemKey) {
        clearDetail("Scope item key is required for detail.");
        return;
      }

      state.selectedScopeItemKey = scopeItemKey;
      updateQueueSelectionUi();
      await loadDetail(scopeItemKey);
    }

    async function loadDetail(scopeItemKey) {
      const trackedPersonId = state.activeTrackedPersonId || trackedPersonSelect.value;
      if (!trackedPersonId || !scopeItemKey) {
        clearDetail("Select tracked person and queue item before reading detail.");
        return;
      }

      setDetailState("loading", "Loading selected item detail...");
      try {
        const result = await operatorPostJson("/api/operator/resolution/detail/query", {
          trackedPersonId: trackedPersonId,
          scopeItemKey: scopeItemKey,
          evidenceLimit: Number(evidenceLimitSelect.value || 8),
          evidenceSortBy: evidenceSortBySelect.value || "observed_at",
          evidenceSortDirection: evidenceSortDirectionSelect.value || "desc"
        });

        if (!result.accepted) {
          throw new Error(result.failureReason || "detail_query_rejected");
        }

        const detail = result.detail || null;
        if (!detail || !detail.itemFound || !detail.item) {
          detailContentNode.innerHTML = "";
          setDetailState("empty", "Selected item is not available for this tracked person.");
          return;
        }

        renderDetail(detail);
      } catch (error) {
        const message = error && error.message ? error.message : "unknown_error";
        if (message === "scope_item_not_found") {
          detailContentNode.innerHTML = "";
          setDetailState("empty", "Selected item is not available after refresh.");
          return;
        }

        setDetailState("error", "Detail load failed: " + describeFailureReason(message));
      }
    }

    async function loadTrackedPersons() {
      setState("loading", "Loading tracked person scope...");
      const result = await operatorPostJson("/api/operator/tracked-persons/query", { limit: 50 });
      if (!result.accepted) {
        throw new Error(result.failureReason || "tracked_person_query_rejected");
      }

      renderTrackedPersons(result);
      if (!state.activeTrackedPersonId && trackedPersonSelect.value) {
        state.activeTrackedPersonId = trackedPersonSelect.value;
      }
    }

    async function applyTrackedPersonSelection() {
      const selected = trackedPersonSelect.value;
      if (!selected) {
        throw new Error("tracked_person_selection_required");
      }

      const result = await operatorPostJson("/api/operator/tracked-persons/select", {
        trackedPersonId: selected,
        requestedAtUtc: new Date().toISOString()
      });

      if (!result.accepted) {
        throw new Error(result.failureReason || "tracked_person_select_rejected");
      }

      if (result.activeTrackedPerson && result.activeTrackedPerson.trackedPersonId) {
        state.activeTrackedPersonId = result.activeTrackedPerson.trackedPersonId;
      } else {
        state.activeTrackedPersonId = selected;
      }
    }

    async function applyResolutionHandoffContextIfPresent() {
      if (state.handoff.consumed) {
        return;
      }

      if (!state.handoff.trackedPersonId
        || !state.handoff.scopeItemKey
        || !state.handoff.operatorSessionId
        || !state.handoff.handoffToken) {
        setHandoffState("empty", "Контекст handoff отсутствует.");
        return;
      }

      const result = await operatorPostJson("/api/operator/resolution/handoff/consume", {
        trackedPersonId: state.handoff.trackedPersonId,
        scopeItemKey: state.handoff.scopeItemKey,
        operatorSessionId: state.handoff.operatorSessionId,
        activeMode: state.handoff.activeMode || "resolution_detail",
        handoffToken: state.handoff.handoffToken,
        targetApi: query.get("target_api") || null
      });
      if (!result.accepted) {
        throw new Error(result.failureReason || "handoff_consume_rejected");
      }

      state.handoff.consumed = true;
      state.bootTrackedPersonApplied = true;
      state.activeTrackedPersonId = result.activeTrackedPersonId || state.handoff.trackedPersonId;
      state.bootTrackedPersonId = state.activeTrackedPersonId || null;
      state.bootScopeItemKey = result.activeScopeItemKey || state.handoff.scopeItemKey;
      if (state.activeTrackedPersonId) {
        trackedPersonSelect.value = state.activeTrackedPersonId;
      }
      clearConsumedHandoffQuery();
      setHandoffState(
        "success",
        "Контекст из Telegram применен: выбран человек и нужная карточка.");
    }

    async function loadQueue() {
      const trackedPersonId = state.activeTrackedPersonId || trackedPersonSelect.value;
      if (!trackedPersonId) {
        setState("empty", "Tracked person selection is required before queue read.");
        queueNode.innerHTML = "";
        countsNode.innerHTML = "";
        clearDetail("Tracked person selection is required before detail view.");
        return;
      }

      setState("loading", "Loading resolution queue...");
      const result = await operatorPostJson("/api/operator/resolution/queue/query", {
        trackedPersonId: trackedPersonId,
        sortBy: sortBySelect.value,
        sortDirection: sortDirectionSelect.value,
        priorities: selectedChecklistValues("priority-filters"),
        statuses: selectedChecklistValues("status-filters"),
        itemTypes: selectedChecklistValues("type-filters"),
        limit: 100
      });

      if (!result.accepted) {
        throw new Error(result.failureReason || "queue_query_rejected");
      }

      state.queue = result.queue || null;
      if (!state.queue) {
        throw new Error("queue_payload_missing");
      }

      renderCounts(state.queue);
      renderQueueItems(state.queue);
    }

    async function refreshAll() {
      try {
        const token = tokenInput.value.trim();
        if (token) {
          writeAccessToken(token);
        }

        await applyResolutionHandoffContextIfPresent();

        const previousTrackedPersonId = state.activeTrackedPersonId;
        await loadTrackedPersons();
        if (previousTrackedPersonId && previousTrackedPersonId !== state.activeTrackedPersonId) {
          clearDetail("Tracked person changed. Select an item to inspect detail.");
        }

        if (state.activeTrackedPersonId) {
          if (state.bootTrackedPersonId
            && !state.bootTrackedPersonApplied
            && state.activeTrackedPersonId === state.bootTrackedPersonId) {
            await applyTrackedPersonSelection();
            state.bootTrackedPersonApplied = true;
          }
          await loadQueue();
          if (state.bootScopeItemKey) {
            const availableItems = state.queue && Array.isArray(state.queue.items) ? state.queue.items : [];
            const bootstrapItem = availableItems.find(function(item) {
              return item.scopeItemKey === state.bootScopeItemKey;
            });
            if (bootstrapItem && bootstrapItem.scopeItemKey) {
              await selectScopeItem(bootstrapItem.scopeItemKey);
              state.bootScopeItemKey = null;
              setState("success", "Queue loaded and drilldown focused on scoped resolution item.");
            }
          }
        } else {
          setState("empty", "Select a tracked person to load queue data.");
          queueNode.innerHTML = "";
          countsNode.innerHTML = "";
          clearDetail("Select a tracked person to inspect resolution detail.");
        }
      } catch (error) {
        const reason = error && error.message ? error.message : "unknown_error";
        setHandoffState("error", "Handoff not applied: " + describeFailureReason(reason));
        setState("error", "Resolution queue load failed: " + describeFailureReason(reason));
      }
    }

    async function onApplyScope() {
      try {
        const token = tokenInput.value.trim();
        if (token) {
          writeAccessToken(token);
        }

        await applyTrackedPersonSelection();
        clearDetail("Tracked person scope updated. Select an item to inspect detail.");
        await loadQueue();
      } catch (error) {
        const reason = error && error.message ? error.message : "unknown_error";
        setState("error", "Scope update failed: " + describeFailureReason(reason));
      }
    }

    function setEvidenceState(kind, message) {
      evidenceStateNode.className = "state " + kind;
      evidenceStateNode.textContent = message;
    }

    function resetEvidenceState() {
      state.evidenceIndex = -1;
      evidenceListNode.innerHTML = "";
      evidenceFocusNode.innerHTML = "<p class='muted'>Select an evidence entry to inspect provenance and confidence cues.</p>";
      setEvidenceState("empty", "Open evidence from a selected detail item.");
      evidencePrevButton.disabled = true;
      evidenceNextButton.disabled = true;
    }

    function evidenceEntries() {
      if (!state.selectedDetailItem || !Array.isArray(state.selectedDetailItem.evidence)) {
        return [];
      }

      return state.selectedDetailItem.evidence;
    }

    function renderEvidenceFocus(index) {
      const entries = evidenceEntries();
      const entry = entries[index];
      if (!entry) {
        evidenceFocusNode.innerHTML = "<p class='muted'>No evidence entry selected.</p>";
        evidencePrevButton.disabled = true;
        evidenceNextButton.disabled = true;
        return;
      }

      evidenceFocusNode.innerHTML = "";
      const summary = document.createElement("p");
      summary.innerHTML = "<strong>Summary:</strong> " + (entry.summary || "No summary.");
      evidenceFocusNode.appendChild(summary);

      const trust = document.createElement("p");
      trust.innerHTML = "<strong>Сигнал уверенности:</strong> " + formatEvidenceTrustCue(entry);
      evidenceFocusNode.appendChild(trust);

      const observed = document.createElement("p");
      observed.innerHTML = "<strong>Observed:</strong> " + formatUtc(entry.observedAtUtc);
      evidenceFocusNode.appendChild(observed);

      const sender = document.createElement("p");
      const senderLabel = document.createElement("strong");
      senderLabel.textContent = "Отправитель:";
      sender.appendChild(senderLabel);
      sender.appendChild(document.createTextNode(" " + (entry.senderDisplay || "не определен")));
      evidenceFocusNode.appendChild(sender);

      const source = document.createElement("p");
      source.innerHTML = "<strong>Provenance:</strong> " + (entry.sourceLabel || "n/a") + " | " + (entry.sourceRef || "n/a");
      evidenceFocusNode.appendChild(source);

      const evidenceId = document.createElement("p");
      evidenceId.innerHTML = "<strong>Evidence ID:</strong> " + (entry.evidenceItemId || "n/a");
      evidenceFocusNode.appendChild(evidenceId);

      const relevance = document.createElement("p");
      relevance.innerHTML = "<strong>Почему показано:</strong> "
        + (entry.relevanceHint || "Контекстный сигнал для ручной проверки.")
        + (entry.relevanceHintIsHeuristic !== false ? " (эвристика)" : "");
      evidenceFocusNode.appendChild(relevance);

      const decisionLink = document.createElement("p");
      decisionLink.innerHTML = "<strong>Эвристическая связь с решением:</strong> " + formatDecisionLinkage(entry.decisionLinkage);
      evidenceFocusNode.appendChild(decisionLink);

      evidencePrevButton.disabled = index <= 0;
      evidenceNextButton.disabled = index >= entries.length - 1;
    }

    function selectEvidence(index) {
      const entries = evidenceEntries();
      if (!entries[index]) {
        return;
      }

      state.evidenceIndex = index;
      document.querySelectorAll("#evidence-list .evidence-card").forEach(function(cardNode, cardIndex) {
        cardNode.classList.toggle("active", cardIndex === index);
      });
      renderEvidenceFocus(index);
    }

    function renderEvidencePanelFromSelectedDetail() {
      const item = state.selectedDetailItem;
      if (!item) {
        resetEvidenceState();
        setEvidenceState("empty", "Select a queue item detail before opening evidence.");
        return;
      }

      const entries = evidenceEntries();
      evidenceListNode.innerHTML = "";
      if (entries.length === 0) {
        state.evidenceIndex = -1;
        evidenceFocusNode.innerHTML = "<p class='muted'>No bounded evidence summaries were returned for this item.</p>";
        setEvidenceState("empty", "No evidence summaries available for the selected item.");
        evidencePrevButton.disabled = true;
        evidenceNextButton.disabled = true;
        return;
      }

      entries.forEach(function(entry, index) {
        const card = document.createElement("article");
        card.className = "evidence-card";
        const summary = document.createElement("p");
        summary.textContent = entry.summary || "No summary.";
        const meta = document.createElement("p");
        meta.className = "muted";
        meta.textContent = "Сигнал уверенности " + formatEvidenceTrustCue(entry) + " | " + formatUtc(entry.observedAtUtc);
        const prov = document.createElement("p");
        prov.className = "muted";
        prov.textContent = (entry.sourceLabel || "n/a") + " | " + (entry.sourceRef || "n/a");
        const sender = document.createElement("p");
        sender.className = "muted";
        sender.textContent = "Отправитель: " + (entry.senderDisplay || "не определен");
        const relevance = document.createElement("p");
        relevance.className = "muted";
        relevance.textContent = "Почему показано: "
          + (entry.relevanceHint || "Контекстный сигнал для ручной проверки.")
          + (entry.relevanceHintIsHeuristic !== false ? " (эвристика)" : "");
        const decisionLink = document.createElement("p");
        decisionLink.className = "muted";
        decisionLink.textContent = "Эвристическая связь с решением: " + formatDecisionLinkage(entry.decisionLinkage);
        card.appendChild(summary);
        card.appendChild(meta);
        card.appendChild(sender);
        card.appendChild(relevance);
        card.appendChild(decisionLink);
        card.appendChild(prov);
        card.addEventListener("click", function() {
          selectEvidence(index);
        });
        evidenceListNode.appendChild(card);
      });

      setEvidenceState("loading", "Evidence panel loaded for " + (item.scopeItemKey || "selected item") + ".");
      selectEvidence(0);
    }

    function openEvidenceDrawer() {
      state.evidenceDrawerOpen = true;
      evidenceBackdropNode.classList.add("open");
      evidenceDrawerNode.classList.add("open");
      evidenceBackdropNode.setAttribute("aria-hidden", "false");
      evidenceDrawerNode.setAttribute("aria-hidden", "false");
      renderEvidencePanelFromSelectedDetail();
    }

    function closeEvidenceDrawer() {
      state.evidenceDrawerOpen = false;
      evidenceBackdropNode.classList.remove("open");
      evidenceDrawerNode.classList.remove("open");
      evidenceBackdropNode.setAttribute("aria-hidden", "true");
      evidenceDrawerNode.setAttribute("aria-hidden", "true");
    }

    async function refreshEvidenceFromDetailContext() {
      if (!state.selectedScopeItemKey || !state.activeTrackedPersonId) {
        return;
      }

      await loadDetail(state.selectedScopeItemKey);
      if (state.evidenceDrawerOpen) {
        renderEvidencePanelFromSelectedDetail();
      }
    }

    tokenInput.value = readAccessToken();
    createChecklist("priority-filters", "Priority", priorityValues);
    createChecklist("status-filters", "Status", statusValues);
    createChecklist("type-filters", "Item type", typeValues);

    refreshPeopleButton.addEventListener("click", refreshAll);
    applyPersonButton.addEventListener("click", onApplyScope);
    refreshQueueButton.addEventListener("click", loadQueue);
    sortBySelect.addEventListener("change", loadQueue);
    sortDirectionSelect.addEventListener("change", loadQueue);
    evidenceCloseButton.addEventListener("click", closeEvidenceDrawer);
    evidenceBackdropNode.addEventListener("click", closeEvidenceDrawer);
    evidencePrevButton.addEventListener("click", function() {
      selectEvidence(state.evidenceIndex - 1);
    });
    evidenceNextButton.addEventListener("click", function() {
      selectEvidence(state.evidenceIndex + 1);
    });
    evidenceSortBySelect.addEventListener("change", refreshEvidenceFromDetailContext);
    evidenceSortDirectionSelect.addEventListener("change", refreshEvidenceFromDetailContext);
    evidenceLimitSelect.addEventListener("change", refreshEvidenceFromDetailContext);
    clarificationCloseButton.addEventListener("click", closeClarificationDrawer);
    clarificationCancelButton.addEventListener("click", closeClarificationDrawer);
    clarificationBackdropNode.addEventListener("click", closeClarificationDrawer);
    clarificationSubmitButton.addEventListener("click", submitClarifyAction);
    window.addEventListener("keydown", function(event) {
      if (event.key === "Escape" && state.evidenceDrawerOpen) {
        closeEvidenceDrawer();
      }

      if (event.key === "Escape" && state.clarificationDrawerOpen) {
        closeClarificationDrawer();
      }
    });

    document.querySelectorAll("#priority-filters input, #status-filters input, #type-filters input")
      .forEach(function(node) {
        node.addEventListener("change", loadQueue);
      });

    refreshAll();
  </script>
</body>
</html>
""";

    private const string OperatorOfflineEventsHtml = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Operator Offline Events</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #f4f8fb;
      --panel: #ffffff;
      --line: #d7e3ed;
      --ink: #16324a;
      --muted: #587188;
      --accent: #0f5e87;
      --warn: #a12222;
      --ok: #177145;
      --chip: #ecf3fa;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      background: linear-gradient(180deg, #e8f1f8, var(--bg) 220px);
      color: var(--ink);
    }
    main {
      max-width: 1220px;
      margin: 24px auto;
      padding: 0 14px;
      display: grid;
      grid-template-columns: 320px 1fr;
      gap: 14px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 14px;
      box-shadow: 0 10px 22px rgba(17, 44, 68, 0.08);
    }
    .stack { display: grid; gap: 10px; }
    .row {
      display: flex;
      gap: 8px;
      align-items: center;
      flex-wrap: wrap;
    }
    .row > * { flex: 1 1 140px; }
    label {
      display: grid;
      gap: 6px;
      font-size: 13px;
      color: var(--muted);
    }
    input, select, textarea, button, a {
      font: inherit;
      color: inherit;
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 8px 10px;
      background: #fff;
      text-decoration: none;
    }
    button { cursor: pointer; }
    button.primary {
      background: var(--accent);
      border-color: var(--accent);
      color: #fff;
    }
    .state {
      border-left: 4px solid var(--accent);
      background: #f7fbff;
      border-radius: 8px;
      padding: 10px;
      font-size: 14px;
    }
    .state.loading { border-left-color: var(--accent); }
    .state.empty { border-left-color: var(--ok); }
    .state.error { border-left-color: var(--warn); background: #fff5f5; }
    .muted { color: var(--muted); }
    .layout {
      display: grid;
      grid-template-columns: 330px 1fr;
      gap: 12px;
      align-items: start;
    }
    .events-list {
      max-height: 72vh;
      overflow: auto;
      display: grid;
      gap: 8px;
      padding-right: 2px;
    }
    .event-card {
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 10px;
      background: #fcfeff;
      cursor: pointer;
    }
    .event-card.active {
      border-color: var(--accent);
      box-shadow: inset 0 0 0 1px var(--accent);
      background: #f0f8ff;
    }
    .event-card h3 {
      margin: 0 0 6px;
      font-size: 15px;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-top: 8px;
      font-size: 12px;
    }
    .meta span {
      background: var(--chip);
      border-radius: 999px;
      padding: 3px 8px;
    }
    .detail-grid {
      display: grid;
      gap: 10px;
    }
    .detail-block {
      border: 1px solid var(--line);
      border-radius: 10px;
      background: #fcfeff;
      padding: 10px;
    }
    .detail-block h3,
    .detail-block h4 {
      margin: 0 0 8px;
    }
    .detail-block p {
      margin: 6px 0;
      font-size: 14px;
    }
    .history-list {
      max-height: 220px;
      overflow: auto;
      display: grid;
      gap: 8px;
    }
    .history-item {
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 8px;
      background: #fff;
      font-size: 13px;
    }
    .history-item p { margin: 4px 0; }
    .refine-actions {
      display: grid;
      gap: 8px;
    }
    @media (max-width: 980px) {
      main { grid-template-columns: 1fr; }
      .layout { grid-template-columns: 1fr; }
      .events-list { max-height: none; }
    }
  </style>
</head>
<body>
  <main>
    <section class="panel stack">
      <div>
        <h1>Offline Events</h1>
        <p class="muted">Web inspection and bounded refinement for Telegram-captured offline events.</p>
      </div>

      <label>
        Operator access token
        <input id="access-token" type="password" autocomplete="off" placeholder="X-Tga-Operator-Key">
      </label>

      <label>
        Tracked person
        <select id="tracked-person"></select>
      </label>
      <div class="row">
        <button id="refresh-people" type="button">Refresh scope</button>
        <button id="apply-person" type="button">Apply scope</button>
      </div>

      <label>
        Status filter
        <select id="status-filter">
          <option value="">All statuses</option>
          <option value="draft">Draft</option>
          <option value="captured">Captured</option>
          <option value="saved" selected>Saved</option>
          <option value="archived">Archived</option>
        </select>
      </label>

      <label>
        Sort
        <select id="sort-by">
          <option value="updated_at" selected>Updated at</option>
          <option value="captured_at">Captured at</option>
          <option value="saved_at">Saved at</option>
          <option value="created_at">Created at</option>
        </select>
      </label>

      <div class="row">
        <button id="refresh-events" class="primary" type="button">Refresh events</button>
        <a href="/operator">Back to home</a>
      </div>
      <div id="state" class="state loading">Loading tracked person scope...</div>
    </section>

    <section class="panel stack">
      <h2>Inspection + Refinement</h2>
      <div class="layout">
        <div class="stack">
          <div id="counts" class="meta"></div>
          <div id="events-list" class="events-list"></div>
        </div>

        <div class="detail-grid">
          <div id="detail-state" class="state empty">Select an offline event to inspect detail.</div>
          <section id="detail-content" class="detail-grid"></section>
        </div>
      </div>
    </section>
  </main>

  <script>
    const stateNode = document.getElementById("state");
    const countsNode = document.getElementById("counts");
    const eventsListNode = document.getElementById("events-list");
    const detailStateNode = document.getElementById("detail-state");
    const detailContentNode = document.getElementById("detail-content");
    const tokenInput = document.getElementById("access-token");
    const trackedPersonSelect = document.getElementById("tracked-person");
    const refreshPeopleButton = document.getElementById("refresh-people");
    const applyPersonButton = document.getElementById("apply-person");
    const refreshEventsButton = document.getElementById("refresh-events");
    const statusFilterSelect = document.getElementById("status-filter");
    const sortBySelect = document.getElementById("sort-by");

    const appState = {
      trackedPersons: [],
      activeTrackedPersonId: null,
      scopeKey: "",
      items: [],
      selectedOfflineEventId: null,
      detail: null
    };

    function titleize(value) {
      return String(value || "").replaceAll("_", " ").replace(/\b\w/g, function(c) { return c.toUpperCase(); });
    }

    function formatUtc(value) {
      if (!value) { return "n/a"; }
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) { return value; }
      return date.toLocaleString();
    }

    function formatPercent(value) {
      const numeric = Number(value);
      if (Number.isNaN(numeric)) { return "n/a"; }
      return Math.round(numeric * 100) + "%";
    }

    function setState(kind, message) {
      stateNode.className = "state " + kind;
      stateNode.textContent = message;
    }

    function setDetailState(kind, message) {
      detailStateNode.className = "state " + kind;
      detailStateNode.textContent = message;
    }

    function readAccessToken() {
      return window.localStorage.getItem("operator_web_access_token") || "";
    }

    function writeAccessToken(token) {
      window.localStorage.setItem("operator_web_access_token", token);
      document.cookie = "tga_operator_key=" + encodeURIComponent(token) + "; path=/; SameSite=Lax";
    }

    function resolveHeaders() {
      const token = readAccessToken();
      const headers = { "accept": "application/json", "content-type": "application/json" };
      if (token) {
        headers["X-Tga-Operator-Key"] = token;
      }
      return headers;
    }

    async function operatorPostJson(path, payload) {
      const response = await fetch(path, {
        method: "POST",
        credentials: "same-origin",
        headers: resolveHeaders(),
        body: JSON.stringify(payload || {})
      });

      let body = null;
      try {
        body = await response.json();
      } catch (_) {
        body = null;
      }

      if (!response.ok) {
        const reason = body && (body.failureReason || body.reason || body.message)
          ? (body.failureReason || body.reason || body.message)
          : "request_failed";
        const error = new Error(reason);
        error.status = response.status;
        throw error;
      }

      return body || {};
    }

    function renderTrackedPersons(result) {
      const trackedPersons = Array.isArray(result.trackedPersons) ? result.trackedPersons : [];
      appState.trackedPersons = trackedPersons;
      appState.activeTrackedPersonId = result.activeTrackedPersonId || null;
      trackedPersonSelect.innerHTML = "";

      if (trackedPersons.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = "No tracked persons available";
        trackedPersonSelect.appendChild(option);
        trackedPersonSelect.disabled = true;
        return;
      }

      trackedPersonSelect.disabled = false;
      trackedPersons.forEach(function(person) {
        const option = document.createElement("option");
        option.value = person.trackedPersonId;
        option.textContent = (person.displayName || "Tracked person") + (person.scopeKey ? " | " + person.scopeKey : "");
        if (appState.activeTrackedPersonId && person.trackedPersonId === appState.activeTrackedPersonId) {
          option.selected = true;
        }
        trackedPersonSelect.appendChild(option);
      });

      if (!appState.activeTrackedPersonId && trackedPersonSelect.options.length > 0) {
        trackedPersonSelect.selectedIndex = 0;
      }
    }

    function renderCounts(result) {
      countsNode.innerHTML = "";
      [
        "Total: " + (result.totalCount || 0),
        "Filtered: " + (result.filteredCount || 0),
        "Scope: " + (result.scopeKey || "n/a")
      ].forEach(function(text) {
        const chip = document.createElement("span");
        chip.textContent = text;
        countsNode.appendChild(chip);
      });
    }

    function renderEvents(result) {
      const items = Array.isArray(result.items) ? result.items : [];
      appState.items = items;
      appState.scopeKey = result.scopeKey || "";
      eventsListNode.innerHTML = "";
      if (items.length === 0) {
        setState("empty", "No offline events match current filters.");
        clearDetail("No offline event selected.");
        return;
      }

      items.forEach(function(item) {
        const card = document.createElement("article");
        card.className = "event-card";
        card.setAttribute("role", "button");
        card.tabIndex = 0;
        card.dataset.id = item.offlineEventId || "";
        const summary = item.summary || "Offline event";
        card.innerHTML =
          "<h3>" + summary + "</h3>" +
          "<p class='muted'>Status: " + titleize(item.status || "unknown")
            + " | Trust: " + formatPercent(item.confidence)
            + " | Updated: " + formatUtc(item.updatedAtUtc) + "</p>";
        const meta = document.createElement("div");
        meta.className = "meta";
        [
          "Captured: " + formatUtc(item.capturedAtUtc),
          "Saved: " + formatUtc(item.savedAtUtc),
          "Linkage: " + titleize(item.timelineLinkage && item.timelineLinkage.linkageStatus ? item.timelineLinkage.linkageStatus : "unlinked")
        ].forEach(function(text) {
          const tag = document.createElement("span");
          tag.textContent = text;
          meta.appendChild(tag);
        });
        card.appendChild(meta);
        card.addEventListener("click", function() {
          if (!item.offlineEventId) {
            return;
          }
          selectOfflineEvent(item.offlineEventId);
        });
        card.addEventListener("keydown", function(event) {
          if (event.key !== "Enter" && event.key !== " ") {
            return;
          }

          event.preventDefault();
          if (!item.offlineEventId) {
            return;
          }

          selectOfflineEvent(item.offlineEventId);
        });
        eventsListNode.appendChild(card);
      });

      if (!appState.selectedOfflineEventId && items[0] && items[0].offlineEventId) {
        selectOfflineEvent(items[0].offlineEventId);
      } else {
        syncActiveCard();
      }

      setState("success", "Offline-event list loaded.");
    }

    function clearDetail(message) {
      appState.selectedOfflineEventId = null;
      appState.detail = null;
      detailContentNode.innerHTML = "";
      setDetailState("empty", message || "Select an offline event to inspect detail.");
      syncActiveCard();
    }

    function syncActiveCard() {
      document.querySelectorAll("#events-list .event-card").forEach(function(node) {
        const isActive = appState.selectedOfflineEventId
          && node.dataset.id
          && node.dataset.id === appState.selectedOfflineEventId;
        node.classList.toggle("active", !!isActive);
      });
    }

    function renderClarificationSummary(detail) {
      const wrapper = document.createElement("section");
      wrapper.className = "detail-block";
      wrapper.innerHTML = "<h4>Clarification Summary</h4>";

      const meta = document.createElement("div");
      meta.className = "meta";
      [
        "History count: " + (detail.clarificationHistoryCount || 0),
        "Stop reason: " + titleize(detail.stopReason || "none")
      ].forEach(function(text) {
        const tag = document.createElement("span");
        tag.textContent = text;
        meta.appendChild(tag);
      });
      wrapper.appendChild(meta);
      return wrapper;
    }

    function renderRefinementForm(detail) {
      const block = document.createElement("section");
      block.className = "detail-block refine-actions";
      block.innerHTML = "<h4>Refine Offline Event</h4>";

      const summaryLabel = document.createElement("label");
      summaryLabel.textContent = "Refined summary";
      const summaryInput = document.createElement("textarea");
      summaryInput.rows = 3;
      summaryInput.maxLength = 2000;
      summaryInput.value = detail.summary || "";
      summaryLabel.appendChild(summaryInput);
      block.appendChild(summaryLabel);

      const recordingLabel = document.createElement("label");
      recordingLabel.textContent = "Recording reference";
      const recordingInput = document.createElement("input");
      recordingInput.type = "text";
      recordingInput.maxLength = 1000;
      recordingInput.value = detail.recordingReference || "";
      recordingLabel.appendChild(recordingInput);
      block.appendChild(recordingLabel);

      const noteLabel = document.createElement("label");
      noteLabel.textContent = "Refinement note";
      const noteInput = document.createElement("input");
      noteInput.type = "text";
      noteInput.maxLength = 280;
      noteInput.placeholder = "Optional bounded context note";
      noteLabel.appendChild(noteInput);
      block.appendChild(noteLabel);

      const linkageStatus = detail.linkageTargetFamily && detail.linkageTargetRef ? "linked" : "unlinked";
      const linkageLabel = document.createElement("label");
      linkageLabel.textContent = "Timeline linkage status";
      const linkageStatusSelect = document.createElement("select");
      ["unlinked", "linked", "review_needed"].forEach(function(status) {
        const option = document.createElement("option");
        option.value = status;
        option.textContent = titleize(status);
        if (linkageStatus === status) {
          option.selected = true;
        }
        linkageStatusSelect.appendChild(option);
      });
      linkageLabel.appendChild(linkageStatusSelect);
      block.appendChild(linkageLabel);

      const linkageTargetFamilyLabel = document.createElement("label");
      linkageTargetFamilyLabel.textContent = "Timeline target family";
      const linkageTargetFamilyInput = document.createElement("input");
      linkageTargetFamilyInput.type = "text";
      linkageTargetFamilyInput.maxLength = 128;
      linkageTargetFamilyInput.value = detail.linkageTargetFamily || "";
      linkageTargetFamilyLabel.appendChild(linkageTargetFamilyInput);
      block.appendChild(linkageTargetFamilyLabel);

      const linkageTargetRefLabel = document.createElement("label");
      linkageTargetRefLabel.textContent = "Timeline target ref";
      const linkageTargetRefInput = document.createElement("input");
      linkageTargetRefInput.type = "text";
      linkageTargetRefInput.maxLength = 256;
      linkageTargetRefInput.value = detail.linkageTargetRef || "";
      linkageTargetRefLabel.appendChild(linkageTargetRefInput);
      block.appendChild(linkageTargetRefLabel);

      const feedback = document.createElement("div");
      feedback.className = "state empty";
      feedback.textContent = "Refinement is bounded to the selected offline event and active operator session.";
      block.appendChild(feedback);

      const saveButton = document.createElement("button");
      saveButton.type = "button";
      saveButton.className = "primary";
      saveButton.textContent = "Save refinement";
      saveButton.addEventListener("click", async function() {
        if (!appState.selectedOfflineEventId || !appState.activeTrackedPersonId) {
          feedback.className = "state error";
          feedback.textContent = "Select tracked person and offline event before saving refinement.";
          return;
        }

        saveButton.disabled = true;
        feedback.className = "state loading";
        feedback.textContent = "Saving refinement...";
        try {
          const refinedSummary = summaryInput.value.trim();
          const refinedRecording = recordingInput.value.trim();
          const clearRecordingReference = refinedRecording.length === 0;
          const result = await operatorPostJson("/api/operator/offline-events/refine", {
            trackedPersonId: appState.activeTrackedPersonId,
            offlineEventId: appState.selectedOfflineEventId,
            summary: refinedSummary.length > 0 ? refinedSummary : null,
            recordingReference: clearRecordingReference ? null : refinedRecording,
            clearRecordingReference: clearRecordingReference,
            refinementNote: noteInput.value.trim() || null,
            submittedAtUtc: new Date().toISOString()
          });
          if (!result.accepted || !result.offlineEvent || !result.offlineEvent.found) {
            throw new Error(result.failureReason || "offline_event_refinement_rejected");
          }

          appState.detail = result.offlineEvent;
          feedback.className = "state success";
          feedback.textContent = "Refinement saved."
            + (result.auditEventId ? " Audit event: " + result.auditEventId + "." : "");
          await loadEvents();
          await loadSelectedDetail();
        } catch (error) {
          feedback.className = "state error";
          feedback.textContent = "Refinement failed: " + (error && error.message ? error.message : "unknown_error");
        } finally {
          saveButton.disabled = false;
        }
      });
      block.appendChild(saveButton);

      const linkageButton = document.createElement("button");
      linkageButton.type = "button";
      linkageButton.textContent = "Update timeline linkage";
      linkageButton.addEventListener("click", async function() {
        if (!appState.selectedOfflineEventId || !appState.activeTrackedPersonId) {
          feedback.className = "state error";
          feedback.textContent = "Select tracked person and offline event before updating linkage.";
          return;
        }

        const linkageStatus = (linkageStatusSelect.value || "unlinked").trim();
        const targetFamily = linkageTargetFamilyInput.value.trim();
        const targetRef = linkageTargetRefInput.value.trim();
        if (linkageStatus === "linked" && (!targetFamily || !targetRef)) {
          feedback.className = "state error";
          feedback.textContent = "Target family and target ref are required for linked status.";
          return;
        }

        linkageButton.disabled = true;
        feedback.className = "state loading";
        feedback.textContent = "Updating timeline linkage...";
        try {
          const result = await operatorPostJson("/api/operator/offline-events/timeline-linkage", {
            trackedPersonId: appState.activeTrackedPersonId,
            offlineEventId: appState.selectedOfflineEventId,
            linkageStatus: linkageStatus,
            targetFamily: linkageStatus === "linked" ? targetFamily : null,
            targetRef: linkageStatus === "linked" ? targetRef : null,
            linkageNote: noteInput.value.trim() || null,
            submittedAtUtc: new Date().toISOString()
          });
          if (!result.accepted || !result.offlineEvent || !result.offlineEvent.found) {
            throw new Error(result.failureReason || "offline_event_timeline_linkage_update_rejected");
          }

          appState.detail = result.offlineEvent;
          feedback.className = "state success";
          feedback.textContent = "Timeline linkage updated."
            + (result.auditEventId ? " Audit event: " + result.auditEventId + "." : "");
          await loadEvents();
          await loadSelectedDetail();
        } catch (error) {
          feedback.className = "state error";
          feedback.textContent = "Timeline linkage update failed: " + (error && error.message ? error.message : "unknown_error");
        } finally {
          linkageButton.disabled = false;
        }
      });
      block.appendChild(linkageButton);
      return block;
    }

    function renderDetail(detail) {
      appState.detail = detail;
      detailContentNode.innerHTML = "";

      const summaryBlock = document.createElement("section");
      summaryBlock.className = "detail-block";
      summaryBlock.innerHTML =
        "<h3>Offline Event Detail</h3>" +
        "<p><strong>ID:</strong> " + (detail.id || "n/a") + "</p>" +
        "<p><strong>Scope:</strong> " + (detail.scopeKey || "n/a") + "</p>" +
        "<p><strong>Summary:</strong> " + (detail.summary || "n/a") + "</p>" +
        "<p><strong>Trust:</strong> " + formatPercent(detail.confidence) + "</p>";
      detailContentNode.appendChild(summaryBlock);

      const statusBlock = document.createElement("section");
      statusBlock.className = "detail-block";
      const linkageStatus = detail.linkageTargetFamily && detail.linkageTargetRef ? "linked" : "unlinked";
      statusBlock.innerHTML = "<h4>Status + Linkage</h4>";
      const meta = document.createElement("div");
      meta.className = "meta";
      [
        "Linkage status: " + titleize(linkageStatus),
        "Target family: " + (detail.linkageTargetFamily || "n/a"),
        "Target ref: " + (detail.linkageTargetRef || "n/a"),
        "Scope bound: " + (detail.scopeBound ? "yes" : "no"),
        "Found: " + (detail.found ? "yes" : "no")
      ].forEach(function(text) {
        const tag = document.createElement("span");
        tag.textContent = text;
        meta.appendChild(tag);
      });
      statusBlock.appendChild(meta);
      detailContentNode.appendChild(statusBlock);

      detailContentNode.appendChild(renderClarificationSummary(detail));
      detailContentNode.appendChild(renderRefinementForm(detail));
      setDetailState("success", "Offline-event detail loaded.");
      syncActiveCard();
    }

    async function loadTrackedPersons() {
      setState("loading", "Loading tracked person scope...");
      const result = await operatorPostJson("/api/operator/tracked-persons/query", { limit: 50 });
      if (!result.accepted) {
        throw new Error(result.failureReason || "tracked_person_query_rejected");
      }

      renderTrackedPersons(result);
      if (!appState.activeTrackedPersonId && trackedPersonSelect.value) {
        appState.activeTrackedPersonId = trackedPersonSelect.value;
      }
    }

    async function applyTrackedPersonSelection() {
      const selected = trackedPersonSelect.value;
      if (!selected) {
        throw new Error("tracked_person_selection_required");
      }

      const result = await operatorPostJson("/api/operator/tracked-persons/select", {
        trackedPersonId: selected,
        requestedAtUtc: new Date().toISOString()
      });
      if (!result.accepted) {
        throw new Error(result.failureReason || "tracked_person_select_rejected");
      }
      appState.activeTrackedPersonId = result.activeTrackedPerson && result.activeTrackedPerson.trackedPersonId
        ? result.activeTrackedPerson.trackedPersonId
        : selected;
    }

    async function loadEvents() {
      const trackedPersonId = appState.activeTrackedPersonId || trackedPersonSelect.value;
      if (!trackedPersonId) {
        setState("empty", "Select a tracked person before reading offline events.");
        eventsListNode.innerHTML = "";
        countsNode.innerHTML = "";
        clearDetail("Select a tracked person to inspect offline-event detail.");
        return;
      }

      setState("loading", "Loading offline events...");
      const status = statusFilterSelect.value;
      const result = await operatorPostJson("/api/operator/offline-events/query", {
        trackedPersonId: trackedPersonId,
        statuses: status ? [status] : [],
        sortBy: sortBySelect.value,
        sortDirection: "desc",
        limit: 100
      });
      if (!result.accepted) {
        throw new Error(result.failureReason || "offline_event_query_rejected");
      }

      renderCounts(result.offlineEvents || {});
      renderEvents(result.offlineEvents || {});
    }

    async function loadSelectedDetail() {
      if (!appState.selectedOfflineEventId) {
        clearDetail("Select an offline event to inspect detail.");
        return;
      }
      if (!appState.activeTrackedPersonId) {
        clearDetail("Select tracked person before loading offline-event detail.");
        return;
      }

      setDetailState("loading", "Loading offline-event detail...");
      const result = await operatorPostJson("/api/operator/offline-events/detail", {
        trackedPersonId: appState.activeTrackedPersonId,
        offlineEventId: appState.selectedOfflineEventId
      });
      if (!result.accepted || !result.offlineEvent || !result.offlineEvent.found) {
        throw new Error(result.failureReason || "offline_event_not_found");
      }

      renderDetail(result.offlineEvent);
    }

    async function selectOfflineEvent(offlineEventId) {
      if (!offlineEventId) {
        return;
      }
      appState.selectedOfflineEventId = offlineEventId;
      syncActiveCard();
      try {
        await loadSelectedDetail();
      } catch (error) {
        const reason = error && error.message ? error.message : "unknown_error";
        setDetailState("error", "Offline-event detail load failed: " + describeFailureReason(reason));
      }
    }

    async function refreshAll() {
      try {
        const token = tokenInput.value.trim();
        if (token) {
          writeAccessToken(token);
        }
        await loadTrackedPersons();
        await loadEvents();
      } catch (error) {
        const reason = error && error.message ? error.message : "unknown_error";
        setState("error", "Offline-event load failed: " + describeFailureReason(reason));
      }
    }

    async function onApplyScope() {
      try {
        const token = tokenInput.value.trim();
        if (token) {
          writeAccessToken(token);
        }
        await applyTrackedPersonSelection();
        clearDetail("Tracked person scope updated. Select an offline event to inspect detail.");
        await loadEvents();
      } catch (error) {
        const reason = error && error.message ? error.message : "unknown_error";
        setState("error", "Scope update failed: " + describeFailureReason(reason));
      }
    }

    tokenInput.value = readAccessToken();
    refreshPeopleButton.addEventListener("click", refreshAll);
    applyPersonButton.addEventListener("click", onApplyScope);
    refreshEventsButton.addEventListener("click", loadEvents);
    statusFilterSelect.addEventListener("change", loadEvents);
    sortBySelect.addEventListener("change", loadEvents);
    refreshAll();
  </script>
</body>
</html>
""";
}

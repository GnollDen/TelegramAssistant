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
      <p class="critical">Critical unresolved items: resolution queue connected; detail/evidence/action slices remain.</p>
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

    const state = {
      trackedPersons: [],
      activeTrackedPersonId: null,
      queue: null,
      selectedScopeItemKey: null,
      selectedDetailItem: null,
      evidenceIndex: -1,
      evidenceDrawerOpen: false,
      clarificationDrawerOpen: false,
      actionSubmitting: false,
      clarificationSubmitting: false,
      toggleDetailActionButtons: null
    };

    function setState(kind, message) {
      stateNode.className = "state " + kind;
      stateNode.textContent = message;
    }

    function setDetailState(kind, message) {
      detailStateNode.className = "state " + kind;
      detailStateNode.textContent = message;
    }

    function titleize(value) {
      return value.replaceAll("_", " ").replace(/\b\w/g, function(c) { return c.toUpperCase(); });
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

        const title = document.createElement("h3");
        title.textContent = item.title || item.scopeItemKey;

        const priority = document.createElement("strong");
        const priorityValue = (item.priority || "unknown").toLowerCase();
        priority.className = "priority-" + priorityValue;
        priority.textContent = titleize(priorityValue);

        top.appendChild(title);
        top.appendChild(priority);
        card.appendChild(top);

        const summary = document.createElement("p");
        summary.textContent = item.summary || "No summary.";
        card.appendChild(summary);

        const why = document.createElement("p");
        why.className = "muted";
        why.textContent = "Why it matters: " + (item.whyItMatters || "Not provided.");
        card.appendChild(why);

        const meta = document.createElement("div");
        meta.className = "meta";

        const metaValues = [
          "Type: " + titleize(item.itemType || "unknown"),
          "Status: " + titleize(item.status || "unknown"),
          "Trust: " + formatPercent(item.trustFactor),
          "Evidence: " + (item.evidenceCount || 0),
          "Updated: " + formatUtc(item.updatedAtUtc),
          "Family: " + (item.affectedFamily || "n/a"),
          "Action: " + titleize(item.recommendedNextAction || "none")
        ];

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
      setState("loading", "Queue loaded from bounded operator projection.");
    }

    function clearDetail(message) {
      state.selectedScopeItemKey = null;
      state.selectedDetailItem = null;
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

      const summaryBlock = document.createElement("section");
      summaryBlock.className = "detail-block";
      summaryBlock.innerHTML =
        "<h4>" + (item.title || item.scopeItemKey || "Untitled item") + "</h4>" +
        "<p><strong>Summary:</strong> " + (item.summary || "No summary.") + "</p>" +
        "<p><strong>Why it matters:</strong> " + (item.whyItMatters || "Not provided.") + "</p>";
      detailContentNode.appendChild(summaryBlock);

      const statusBlock = document.createElement("section");
      statusBlock.className = "detail-block";
      const statusMeta = [
        "Type: " + titleize(item.itemType || "unknown"),
        "Status: " + titleize(item.status || "unknown"),
        "Priority: " + titleize(item.priority || "unknown"),
        "Trust: " + formatPercent(item.trustFactor),
        "Evidence count: " + (item.evidenceCount || 0),
        "Updated: " + formatUtc(item.updatedAtUtc),
        "Family: " + (item.affectedFamily || "n/a"),
        "Object: " + (item.affectedObjectRef || "n/a"),
        "Scope item: " + (item.scopeItemKey || "n/a"),
        "Recommended action: " + titleize(item.recommendedNextAction || "none")
      ];
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

          const recomputeStatus = action.recompute && action.recompute.lifecycleStatus
            ? titleize(action.recompute.lifecycleStatus)
            : "n/a";
          const actionId = action.actionId || "n/a";
          const auditEventId = action.auditEventId || "n/a";
          actionFeedback.className = "state empty action-feedback";
          actionFeedback.textContent = titleize(actionType) + " accepted. Recompute: " + recomputeStatus + ". Action ID: " + actionId + ". Audit: " + auditEventId + ".";
          await loadQueue();
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

      setDetailState("loading", "Detail loaded for selected queue item.");
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

        const recomputeStatus = action.recompute && action.recompute.lifecycleStatus
          ? titleize(action.recompute.lifecycleStatus)
          : "n/a";
        const actionId = action.actionId || "n/a";
        const auditEventId = action.auditEventId || "n/a";
        setClarificationState("empty", "Clarify accepted. Recompute: " + recomputeStatus + ". Action ID: " + actionId + ". Audit: " + auditEventId + ".");
        closeClarificationDrawer();
        await loadQueue();
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

        setDetailState("error", "Detail load failed: " + message);
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

        const previousTrackedPersonId = state.activeTrackedPersonId;
        await loadTrackedPersons();
        if (previousTrackedPersonId && previousTrackedPersonId !== state.activeTrackedPersonId) {
          clearDetail("Tracked person changed. Select an item to inspect detail.");
        }

        if (state.activeTrackedPersonId) {
          await loadQueue();
        } else {
          setState("empty", "Select a tracked person to load queue data.");
          queueNode.innerHTML = "";
          countsNode.innerHTML = "";
          clearDetail("Select a tracked person to inspect resolution detail.");
        }
      } catch (error) {
        setState("error", "Resolution queue load failed: " + (error && error.message ? error.message : "unknown_error"));
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
        setState("error", "Scope update failed: " + (error && error.message ? error.message : "unknown_error"));
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
      trust.innerHTML = "<strong>Confidence cue:</strong> " + formatPercent(entry.trustFactor);
      evidenceFocusNode.appendChild(trust);

      const observed = document.createElement("p");
      observed.innerHTML = "<strong>Observed:</strong> " + formatUtc(entry.observedAtUtc);
      evidenceFocusNode.appendChild(observed);

      const source = document.createElement("p");
      source.innerHTML = "<strong>Provenance:</strong> " + (entry.sourceLabel || "n/a") + " | " + (entry.sourceRef || "n/a");
      evidenceFocusNode.appendChild(source);

      const evidenceId = document.createElement("p");
      evidenceId.innerHTML = "<strong>Evidence ID:</strong> " + (entry.evidenceItemId || "n/a");
      evidenceFocusNode.appendChild(evidenceId);

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
        meta.textContent = "Trust " + formatPercent(entry.trustFactor) + " | " + formatUtc(entry.observedAtUtc);
        const prov = document.createElement("p");
        prov.className = "muted";
        prov.textContent = (entry.sourceLabel || "n/a") + " | " + (entry.sourceRef || "n/a");
        card.appendChild(summary);
        card.appendChild(meta);
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
}

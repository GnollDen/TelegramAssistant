namespace TgAssistant.Host.OperatorWeb;

public static class OperatorAlertsWebShell
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Operator Alerts</title>
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
      --critical: #7d1025;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Segoe UI", "Noto Sans", sans-serif;
      color: var(--ink);
      background: linear-gradient(180deg, #e7eefb, var(--bg) 220px);
    }
    main {
      max-width: 1160px;
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
      align-items: end;
    }
    label {
      display: grid;
      gap: 6px;
      color: var(--muted);
      font-size: 13px;
      flex: 1 1 180px;
    }
    input, select, button, a {
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
      border-left: 4px solid var(--accent);
      background: #f8fbff;
      padding: 10px;
      border-radius: 8px;
      font-size: 14px;
      margin-top: 10px;
    }
    .state.loading { border-left-color: var(--accent); }
    .state.success { border-left-color: var(--ok); background: #ecf8f0; }
    .state.empty { border-left-color: var(--ok); }
    .state.error { border-left-color: var(--warn); background: #fff6f6; }
    .counts {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      margin-top: 8px;
      color: var(--muted);
      font-size: 13px;
    }
    .counts span, .chip {
      border: 1px solid var(--line);
      border-radius: 999px;
      background: var(--chip);
      padding: 3px 9px;
    }
    .muted { color: var(--muted); }
    #alerts-groups {
      display: grid;
      gap: 12px;
    }
    .group-card {
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 12px;
      background: #fbfdff;
      display: grid;
      gap: 10px;
    }
    .group-header {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      flex-wrap: wrap;
      align-items: start;
    }
    .group-header h3 {
      margin: 0 0 4px;
      font-size: 18px;
    }
    .group-actions {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    .group-actions a {
      background: #f7faff;
    }
    .alert-list {
      display: grid;
      gap: 10px;
    }
    .alert-card {
      border: 1px solid var(--line);
      border-radius: 10px;
      padding: 12px;
      background: #fff;
      display: grid;
      gap: 8px;
    }
    .alert-card header {
      display: flex;
      justify-content: space-between;
      gap: 8px;
      flex-wrap: wrap;
      align-items: start;
    }
    .alert-card h4 {
      margin: 0;
      font-size: 16px;
    }
    .alert-card p {
      margin: 0;
      font-size: 14px;
      line-height: 1.45;
    }
    .priority {
      color: var(--critical);
      font-weight: 700;
    }
    .meta-row, .link-row {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      align-items: center;
    }
    .meta-row span {
      border: 1px solid var(--line);
      border-radius: 999px;
      padding: 3px 8px;
      background: #f8faff;
      font-size: 12px;
      color: #2a4169;
    }
    .link-row a {
      background: #f7faff;
    }
  </style>
</head>
<body>
  <main>
    <section class="panel">
      <h1>Alerts</h1>
      <p class="muted">Workflow-critical web alert surface only. Groups are bound to tracked-person context and link directly to resolution and person views.</p>
      <div class="row">
        <label>
          Operator access token
          <input id="access-token" type="password" autocomplete="off" placeholder="X-Tga-Operator-Key">
        </label>
        <label>
          Tracked person
          <select id="tracked-person-filter">
            <option value="">All tracked persons</option>
          </select>
        </label>
        <label>
          Escalation boundary
          <select id="boundary-filter">
            <option value="all">All web-visible</option>
            <option value="web_only">Web-only</option>
            <option value="telegram_push_acknowledge">Telegram + acknowledge</option>
          </select>
        </label>
        <label>
          Search
          <input id="search-input" type="search" autocomplete="off" placeholder="Person, title, scope item, reason">
        </label>
        <button id="refresh-button" class="primary" type="button">Refresh Alerts</button>
        <a href="/operator">Back To Home</a>
      </div>
      <div id="state" class="state empty">Ready to load alerts.</div>
      <div id="counts" class="counts"></div>
    </section>
    <section class="panel">
      <h2>Grouped Alerts</h2>
      <div id="alerts-groups">
        <p class="muted">No alerts loaded yet.</p>
      </div>
    </section>
  </main>

  <script>
    const tokenInput = document.getElementById("access-token");
    const personFilter = document.getElementById("tracked-person-filter");
    const boundaryFilter = document.getElementById("boundary-filter");
    const searchInput = document.getElementById("search-input");
    const refreshButton = document.getElementById("refresh-button");
    const stateNode = document.getElementById("state");
    const countsNode = document.getElementById("counts");
    const groupsNode = document.getElementById("alerts-groups");
    const personWorkspaceBasePath = "/operator/person-workspace";
    const resolutionBasePath = "/operator/resolution";

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

    function describeFailureReason(reason) {
      switch ((reason || "").toLowerCase()) {
        case "auth_denied":
          return "Access token is invalid. Enter a valid operator token and retry.";
        case "session_expired":
          return "Session expired. Refresh and authenticate again.";
        case "alerts_boundary_not_supported":
          return "Selected escalation boundary is not supported.";
        case "tracked_person_not_found_or_inactive":
          return "Tracked person is unavailable in the current bounded scope.";
        default:
          return reason || "unknown_error";
      }
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

    function syncTrackedPersonFilter(result) {
      const groups = Array.isArray(result.groups) ? result.groups : [];
      const selected = personFilter.value;
      const options = [
        "<option value=\"\">All tracked persons</option>"
      ];
      groups.forEach(function(group) {
        const person = group.trackedPerson || {};
        if (!person.trackedPersonId) {
          return;
        }

        options.push(
          "<option value=\"" + person.trackedPersonId + "\">"
          + (person.displayName || person.trackedPersonId)
          + "</option>");
      });
      personFilter.innerHTML = options.join("");
      if (selected && Array.from(personFilter.options).some(function(option) { return option.value === selected; })) {
        personFilter.value = selected;
      }
    }

    function renderCounts(result) {
      const summary = result.summary || {};
      countsNode.innerHTML = "";
      [
        "Tracked persons " + Number(summary.trackedPersonCount || 0),
        "Groups " + Number(summary.groupCount || 0),
        "Alerts " + Number(summary.totalAlerts || 0),
        "Telegram-bound " + Number(summary.telegramPushCount || 0),
        "Web-only " + Number(summary.webOnlyCount || 0)
      ].forEach(function(text) {
        const chip = document.createElement("span");
        chip.textContent = text;
        countsNode.appendChild(chip);
      });
    }

    function renderGroups(result) {
      const groups = Array.isArray(result.groups) ? result.groups : [];
      groupsNode.innerHTML = "";
      if (groups.length === 0) {
        groupsNode.innerHTML = "<p class='muted'>No workflow-critical alerts match the current filters.</p>";
        return;
      }

      groups.forEach(function(group) {
        const person = group.trackedPerson || {};
        const card = document.createElement("article");
        card.className = "group-card";

        const header = document.createElement("div");
        header.className = "group-header";
        header.innerHTML =
          "<div>"
          + "<h3>" + (person.displayName || person.trackedPersonId || "Tracked person") + "</h3>"
          + "<div class='meta-row'>"
          + "<span>Scope " + (person.scopeKey || "n/a") + "</span>"
          + "<span>Alerts " + Number(group.alertCount || 0) + "</span>"
          + "<span>Telegram-bound " + Number(group.telegramPushCount || 0) + "</span>"
          + "<span>Web-only " + Number(group.webOnlyCount || 0) + "</span>"
          + "</div>"
          + "</div>";

        const actions = document.createElement("div");
        actions.className = "group-actions";
        actions.innerHTML =
          "<a href=\"" + (group.personWorkspaceUrl || personWorkspaceBasePath) + "\">Open Person Workspace</a>"
          + "<a href=\"" + (group.resolutionQueueUrl || resolutionBasePath) + "\">Open Resolution Queue</a>";
        header.appendChild(actions);
        card.appendChild(header);

        const alertList = document.createElement("div");
        alertList.className = "alert-list";
        (group.alerts || []).forEach(function(alert) {
          const item = document.createElement("section");
          item.className = "alert-card";
          item.innerHTML =
            "<header>"
            + "<div><h4>" + (alert.title || "Alert") + "</h4><p>" + (alert.summary || "No summary.") + "</p></div>"
            + "<div class='priority'>" + (alert.priority || "n/a") + "</div>"
            + "</header>"
            + "<p><strong>Why it matters:</strong> " + (alert.whyItMatters || "n/a") + "</p>"
            + "<div class='meta-row'>"
            + "<span>" + (alert.itemType || "item") + "</span>"
            + "<span>Status " + (alert.status || "n/a") + "</span>"
            + "<span>Evidence " + Number(alert.evidenceCount || 0) + "</span>"
            + "<span>Boundary " + (alert.escalationBoundary || "n/a") + "</span>"
            + "<span>Rule " + (alert.alertRuleId || "n/a") + "</span>"
            + "<span>Updated " + formatUtc(alert.updatedAtUtc) + "</span>"
            + "</div>"
            + "<p><strong>Related object:</strong> " + (alert.affectedFamily || "n/a") + " / " + (alert.affectedObjectRef || "n/a") + "</p>"
            + "<p><strong>Recommended action:</strong> " + (alert.recommendedNextAction || "review") + "</p>"
            + "<div class='link-row'>"
            + "<a href=\"" + (alert.resolutionUrl || resolutionBasePath) + "\">Open Resolution</a>"
            + "<a href=\"" + (alert.personWorkspaceUrl || personWorkspaceBasePath) + "\">Open Person</a>"
            + "</div>";
          alertList.appendChild(item);
        });

        card.appendChild(alertList);
        groupsNode.appendChild(card);
      });
    }

    async function refreshAlerts() {
      try {
        const token = tokenInput.value.trim();
        if (token) {
          writeAccessToken(token);
        }

        setState("loading", "Loading workflow-critical alerts...");
        const result = await operatorPostJson("/api/operator/alerts/query", {
          trackedPersonId: personFilter.value || null,
          escalationBoundary: boundaryFilter.value || "all",
          search: searchInput.value.trim() || null,
          personLimit: 24,
          alertsPerPersonLimit: 6
        });
        if (!result.accepted) {
          throw new Error(result.failureReason || "alerts_query_rejected");
        }

        syncTrackedPersonFilter(result);
        renderCounts(result);
        renderGroups(result);
        setState("success", "Alerts loaded from bounded operator projections.");
      } catch (error) {
        setState("error", "Alerts load failed: " + describeFailureReason(error && error.message ? error.message : "unknown_error"));
      }
    }

    tokenInput.value = readAccessToken();
    refreshButton.addEventListener("click", refreshAlerts);
    personFilter.addEventListener("change", refreshAlerts);
    boundaryFilter.addEventListener("change", refreshAlerts);
    searchInput.addEventListener("keydown", function(event) {
      if (event.key === "Enter") {
        refreshAlerts();
      }
    });

    refreshAlerts();
  </script>
</body>
</html>
""";
}

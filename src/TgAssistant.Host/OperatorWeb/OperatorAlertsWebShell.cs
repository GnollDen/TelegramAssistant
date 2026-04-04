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
    .widgets-grid {
      display: grid;
      gap: 12px;
      grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
    }
    .widget-card {
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 12px;
      background: #fbfdff;
      display: grid;
      gap: 10px;
    }
    .widget-card h3 {
      margin: 0;
      font-size: 16px;
    }
    .widget-metric {
      font-size: 28px;
      font-weight: 700;
      color: var(--accent);
    }
    .widget-copy {
      margin: 0;
      font-size: 14px;
      line-height: 1.45;
    }
    .widget-actions, .widget-facets {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
      align-items: center;
    }
    .widget-actions a, .widget-facets a {
      background: #f7faff;
    }
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
      <h2>Workflow Widgets</h2>
      <p class="muted">Compact audit-friendly shortcuts. Widgets only link into existing operator pages and never expose raw admin or debug controls.</p>
      <div id="widgets" class="widgets-grid">
        <p class="muted">Load alerts to see workflow widgets.</p>
      </div>
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
    const widgetsNode = document.getElementById("widgets");
    const personWorkspaceBasePath = "/operator/person-workspace";
    const resolutionBasePath = "/operator/resolution";
    const query = new URLSearchParams(window.location.search);
    const initialTrackedPersonId = query.get("trackedPersonId") || "";
    const initialBoundary = query.get("boundary") || "";
    const initialSearch = query.get("search") || "";
    let pendingTrackedPersonFilter = initialTrackedPersonId;

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
      const selected = personFilter.value || pendingTrackedPersonFilter;
      personFilter.innerHTML = "";
      const defaultOption = document.createElement("option");
      defaultOption.value = "";
      defaultOption.textContent = "All tracked persons";
      personFilter.appendChild(defaultOption);

      groups.forEach(function(group) {
        const person = group.trackedPerson || {};
        if (!person.trackedPersonId) {
          return;
        }

        const option = document.createElement("option");
        option.value = person.trackedPersonId;
        option.textContent = person.displayName || person.trackedPersonId;
        personFilter.appendChild(option);
      });

      if (selected && Array.from(personFilter.options).some(function(option) { return option.value === selected; })) {
        personFilter.value = selected;
        pendingTrackedPersonFilter = "";
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

    function titleize(value) {
      return String(value || "")
        .replaceAll("_", " ")
        .replace(/\b\w/g, function(char) { return char.toUpperCase(); });
    }

    function safeHref(value, fallback) {
      const candidate = String(value || "").trim();
      if (!candidate) {
        return fallback;
      }

      if (candidate.startsWith("/")) {
        return candidate;
      }

      return fallback;
    }

    function buildWidgetCard(title, metric, description) {
      const card = document.createElement("article");
      card.className = "widget-card";

      const titleNode = document.createElement("h3");
      titleNode.textContent = title;
      card.appendChild(titleNode);

      const metricNode = document.createElement("div");
      metricNode.className = "widget-metric";
      metricNode.textContent = String(metric);
      card.appendChild(metricNode);

      const copyNode = document.createElement("p");
      copyNode.className = "widget-copy";
      copyNode.textContent = description;
      card.appendChild(copyNode);

      return card;
    }

    function appendWidgetLinkRow(card, links) {
      const row = document.createElement("div");
      row.className = "widget-actions";
      links.forEach(function(link) {
        if (!link || !link.href) {
          return;
        }

        const anchor = document.createElement("a");
        anchor.href = safeHref(link.href, "/operator/alerts");
        anchor.textContent = link.label;
        row.appendChild(anchor);
      });

      if (row.childNodes.length > 0) {
        card.appendChild(row);
      }
    }

    function appendFacetRow(card, facets, emptyText) {
      const row = document.createElement("div");
      row.className = "widget-facets";
      if (!Array.isArray(facets) || facets.length === 0) {
        const muted = document.createElement("span");
        muted.className = "muted";
        muted.textContent = emptyText;
        row.appendChild(muted);
        card.appendChild(row);
        return;
      }

      facets.forEach(function(facet) {
        const anchor = document.createElement("a");
        anchor.href = safeHref(facet.alertsUrl, "/operator/alerts");
        anchor.className = "chip";
        anchor.textContent = (facet.label || facet.key || "Unknown") + " (" + Number(facet.count || 0) + ")";
        row.appendChild(anchor);
      });
      card.appendChild(row);
    }

    function findFocusAlert(groups, predicate) {
      for (const group of Array.isArray(groups) ? groups : []) {
        for (const alert of Array.isArray(group.alerts) ? group.alerts : []) {
          if (predicate(alert, group)) {
            return {
              alert: alert,
              group: group
            };
          }
        }
      }

      return null;
    }

    function syncLocationState(activeTrackedPersonId) {
      const params = new URLSearchParams();
      if (activeTrackedPersonId) {
        params.set("trackedPersonId", activeTrackedPersonId);
      }

      if (boundaryFilter.value && boundaryFilter.value !== "all") {
        params.set("boundary", boundaryFilter.value);
      }

      if (searchInput.value.trim()) {
        params.set("search", searchInput.value.trim());
      }

      const queryString = params.toString();
      const nextUrl = queryString ? "/operator/alerts?" + queryString : "/operator/alerts";
      window.history.replaceState(null, "", nextUrl);
    }

    function renderWidgets(result) {
      const summary = result.summary || {};
      const groups = Array.isArray(result.groups) ? result.groups : [];
      widgetsNode.innerHTML = "";

      const acknowledgementFocus = findFocusAlert(groups, function(alert) {
        return !!alert.requiresAcknowledgement;
      });
      const resolutionFocus = findFocusAlert(groups, function(alert) {
        return !!alert.enterResolutionContext;
      });

      const ackWidget = buildWidgetCard(
        "Acknowledgement Queue",
        Number(summary.requiresAcknowledgementCount || 0),
        acknowledgementFocus
          ? "Highest-priority acknowledgement path stays bounded to the linked resolution and person pages."
          : "No acknowledgement-required alerts in the current filter.");
      appendWidgetLinkRow(
        ackWidget,
        acknowledgementFocus
          ? [
              { href: acknowledgementFocus.alert.resolutionUrl || resolutionBasePath, label: "Open Focus Resolution" },
              { href: acknowledgementFocus.alert.personWorkspaceUrl || personWorkspaceBasePath, label: "Open Focus Person" }
            ]
          : []);
      widgetsNode.appendChild(ackWidget);

      const resolutionWidget = buildWidgetCard(
        "Enter Resolution",
        Number(summary.enterResolutionCount || 0),
        resolutionFocus
          ? (resolutionFocus.alert.title || "Workflow blocker") + " is ready for direct drilldown."
          : "No resolution-entry alert is available in the current filter.");
      appendWidgetLinkRow(
        resolutionWidget,
        resolutionFocus
          ? [
              { href: resolutionFocus.alert.resolutionUrl || resolutionBasePath, label: "Open Resolution Drilldown" },
              { href: resolutionFocus.group && resolutionFocus.group.resolutionQueueUrl ? resolutionFocus.group.resolutionQueueUrl : resolutionBasePath, label: "Open Person Queue" }
            ]
          : []);
      widgetsNode.appendChild(resolutionWidget);

      const reasonsWidget = buildWidgetCard(
        "Top Alert Reasons",
        Array.isArray(summary.topReasons) ? summary.topReasons.length : 0,
        "Reason facets deep-link back into the bounded alerts page using stable workflow filters.");
      appendFacetRow(reasonsWidget, summary.topReasons, "No reason facets available.");
      widgetsNode.appendChild(reasonsWidget);

      const boundariesWidget = buildWidgetCard(
        "Boundary Mix",
        Array.isArray(summary.boundaryBreakdown) ? summary.boundaryBreakdown.length : 0,
        "Boundary facets keep operators inside approved alert scopes with explicit labels.");
      appendFacetRow(boundariesWidget, summary.boundaryBreakdown, "No boundary facets available.");
      widgetsNode.appendChild(boundariesWidget);
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
        const alerts = Array.isArray(group.alerts) ? group.alerts : [];
        const ackRequiredCount = alerts.filter(function(alert) { return !!alert.requiresAcknowledgement; }).length;
        const enterResolutionCount = alerts.filter(function(alert) { return !!alert.enterResolutionContext; }).length;
        const focusAlert = alerts.find(function(alert) { return !!alert.enterResolutionContext; }) || alerts[0] || null;
        const card = document.createElement("article");
        card.className = "group-card";

        const header = document.createElement("div");
        header.className = "group-header";
        const headerCopy = document.createElement("div");
        const title = document.createElement("h3");
        title.textContent = person.displayName || person.trackedPersonId || "Tracked person";
        headerCopy.appendChild(title);

        const metaRow = document.createElement("div");
        metaRow.className = "meta-row";
        [
          "Scope " + (person.scopeKey || "n/a"),
          "Alerts " + Number(group.alertCount || 0),
          "Telegram-bound " + Number(group.telegramPushCount || 0),
          "Web-only " + Number(group.webOnlyCount || 0),
          "Ack-required " + ackRequiredCount,
          "Enter resolution " + enterResolutionCount
        ].forEach(function(text) {
          const chip = document.createElement("span");
          chip.textContent = text;
          metaRow.appendChild(chip);
        });
        headerCopy.appendChild(metaRow);
        header.appendChild(headerCopy);

        const actions = document.createElement("div");
        actions.className = "group-actions";
        if (focusAlert && focusAlert.resolutionUrl) {
          const focusAnchor = document.createElement("a");
          focusAnchor.href = safeHref(focusAlert.resolutionUrl, resolutionBasePath);
          focusAnchor.textContent = "Open Focus Alert";
          actions.appendChild(focusAnchor);
        }
        const personAnchor = document.createElement("a");
        personAnchor.href = safeHref(group.personWorkspaceUrl, personWorkspaceBasePath);
        personAnchor.textContent = "Open Person Workspace";
        actions.appendChild(personAnchor);
        const queueAnchor = document.createElement("a");
        queueAnchor.href = safeHref(group.resolutionQueueUrl, resolutionBasePath);
        queueAnchor.textContent = "Open Resolution Queue";
        actions.appendChild(queueAnchor);
        header.appendChild(actions);
        card.appendChild(header);

        const alertList = document.createElement("div");
        alertList.className = "alert-list";
        alerts.forEach(function(alert) {
          const item = document.createElement("section");
          item.className = "alert-card";
          const itemHeader = document.createElement("header");
          const left = document.createElement("div");
          const titleNode = document.createElement("h4");
          titleNode.textContent = alert.title || "Alert";
          left.appendChild(titleNode);
          const summaryNode = document.createElement("p");
          summaryNode.textContent = alert.summary || "No summary.";
          left.appendChild(summaryNode);
          itemHeader.appendChild(left);
          const priorityNode = document.createElement("div");
          priorityNode.className = "priority";
          priorityNode.textContent = alert.priority || "n/a";
          itemHeader.appendChild(priorityNode);
          item.appendChild(itemHeader);

          const why = document.createElement("p");
          const whyStrong = document.createElement("strong");
          whyStrong.textContent = "Why it matters:";
          why.appendChild(whyStrong);
          why.appendChild(document.createTextNode(" " + (alert.whyItMatters || "n/a")));
          item.appendChild(why);

          const meta = document.createElement("div");
          meta.className = "meta-row";
          [
            alert.itemType || "item",
            "Status " + (alert.status || "n/a"),
            "Evidence " + Number(alert.evidenceCount || 0),
            "Boundary " + (alert.escalationBoundary || "n/a"),
            "Rule " + (alert.alertRuleId || "n/a"),
            "Updated " + formatUtc(alert.updatedAtUtc)
          ].forEach(function(text) {
            const chip = document.createElement("span");
            chip.textContent = text;
            meta.appendChild(chip);
          });
          item.appendChild(meta);

          const related = document.createElement("p");
          const relatedStrong = document.createElement("strong");
          relatedStrong.textContent = "Related object:";
          related.appendChild(relatedStrong);
          related.appendChild(document.createTextNode(" " + (alert.affectedFamily || "n/a") + " / " + (alert.affectedObjectRef || "n/a")));
          item.appendChild(related);

          const action = document.createElement("p");
          const actionStrong = document.createElement("strong");
          actionStrong.textContent = "Recommended action:";
          action.appendChild(actionStrong);
          action.appendChild(document.createTextNode(" " + (alert.recommendedNextAction || "review")));
          item.appendChild(action);

          const linkRow = document.createElement("div");
          linkRow.className = "link-row";
          const resolutionLink = document.createElement("a");
          resolutionLink.href = safeHref(alert.resolutionUrl, resolutionBasePath);
          resolutionLink.textContent = "Open Resolution";
          linkRow.appendChild(resolutionLink);
          const personLink = document.createElement("a");
          personLink.href = safeHref(alert.personWorkspaceUrl, personWorkspaceBasePath);
          personLink.textContent = "Open Person";
          linkRow.appendChild(personLink);
          item.appendChild(linkRow);
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
          trackedPersonId: personFilter.value || pendingTrackedPersonFilter || null,
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
        renderWidgets(result);
        syncLocationState(personFilter.value || pendingTrackedPersonFilter || "");
        setState("success", "Alerts loaded from bounded operator projections.");
      } catch (error) {
        setState("error", "Alerts load failed: " + describeFailureReason(error && error.message ? error.message : "unknown_error"));
      }
    }

    tokenInput.value = readAccessToken();
    if (initialBoundary === "web_only" || initialBoundary === "telegram_push_acknowledge") {
      boundaryFilter.value = initialBoundary;
    }
    if (initialSearch) {
      searchInput.value = initialSearch;
    }
    refreshButton.addEventListener("click", refreshAlerts);
    personFilter.addEventListener("change", function() {
      pendingTrackedPersonFilter = "";
      refreshAlerts();
    });
    boundaryFilter.addEventListener("change", function() {
      refreshAlerts();
    });
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

#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_PROJECT="$ROOT_DIR/src/TgAssistant.Host/TgAssistant.Host.csproj"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-tga-postgres}"
BASE_URL="${BASE_URL:-http://127.0.0.1:5091}"
DEGRADED_BASE_URL="${DEGRADED_BASE_URL:-http://127.0.0.1:5092}"
SCOPE_KEY="${SCOPE_KEY:-chat:885574984}"
REPORT_PATH="${REPORT_PATH:-$ROOT_DIR/logs/opint-005-f-web-validation-report.json}"
DEGRADED_REPORT_PATH="${DEGRADED_REPORT_PATH:-$ROOT_DIR/logs/opint-005-f-recompute-degraded-report.json}"
OPERATOR_TOKEN="${OPERATOR_TOKEN:-opint-005-f-web-token}"

mkdir -p "$(dirname "$REPORT_PATH")"

for tool in docker jq curl uuidgen dotnet; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "$tool is required" >&2
        exit 1
    fi
done

POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-$(docker inspect "$POSTGRES_CONTAINER" --format '{{range .Config.Env}}{{println .}}{{end}}' | awk -F= '$1 == "POSTGRES_PASSWORD" { print $2 }')}"
if [[ -z "$POSTGRES_PASSWORD" ]]; then
    echo "POSTGRES_PASSWORD could not be resolved" >&2
    exit 1
fi

DB_CONN="Host=127.0.0.1;Database=tgassistant;Username=tgassistant;Password=${POSTGRES_PASSWORD}"
HOST_LOG="$(mktemp)"
COOKIE_JAR="$(mktemp)"
RESPONSE_BODY_FILE="$(mktemp)"
RESPONSE_HEADERS_FILE="$(mktemp)"

DEFECT_ID_1="$(uuidgen)"
DEFECT_ID_2="$(uuidgen)"
DEFECT_ID_3="$(uuidgen)"

REQ_REJECT="opint-005-f-reject-$(uuidgen)"
REQ_APPROVE="opint-005-f-approve-$(uuidgen)"
REQ_DEFER="opint-005-f-defer-$(uuidgen)"
REQ_CLARIFY="opint-005-f-clarify-$(uuidgen)"
REQ_DENIED="opint-005-f-denied-$(uuidgen)"

TRACKED_PERSON_ID=""
TRACKED_PERSON_NAME=""
ITEM_KEY_1=""
ITEM_KEY_2=""
ITEM_KEY_3=""

BOOTSTRAP_CODE=""
TRACKED_QUERY_CODE=""
SELECT_CODE=""
QUEUE_CODE=""
DETAIL_CODE=""
REJECT_CODE=""
APPROVE_CODE=""
DEFER_CODE=""
CLARIFY_CODE=""
DENIED_CODE=""

BOOTSTRAP_BODY='{}'
TRACKED_QUERY_BODY='{}'
SELECT_BODY='{}'
QUEUE_BODY='{}'
DETAIL_BODY='{}'
REJECT_BODY='{}'
APPROVE_BODY='{}'
DEFER_BODY='{}'
CLARIFY_BODY='{}'
DENIED_BODY='{}'

HTML_HAS_QUEUE_ROUTE=false
HTML_HAS_DETAIL_ROUTE=false
HTML_HAS_NO_LEGACY_NOTE=false
CURRENT_SESSION_ID=""

cleanup() {
    local status=$?

    docker exec -i "$POSTGRES_CONTAINER" psql -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant >/dev/null <<SQL || true
delete from operator_resolution_actions where request_id in ('${REQ_REJECT}','${REQ_APPROVE}','${REQ_DEFER}','${REQ_CLARIFY}','${REQ_DENIED}');
delete from operator_audit_events where request_id in ('${REQ_REJECT}','${REQ_APPROVE}','${REQ_DEFER}','${REQ_CLARIFY}','${REQ_DENIED}');
delete from runtime_defects where id in ('${DEFECT_ID_1}','${DEFECT_ID_2}','${DEFECT_ID_3}');
SQL

    if [[ -n "${HOST_PID:-}" ]]; then
        kill "$HOST_PID" >/dev/null 2>&1 || true
        wait "$HOST_PID" >/dev/null 2>&1 || true
    fi

    if [[ $status -ne 0 ]]; then
        echo "Validation failed. Host log:" >&2
        cat "$HOST_LOG" >&2 || true
    fi

    rm -f "$HOST_LOG" "$COOKIE_JAR" "$RESPONSE_BODY_FILE" "$RESPONSE_HEADERS_FILE"

    exit $status
}
trap cleanup EXIT

psql_exec() {
    local sql="$1"
    docker exec -i "$POSTGRES_CONTAINER" psql -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant >/dev/null -c "$sql"
}

expect_http() {
    local actual="$1"
    local expected="$2"
    local label="$3"
    if [[ "$actual" != "$expected" ]]; then
        echo "${label}: expected HTTP ${expected}, got ${actual}" >&2
        if [[ -f "$RESPONSE_BODY_FILE" && -s "$RESPONSE_BODY_FILE" ]]; then
            echo "response body:" >&2
            cat "$RESPONSE_BODY_FILE" >&2 || true
        fi
        exit 1
    fi
}

expect_jq() {
    local body="$1"
    local expression="$2"
    local label="$3"
    if ! jq -e "$expression" >/dev/null <<<"$body"; then
        echo "${label}: jq assertion failed: ${expression}" >&2
        jq . <<<"$body" >&2 || printf '%s\n' "$body" >&2
        exit 1
    fi
}

web_get() {
    local endpoint="$1"
    local include_token="${2:-true}"
    local code

    : >"$RESPONSE_HEADERS_FILE"
    if [[ "$include_token" == "true" ]]; then
        if [[ -n "$CURRENT_SESSION_ID" ]]; then
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "X-Tga-Operator-Key: ${OPERATOR_TOKEN}" \
                -H "X-Tga-Operator-Session: ${CURRENT_SESSION_ID}" \
                "${BASE_URL}${endpoint}")"
        else
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "X-Tga-Operator-Key: ${OPERATOR_TOKEN}" \
                "${BASE_URL}${endpoint}")"
        fi
    else
        if [[ -n "$CURRENT_SESSION_ID" ]]; then
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "X-Tga-Operator-Session: ${CURRENT_SESSION_ID}" \
                "${BASE_URL}${endpoint}")"
        else
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                "${BASE_URL}${endpoint}")"
        fi
    fi

    local resolved_session
    resolved_session="$(awk 'BEGIN{IGNORECASE=1} /^X-Tga-Operator-Session:/ {print $2}' "$RESPONSE_HEADERS_FILE" | tr -d '\r' | tail -n 1)"
    if [[ -n "$resolved_session" ]]; then
        CURRENT_SESSION_ID="$resolved_session"
    else
        local body_session
        body_session="$(jq -r '.session.operatorSessionId // empty' "$RESPONSE_BODY_FILE" 2>/dev/null || true)"
        if [[ -n "$body_session" ]]; then
            CURRENT_SESSION_ID="$body_session"
        fi
    fi

    printf '%s\n' "$code"
}

web_post_json() {
    local endpoint="$1"
    local payload="$2"
    local include_token="${3:-true}"
    local code

    : >"$RESPONSE_HEADERS_FILE"
    if [[ "$include_token" == "true" ]]; then
        if [[ -n "$CURRENT_SESSION_ID" ]]; then
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -X POST "${BASE_URL}${endpoint}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "Content-Type: application/json" \
                -H "X-Tga-Operator-Key: ${OPERATOR_TOKEN}" \
                -H "X-Tga-Operator-Session: ${CURRENT_SESSION_ID}" \
                --data "$payload")"
        else
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -X POST "${BASE_URL}${endpoint}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "Content-Type: application/json" \
                -H "X-Tga-Operator-Key: ${OPERATOR_TOKEN}" \
                --data "$payload")"
        fi
    else
        if [[ -n "$CURRENT_SESSION_ID" ]]; then
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -X POST "${BASE_URL}${endpoint}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "Content-Type: application/json" \
                -H "X-Tga-Operator-Session: ${CURRENT_SESSION_ID}" \
                --data "$payload")"
        else
            code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
                -X POST "${BASE_URL}${endpoint}" \
                -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
                -H "Content-Type: application/json" \
                --data "$payload")"
        fi
    fi

    local resolved_session
    resolved_session="$(awk 'BEGIN{IGNORECASE=1} /^X-Tga-Operator-Session:/ {print $2}' "$RESPONSE_HEADERS_FILE" | tr -d '\r' | tail -n 1)"
    if [[ -n "$resolved_session" ]]; then
        CURRENT_SESSION_ID="$resolved_session"
    else
        local body_session
        body_session="$(jq -r '.session.operatorSessionId // empty' "$RESPONSE_BODY_FILE" 2>/dev/null || true)"
        if [[ -n "$body_session" ]]; then
            CURRENT_SESSION_ID="$body_session"
        fi
    fi

    printf '%s\n' "$code"
}

Runtime__Role=ops \
Database__ConnectionString="$DB_CONN" \
Redis__ConnectionString=127.0.0.1:6379 \
ASPNETCORE_URLS="$BASE_URL" \
LegacyDiagnostics__Web__RequireOperatorAccessToken=true \
LegacyDiagnostics__Web__OperatorAccessToken="$OPERATOR_TOKEN" \
LlmGateway__Providers__openrouter__ApiKey=or-live-opint005fvalidationkey \
dotnet run --project "$HOST_PROJECT" -- --operator-schema-init >"$HOST_LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 120); do
    probe_code="$(curl -s -o /dev/null -w "%{http_code}" "${BASE_URL}/operator/resolution/bootstrap" || true)"
    if [[ "$probe_code" == "200" || "$probe_code" == "503" ]]; then
        break
    fi
    sleep 1
done

if [[ "${probe_code:-000}" == "000" ]]; then
    echo "Host did not become reachable at ${BASE_URL}" >&2
    exit 1
fi

# Seed three bounded review items so action coverage stays deterministic.
psql_exec "insert into runtime_defects (id, defect_class, severity, status, scope_key, dedupe_key, object_type, object_ref, summary, details_json, occurrence_count, escalation_action, escalation_reason, first_seen_at_utc, last_seen_at_utc, created_at_utc, updated_at_utc) values ('${DEFECT_ID_1}', 'normalization', 'high', 'open', '${SCOPE_KEY}', 'opint-005-f|1|${DEFECT_ID_1}', 'profile', 'scope:${SCOPE_KEY}:opint-005-f-1', 'OPINT-005-F seeded review item 1', '{}'::jsonb, 1, 'switch_review_only', 'validation seed', now(), now(), now(), now());"
psql_exec "insert into runtime_defects (id, defect_class, severity, status, scope_key, dedupe_key, object_type, object_ref, summary, details_json, occurrence_count, escalation_action, escalation_reason, first_seen_at_utc, last_seen_at_utc, created_at_utc, updated_at_utc) values ('${DEFECT_ID_2}', 'normalization', 'medium', 'open', '${SCOPE_KEY}', 'opint-005-f|2|${DEFECT_ID_2}', 'profile', 'scope:${SCOPE_KEY}:opint-005-f-2', 'OPINT-005-F seeded review item 2', '{}'::jsonb, 1, 'switch_review_only', 'validation seed', now(), now(), now(), now());"
psql_exec "insert into runtime_defects (id, defect_class, severity, status, scope_key, dedupe_key, object_type, object_ref, summary, details_json, occurrence_count, escalation_action, escalation_reason, first_seen_at_utc, last_seen_at_utc, created_at_utc, updated_at_utc) values ('${DEFECT_ID_3}', 'normalization', 'low', 'open', '${SCOPE_KEY}', 'opint-005-f|3|${DEFECT_ID_3}', 'profile', 'scope:${SCOPE_KEY}:opint-005-f-3', 'OPINT-005-F seeded review item 3', '{}'::jsonb, 1, 'switch_review_only', 'validation seed', now(), now(), now(), now());"

RESOLUTION_HTML_CODE="$(web_get "/operator/resolution" false)"
RESOLUTION_HTML="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$RESOLUTION_HTML_CODE" "200" "operator resolution route"
if grep -Fq '/api/operator/resolution/queue/query' <<<"$RESOLUTION_HTML"; then
    HTML_HAS_QUEUE_ROUTE=true
fi
if grep -Fq '/api/operator/resolution/detail/query' <<<"$RESOLUTION_HTML"; then
    HTML_HAS_DETAIL_ROUTE=true
fi
if grep -Fq 'no legacy Stage6 queue/case pages' <<<"$RESOLUTION_HTML"; then
    HTML_HAS_NO_LEGACY_NOTE=true
fi

BOOTSTRAP_CODE="$(web_get "/operator/resolution/bootstrap" false)"
BOOTSTRAP_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$BOOTSTRAP_CODE" "200" "resolution bootstrap"
expect_jq "$BOOTSTRAP_BODY" '.state == "ready" and .queueRoute == "/api/operator/resolution/queue/query" and .trackedPersonsRoute == "/api/operator/tracked-persons/query"' "resolution bootstrap payload"

TRACKED_QUERY_CODE="$(web_post_json "/api/operator/tracked-persons/query" '{}')"
TRACKED_QUERY_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$TRACKED_QUERY_CODE" "200" "tracked-person query"
expect_jq "$TRACKED_QUERY_BODY" '.accepted == true and (.trackedPersons | length) >= 1' "tracked-person query"
TRACKED_PERSON_ID="$(jq -r '.activeTrackedPersonId // .trackedPersons[0].trackedPersonId' <<<"$TRACKED_QUERY_BODY")"
TRACKED_PERSON_NAME="$(jq -r '.activeTrackedPerson.displayName // .trackedPersons[0].displayName' <<<"$TRACKED_QUERY_BODY")"

SELECT_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" '{ trackedPersonId: $trackedPersonId }')"
SELECT_CODE="$(web_post_json "/api/operator/tracked-persons/select" "$SELECT_PAYLOAD")"
SELECT_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$SELECT_CODE" "200" "tracked-person select"
expect_jq "$SELECT_BODY" '.accepted == true and .session.activeTrackedPersonId == "'"$TRACKED_PERSON_ID"'" and .session.activeMode == "resolution_queue"' "tracked-person select"
CURRENT_SESSION_ID="$(jq -r '.session.operatorSessionId // empty' <<<"$SELECT_BODY")"

QUEUE_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" '{ trackedPersonId: $trackedPersonId, itemTypes: ["review"], sortBy: "priority", sortDirection: "desc", limit: 30 }')"
QUEUE_CODE="$(web_post_json "/api/operator/resolution/queue/query" "$QUEUE_PAYLOAD")"
QUEUE_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$QUEUE_CODE" "200" "resolution queue"
expect_jq "$QUEUE_BODY" '.accepted == true and (.queue.items | length) >= 3' "resolution queue seeded coverage"

ITEM_KEY_1="review:runtime_defect:${DEFECT_ID_1}"
ITEM_KEY_2="review:runtime_defect:${DEFECT_ID_2}"
ITEM_KEY_3="review:runtime_defect:${DEFECT_ID_3}"
expect_jq "$QUEUE_BODY" '.queue.items | any(.scopeItemKey == "'"$ITEM_KEY_1"'")' "seeded item 1 visible"
expect_jq "$QUEUE_BODY" '.queue.items | any(.scopeItemKey == "'"$ITEM_KEY_2"'")' "seeded item 2 visible"
expect_jq "$QUEUE_BODY" '.queue.items | any(.scopeItemKey == "'"$ITEM_KEY_3"'")' "seeded item 3 visible"

DETAIL_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_1" '{ trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, evidenceLimit: 8, evidenceSortBy: "observed_at", evidenceSortDirection: "desc" }')"
DETAIL_CODE="$(web_post_json "/api/operator/resolution/detail/query" "$DETAIL_PAYLOAD")"
DETAIL_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$DETAIL_CODE" "200" "resolution detail"
expect_jq "$DETAIL_BODY" '.accepted == true and .detail.item.scopeItemKey == "'"$ITEM_KEY_1"'" and (.detail.item.evidence | type == "array")' "resolution detail evidence"

REJECT_PAYLOAD="$(jq -nc --arg requestId "$REQ_REJECT" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_1" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, actionType: "reject" }')"
REJECT_CODE="$(web_post_json "/api/operator/resolution/actions" "$REJECT_PAYLOAD")"
REJECT_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$REJECT_CODE" "400" "reject missing explanation"
expect_jq "$REJECT_BODY" '.accepted == false and .failureReason == "explanation_required" and .action.accepted == false' "reject missing explanation"

APPROVE_PAYLOAD="$(jq -nc --arg requestId "$REQ_APPROVE" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_1" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, actionType: "approve" }')"
APPROVE_CODE="$(web_post_json "/api/operator/resolution/actions" "$APPROVE_PAYLOAD")"
APPROVE_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$APPROVE_CODE" "200" "approve action"
expect_jq "$APPROVE_BODY" '.accepted == true and .action.accepted == true and .action.recompute.lifecycleStatus != null' "approve action"

DEFER_PAYLOAD="$(jq -nc --arg requestId "$REQ_DEFER" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_2" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, actionType: "defer", explanation: "Bounded defer validation note." }')"
RESET_QUEUE_BEFORE_ITEM2_CODE="$(web_post_json "/api/operator/resolution/queue/query" "$QUEUE_PAYLOAD")"
expect_http "$RESET_QUEUE_BEFORE_ITEM2_CODE" "200" "queue reset before item2 detail"
DETAIL_ITEM2_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_2" '{ trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, evidenceLimit: 5, evidenceSortBy: "observed_at", evidenceSortDirection: "desc" }')"
DETAIL_ITEM2_CODE="$(web_post_json "/api/operator/resolution/detail/query" "$DETAIL_ITEM2_PAYLOAD")"
expect_http "$DETAIL_ITEM2_CODE" "200" "resolution detail item2"
DEFER_CODE="$(web_post_json "/api/operator/resolution/actions" "$DEFER_PAYLOAD")"
DEFER_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$DEFER_CODE" "200" "defer action"
expect_jq "$DEFER_BODY" '.accepted == true and .action.accepted == true and .action.recompute.lifecycleStatus != null' "defer action"

CLARIFY_PAYLOAD="$(jq -nc --arg requestId "$REQ_CLARIFY" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_3" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, actionType: "clarify", explanation: "Clarification required for bounded follow-up.", clarificationPayload: { summary: "Seeded follow-up", followUpQuestion: "What changed in context?", followUpAnswer: "Operator provided bounded answer.", answerKind: "free_text", notes: "OPINT-005-F validation payload" } }')"
RESET_QUEUE_BEFORE_ITEM3_CODE="$(web_post_json "/api/operator/resolution/queue/query" "$QUEUE_PAYLOAD")"
expect_http "$RESET_QUEUE_BEFORE_ITEM3_CODE" "200" "queue reset before item3 detail"
DETAIL_ITEM3_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_3" '{ trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, evidenceLimit: 5, evidenceSortBy: "observed_at", evidenceSortDirection: "desc" }')"
DETAIL_ITEM3_CODE="$(web_post_json "/api/operator/resolution/detail/query" "$DETAIL_ITEM3_PAYLOAD")"
expect_http "$DETAIL_ITEM3_CODE" "200" "resolution detail item3"
CLARIFY_CODE="$(web_post_json "/api/operator/resolution/actions" "$CLARIFY_PAYLOAD")"
CLARIFY_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$CLARIFY_CODE" "200" "clarify action"
expect_jq "$CLARIFY_BODY" '.accepted == true and .action.accepted == true and .action.recompute.lifecycleStatus != null' "clarify action"

DENIED_PAYLOAD="$(jq -nc --arg requestId "$REQ_DENIED" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg scopeItemKey "$ITEM_KEY_2" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $scopeItemKey, actionType: "approve" }')"
DENIED_CODE="$(web_post_json "/api/operator/resolution/actions" "$DENIED_PAYLOAD")"
DENIED_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$DENIED_CODE" "403" "denied action mismatch"
expect_jq "$DENIED_BODY" '.accepted == false and .failureReason == "session_scope_item_mismatch" and .action.accepted == false' "denied action mismatch"

QUEUE_AFTER_CODE="$(web_post_json "/api/operator/resolution/queue/query" "$QUEUE_PAYLOAD")"
QUEUE_AFTER_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$QUEUE_AFTER_CODE" "200" "queue refresh after actions"
expect_jq "$QUEUE_AFTER_BODY" '.accepted == true' "queue refresh after actions"

Runtime__Role=ops \
Database__ConnectionString="$DB_CONN" \
Redis__ConnectionString=127.0.0.1:6379 \
ASPNETCORE_URLS="$DEGRADED_BASE_URL" \
LlmGateway__Providers__openrouter__ApiKey=or-live-opint005fdegradedkey \
dotnet run --project "$HOST_PROJECT" -- \
--operator-schema-init --opint-003-d-validate \
--opint-003-d-validate-output="$DEGRADED_REPORT_PATH" >/dev/null

DEGRADED_BODY="$(cat "$DEGRADED_REPORT_PATH")"
expect_jq "$DEGRADED_BODY" '.AllChecksPassed == true' "opint-003 degraded report pass"
expect_jq "$DEGRADED_BODY" '.ClarificationScenario.Passed == true and .ClarificationScenario.ActionRowLifecycleStatus == "clarification_blocked"' "opint-003 degraded scenario"

jq -n \
    --arg generatedAt "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
    --arg scopeKey "$SCOPE_KEY" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg trackedPersonName "$TRACKED_PERSON_NAME" \
    --arg reportPath "$REPORT_PATH" \
    --arg degradedReportPath "$DEGRADED_REPORT_PATH" \
    --argjson bootstrapBody "$BOOTSTRAP_BODY" \
    --argjson queueBody "$QUEUE_BODY" \
    --argjson detailBody "$DETAIL_BODY" \
    --argjson rejectBody "$REJECT_BODY" \
    --argjson approveBody "$APPROVE_BODY" \
    --argjson deferBody "$DEFER_BODY" \
    --argjson clarifyBody "$CLARIFY_BODY" \
    --argjson deniedBody "$DENIED_BODY" \
    --argjson queueAfterBody "$QUEUE_AFTER_BODY" \
    --argjson degradedBody "$DEGRADED_BODY" \
    --arg bootstrapCode "$BOOTSTRAP_CODE" \
    --arg trackedQueryCode "$TRACKED_QUERY_CODE" \
    --arg selectCode "$SELECT_CODE" \
    --arg queueCode "$QUEUE_CODE" \
    --arg detailCode "$DETAIL_CODE" \
    --arg rejectCode "$REJECT_CODE" \
    --arg approveCode "$APPROVE_CODE" \
    --arg deferCode "$DEFER_CODE" \
    --arg clarifyCode "$CLARIFY_CODE" \
    --arg deniedCode "$DENIED_CODE" \
    --arg queueAfterCode "$QUEUE_AFTER_CODE" \
    --arg itemKey1 "$ITEM_KEY_1" \
    --arg itemKey2 "$ITEM_KEY_2" \
    --arg itemKey3 "$ITEM_KEY_3" \
    --argjson htmlHasQueueRoute "$HTML_HAS_QUEUE_ROUTE" \
    --argjson htmlHasDetailRoute "$HTML_HAS_DETAIL_ROUTE" \
    --argjson htmlHasNoLegacyNote "$HTML_HAS_NO_LEGACY_NOTE" \
    '{
        generatedAtUtc: $generatedAt,
        scopeKey: $scopeKey,
        trackedPerson: {
            trackedPersonId: $trackedPersonId,
            displayName: $trackedPersonName
        },
        webRouteChecks: {
            resolutionRouteHttp: ($bootstrapCode | tonumber),
            htmlHasQueueRoute: $htmlHasQueueRoute,
            htmlHasDetailRoute: $htmlHasDetailRoute,
            htmlHasNoLegacyStage6QueueCaseNote: $htmlHasNoLegacyNote,
            bootstrapState: $bootstrapBody.state,
            bootstrapQueueRoute: $bootstrapBody.queueRoute,
            bootstrapTrackedPersonsRoute: $bootstrapBody.trackedPersonsRoute
        },
        seededItems: [
            { scopeItemKey: $itemKey1 },
            { scopeItemKey: $itemKey2 },
            { scopeItemKey: $itemKey3 }
        ],
        queueDetailEvidence: {
            trackedPersonsHttp: ($trackedQueryCode | tonumber),
            trackedPersonSelectHttp: ($selectCode | tonumber),
            queueHttp: ($queueCode | tonumber),
            detailHttp: ($detailCode | tonumber),
            totalOpenCount: ($queueBody.queue.totalOpenCount // 0),
            filteredCount: ($queueBody.queue.filteredCount // 0),
            detailEvidenceCount: ($detailBody.detail.item.evidence | length)
        },
        actionScenarios: [
            {
                name: "reject_missing_explanation",
                http: ($rejectCode | tonumber),
                failureReason: $rejectBody.failureReason
            },
            {
                name: "approve_normal",
                http: ($approveCode | tonumber),
                accepted: $approveBody.accepted,
                recomputeLifecycleStatus: $approveBody.action.recompute.lifecycleStatus,
                recomputeLastResultStatus: $approveBody.action.recompute.lastResultStatus
            },
            {
                name: "defer_normal",
                http: ($deferCode | tonumber),
                accepted: $deferBody.accepted,
                recomputeLifecycleStatus: $deferBody.action.recompute.lifecycleStatus,
                recomputeLastResultStatus: $deferBody.action.recompute.lastResultStatus
            },
            {
                name: "clarify_normal",
                http: ($clarifyCode | tonumber),
                accepted: $clarifyBody.accepted,
                recomputeLifecycleStatus: $clarifyBody.action.recompute.lifecycleStatus,
                recomputeLastResultStatus: $clarifyBody.action.recompute.lastResultStatus
            },
            {
                name: "approve_denied_scope_mismatch",
                http: ($deniedCode | tonumber),
                failureReason: $deniedBody.failureReason
            }
        ],
        recomputeDegradedScenario: {
            reportPath: $degradedReportPath,
            passed: $degradedBody.AllChecksPassed,
            degradedScenario: $degradedBody.ClarificationScenario
        },
        queueAfterActions: {
            http: ($queueAfterCode | tonumber),
            totalOpenCount: ($queueAfterBody.queue.totalOpenCount // 0),
            filteredCount: ($queueAfterBody.queue.filteredCount // 0)
        },
        passed: (
            $htmlHasQueueRoute == true
            and $htmlHasDetailRoute == true
            and $htmlHasNoLegacyNote == true
            and (($trackedQueryCode | tonumber) == 200)
            and (($selectCode | tonumber) == 200)
            and (($queueCode | tonumber) == 200)
            and (($detailCode | tonumber) == 200)
            and (($rejectCode | tonumber) == 400)
            and (($approveCode | tonumber) == 200)
            and (($deferCode | tonumber) == 200)
            and (($clarifyCode | tonumber) == 200)
            and (($deniedCode | tonumber) == 403)
            and ($degradedBody.AllChecksPassed == true)
        )
    }' >"$REPORT_PATH"

cat "$REPORT_PATH"

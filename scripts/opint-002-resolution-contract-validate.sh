#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_PROJECT="$ROOT_DIR/src/TgAssistant.Host/TgAssistant.Host.csproj"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-tga-postgres}"
BASE_URL="${BASE_URL:-http://127.0.0.1:5087}"
SCOPE_KEY="${SCOPE_KEY:-chat:885574984}"
REPORT_PATH="${REPORT_PATH:-$ROOT_DIR/logs/opint-002-d-validation-report.json}"
OPERATOR_TOKEN="${OPERATOR_TOKEN:-opint-002-d-web-token}"
OPERATOR_SUBJECT="${OPERATOR_SUBJECT:-validation}"
TRACKED_PERSON_NAME_EXPECTED="${TRACKED_PERSON_NAME_EXPECTED:-Гайнутдинова Алёна Амировна}"
PRESERVE_EVIDENCE="${PRESERVE_EVIDENCE:-false}"

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
DEFECT_ID="$(uuidgen)"
APPROVE_REQUEST_ID="opint-002-d-approve-$(uuidgen)"
REJECT_REQUEST_ID="opint-002-d-reject-$(uuidgen)"
HOST_LOG="$(mktemp)"
COOKIE_JAR="$(mktemp)"
RESPONSE_BODY_FILE="$(mktemp)"
RESPONSE_HEADERS_FILE="$(mktemp)"

TRACKED_PERSON_ID=""
TRACKED_PERSON_NAME=""
ITEM_KEY=""
CURRENT_SESSION_ID=""
ESTABLISHED_SESSION_ID=""

TRACKED_QUERY_CODE=""
SELECT_CODE=""
QUEUE_CODE=""
DETAIL_CODE=""
APPROVE_CODE=""
REPLAY_APPROVE_CODE=""
REJECT_CODE=""
AUTH_DENIED_CODE=""
LAST_HTTP_CODE=""

TRACKED_QUERY_BODY='{}'
SELECT_BODY='{}'
QUEUE_BODY='{}'
DETAIL_BODY='{}'
APPROVE_BODY='{}'
REPLAY_APPROVE_BODY='{}'
REJECT_BODY='{}'
AUTH_DENIED_BODY='{}'

APPROVE_ACTION_ID=""
APPROVE_AUDIT_ID=""
REPLAY_APPROVE_LIFECYCLE=""
REPLAY_APPROVE_TARGET_LIFECYCLE=""
REJECT_AUDIT_ID=""
SELECTION_AUDIT_COUNT="0"
APPROVE_ACTION_COUNT="0"
APPROVE_AUDIT_COUNT="0"
REJECT_ACTION_COUNT="0"
REJECT_AUDIT_COUNT="0"
APPROVE_QUEUE_ITEM_ID=""
APPROVE_RUNTIME_EVIDENCE='{}'
MIGRATION_PRESENT="false"
TABLES_PRESENT="false"

cleanup() {
    local status=$?

    if [[ "$PRESERVE_EVIDENCE" != "true" && -n "${TRACKED_PERSON_ID:-}" ]]; then
        docker exec -i "$POSTGRES_CONTAINER" psql -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant >/dev/null <<SQL || true
delete from operator_resolution_actions where request_id in ('${APPROVE_REQUEST_ID}','${REJECT_REQUEST_ID}');
delete from operator_audit_events where request_id in ('${APPROVE_REQUEST_ID}','${REJECT_REQUEST_ID}');
delete from operator_audit_events where operator_session_id = nullif('${CURRENT_SESSION_ID}', '');
delete from runtime_defects where id = '${DEFECT_ID}';
SQL
    fi

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

psql_scalar() {
    local sql="$1"
    docker exec -i "$POSTGRES_CONTAINER" psql -At -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant -c "$sql"
}

psql_json() {
    local sql="$1"
    docker exec -i "$POSTGRES_CONTAINER" psql -At -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant -c "$sql"
}

expect_http() {
    local actual="$1"
    local expected="$2"
    local label="$3"
    if [[ "$actual" != "$expected" ]]; then
        echo "${label}: expected HTTP ${expected}, got ${actual}" >&2
        if [[ -s "$RESPONSE_BODY_FILE" ]]; then
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

resolve_current_session() {
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
}

web_post_json() {
    local endpoint="$1"
    local payload="$2"
    local token="${3:-$OPERATOR_TOKEN}"
    local code

    : >"$RESPONSE_HEADERS_FILE"
    if [[ -n "$CURRENT_SESSION_ID" ]]; then
        code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
            -X POST "${BASE_URL}${endpoint}" \
            -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
            -H "Content-Type: application/json" \
            -H "X-Tga-Operator-Key: ${token}" \
            -H "X-Tga-Operator-Session: ${CURRENT_SESSION_ID}" \
            -H "X-Tga-Operator-Subject: ${OPERATOR_SUBJECT}" \
            --data "$payload")"
    else
        code="$(curl -sS -D "$RESPONSE_HEADERS_FILE" -o "$RESPONSE_BODY_FILE" -w "%{http_code}" \
            -X POST "${BASE_URL}${endpoint}" \
            -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
            -H "Content-Type: application/json" \
            -H "X-Tga-Operator-Key: ${token}" \
            -H "X-Tga-Operator-Subject: ${OPERATOR_SUBJECT}" \
            --data "$payload")"
    fi

    resolve_current_session
    LAST_HTTP_CODE="$code"
}

load_approve_runtime_evidence() {
    if [[ -z "$APPROVE_REQUEST_ID" ]]; then
        printf '%s\n' '{}'
        return 0
    fi

    psql_json "
select json_build_object(
    'actionId', action_row.id,
    'requestId', action_row.request_id,
    'operatorSessionId', action_row.operator_session_id,
    'recomputeStatus', action_row.recompute_status,
    'recomputeLastResultStatus', action_row.recompute_last_result_status,
    'recomputeCompletedAtUtc', action_row.recompute_completed_at_utc,
    'auditQueueItemId', accepted_audit.details_json #>> '{recompute,targets,0,queue_item_id}',
    'auditTriggerRef', accepted_audit.details_json #>> '{recompute,trigger_ref}',
    'queueItemId', queue_item.id,
    'queueStatus', queue_item.status,
    'queueLastResultStatus', queue_item.last_result_status,
    'queueCompletedAtUtc', queue_item.completed_at_utc,
    'queueTargetFamily', queue_item.target_family,
    'queueTargetRef', queue_item.target_ref,
    'queueTriggerRef', queue_item.trigger_ref
)
from operator_resolution_actions action_row
left join lateral (
    select details_json
    from operator_audit_events
    where request_id = '${APPROVE_REQUEST_ID}'
      and decision_outcome = 'accepted'
      and action_type is not null
    order by event_time_utc desc
    limit 1
) accepted_audit on true
left join stage8_recompute_queue_items queue_item
    on queue_item.id = nullif(accepted_audit.details_json #>> '{recompute,targets,0,queue_item_id}', '')::uuid
where action_row.request_id = '${APPROVE_REQUEST_ID}'
limit 1;"
}

wait_for_terminal_recompute() {
    local attempts="${1:-90}"
    local sleep_seconds="${2:-2}"
    local evidence='{}'

    for _ in $(seq 1 "$attempts"); do
        evidence="$(load_approve_runtime_evidence)"
        if [[ -n "$evidence" && "$evidence" != "null" ]]; then
            local status
            status="$(jq -r '.recomputeStatus // empty' <<<"$evidence")"
            if [[ "$status" == "done" || "$status" == "clarification_blocked" ]]; then
                printf '%s\n' "$evidence"
                return 0
            fi
        fi

        sleep "$sleep_seconds"
    done

    printf '%s\n' "$evidence"
    return 1
}

Runtime__Role=ops \
Database__ConnectionString="$DB_CONN" \
Redis__ConnectionString=127.0.0.1:6379 \
ASPNETCORE_URLS="$BASE_URL" \
LegacyDiagnostics__Web__RequireOperatorAccessToken=true \
LegacyDiagnostics__Web__OperatorAccessToken="$OPERATOR_TOKEN" \
LegacyDiagnostics__Web__OperatorIdentity=opint-002-d-validator \
LlmGateway__Providers__openrouter__ApiKey=or-live-opint002dvalidationkey \
dotnet run --project "$HOST_PROJECT" -- --operator-schema-init >"$HOST_LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 120); do
    ready_code="$(curl -s -o /dev/null -w "%{http_code}" "${BASE_URL}/operator/resolution/bootstrap" || true)"
    ready_code="${ready_code:-000}"
    if [[ "$ready_code" == "200" || "$ready_code" == "503" ]]; then
        break
    fi
    sleep 1
done

if [[ "${ready_code:-000}" == "000" ]]; then
    echo "Host did not become reachable at ${BASE_URL}" >&2
    exit 1
fi

MIGRATION_PRESENT="$(psql_scalar "select exists(select 1 from schema_migrations where id = '0052_operator_resolution_actions.sql');")"
TABLES_PRESENT="$(psql_scalar "select (exists(select 1 from information_schema.tables where table_schema='public' and table_name='operator_resolution_actions') and exists(select 1 from information_schema.tables where table_schema='public' and table_name='operator_audit_events'));")"

web_post_json "/api/operator/tracked-persons/query" '{"limit":10}'
TRACKED_QUERY_CODE="$LAST_HTTP_CODE"
TRACKED_QUERY_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$TRACKED_QUERY_CODE" "200" "tracked-person query"
expect_jq "$TRACKED_QUERY_BODY" '.accepted == true and (.trackedPersons | length) >= 1' "tracked-person query"
if [[ -z "$CURRENT_SESSION_ID" ]]; then
    echo "tracked-person query did not establish an operator session" >&2
    exit 1
fi
ESTABLISHED_SESSION_ID="$CURRENT_SESSION_ID"

TRACKED_PERSON_ID="$(jq -r --arg expected "$TRACKED_PERSON_NAME_EXPECTED" '.trackedPersons[] | select(.displayName == $expected) | .trackedPersonId' <<<"$TRACKED_QUERY_BODY" | head -n 1)"
TRACKED_PERSON_NAME="$(jq -r --arg expected "$TRACKED_PERSON_NAME_EXPECTED" '.trackedPersons[] | select(.displayName == $expected) | .displayName' <<<"$TRACKED_QUERY_BODY" | head -n 1)"
if [[ -z "$TRACKED_PERSON_ID" || "$TRACKED_PERSON_ID" == "null" ]]; then
    echo "expected tracked person '${TRACKED_PERSON_NAME_EXPECTED}' not found in tracked-person query" >&2
    exit 1
fi

OBJECT_REF="person:${TRACKED_PERSON_ID}:profile:global"
DEDUPE_KEY="opint-002-d|${TRACKED_PERSON_ID}|profile_runtime_review"
ITEM_KEY="review:runtime_defect:${DEFECT_ID}"

docker exec -i "$POSTGRES_CONTAINER" psql -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant >/dev/null <<SQL
insert into runtime_defects (
    id,
    defect_class,
    severity,
    status,
    scope_key,
    dedupe_key,
    object_type,
    object_ref,
    summary,
    details_json,
    occurrence_count,
    escalation_action,
    escalation_reason,
    first_seen_at_utc,
    last_seen_at_utc,
    created_at_utc,
    updated_at_utc
) values (
    '${DEFECT_ID}',
    'normalization',
    'high',
    'open',
    '${SCOPE_KEY}',
    '${DEDUPE_KEY}',
    'profile',
    '${OBJECT_REF}',
    'Bounded OPINT-002-D runtime review item',
    '{}'::jsonb,
    1,
    'switch_review_only',
    'Bounded validation review item',
    now(),
    now(),
    now(),
    now()
);
SQL

SELECT_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" '{ trackedPersonId: $trackedPersonId }')"
web_post_json "/api/operator/tracked-persons/select" "$SELECT_PAYLOAD"
SELECT_CODE="$LAST_HTTP_CODE"
SELECT_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$SELECT_CODE" "200" "tracked-person selection"
expect_jq "$SELECT_BODY" '.accepted == true and .session.activeTrackedPersonId == "'"$TRACKED_PERSON_ID"'" and .session.activeMode == "resolution_queue"' "tracked-person selection"
expect_jq "$SELECT_BODY" '.session.operatorSessionId == "'"$ESTABLISHED_SESSION_ID"'"' "tracked-person selection session continuity"

QUEUE_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" '{ trackedPersonId: $trackedPersonId, itemTypes: ["review"], sortBy: "updated_at", sortDirection: "desc", limit: 20 }')"
web_post_json "/api/operator/resolution/queue/query" "$QUEUE_PAYLOAD"
QUEUE_CODE="$LAST_HTTP_CODE"
QUEUE_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$QUEUE_CODE" "200" "resolution queue query"
expect_jq "$QUEUE_BODY" '.accepted == true' "resolution queue query"
expect_jq "$QUEUE_BODY" '.queue.items | any(.scopeItemKey == "'"$ITEM_KEY"'")' "resolution queue contains seeded item"

DETAIL_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" --arg itemKey "$ITEM_KEY" '{ trackedPersonId: $trackedPersonId, scopeItemKey: $itemKey, evidenceLimit: 3, evidenceSortBy: "observed_at", evidenceSortDirection: "desc" }')"
web_post_json "/api/operator/resolution/detail/query" "$DETAIL_PAYLOAD"
DETAIL_CODE="$LAST_HTTP_CODE"
DETAIL_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$DETAIL_CODE" "200" "resolution detail query"
expect_jq "$DETAIL_BODY" '.accepted == true and .detail.item.scopeItemKey == "'"$ITEM_KEY"'"' "resolution detail query"
expect_jq "$DETAIL_BODY" '.session.operatorSessionId == "'"$ESTABLISHED_SESSION_ID"'" and .session.activeScopeItemKey == "'"$ITEM_KEY"'"' "resolution detail session continuity"

APPROVE_PAYLOAD="$(jq -nc --arg requestId "$APPROVE_REQUEST_ID" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg itemKey "$ITEM_KEY" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $itemKey, actionType: "approve" }')"
web_post_json "/api/operator/resolution/actions" "$APPROVE_PAYLOAD"
APPROVE_CODE="$LAST_HTTP_CODE"
APPROVE_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$APPROVE_CODE" "200" "resolution approve action"
expect_jq "$APPROVE_BODY" '.accepted == true and .action.accepted == true and .action.idempotentReplay == false and .action.recompute.lifecycleStatus == "running"' "resolution approve action"
expect_jq "$APPROVE_BODY" '.action.recompute.targets | length >= 1' "resolution approve recompute targets"
APPROVE_ACTION_ID="$(jq -r '.action.actionId' <<<"$APPROVE_BODY")"
APPROVE_AUDIT_ID="$(jq -r '.action.auditEventId' <<<"$APPROVE_BODY")"
APPROVE_QUEUE_ITEM_ID="$(jq -r '.action.recompute.targets[0].queueItemId // empty' <<<"$APPROVE_BODY")"
if [[ -z "$APPROVE_QUEUE_ITEM_ID" ]]; then
    echo "approve action did not return a recompute queue item id" >&2
    exit 1
fi

APPROVE_ACTION_COUNT="$(psql_scalar "select count(*) from operator_resolution_actions where request_id = '${APPROVE_REQUEST_ID}';")"
APPROVE_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where request_id = '${APPROVE_REQUEST_ID}' and decision_outcome = 'accepted';")"

if ! APPROVE_RUNTIME_EVIDENCE="$(wait_for_terminal_recompute 120 2)"; then
    echo "approve recompute lifecycle did not reach done or clarification_blocked within bounded wait" >&2
    jq . <<<"$APPROVE_RUNTIME_EVIDENCE" >&2 || printf '%s\n' "$APPROVE_RUNTIME_EVIDENCE" >&2
    exit 1
fi

expect_jq "$APPROVE_RUNTIME_EVIDENCE" '.actionId == "'"$APPROVE_ACTION_ID"'"' "approve action row persisted"
expect_jq "$APPROVE_RUNTIME_EVIDENCE" '.auditQueueItemId == "'"$APPROVE_QUEUE_ITEM_ID"'" and .queueItemId == "'"$APPROVE_QUEUE_ITEM_ID"'"' "approve action queue linkage"
expect_jq "$APPROVE_RUNTIME_EVIDENCE" '.recomputeStatus == "done" or .recomputeStatus == "clarification_blocked"' "approve recompute terminal lifecycle"

web_post_json "/api/operator/resolution/actions" "$APPROVE_PAYLOAD"
REPLAY_APPROVE_CODE="$LAST_HTTP_CODE"
REPLAY_APPROVE_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$REPLAY_APPROVE_CODE" "200" "resolution approve replay"
expect_jq "$REPLAY_APPROVE_BODY" '.accepted == true and .action.accepted == true and .action.idempotentReplay == true and (.action.recompute.lifecycleStatus == "done" or .action.recompute.lifecycleStatus == "clarification_blocked")' "resolution approve replay"
REPLAY_APPROVE_LIFECYCLE="$(jq -r '.action.recompute.lifecycleStatus // empty' <<<"$REPLAY_APPROVE_BODY")"
REPLAY_APPROVE_TARGET_LIFECYCLE="$(jq -r '.action.recompute.targets[0].lifecycleStatus // empty' <<<"$REPLAY_APPROVE_BODY")"

REJECT_PAYLOAD="$(jq -nc --arg requestId "$REJECT_REQUEST_ID" --arg trackedPersonId "$TRACKED_PERSON_ID" --arg itemKey "$ITEM_KEY" '{ requestId: $requestId, trackedPersonId: $trackedPersonId, scopeItemKey: $itemKey, actionType: "reject" }')"
web_post_json "/api/operator/resolution/actions" "$REJECT_PAYLOAD"
REJECT_CODE="$LAST_HTTP_CODE"
REJECT_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$REJECT_CODE" "400" "resolution reject action missing explanation"
expect_jq "$REJECT_BODY" '.accepted == false and .failureReason == "explanation_required" and .action.accepted == false and .action.failureReason == "explanation_required"' "resolution reject action missing explanation"
REJECT_AUDIT_ID="$(jq -r '.action.auditEventId' <<<"$REJECT_BODY")"
REJECT_ACTION_COUNT="$(psql_scalar "select count(*) from operator_resolution_actions where request_id = '${REJECT_REQUEST_ID}';")"
REJECT_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where request_id = '${REJECT_REQUEST_ID}' and decision_outcome = 'denied' and failure_reason = 'explanation_required';")"

web_post_json "/api/operator/tracked-persons/query" '{"limit":1}' "opint-002-d-invalid-token"
AUTH_DENIED_CODE="$LAST_HTTP_CODE"
AUTH_DENIED_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$AUTH_DENIED_CODE" "401" "tracked-person query auth-denied"
expect_jq "$AUTH_DENIED_BODY" '.accepted == false and .failureReason == "auth_denied"' "tracked-person query auth-denied"

CURRENT_SESSION_ID="$ESTABLISHED_SESSION_ID"
SELECTION_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where operator_session_id = '${ESTABLISHED_SESSION_ID}' and session_event_type = 'tracked_person_switch';")"

jq -n \
    --arg generatedAt "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
    --arg scopeKey "$SCOPE_KEY" \
    --arg operatorToken "$OPERATOR_TOKEN" \
    --arg operatorSessionId "$ESTABLISHED_SESSION_ID" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg trackedPersonName "$TRACKED_PERSON_NAME" \
    --arg defectId "$DEFECT_ID" \
    --arg itemKey "$ITEM_KEY" \
    --arg migrationPresent "$MIGRATION_PRESENT" \
    --arg tablesPresent "$TABLES_PRESENT" \
    --arg approveRequestId "$APPROVE_REQUEST_ID" \
    --arg approveActionId "$APPROVE_ACTION_ID" \
    --arg approveAuditId "$APPROVE_AUDIT_ID" \
    --arg approveQueueItemId "$APPROVE_QUEUE_ITEM_ID" \
    --arg replayApproveLifecycle "$REPLAY_APPROVE_LIFECYCLE" \
    --arg replayApproveTargetLifecycle "$REPLAY_APPROVE_TARGET_LIFECYCLE" \
    --arg rejectRequestId "$REJECT_REQUEST_ID" \
    --arg rejectAuditId "$REJECT_AUDIT_ID" \
    --arg approveActionCount "$APPROVE_ACTION_COUNT" \
    --arg approveAuditCount "$APPROVE_AUDIT_COUNT" \
    --arg rejectActionCount "$REJECT_ACTION_COUNT" \
    --arg rejectAuditCount "$REJECT_AUDIT_COUNT" \
    --arg selectionAuditCount "$SELECTION_AUDIT_COUNT" \
    --arg trackedQueryCode "$TRACKED_QUERY_CODE" \
    --arg selectCode "$SELECT_CODE" \
    --arg queueCode "$QUEUE_CODE" \
    --arg detailCode "$DETAIL_CODE" \
    --arg approveCode "$APPROVE_CODE" \
    --arg replayApproveCode "$REPLAY_APPROVE_CODE" \
    --arg rejectCode "$REJECT_CODE" \
    --arg authDeniedCode "$AUTH_DENIED_CODE" \
    --argjson approveBody "$APPROVE_BODY" \
    --argjson replayApproveBody "$REPLAY_APPROVE_BODY" \
    --argjson approveRuntimeEvidence "$APPROVE_RUNTIME_EVIDENCE" \
    '{
        generatedAt: $generatedAt,
        scopeKey: $scopeKey,
        trackedPerson: {
            trackedPersonId: $trackedPersonId,
            displayName: $trackedPersonName
        },
        seededValidationItem: {
            runtimeDefectId: $defectId,
            scopeItemKey: $itemKey
        },
        sessionContract: {
            operatorAccessToken: $operatorToken,
            establishedOperatorSessionId: $operatorSessionId,
            trackedPersonSwitchAuditRows: ($selectionAuditCount | tonumber)
        },
        schemaCheck: {
            migration0052Present: ($migrationPresent == "t"),
            operatorTablesPresent: ($tablesPresent == "t")
        },
        scenarios: [
            {
                name: "read_normal",
                trackedPersonQueryHttp: ($trackedQueryCode | tonumber),
                trackedPersonSelectionHttp: ($selectCode | tonumber),
                queueHttp: ($queueCode | tonumber),
                detailHttp: ($detailCode | tonumber)
            },
            {
                name: "write_normal_initial_running",
                actionHttp: ($approveCode | tonumber),
                requestId: $approveRequestId,
                actionId: $approveActionId,
                auditEventId: $approveAuditId,
                queueItemId: $approveQueueItemId,
                initialLifecycleStatus: $approveBody.action.recompute.lifecycleStatus,
                initialTargetLifecycleStatus: $approveBody.action.recompute.targets[0].lifecycleStatus,
                persistedActionRows: ($approveActionCount | tonumber),
                persistedAcceptedAuditRows: ($approveAuditCount | tonumber)
            },
            {
                name: "write_replay_terminal_projection",
                actionHttp: ($replayApproveCode | tonumber),
                lifecycleStatus: $replayApproveLifecycle,
                targetLifecycleStatus: $replayApproveTargetLifecycle,
                idempotentReplay: $replayApproveBody.action.idempotentReplay
            },
            {
                name: "write_failure_missing_explanation",
                actionHttp: ($rejectCode | tonumber),
                requestId: $rejectRequestId,
                auditEventId: $rejectAuditId,
                persistedActionRows: ($rejectActionCount | tonumber),
                persistedDeniedAuditRows: ($rejectAuditCount | tonumber),
                failureReason: "explanation_required"
            },
            {
                name: "read_auth_denied_invalid_token",
                queryHttp: ($authDeniedCode | tonumber),
                failureReason: "auth_denied"
            }
        ],
        approveActionChain: {
            initialApiResponse: $approveBody.action.recompute,
            replayApiResponse: $replayApproveBody.action.recompute,
            runtimeEvidence: $approveRuntimeEvidence
        }
    }' >"$REPORT_PATH"

cat "$REPORT_PATH"

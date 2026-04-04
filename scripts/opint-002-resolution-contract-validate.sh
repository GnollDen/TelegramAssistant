#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_PROJECT="$ROOT_DIR/src/TgAssistant.Host/TgAssistant.Host.csproj"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-tga-postgres}"
BASE_URL="${BASE_URL:-http://127.0.0.1:5087}"
SCOPE_KEY="${SCOPE_KEY:-chat:885574984}"
REPORT_PATH="${REPORT_PATH:-$ROOT_DIR/logs/opint-002-d-validation-report.json}"

mkdir -p "$(dirname "$REPORT_PATH")"

if ! command -v docker >/dev/null 2>&1; then
    echo "docker is required" >&2
    exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required" >&2
    exit 1
fi

if ! command -v curl >/dev/null 2>&1; then
    echo "curl is required" >&2
    exit 1
fi

POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-$(docker inspect "$POSTGRES_CONTAINER" --format '{{range .Config.Env}}{{println .}}{{end}}' | awk -F= '$1 == "POSTGRES_PASSWORD" { print $2 }')}"
if [[ -z "$POSTGRES_PASSWORD" ]]; then
    echo "POSTGRES_PASSWORD could not be resolved" >&2
    exit 1
fi

DB_CONN="Host=127.0.0.1;Database=tgassistant;Username=tgassistant;Password=${POSTGRES_PASSWORD}"
SESSION_ID="opint-002-d-session-$(uuidgen)"
AUTH_NOW="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
EXPIRES_FUTURE="$(date -u -d '+30 minutes' +"%Y-%m-%dT%H:%M:%SZ")"
EXPIRES_PAST="$(date -u -d '-5 minutes' +"%Y-%m-%dT%H:%M:%SZ")"
DEFECT_ID="$(uuidgen)"
OTHER_TRACKED_PERSON_ID="$(uuidgen)"
APPROVE_REQUEST_ID="opint-002-d-approve-$(uuidgen)"
REJECT_REQUEST_ID="opint-002-d-reject-$(uuidgen)"
DENIED_REQUEST_ID="opint-002-d-denied-$(uuidgen)"
HOST_LOG="$(mktemp)"

TRACKED_PERSON_ID=""
TRACKED_PERSON_NAME=""
ITEM_KEY=""
APPROVE_ACTION_ID=""
APPROVE_AUDIT_ID=""
REJECT_AUDIT_ID=""
DENIED_AUDIT_ID=""
SELECTION_AUDIT_COUNT="0"
MIGRATION_PRESENT="false"
TABLES_PRESENT="false"

cleanup() {
    local status=$?

    if [[ -n "${TRACKED_PERSON_ID:-}" ]]; then
        docker exec -i "$POSTGRES_CONTAINER" psql -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant >/dev/null <<SQL || true
delete from operator_resolution_actions where operator_session_id = '${SESSION_ID}';
delete from operator_audit_events where operator_session_id = '${SESSION_ID}';
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

    rm -f "$HOST_LOG"
    exit $status
}
trap cleanup EXIT

post_json() {
    local endpoint="$1"
    local payload="$2"
    local response_file
    response_file="$(mktemp)"
    local http_code
    http_code="$(curl -sS -o "$response_file" -w "%{http_code}" \
        -X POST "${BASE_URL}${endpoint}" \
        -H "Content-Type: application/json" \
        --data "$payload")"
    local body
    body="$(cat "$response_file")"
    rm -f "$response_file"
    printf '%s\n%s' "$http_code" "$body"
}

psql_scalar() {
    local sql="$1"
    docker exec -i "$POSTGRES_CONTAINER" psql -At -v ON_ERROR_STOP=1 -U tgassistant -d tgassistant -c "$sql"
}

expect_http() {
    local actual="$1"
    local expected="$2"
    local label="$3"
    if [[ "$actual" != "$expected" ]]; then
        echo "${label}: expected HTTP ${expected}, got ${actual}" >&2
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

Runtime__Role=ops \
Database__ConnectionString="$DB_CONN" \
Redis__ConnectionString=127.0.0.1:6379 \
ASPNETCORE_URLS="$BASE_URL" \
LlmGateway__Providers__openrouter__ApiKey=or-live-opint002dvalidationkey \
dotnet run --project "$HOST_PROJECT" -- --operator-schema-init >"$HOST_LOG" 2>&1 &
HOST_PID=$!

for _ in $(seq 1 120); do
    ready_code="$(curl -s -o /dev/null -w "%{http_code}" \
        -X POST "${BASE_URL}/api/operator/tracked-persons/query" \
        -H "Content-Type: application/json" \
        --data '{}' || true)"
    ready_code="${ready_code:-000}"
    if [[ "$ready_code" != "000" ]]; then
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

TRACKED_QUERY_PAYLOAD="$(jq -nc \
    --arg operatorId "opint-002-d-validator" \
    --arg operatorDisplay "OPINT-002-D Validator" \
    --arg surfaceSubject "validation" \
    --arg authSource "local_validation" \
    --arg authNow "$AUTH_NOW" \
    --arg sessionId "$SESSION_ID" \
    --arg expiresAt "$EXPIRES_FUTURE" \
    '{
        operatorIdentity: {
            operatorId: $operatorId,
            operatorDisplay: $operatorDisplay,
            surfaceSubject: $surfaceSubject,
            authSource: $authSource,
            authTimeUtc: $authNow
        },
        session: {
            operatorSessionId: $sessionId,
            surface: "web",
            authenticatedAtUtc: $authNow,
            lastSeenAtUtc: $authNow,
            expiresAtUtc: $expiresAt
        },
        limit: 10
    }')"

mapfile -t tracked_query_result < <(post_json "/api/operator/tracked-persons/query" "$TRACKED_QUERY_PAYLOAD")
TRACKED_QUERY_CODE="${tracked_query_result[0]}"
TRACKED_QUERY_BODY="${tracked_query_result[1]}"
expect_http "$TRACKED_QUERY_CODE" "200" "tracked-person query"
expect_jq "$TRACKED_QUERY_BODY" '.accepted == true and (.trackedPersons | length) >= 1' "tracked-person query"

TRACKED_PERSON_ID="$(jq -r '.activeTrackedPersonId // .trackedPersons[0].trackedPersonId' <<<"$TRACKED_QUERY_BODY")"
TRACKED_PERSON_NAME="$(jq -r '.activeTrackedPerson.displayName // .trackedPersons[0].displayName' <<<"$TRACKED_QUERY_BODY")"
if [[ -z "$TRACKED_PERSON_ID" || "$TRACKED_PERSON_ID" == "null" ]]; then
    echo "tracked person id could not be resolved" >&2
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

SELECT_PAYLOAD="$(jq -nc \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    '{
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: "'"$AUTH_NOW"'"
        },
        session: {
            operatorSessionId: "'"$SESSION_ID"'",
            surface: "web",
            authenticatedAtUtc: "'"$AUTH_NOW"'",
            lastSeenAtUtc: "'"$AUTH_NOW"'",
            expiresAtUtc: "'"$EXPIRES_FUTURE"'"
        },
        trackedPersonId: $trackedPersonId
    }')"
mapfile -t select_result < <(post_json "/api/operator/tracked-persons/select" "$SELECT_PAYLOAD")
SELECT_CODE="${select_result[0]}"
SELECT_BODY="${select_result[1]}"
expect_http "$SELECT_CODE" "200" "tracked-person selection"
expect_jq "$SELECT_BODY" '.accepted == true and .session.activeTrackedPersonId == "'"$TRACKED_PERSON_ID"'" and .session.activeMode == "resolution_queue"' "tracked-person selection"

QUEUE_PAYLOAD="$(jq -nc \
    --argjson selected "$SELECT_BODY" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    '{
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: $selected.session.authenticatedAtUtc
        },
        session: $selected.session,
        trackedPersonId: $trackedPersonId,
        itemTypes: ["review"],
        sortBy: "updated_at",
        sortDirection: "desc",
        limit: 20
    }')"
mapfile -t queue_result < <(post_json "/api/operator/resolution/queue/query" "$QUEUE_PAYLOAD")
QUEUE_CODE="${queue_result[0]}"
QUEUE_BODY="${queue_result[1]}"
expect_http "$QUEUE_CODE" "200" "resolution queue query"
expect_jq "$QUEUE_BODY" '.accepted == true' "resolution queue query"
expect_jq "$QUEUE_BODY" '.queue.items | any(.scopeItemKey == "'"$ITEM_KEY"'")' "resolution queue contains seeded item"

DETAIL_PAYLOAD="$(jq -nc \
    --argjson queue "$QUEUE_BODY" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg itemKey "$ITEM_KEY" \
    '{
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: $queue.session.authenticatedAtUtc
        },
        session: $queue.session,
        trackedPersonId: $trackedPersonId,
        scopeItemKey: $itemKey,
        evidenceLimit: 3,
        evidenceSortBy: "observed_at",
        evidenceSortDirection: "desc"
    }')"
mapfile -t detail_result < <(post_json "/api/operator/resolution/detail/query" "$DETAIL_PAYLOAD")
DETAIL_CODE="${detail_result[0]}"
DETAIL_BODY="${detail_result[1]}"
expect_http "$DETAIL_CODE" "200" "resolution detail query"
expect_jq "$DETAIL_BODY" '.accepted == true and .detail.item.scopeItemKey == "'"$ITEM_KEY"'"' "resolution detail query"

APPROVE_PAYLOAD="$(jq -nc \
    --arg requestId "$APPROVE_REQUEST_ID" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg itemKey "$ITEM_KEY" \
    --argjson detail "$DETAIL_BODY" \
    '{
        requestId: $requestId,
        trackedPersonId: $trackedPersonId,
        scopeItemKey: $itemKey,
        actionType: "approve",
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: $detail.session.authenticatedAtUtc
        },
        session: $detail.session
    }')"
mapfile -t approve_result < <(post_json "/api/operator/resolution/actions" "$APPROVE_PAYLOAD")
APPROVE_CODE="${approve_result[0]}"
APPROVE_BODY="${approve_result[1]}"
expect_http "$APPROVE_CODE" "200" "resolution approve action"
expect_jq "$APPROVE_BODY" '.accepted == true and .action.accepted == true and .action.idempotentReplay == false' "resolution approve action"
APPROVE_ACTION_ID="$(jq -r '.action.actionId' <<<"$APPROVE_BODY")"
APPROVE_AUDIT_ID="$(jq -r '.action.auditEventId' <<<"$APPROVE_BODY")"

APPROVE_ACTION_COUNT="$(psql_scalar "select count(*) from operator_resolution_actions where request_id = '${APPROVE_REQUEST_ID}';")"
APPROVE_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where request_id = '${APPROVE_REQUEST_ID}' and decision_outcome = 'accepted';")"

REJECT_PAYLOAD="$(jq -nc \
    --arg requestId "$REJECT_REQUEST_ID" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg itemKey "$ITEM_KEY" \
    --argjson detail "$DETAIL_BODY" \
    '{
        requestId: $requestId,
        trackedPersonId: $trackedPersonId,
        scopeItemKey: $itemKey,
        actionType: "reject",
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: $detail.session.authenticatedAtUtc
        },
        session: $detail.session
    }')"
mapfile -t reject_result < <(post_json "/api/operator/resolution/actions" "$REJECT_PAYLOAD")
REJECT_CODE="${reject_result[0]}"
REJECT_BODY="${reject_result[1]}"
expect_http "$REJECT_CODE" "400" "resolution reject action missing explanation"
expect_jq "$REJECT_BODY" '.accepted == false and .failureReason == "explanation_required" and .action.accepted == false and .action.failureReason == "explanation_required"' "resolution reject action missing explanation"
REJECT_AUDIT_ID="$(jq -r '.action.auditEventId' <<<"$REJECT_BODY")"
REJECT_ACTION_COUNT="$(psql_scalar "select count(*) from operator_resolution_actions where request_id = '${REJECT_REQUEST_ID}';")"
REJECT_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where request_id = '${REJECT_REQUEST_ID}' and decision_outcome = 'denied' and failure_reason = 'explanation_required';")"

EXPIRED_QUEUE_PAYLOAD="$(jq -nc \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg expiresAt "$EXPIRES_PAST" \
    --arg authNow "$AUTH_NOW" \
    --arg sessionId "$SESSION_ID" \
    '{
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: $authNow
        },
        session: {
            operatorSessionId: $sessionId,
            surface: "web",
            authenticatedAtUtc: $authNow,
            lastSeenAtUtc: $authNow,
            expiresAtUtc: $expiresAt,
            activeTrackedPersonId: $trackedPersonId,
            activeMode: "resolution_queue"
        },
        trackedPersonId: $trackedPersonId
    }')"
mapfile -t expired_queue_result < <(post_json "/api/operator/resolution/queue/query" "$EXPIRED_QUEUE_PAYLOAD")
EXPIRED_QUEUE_CODE="${expired_queue_result[0]}"
EXPIRED_QUEUE_BODY="${expired_queue_result[1]}"
expect_http "$EXPIRED_QUEUE_CODE" "401" "resolution queue auth-denied"
expect_jq "$EXPIRED_QUEUE_BODY" '.accepted == false and .failureReason == "session_expired"' "resolution queue auth-denied"

DENIED_PAYLOAD="$(jq -nc \
    --arg requestId "$DENIED_REQUEST_ID" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg otherTrackedPersonId "$OTHER_TRACKED_PERSON_ID" \
    --arg itemKey "$ITEM_KEY" \
    --argjson detail "$DETAIL_BODY" \
    '{
        requestId: $requestId,
        trackedPersonId: $trackedPersonId,
        scopeItemKey: $itemKey,
        actionType: "approve",
        operatorIdentity: {
            operatorId: "opint-002-d-validator",
            operatorDisplay: "OPINT-002-D Validator",
            surfaceSubject: "validation",
            authSource: "local_validation",
            authTimeUtc: $detail.session.authenticatedAtUtc
        },
        session: ($detail.session + { activeTrackedPersonId: $otherTrackedPersonId })
    }')"
mapfile -t denied_result < <(post_json "/api/operator/resolution/actions" "$DENIED_PAYLOAD")
DENIED_CODE="${denied_result[0]}"
DENIED_BODY="${denied_result[1]}"
expect_http "$DENIED_CODE" "403" "resolution action auth-denied"
expect_jq "$DENIED_BODY" '.accepted == false and .failureReason == "session_active_tracked_person_mismatch" and .action.accepted == false and .action.failureReason == "session_active_tracked_person_mismatch"' "resolution action auth-denied"
DENIED_AUDIT_ID="$(jq -r '.action.auditEventId' <<<"$DENIED_BODY")"
DENIED_ACTION_COUNT="$(psql_scalar "select count(*) from operator_resolution_actions where request_id = '${DENIED_REQUEST_ID}';")"
DENIED_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where request_id = '${DENIED_REQUEST_ID}' and decision_outcome = 'denied' and failure_reason = 'session_active_tracked_person_mismatch';")"
SELECTION_AUDIT_COUNT="$(psql_scalar "select count(*) from operator_audit_events where operator_session_id = '${SESSION_ID}' and session_event_type = 'tracked_person_switch';")"

jq -n \
    --arg scopeKey "$SCOPE_KEY" \
    --arg trackedPersonId "$TRACKED_PERSON_ID" \
    --arg trackedPersonName "$TRACKED_PERSON_NAME" \
    --arg defectId "$DEFECT_ID" \
    --arg itemKey "$ITEM_KEY" \
    --arg migrationPresent "$MIGRATION_PRESENT" \
    --arg tablesPresent "$TABLES_PRESENT" \
    --arg approveActionId "$APPROVE_ACTION_ID" \
    --arg approveAuditId "$APPROVE_AUDIT_ID" \
    --arg rejectAuditId "$REJECT_AUDIT_ID" \
    --arg deniedAuditId "$DENIED_AUDIT_ID" \
    --arg approveRequestId "$APPROVE_REQUEST_ID" \
    --arg rejectRequestId "$REJECT_REQUEST_ID" \
    --arg deniedRequestId "$DENIED_REQUEST_ID" \
    --arg approveActionCount "$APPROVE_ACTION_COUNT" \
    --arg approveAuditCount "$APPROVE_AUDIT_COUNT" \
    --arg rejectActionCount "$REJECT_ACTION_COUNT" \
    --arg rejectAuditCount "$REJECT_AUDIT_COUNT" \
    --arg deniedActionCount "$DENIED_ACTION_COUNT" \
    --arg deniedAuditCount "$DENIED_AUDIT_COUNT" \
    --arg selectionAuditCount "$SELECTION_AUDIT_COUNT" \
    --arg trackedQueryCode "$TRACKED_QUERY_CODE" \
    --arg selectCode "$SELECT_CODE" \
    --arg queueCode "$QUEUE_CODE" \
    --arg detailCode "$DETAIL_CODE" \
    --arg approveCode "$APPROVE_CODE" \
    --arg rejectCode "$REJECT_CODE" \
    --arg expiredQueueCode "$EXPIRED_QUEUE_CODE" \
    --arg deniedCode "$DENIED_CODE" \
    '{
        scopeKey: $scopeKey,
        trackedPerson: {
            trackedPersonId: $trackedPersonId,
            displayName: $trackedPersonName
        },
        seededValidationItem: {
            runtimeDefectId: $defectId,
            scopeItemKey: $itemKey
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
                name: "write_normal",
                actionHttp: ($approveCode | tonumber),
                requestId: $approveRequestId,
                actionId: $approveActionId,
                auditEventId: $approveAuditId,
                persistedActionRows: ($approveActionCount | tonumber),
                persistedAcceptedAuditRows: ($approveAuditCount | tonumber)
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
                name: "read_auth_denied_expired_session",
                queueHttp: ($expiredQueueCode | tonumber),
                failureReason: "session_expired"
            },
            {
                name: "write_auth_denied_scope_mismatch",
                actionHttp: ($deniedCode | tonumber),
                requestId: $deniedRequestId,
                auditEventId: $deniedAuditId,
                persistedActionRows: ($deniedActionCount | tonumber),
                persistedDeniedAuditRows: ($deniedAuditCount | tonumber),
                failureReason: "session_active_tracked_person_mismatch"
            }
        ],
        additionalChecks: {
            trackedPersonSwitchAuditRows: ($selectionAuditCount | tonumber)
        }
    }' >"$REPORT_PATH"

cat "$REPORT_PATH"

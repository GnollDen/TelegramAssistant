#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
HOST_PROJECT="$ROOT_DIR/src/TgAssistant.Host/TgAssistant.Host.csproj"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-tga-postgres}"
BASE_URL="${BASE_URL:-http://127.0.0.1:5195}"
SCOPE_KEY="${SCOPE_KEY:-chat:885574984}"
TRACKED_PERSON_NAME_EXPECTED="${TRACKED_PERSON_NAME_EXPECTED:-Гайнутдинова Алёна Амировна}"
REPORT_PATH="${REPORT_PATH:-$ROOT_DIR/logs/opint-008-workspace-summary-snapshot-report.json}"
OPERATOR_TOKEN="${OPERATOR_TOKEN:-opint-008-workspace-snapshot-token}"

mkdir -p "$(dirname "$REPORT_PATH")"

for tool in docker jq curl dotnet; do
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
CURRENT_SESSION_ID=""

cleanup() {
  local status=$?

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

web_post_json() {
  local endpoint="$1"
  local payload="$2"
  : >"$RESPONSE_HEADERS_FILE"
  local code

  if [[ -n "$CURRENT_SESSION_ID" ]]; then
    code="$(curl -sS -o "$RESPONSE_BODY_FILE" -D "$RESPONSE_HEADERS_FILE" -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
      -H "Content-Type: application/json" \
      -H "X-Tga-Operator-Key: ${OPERATOR_TOKEN}" \
      -H "X-Tga-Operator-Session: ${CURRENT_SESSION_ID}" \
      -X POST "${BASE_URL}${endpoint}" \
      -d "$payload" \
      -w "%{http_code}")"
  else
    code="$(curl -sS -o "$RESPONSE_BODY_FILE" -D "$RESPONSE_HEADERS_FILE" -b "$COOKIE_JAR" -c "$COOKIE_JAR" \
      -H "Content-Type: application/json" \
      -H "X-Tga-Operator-Key: ${OPERATOR_TOKEN}" \
      -X POST "${BASE_URL}${endpoint}" \
      -d "$payload" \
      -w "%{http_code}")"
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
LlmGateway__Providers__openrouter__ApiKey=or-live-opint008snapshotvalidationkey \
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

WORKSPACE_HTML="$(curl -sS "${BASE_URL}/operator/person-workspace")"
if ! grep -Fq "Человек в фокусе" <<<"$WORKSPACE_HTML"; then
  echo "workspace html check failed: missing person-first heading" >&2
  exit 1
fi
if ! grep -Fq "Ваш контекст" <<<"$WORKSPACE_HTML"; then
  echo "workspace html check failed: missing operator context label" >&2
  exit 1
fi
if ! grep -Fq "Версия взаимодействия" <<<"$WORKSPACE_HTML"; then
  echo "workspace html check failed: missing calibrated pair label" >&2
  exit 1
fi

TRACKED_QUERY_CODE="$(web_post_json "/api/operator/tracked-persons/query" '{"limit":50}')"
TRACKED_QUERY_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$TRACKED_QUERY_CODE" "200" "tracked-person query"
expect_jq "$TRACKED_QUERY_BODY" '.accepted == true and (.trackedPersons | type == "array") and (.trackedPersons | length) >= 1' "tracked-person query accepted"

TRACKED_PERSON_ID="$(jq -r --arg expected "$TRACKED_PERSON_NAME_EXPECTED" '.trackedPersons[] | select(.displayName == $expected and .scopeKey == "'"$SCOPE_KEY"'") | .trackedPersonId' <<<"$TRACKED_QUERY_BODY" | head -n 1)"
if [[ -z "$TRACKED_PERSON_ID" || "$TRACKED_PERSON_ID" == "null" ]]; then
  echo "Failed to resolve tracked person id for expected bounded scope/person." >&2
  jq . <<<"$TRACKED_QUERY_BODY" >&2
  exit 1
fi

SELECT_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" '{ trackedPersonId: $trackedPersonId }')"
SELECT_CODE="$(web_post_json "/api/operator/tracked-persons/select" "$SELECT_PAYLOAD")"
SELECT_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$SELECT_CODE" "200" "tracked-person select"
expect_jq "$SELECT_BODY" '.accepted == true and .session.activeTrackedPersonId == "'"$TRACKED_PERSON_ID"'"' "tracked-person selected"

SUMMARY_PAYLOAD="$(jq -nc --arg trackedPersonId "$TRACKED_PERSON_ID" '{ trackedPersonId: $trackedPersonId }')"
SUMMARY_CODE="$(web_post_json "/api/operator/person-workspace/summary/query" "$SUMMARY_PAYLOAD")"
SUMMARY_BODY="$(cat "$RESPONSE_BODY_FILE")"
expect_http "$SUMMARY_CODE" "200" "workspace summary query"
expect_jq "$SUMMARY_BODY" '.accepted == true and .workspace.summary.snapshot != null' "summary snapshot present"
expect_jq "$SUMMARY_BODY" '.workspace.summary.snapshot.operator != null and .workspace.summary.snapshot.trackedPerson != null and .workspace.summary.snapshot.pair != null' "summary snapshot shape"
expect_jq "$SUMMARY_BODY" '.workspace.summary.snapshot.trackedPerson.scopeKey == "'"$SCOPE_KEY"'" and .workspace.summary.snapshot.trackedPerson.displayName == "'"$TRACKED_PERSON_NAME_EXPECTED"'"' "summary tracked scope/person"
expect_jq "$SUMMARY_BODY" '(.workspace.summary.snapshot.operator.operatorDisplay // .workspace.summary.snapshot.operator.operatorId // "") | tostring | length > 0' "summary operator side present"

jq -n \
  --arg generatedAtUtc "$(date -u +"%Y-%m-%dT%H:%M:%SZ")" \
  --arg scopeKey "$SCOPE_KEY" \
  --arg trackedPersonId "$TRACKED_PERSON_ID" \
  --arg trackedPersonExpected "$TRACKED_PERSON_NAME_EXPECTED" \
  --arg operatorDisplay "$(jq -r '.workspace.summary.snapshot.operator.operatorDisplay // ""' <<<"$SUMMARY_BODY")" \
  --arg operatorId "$(jq -r '.workspace.summary.snapshot.operator.operatorId // ""' <<<"$SUMMARY_BODY")" \
  --arg pairLabel "$(jq -r '.workspace.summary.snapshot.pair.label // ""' <<<"$SUMMARY_BODY")" \
  --argjson pairAvailable "$(jq '.workspace.summary.snapshot.pair.available // false' <<<"$SUMMARY_BODY")" \
  --argjson contradictionCount "$(jq '.workspace.summary.snapshot.pair.contradictionCount // 0' <<<"$SUMMARY_BODY")" \
  --argjson trust "$(jq '.workspace.summary.snapshot.pair.trust // null' <<<"$SUMMARY_BODY")" \
  --argjson uncertainty "$(jq '.workspace.summary.snapshot.pair.uncertainty // null' <<<"$SUMMARY_BODY")" \
  --argjson unresolvedCount "$(jq '.workspace.summary.snapshot.trackedPerson.unresolvedCount // null' <<<"$SUMMARY_BODY")" \
  '{
    generatedAtUtc: $generatedAtUtc,
    scopeKey: $scopeKey,
    trackedPerson: {
      trackedPersonId: $trackedPersonId,
      expectedDisplayName: $trackedPersonExpected
    },
    snapshotShape: {
      operatorPresent: true,
      trackedPresent: true,
      pairPresent: true
    },
    uiCopyChecks: {
      personFirstHeadingPresent: true,
      operatorContextLabelPresent: true,
      calibratedPairLabelPresent: true
    },
    snapshotValues: {
      operatorDisplay: $operatorDisplay,
      operatorId: $operatorId,
      pairLabel: $pairLabel,
      pairAvailable: $pairAvailable,
      contradictionCount: $contradictionCount,
      trust: $trust,
      uncertainty: $uncertainty,
      trackedUnresolvedCount: $unresolvedCount
    },
    passed: true
  }' >"$REPORT_PATH"

cat "$REPORT_PATH"

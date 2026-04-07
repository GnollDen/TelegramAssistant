#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OVERLAY_DEFAULT="${REPO_ROOT}/docs/planning/artifacts/task-by-task-junior-hardening-2026-04-06.md"
STATE_DEFAULT="${REPO_ROOT}/logs/ralph-all-tasks-state.json"
DTP_GATE_DEFAULT="${REPO_ROOT}/docs/planning/artifacts/dtp-2026-04-06-pre-execution-gate.md"
PHB_GATE_DEFAULT="${REPO_ROOT}/docs/planning/artifacts/phase-b-2026-04-06-pre-execution-gate.md"

OVERLAY_PATH="${OVERLAY_DEFAULT}"
STATE_PATH="${STATE_DEFAULT}"
DTP_GATE_PATH="${DTP_GATE_DEFAULT}"
PHB_GATE_PATH="${PHB_GATE_DEFAULT}"

CMD=""
TASK_ID=""
NOTE=""
FILTER="all"

TASK_ORDER=(
  DTP-001 DTP-002A DTP-002B DTP-003 DTP-004 DTP-005 DTP-006A DTP-006B
  DTP-008A DTP-008B DTP-007 DTP-009A DTP-009B DTP-010 DTP-011 DTP-012A DTP-012B
  DTP-013A DTP-013B DTP-014 DTP-015A DTP-015B
  PHB-001 PHB-002 PHB-003 PHB-004 PHB-005 PHB-006A PHB-006B PHB-006C PHB-007
  PHB-008A PHB-008B PHB-009 PHB-010A PHB-010B PHB-011A PHB-011B PHB-012A PHB-012B
  PHB-013 PHB-014A PHB-014B PHB-015 PHB-016A PHB-016B PHB-016C PHB-017 PHB-018A PHB-018B
)

usage() {
  cat <<'USAGE'
Usage: scripts/ralph-all-tasks.sh [options] <command>

Commands:
  init                    Initialize state file
  list                    Show tasks and statuses
  next                    Show next runnable task
  start --task <id>       Mark task as in_progress (strict dependency enforced)
  done --task <id>        Mark in_progress task as done
  fail --task <id>        Mark in_progress task as failed
  prompt --task <id>      Print execution prompt block for a task from overlay
  reset --task <id>       Reset task to todo
  status                  Print summary counts

Options:
  --task <id>             Task id (e.g., DTP-001, PHB-010A)
  --note <text>           Optional note for start/done/fail/reset
  --filter <kind>         For list: all|todo|in_progress|done|failed
  --state-file <path>     State JSON path (default: logs/ralph-all-tasks-state.json)
  --overlay <path>        Junior-hardening overlay markdown path
  --dtp-gate <path>       DTP gate artifact path
  --phb-gate <path>       PHB gate artifact path
  -h, --help              Show this help
USAGE
}

die() {
  echo "ERROR: $*" >&2
  exit 1
}

require_tools() {
  command -v jq >/dev/null 2>&1 || die "jq not found"
  command -v awk >/dev/null 2>&1 || die "awk not found"
}

is_task_known() {
  local id="$1"
  local t
  for t in "${TASK_ORDER[@]}"; do
    if [[ "$t" == "$id" ]]; then
      return 0
    fi
  done
  return 1
}

task_index() {
  local id="$1"
  local i
  for i in "${!TASK_ORDER[@]}"; do
    if [[ "${TASK_ORDER[$i]}" == "$id" ]]; then
      echo "$i"
      return 0
    fi
  done
  return 1
}

task_phase() {
  local id="$1"
  if [[ "$id" == DTP-* ]]; then
    echo "DTP"
    return 0
  fi
  if [[ "$id" == PHB-* ]]; then
    echo "PHB"
    return 0
  fi
  echo "UNKNOWN"
}

utc_now() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

ensure_dirs() {
  mkdir -p "$(dirname "$STATE_PATH")"
}

init_state() {
  ensure_dirs
  local now
  now="$(utc_now)"

  {
    echo '{'
    echo '  "version": 1,'
    echo "  \"created_at_utc\": \"${now}\","
    echo "  \"updated_at_utc\": \"${now}\","
    echo '  "tasks": {'
    local i
    for i in "${!TASK_ORDER[@]}"; do
      local id="${TASK_ORDER[$i]}"
      cat <<EOF_ENTRY
    "${id}": {
      "status": "todo",
      "phase": "$(task_phase "$id")",
      "index": $((i+1)),
      "started_at_utc": null,
      "finished_at_utc": null,
      "note": null
    }$( [[ "$i" -lt $((${#TASK_ORDER[@]}-1)) ]] && echo "," )
EOF_ENTRY
    done
    echo '  }'
    echo '}'
  } > "$STATE_PATH"

  echo "Initialized state: $STATE_PATH"
}

ensure_state() {
  if [[ ! -f "$STATE_PATH" ]]; then
    init_state
  fi
}

get_status() {
  local id="$1"
  jq -r --arg id "$id" '.tasks[$id].status // "missing"' "$STATE_PATH"
}

update_task() {
  local id="$1"
  local status="$2"
  local note="$3"
  local now
  now="$(utc_now)"
  local tmp
  tmp="$(mktemp)"

  local started_expr='.'
  local finished_expr='.'
  if [[ "$status" == "in_progress" ]]; then
    started_expr=".tasks[\$id].started_at_utc = \$now"
    finished_expr='.'
  elif [[ "$status" == "done" || "$status" == "failed" ]]; then
    finished_expr=".tasks[\$id].finished_at_utc = \$now"
  elif [[ "$status" == "todo" ]]; then
    started_expr=".tasks[\$id].started_at_utc = null"
    finished_expr=".tasks[\$id].finished_at_utc = null"
  fi

  jq \
    --arg id "$id" \
    --arg status "$status" \
    --arg now "$now" \
    --arg note "$note" \
    "
    .updated_at_utc = \$now
    | .tasks[\$id].status = \$status
    | ${started_expr}
    | ${finished_expr}
    | .tasks[\$id].note = (if (\$note|length) > 0 then \$note else .tasks[\$id].note end)
    " \
    "$STATE_PATH" > "$tmp"

  mv "$tmp" "$STATE_PATH"
}

check_linear_dependencies() {
  local id="$1"
  local idx
  idx="$(task_index "$id")" || die "unknown task id: $id"
  local i
  for ((i=0; i<idx; i++)); do
    local prev="${TASK_ORDER[$i]}"
    local st
    st="$(get_status "$prev")"
    if [[ "$st" != "done" ]]; then
      die "dependency not satisfied: $id requires previous task $prev to be done (current: $st)"
    fi
  done
}

gate_is_pass() {
  local gate_file="$1"
  if [[ ! -f "$gate_file" ]]; then
    return 1
  fi
  grep -Eiq 'status:[[:space:]]*`?pass`?' "$gate_file"
}

check_cross_phase_gate() {
  local id="$1"
  if [[ "$(task_phase "$id")" != "PHB" ]]; then
    return 0
  fi
  if ! gate_is_pass "$DTP_GATE_PATH"; then
    die "PHB is blocked: DTP gate is not pass ($DTP_GATE_PATH)"
  fi
}

cmd_list() {
  ensure_state
  local jq_filter='.'
  case "$FILTER" in
    all) jq_filter='.' ;;
    todo|in_progress|done|failed)
      jq_filter="select(.value.status == \"${FILTER}\")"
      ;;
    *)
      die "invalid --filter: $FILTER"
      ;;
  esac

  jq -r "
    .tasks
    | to_entries[]
    | ${jq_filter}
    | [.value.index, .key, .value.phase, .value.status, (.value.note // \"\")]
    | @tsv
  " "$STATE_PATH" | sort -n | awk -F'\t' '{printf "%02d  %-9s %-4s %-12s %s\n",$1,$2,$3,$4,$5}'
}

cmd_status() {
  ensure_state
  jq -r '
    .tasks
    | to_entries
    | reduce .[] as $t (
        {"todo":0,"in_progress":0,"done":0,"failed":0};
        .[$t.value.status] += 1
      )
    | "todo=\(.todo) in_progress=\(.in_progress) done=\(.done) failed=\(.failed)"
  ' "$STATE_PATH"
}

cmd_next() {
  ensure_state
  local id
  for id in "${TASK_ORDER[@]}"; do
    local st
    st="$(get_status "$id")"
    if [[ "$st" == "todo" ]]; then
      if [[ "$(task_phase "$id")" == "PHB" ]] && ! gate_is_pass "$DTP_GATE_PATH"; then
        echo "NEXT: $id (BLOCKED by DTP gate: $DTP_GATE_PATH)"
        return 0
      fi
      echo "NEXT: $id"
      return 0
    fi
    if [[ "$st" == "in_progress" ]]; then
      echo "IN_PROGRESS: $id"
      return 0
    fi
    if [[ "$st" == "failed" ]]; then
      echo "FAILED: $id (resume or reset required)"
      return 0
    fi
  done
  echo "ALL_DONE"
}

cmd_start() {
  ensure_state
  [[ -n "$TASK_ID" ]] || die "--task is required"
  is_task_known "$TASK_ID" || die "unknown task id: $TASK_ID"

  local st
  st="$(get_status "$TASK_ID")"
  [[ "$st" == "todo" ]] || die "cannot start $TASK_ID from status: $st"

  check_linear_dependencies "$TASK_ID"
  check_cross_phase_gate "$TASK_ID"

  update_task "$TASK_ID" "in_progress" "$NOTE"
  echo "STARTED: $TASK_ID"
}

cmd_done() {
  ensure_state
  [[ -n "$TASK_ID" ]] || die "--task is required"
  is_task_known "$TASK_ID" || die "unknown task id: $TASK_ID"

  local st
  st="$(get_status "$TASK_ID")"
  [[ "$st" == "in_progress" ]] || die "cannot mark done: $TASK_ID status is $st"

  update_task "$TASK_ID" "done" "$NOTE"
  echo "DONE: $TASK_ID"
}

cmd_fail() {
  ensure_state
  [[ -n "$TASK_ID" ]] || die "--task is required"
  is_task_known "$TASK_ID" || die "unknown task id: $TASK_ID"

  local st
  st="$(get_status "$TASK_ID")"
  [[ "$st" == "in_progress" ]] || die "cannot mark failed: $TASK_ID status is $st"

  update_task "$TASK_ID" "failed" "$NOTE"
  echo "FAILED: $TASK_ID"
}

cmd_reset() {
  ensure_state
  [[ -n "$TASK_ID" ]] || die "--task is required"
  is_task_known "$TASK_ID" || die "unknown task id: $TASK_ID"
  update_task "$TASK_ID" "todo" "$NOTE"
  echo "RESET: $TASK_ID"
}

extract_overlay_block() {
  local id="$1"
  [[ -f "$OVERLAY_PATH" ]] || die "overlay file not found: $OVERLAY_PATH"
  awk -v id="$id" '
    BEGIN { found=0; printing=0 }
    $0 ~ "^[0-9]+\\. `"id"`" { found=1; printing=1 }
    printing {
      if ($0 ~ "^[0-9]+\\. `" && $0 !~ "^[0-9]+\\. `"id"`") { exit }
      print
    }
    END {
      if (found==0) exit 2
    }
  ' "$OVERLAY_PATH"
}

cmd_prompt() {
  ensure_state
  [[ -n "$TASK_ID" ]] || die "--task is required"
  is_task_known "$TASK_ID" || die "unknown task id: $TASK_ID"

  local st
  st="$(get_status "$TASK_ID")"
  local block
  if ! block="$(extract_overlay_block "$TASK_ID")"; then
    die "task block not found in overlay for $TASK_ID"
  fi

  cat <<EOF_PROMPT
You are executing task ${TASK_ID} in repo:
${REPO_ROOT}

Current state in Ralph:
- task_status: ${st}
- dtp_gate_file: ${DTP_GATE_PATH}
- phb_gate_file: ${PHB_GATE_PATH}

Task execution overlay block:
${block}

Rules:
1. Follow only this task scope; no widening.
2. Stop on first blocker and report exact blocker with evidence.
3. Produce verification artifact exactly as specified.
4. If command/env prereq is missing, mark BLOCKED with concrete missing item.
EOF_PROMPT
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      init|list|next|start|done|fail|prompt|reset|status)
        CMD="$1"
        shift
        ;;
      --task)
        TASK_ID="${2:-}"
        shift 2
        ;;
      --note)
        NOTE="${2:-}"
        shift 2
        ;;
      --filter)
        FILTER="${2:-}"
        shift 2
        ;;
      --state-file)
        STATE_PATH="${2:-}"
        shift 2
        ;;
      --overlay)
        OVERLAY_PATH="${2:-}"
        shift 2
        ;;
      --dtp-gate)
        DTP_GATE_PATH="${2:-}"
        shift 2
        ;;
      --phb-gate)
        PHB_GATE_PATH="${2:-}"
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        die "unknown argument: $1"
        ;;
    esac
  done
}

main() {
  parse_args "$@"
  require_tools
  [[ -n "$CMD" ]] || { usage; exit 1; }

  case "$CMD" in
    init) init_state ;;
    list) cmd_list ;;
    next) cmd_next ;;
    start) cmd_start ;;
    done) cmd_done ;;
    fail) cmd_fail ;;
    reset) cmd_reset ;;
    prompt) cmd_prompt ;;
    status) cmd_status ;;
    *) die "unsupported command: $CMD" ;;
  esac
}

main "$@"


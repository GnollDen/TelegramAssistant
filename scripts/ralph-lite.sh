#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
TASKS_FILE="${REPO_ROOT}/tasks.json"
SLICES_FILE="${REPO_ROOT}/task_slices.json"
PROMPT_TEMPLATE_DEFAULT="${REPO_ROOT}/scripts/ralph-lite-prompt.md"
PROGRESS_FILE_DEFAULT="${REPO_ROOT}/logs/ralph-lite-progress.md"
STATE_FILE_DEFAULT="${REPO_ROOT}/logs/ralph-lite-current.json"
LOG_DIR_DEFAULT="${REPO_ROOT}/logs/ralph-lite"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

DRY_RUN=0
VERBOSE=0
QUIET=0
FORCE_TASK_ID=""
MAX_ITERATIONS=100
MAX_RETRIES=3
PROMPT_TEMPLATE="${PROMPT_TEMPLATE_DEFAULT}"
PROGRESS_FILE="${PROGRESS_FILE_DEFAULT}"
STATE_FILE="${STATE_FILE_DEFAULT}"
LOG_DIR="${LOG_DIR_DEFAULT}"
CODEX_BIN="codex"
MODEL=""

usage() {
  cat <<'USAGE'
Usage: scripts/ralph-lite.sh [options]

Options:
  --dry-run                  Select/render/log only; do not run codex
  --verbose                  Stream Codex transcript to console as well as log file
  --quiet                    Suppress non-error status lines
  --task-id <id>             Force specific parent task id (must be eligible)
  --max-iterations <n>       Max loop iterations in this invocation (default: 100)
  --max-retries <n>          Max retries per current unfinished unit (default: 3)
  --progress-file <path>     Append-only progress file path
  --state-file <path>        Current-unit state marker file path
  --log-dir <path>           Persistent directory for raw Codex logs and last messages
  --prompt-template <path>   Prompt template markdown path
  --codex-bin <path>         Codex binary (default: codex)
  --model <name>             Optional codex model override
  -h, --help                 Show help
USAGE
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --dry-run)
        DRY_RUN=1
        shift
        ;;
      --verbose)
        VERBOSE=1
        shift
        ;;
      --quiet)
        QUIET=1
        shift
        ;;
      --task-id)
        FORCE_TASK_ID="${2:-}"
        shift 2
        ;;
      --max-iterations)
        MAX_ITERATIONS="${2:-}"
        shift 2
        ;;
      --max-retries)
        MAX_RETRIES="${2:-}"
        shift 2
        ;;
      --progress-file)
        PROGRESS_FILE="${2:-}"
        shift 2
        ;;
      --state-file)
        STATE_FILE="${2:-}"
        shift 2
        ;;
      --log-dir)
        LOG_DIR="${2:-}"
        shift 2
        ;;
      --prompt-template)
        PROMPT_TEMPLATE="${2:-}"
        shift 2
        ;;
      --codex-bin)
        CODEX_BIN="${2:-}"
        shift 2
        ;;
      --model)
        MODEL="${2:-}"
        shift 2
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "Unknown argument: $1" >&2
        usage
        exit 1
        ;;
    esac
  done
}

require_tools() {
  command -v jq >/dev/null 2>&1 || { echo "jq not found" >&2; exit 1; }
  command -v git >/dev/null 2>&1 || { echo "git not found" >&2; exit 1; }
  if [[ "${DRY_RUN}" -eq 0 ]]; then
    command -v "${CODEX_BIN}" >/dev/null 2>&1 || {
      echo "codex binary not found: ${CODEX_BIN}" >&2
      exit 1
    }
  fi
}

ensure_paths() {
  [[ -f "${TASKS_FILE}" ]] || { echo "Missing ${TASKS_FILE}" >&2; exit 1; }
  [[ -f "${SLICES_FILE}" ]] || { echo "Missing ${SLICES_FILE}" >&2; exit 1; }
  [[ -f "${PROMPT_TEMPLATE}" ]] || { echo "Missing ${PROMPT_TEMPLATE}" >&2; exit 1; }
  mkdir -p "$(dirname "${PROGRESS_FILE}")"
  mkdir -p "$(dirname "${STATE_FILE}")"
  mkdir -p "${LOG_DIR}"
}

current_utc() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

relative_path() {
  local path="$1"
  if [[ "${path}" == "${REPO_ROOT}/"* ]]; then
    printf '%s\n' "${path#"${REPO_ROOT}/"}"
  else
    printf '%s\n' "${path}"
  fi
}

ui_info() {
  [[ "${QUIET}" -eq 1 ]] && return 0
  printf '%s\n' "$*"
}

ui_error() {
  printf '%s\n' "$*" >&2
}

ui_iteration_header() {
  local iteration_number="$1"
  local parent_id="$2"
  local slice_id="$3"
  local retry_count="$4"
  ui_info "[${iteration_number}/${MAX_ITERATIONS}] ${parent_id} / ${slice_id:-none}"
  ui_info "retry: ${retry_count}/${MAX_RETRIES}"
}

ui_iteration_details() {
  local progress_path="$1"
  local raw_log_path="$2"
  local last_msg_path="$3"
  ui_info "details: $(relative_path "${raw_log_path}")"
  ui_info "last_message: $(relative_path "${last_msg_path}")"
  ui_info "progress: $(relative_path "${progress_path}")"
}

ui_iteration_error_details() {
  local progress_path="$1"
  local raw_log_path="$2"
  local last_msg_path="$3"
  ui_error "details: $(relative_path "${raw_log_path}")"
  ui_error "last_message: $(relative_path "${last_msg_path}")"
  ui_error "progress: $(relative_path "${progress_path}")"
}

git_head() {
  git rev-parse HEAD
}

git_status_snapshot() {
  git status --porcelain=v1
}

git_is_dirty() {
  [[ -n "$(git_status_snapshot)" ]]
}

git_has_bad_state() {
  if [[ -n "$(git diff --name-only --diff-filter=U)" ]]; then
    return 0
  fi

  local git_dir
  git_dir="$(git rev-parse --git-dir)"
  [[ -f "${git_dir}/MERGE_HEAD" ]] && return 0
  [[ -f "${git_dir}/REBASE_HEAD" ]] && return 0
  [[ -d "${git_dir}/rebase-merge" ]] && return 0
  [[ -d "${git_dir}/rebase-apply" ]] && return 0
  [[ -f "${git_dir}/CHERRY_PICK_HEAD" ]] && return 0
  return 1
}

ensure_progress_header() {
  if [[ ! -f "${PROGRESS_FILE}" ]]; then
    cat > "${PROGRESS_FILE}" <<EOF_PROGRESS
# Ralph-lite Progress Log

Append-only execution memory for local bounded Codex loop runs.

EOF_PROGRESS
  fi
}

state_exists() {
  [[ -f "${STATE_FILE}" ]]
}

state_parent() {
  [[ -f "${STATE_FILE}" ]] || { echo ""; return; }
  jq -r '.current_parent_task_id // ""' "${STATE_FILE}"
}

state_slice() {
  [[ -f "${STATE_FILE}" ]] || { echo ""; return; }
  jq -r '.current_slice_id // ""' "${STATE_FILE}"
}

state_retry_count() {
  [[ -f "${STATE_FILE}" ]] || { echo "0"; return; }
  jq -r '.retry_count // 0' "${STATE_FILE}"
}

write_state() {
  local parent_id="$1"
  local slice_id="$2"
  local retry_count="$3"
  local last_outcome="$4"
  local last_iteration_id="$5"
  local note="$6"

  jq -n \
    --arg parent_id "${parent_id}" \
    --arg slice_id "${slice_id}" \
    --argjson retry_count "${retry_count}" \
    --arg last_outcome "${last_outcome}" \
    --arg last_iteration_id "${last_iteration_id}" \
    --arg note "${note}" \
    --arg updated_at "$(current_utc)" \
    --arg head "$(git_head)" \
    --argjson worktree_dirty "$(if git_is_dirty; then echo true; else echo false; fi)" \
    '{
      version: 1,
      current_parent_task_id: $parent_id,
      current_slice_id: (if $slice_id == "" then null else $slice_id end),
      retry_count: $retry_count,
      last_outcome: $last_outcome,
      last_iteration_id: $last_iteration_id,
      updated_at: $updated_at,
      head: $head,
      worktree_dirty: $worktree_dirty,
      note: $note
    }' > "${STATE_FILE}"
}

clear_state() {
  rm -f "${STATE_FILE}"
}

task_status() {
  local task_id="$1"
  jq -r --arg task_id "${task_id}" '
    (.tasks[] | select(.id == $task_id) | .status) // "missing"
  ' "${TASKS_FILE}"
}

task_is_eligible() {
  local task_id="$1"
  jq -e --arg task_id "${task_id}" '
    .tasks as $tasks
    | ($tasks | map({key:.id, value:.status}) | from_entries) as $status
    | ($tasks[] | select(.id == $task_id)) as $task
    | ($task.status == "pending")
    and ((($task.dependencies // []) | all(. as $d | (($status[$d] // "missing") == "done"))))
  ' "${TASKS_FILE}" >/dev/null
}

select_next_parent_task() {
  jq -r '
    .tasks as $tasks
    | ($tasks | map({key:.id, value:.status}) | from_entries) as $status
    | [ $tasks[]
        | select(.status == "pending")
        | select(((.dependencies // []) | all(. as $d | (($status[$d] // "missing") == "done"))))
      ]
    | if length == 0 then empty else .[0].id end
  ' "${TASKS_FILE}"
}

slice_status() {
  local slice_id="$1"
  jq -r --arg slice_id "${slice_id}" '
    (.slices[]? | select(.id == $slice_id) | .status) // "missing"
  ' "${SLICES_FILE}"
}

slice_belongs_to_parent() {
  local slice_id="$1"
  local parent_id="$2"
  jq -e --arg slice_id "${slice_id}" --arg parent_id "${parent_id}" '
    any(.slices[]?; .id == $slice_id and .parent_task_id == $parent_id)
  ' "${SLICES_FILE}" >/dev/null
}

slice_is_open() {
  local slice_id="$1"
  local status
  status="$(slice_status "${slice_id}")"
  [[ "${status}" != "done" && "${status}" != "blocked" && "${status}" != "missing" ]]
}

select_first_unfinished_ready_slice() {
  local parent_id="$1"
  jq -r --arg parent_id "${parent_id}" '
    .slices as $s
    | ($s | map({key:.id, value:.status}) | from_entries) as $status
    | [ $s[]
        | select(.parent_task_id == $parent_id)
        | select((.status != "done") and (.status != "blocked"))
        | select(((.dependencies // []) | all(. as $d | (($status[$d] // "missing") == "done"))))
      ]
    | if length == 0 then empty else .[0].id end
  ' "${SLICES_FILE}"
}

extract_parent_task_json() {
  local parent_id="$1"
  jq -c --arg parent_id "${parent_id}" '.tasks[] | select(.id == $parent_id)' "${TASKS_FILE}"
}

extract_parent_slices_json() {
  local parent_id="$1"
  jq -c --arg parent_id "${parent_id}" '[.slices[]? | select(.parent_task_id == $parent_id)]' "${SLICES_FILE}"
}

extract_prd_paths() {
  find "${REPO_ROOT}/docs/planning" -maxdepth 1 -type f -name '*.md' | sort
}

render_prompt() {
  local parent_id="$1"
  local slice_id="$2"
  local retry_count="$3"
  local prompt_out="$4"

  local parent_task_json slices_json prd_paths
  parent_task_json="$(extract_parent_task_json "${parent_id}")"
  slices_json="$(extract_parent_slices_json "${parent_id}")"
  prd_paths="$(extract_prd_paths)"

  sed \
    -e "s|{{REPO_ROOT}}|${REPO_ROOT}|g" \
    -e "s|{{TASK_ID}}|${parent_id}|g" \
    -e "s|{{SLICE_ID}}|${slice_id}|g" \
    -e "s|{{RETRY_COUNT}}|${retry_count}|g" \
    -e "s|{{TASKS_FILE}}|${TASKS_FILE}|g" \
    -e "s|{{SLICES_FILE}}|${SLICES_FILE}|g" \
    -e "s|{{PROGRESS_FILE}}|${PROGRESS_FILE}|g" \
    -e "s|{{STATE_FILE}}|${STATE_FILE}|g" \
    "${PROMPT_TEMPLATE}" > "${prompt_out}"

  {
    echo
    echo "## Parent Task JSON"
    echo '```json'
    echo "${parent_task_json}"
    echo '```'
    echo
    echo "## Parent Slices JSON"
    echo '```json'
    echo "${slices_json}"
    echo '```'
    echo
    echo "## PRD Planning Docs"
    while IFS= read -r p; do
      [[ -n "${p}" ]] && echo "- ${p}"
    done <<< "${prd_paths}"
    echo
    echo "## Current Task Backlog Snapshot"
    echo '```json'
    jq -c '.tasks' "${TASKS_FILE}"
    echo '```'
  } >> "${prompt_out}"
}

parse_result_marker() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || { echo ""; return; }
  awk '/^RESULT:/ {print substr($0,9); found=1; exit} END{if(!found) print ""}' "${message_file}"
}

parse_blocker_marker() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || { echo ""; return; }
  awk '/^BLOCKER:/ {print substr($0,10); found=1; exit} END{if(!found) print ""}' "${message_file}"
}

parse_progress_marker() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || { echo ""; return; }
  awk '/^PROGRESS:/ {print substr($0,11); found=1; exit} END{if(!found) print ""}' "${message_file}"
}

message_looks_interrupted() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || return 1
  grep -qi 'interrupted\|cancelled\|timeout' "${message_file}"
}

next_iteration_id() {
  printf '%s-%s-%s' "$(date -u +%Y%m%dT%H%M%SZ)" "$$" "$1"
}

append_progress_entry() {
  local iteration_id="$1"
  local target_parent="$2"
  local target_slice="$3"
  local started_at="$4"
  local ended_at="$5"
  local outcome="$6"
  local retry_count="$7"
  local codex_exit="$8"
  local head_before="$9"
  local head_after="${10}"
  local summary="${11}"
  local blocker="${12}"
  local progress_note="${13}"
  local task_status_before="${14}"
  local task_status_after="${15}"
  local git_before="${16}"
  local git_after="${17}"

  {
    echo "## ${started_at} | iteration_id=${iteration_id} | parent=${target_parent} | slice=${target_slice:-none} | outcome=${outcome}"
    echo "- iteration_id: ${iteration_id}"
    echo "- target_parent_task: ${target_parent}"
    echo "- target_slice: ${target_slice:-none}"
    echo "- started_at: ${started_at}"
    echo "- ended_at: ${ended_at}"
    echo "- outcome: ${outcome}"
    echo "- retry_count: ${retry_count}"
    echo "- codex_exit_code: ${codex_exit}"
    echo "- task_status_before: ${task_status_before}"
    echo "- task_status_after: ${task_status_after}"
    echo "- head_before: ${head_before}"
    echo "- head_after: ${head_after}"
    echo "- summary: ${summary:-n/a}"
    echo "- blocker: ${blocker:-none}"
    echo "- progress_note: ${progress_note:-none}"
    echo
    echo "### git_status_before"
    echo '```'
    if [[ -n "${git_before}" ]]; then
      echo "${git_before}"
    else
      echo "(clean)"
    fi
    echo '```'
    echo
    echo "### git_status_after"
    echo '```'
    if [[ -n "${git_after}" ]]; then
      echo "${git_after}"
    else
      echo "(clean)"
    fi
    echo '```'
    echo
  } >> "${PROGRESS_FILE}"
}

# Global outputs populated by resolve_current_unit.
CURRENT_PARENT=""
CURRENT_SLICE=""
CURRENT_RETRY=0

resolve_current_unit() {
  local forced_parent="${FORCE_TASK_ID}"

  if [[ -n "${forced_parent}" ]]; then
    if ! task_is_eligible "${forced_parent}"; then
      echo "Forced task is not eligible (pending + deps done): ${forced_parent}" >&2
      return 2
    fi
    CURRENT_PARENT="${forced_parent}"
    CURRENT_SLICE="$(select_first_unfinished_ready_slice "${CURRENT_PARENT}")"
    CURRENT_RETRY=0
    write_state "${CURRENT_PARENT}" "${CURRENT_SLICE}" "${CURRENT_RETRY}" "assigned" "forced" "forced parent selection"
    return 0
  fi

  if state_exists; then
    local persisted_parent persisted_slice persisted_retry persisted_status
    persisted_parent="$(state_parent)"
    persisted_slice="$(state_slice)"
    persisted_retry="$(state_retry_count)"

    if [[ -n "${persisted_parent}" ]]; then
      persisted_status="$(task_status "${persisted_parent}")"
      if [[ "${persisted_status}" == "done" ]]; then
        clear_state
      elif [[ "${persisted_status}" == "blocked" || "${persisted_status}" == "missing" ]]; then
        echo "Current state parent is blocked/missing: ${persisted_parent}" >&2
        return 2
      elif ! task_is_eligible "${persisted_parent}"; then
        echo "Current state parent is no longer eligible: ${persisted_parent}" >&2
        return 2
      else
        CURRENT_PARENT="${persisted_parent}"
        CURRENT_RETRY="${persisted_retry}"
        if [[ -n "${persisted_slice}" ]] \
          && slice_belongs_to_parent "${persisted_slice}" "${CURRENT_PARENT}" \
          && slice_is_open "${persisted_slice}"; then
          CURRENT_SLICE="${persisted_slice}"
        else
          CURRENT_SLICE="$(select_first_unfinished_ready_slice "${CURRENT_PARENT}")"
        fi
        write_state "${CURRENT_PARENT}" "${CURRENT_SLICE}" "${CURRENT_RETRY}" "resumed" "resume" "resume from state file"
        return 0
      fi
    else
      clear_state
    fi
  fi

  CURRENT_PARENT="$(select_next_parent_task)"
  if [[ -z "${CURRENT_PARENT}" ]]; then
    echo "No eligible pending parent task found. Stopping."
    return 1
  fi

  # Safety: if we are assigning a fresh unit and worktree is dirty, do not guess.
  if git_is_dirty; then
    echo "Dirty worktree with no current unit marker. Manual attention required before selecting new unit." >&2
    return 2
  fi

  CURRENT_SLICE="$(select_first_unfinished_ready_slice "${CURRENT_PARENT}")"
  CURRENT_RETRY=0
  write_state "${CURRENT_PARENT}" "${CURRENT_SLICE}" "${CURRENT_RETRY}" "assigned" "startup" "fresh unit assignment"
  return 0
}

run_iteration() {
  local iteration_number="$1"
  local parent_id="${CURRENT_PARENT}"
  local slice_id="${CURRENT_SLICE}"
  local retry_before="${CURRENT_RETRY}"

  local iteration_id started_at ended_at
  local head_before head_after git_before git_after
  local task_status_before task_status_after
  local prompt_file prompt_log_file last_msg_file raw_log_file
  local summary blocker progress_note
  local codex_exit outcome new_retry
  local task_done=0 head_changed=0 worktree_changed=0 explicit_progress=0

  iteration_id="$(next_iteration_id "${iteration_number}")"
  started_at="$(current_utc)"
  head_before="$(git_head)"
  git_before="$(git_status_snapshot)"
  task_status_before="$(task_status "${parent_id}")"

  if [[ "${retry_before}" -ge "${MAX_RETRIES}" ]]; then
    ui_error "status: failed"
    ui_error "reason: retry threshold reached for ${parent_id} (${retry_before}/${MAX_RETRIES})"
    ui_error "progress: $(relative_path "${PROGRESS_FILE}")"
    append_progress_entry \
      "${iteration_id}" "${parent_id}" "${slice_id}" "${started_at}" "${started_at}" \
      "failed" "${retry_before}" "999" "${head_before}" "${head_before}" \
      "Retry threshold already reached; manual attention required." "" "" \
      "${task_status_before}" "${task_status_before}" "${git_before}" "${git_before}"
    return 2
  fi

  prompt_file="${TMP_DIR}/prompt-${iteration_id}.md"
  prompt_log_file="${LOG_DIR}/${iteration_id}-prompt.md"
  last_msg_file="${LOG_DIR}/${iteration_id}-last-message.txt"
  raw_log_file="${LOG_DIR}/${iteration_id}-codex.log"
  render_prompt "${parent_id}" "${slice_id}" "${retry_before}" "${prompt_file}"
  cp "${prompt_file}" "${prompt_log_file}"

  ui_iteration_header "${iteration_number}" "${parent_id}" "${slice_id}" "${retry_before}"

  if [[ "${DRY_RUN}" -eq 1 ]]; then
    printf 'dry-run: codex not invoked\n' > "${raw_log_file}"
    printf 'RESULT: dry_run\nPROGRESS: dry_run\n' > "${last_msg_file}"
    ended_at="$(current_utc)"
    append_progress_entry \
      "${iteration_id}" "${parent_id}" "${slice_id}" "${started_at}" "${ended_at}" \
      "success" "${retry_before}" "0" "${head_before}" "${head_before}" \
      "Dry run only; codex was not invoked." "" "dry_run" \
      "${task_status_before}" "${task_status_before}" "${git_before}" "${git_before}"
    ui_info "status: dry-run"
    ui_iteration_details "${PROGRESS_FILE}" "${raw_log_file}" "${last_msg_file}"
    return 0
  fi

  local -a cmd
  # Ubuntu dev environments in this repo cannot rely on workspace-write bwrap,
  # and current codex exec no longer supports the legacy -a flag.
  # Keep web search enabled via the top-level codex flag.
  cmd=(
    "${CODEX_BIN}" "--search" "exec"
    "-C" "${REPO_ROOT}"
    "--dangerously-bypass-approvals-and-sandbox"
    "-o" "${last_msg_file}"
  )
  if [[ -n "${MODEL}" ]]; then
    cmd+=("-m" "${MODEL}")
  fi
  cmd+=("-")

  ui_info "status: running"
  set +e
  if [[ "${VERBOSE}" -eq 1 ]]; then
    "${cmd[@]}" < "${prompt_file}" 2>&1 | tee "${raw_log_file}"
    codex_exit=${PIPESTATUS[0]}
  else
    "${cmd[@]}" < "${prompt_file}" > "${raw_log_file}" 2>&1
    codex_exit=$?
  fi
  set -e

  ended_at="$(current_utc)"
  head_after="$(git_head)"
  git_after="$(git_status_snapshot)"
  task_status_after="$(task_status "${parent_id}")"
  summary="$(parse_result_marker "${last_msg_file}")"
  blocker="$(parse_blocker_marker "${last_msg_file}")"
  progress_note="$(parse_progress_marker "${last_msg_file}")"

  [[ "${task_status_after}" == "done" ]] && task_done=1
  [[ "${head_before}" != "${head_after}" ]] && head_changed=1
  [[ "${git_before}" != "${git_after}" ]] && worktree_changed=1
  [[ -n "${progress_note}" ]] && explicit_progress=1

  outcome="success"
  if git_has_bad_state; then
    outcome="failed"
    summary="${summary:-Bad git state detected after codex run}"
  elif [[ "${codex_exit}" -ne 0 ]]; then
    outcome="interrupted"
  elif message_looks_interrupted "${last_msg_file}"; then
    outcome="interrupted"
  elif [[ -n "${blocker}" ]]; then
    outcome="blocked"
  elif [[ -z "${summary}" ]]; then
    outcome="failed"
  elif [[ "${task_done}" -eq 0 && "${head_changed}" -eq 0 && "${worktree_changed}" -eq 0 && "${explicit_progress}" -eq 0 ]]; then
    outcome="no_progress"
  fi

  new_retry="${retry_before}"
  case "${outcome}" in
    success)
      new_retry=0
      ;;
    blocked)
      ;;
    interrupted|failed|no_progress)
      new_retry=$((retry_before + 1))
      ;;
  esac

  append_progress_entry \
    "${iteration_id}" "${parent_id}" "${slice_id}" "${started_at}" "${ended_at}" \
    "${outcome}" "${new_retry}" "${codex_exit}" "${head_before}" "${head_after}" \
    "${summary}" "${blocker}" "${progress_note}" \
    "${task_status_before}" "${task_status_after}" "${git_before}" "${git_after}"

  if [[ "${outcome}" == "success" ]]; then
    if [[ "${task_status_after}" == "done" ]]; then
      clear_state
    else
      local next_slice
      if [[ -n "${slice_id}" ]] && slice_belongs_to_parent "${slice_id}" "${parent_id}" && slice_is_open "${slice_id}"; then
        next_slice="${slice_id}"
      else
        next_slice="$(select_first_unfinished_ready_slice "${parent_id}")"
      fi
      write_state "${parent_id}" "${next_slice}" "0" "success" "${iteration_id}" "parent still open after successful bounded run"
    fi
    ui_info "status: success"
    if [[ "${head_before}" != "${head_after}" ]]; then
      ui_info "commit: ${head_after:0:7}"
    fi
    ui_iteration_details "${PROGRESS_FILE}" "${raw_log_file}" "${last_msg_file}"
    return 0
  fi

  if [[ "${outcome}" == "blocked" ]]; then
    write_state "${parent_id}" "${slice_id}" "${new_retry}" "blocked" "${iteration_id}" "blocker returned by codex"
    ui_error "status: blocked"
    [[ -n "${blocker}" ]] && ui_error "reason: ${blocker}"
    ui_iteration_error_details "${PROGRESS_FILE}" "${raw_log_file}" "${last_msg_file}"
    return 2
  fi

  # interrupted / failed / no_progress
  write_state "${parent_id}" "${slice_id}" "${new_retry}" "${outcome}" "${iteration_id}" "retry same unfinished unit"

  if [[ "${new_retry}" -ge "${MAX_RETRIES}" ]]; then
    ui_error "status: ${outcome}"
    if [[ -n "${summary}" ]]; then
      ui_error "reason: ${summary}"
    else
      ui_error "reason: codex_exit=${codex_exit}"
    fi
    ui_error "retry: ${new_retry}/${MAX_RETRIES} (manual attention required)"
    ui_iteration_error_details "${PROGRESS_FILE}" "${raw_log_file}" "${last_msg_file}"
    return 2
  fi

  ui_error "status: ${outcome}"
  if [[ -n "${summary}" ]]; then
    ui_error "reason: ${summary}"
  else
    ui_error "reason: codex_exit=${codex_exit}"
  fi
  ui_error "retry: ${new_retry}/${MAX_RETRIES}"
  ui_iteration_error_details "${PROGRESS_FILE}" "${raw_log_file}" "${last_msg_file}"
  return 2
}

main() {
  parse_args "$@"
  require_tools
  ensure_paths
  ensure_progress_header

  cd "${REPO_ROOT}"

  if git_has_bad_state; then
    echo "Repository has unresolved/bad git state. Resolve before loop run." >&2
    exit 2
  fi

  local i=1
  while [[ "${i}" -le "${MAX_ITERATIONS}" ]]; do
    if ! resolve_current_unit; then
      case $? in
        1)
          ui_info "status: idle"
          exit 0
          ;;
        *) exit 2 ;;
      esac
    fi

    if ! run_iteration "${i}"; then
      exit 2
    fi

    i=$((i + 1))
  done

  ui_info "status: stopped"
  ui_info "reason: reached max iterations (${MAX_ITERATIONS})"
}

main "$@"

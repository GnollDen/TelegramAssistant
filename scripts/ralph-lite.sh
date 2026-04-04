#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
TASKS_FILE="${REPO_ROOT}/tasks.json"
SLICES_FILE="${REPO_ROOT}/task_slices.json"
PROMPT_TEMPLATE_DEFAULT="${REPO_ROOT}/scripts/ralph-lite-prompt.md"
PROGRESS_FILE_DEFAULT="${REPO_ROOT}/logs/ralph-lite-progress.md"
TMP_DIR="$(mktemp -d)"
trap 'rm -rf "${TMP_DIR}"' EXIT

DRY_RUN=0
FORCE_TASK_ID=""
MAX_ITERATIONS=100
PROMPT_TEMPLATE="${PROMPT_TEMPLATE_DEFAULT}"
PROGRESS_FILE="${PROGRESS_FILE_DEFAULT}"
CODEX_BIN="codex"
MODEL=""

usage() {
  cat <<'EOF'
Usage: scripts/ralph-lite.sh [options]

Options:
  --dry-run                  Select/render only, do not run codex
  --task-id <id>             Force specific parent task id
  --max-iterations <n>       Max parent-task iterations (default: 100)
  --progress-file <path>     Append-only progress file path
  --prompt-template <path>   Prompt template markdown path
  --codex-bin <path>         Codex binary (default: codex)
  --model <name>             Optional codex model override
  -h, --help                 Show help
EOF
}

require_tools() {
  command -v jq >/dev/null 2>&1 || { echo "jq not found" >&2; exit 1; }
  command -v git >/dev/null 2>&1 || { echo "git not found" >&2; exit 1; }
  command -v "${CODEX_BIN}" >/dev/null 2>&1 || {
    echo "codex binary not found: ${CODEX_BIN}" >&2
    exit 1
  }
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --dry-run)
        DRY_RUN=1
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
      --progress-file)
        PROGRESS_FILE="${2:-}"
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

ensure_paths() {
  [[ -f "${TASKS_FILE}" ]] || { echo "Missing ${TASKS_FILE}" >&2; exit 1; }
  [[ -f "${SLICES_FILE}" ]] || { echo "Missing ${SLICES_FILE}" >&2; exit 1; }
  [[ -f "${PROMPT_TEMPLATE}" ]] || { echo "Missing ${PROMPT_TEMPLATE}" >&2; exit 1; }
  mkdir -p "$(dirname "${PROGRESS_FILE}")"
}

ensure_progress_header() {
  if [[ ! -f "${PROGRESS_FILE}" ]]; then
    cat > "${PROGRESS_FILE}" <<EOF
# Ralph-lite Progress Log

Append-only execution memory for local bounded Codex loop runs.

EOF
  fi
}

current_utc() {
  date -u +"%Y-%m-%dT%H:%M:%SZ"
}

git_head() {
  git rev-parse HEAD
}

git_status_snapshot() {
  git status --porcelain=v1
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

extract_parent_task_json() {
  local task_id="$1"
  jq -c --arg task_id "${task_id}" '.tasks[] | select(.id == $task_id)' "${TASKS_FILE}"
}

extract_parent_slices_json() {
  local task_id="$1"
  jq -c --arg task_id "${task_id}" '
    [.slices[]? | select(.parent_task_id == $task_id)]
  ' "${SLICES_FILE}"
}

extract_prd_paths() {
  find "${REPO_ROOT}/docs/planning" -maxdepth 1 -type f -name '*.md' | sort
}

render_prompt() {
  local task_id="$1"
  local prompt_out="$2"
  local parent_task_json slices_json prd_paths
  parent_task_json="$(extract_parent_task_json "${task_id}")"
  slices_json="$(extract_parent_slices_json "${task_id}")"
  prd_paths="$(extract_prd_paths)"

  sed \
    -e "s|{{REPO_ROOT}}|${REPO_ROOT}|g" \
    -e "s|{{TASK_ID}}|${task_id}|g" \
    -e "s|{{TASKS_FILE}}|${TASKS_FILE}|g" \
    -e "s|{{SLICES_FILE}}|${SLICES_FILE}|g" \
    -e "s|{{PROGRESS_FILE}}|${PROGRESS_FILE}|g" \
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

append_progress_entry() {
  local started_at="$1"
  local ended_at="$2"
  local iteration="$3"
  local task_id="$4"
  local status="$5"
  local codex_exit="$6"
  local head_before="$7"
  local head_after="$8"
  local task_status_before="$9"
  local task_status_after="${10}"
  local summary="${11}"
  local blocker="${12}"
  local progress_signal="${13}"
  local git_before="${14}"
  local git_after="${15}"

  {
    echo "## ${started_at} | iteration=${iteration} | task=${task_id} | status=${status}"
    echo "- started_at: ${started_at}"
    echo "- ended_at: ${ended_at}"
    echo "- codex_exit_code: ${codex_exit}"
    echo "- task_status_before: ${task_status_before}"
    echo "- task_status_after: ${task_status_after}"
    echo "- progress_signal: ${progress_signal}"
    echo "- head_before: ${head_before}"
    echo "- head_after: ${head_after}"
    echo "- summary: ${summary:-n/a}"
    echo "- blocker: ${blocker:-none}"
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

task_status() {
  local task_id="$1"
  jq -r --arg task_id "${task_id}" '.tasks[] | select(.id == $task_id) | .status' "${TASKS_FILE}"
}

parse_summary() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || { echo ""; return; }
  awk '
    /^RESULT:/ {print substr($0,9); found=1; exit}
    END {if (!found) print ""}
  ' "${message_file}"
}

parse_blocker() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || { echo ""; return; }
  awk '
    /^BLOCKER:/ {print substr($0,10); found=1; exit}
    END {if (!found) print ""}
  ' "${message_file}"
}

has_explicit_progress_marker() {
  local message_file="$1"
  [[ -f "${message_file}" ]] || return 1
  grep -qE '^PROGRESS:' "${message_file}"
}

run_codex_iteration() {
  local task_id="$1"
  local iteration="$2"

  local started_at ended_at head_before head_after git_before git_after
  local task_status_before task_status_after prompt_file last_msg_file
  local codex_exit summary blocker progress_signal status
  local worktree_changed=0
  local head_changed=0
  local task_done=0
  local explicit_progress=0

  started_at="$(current_utc)"
  task_status_before="$(task_status "${task_id}")"
  head_before="$(git_head)"
  git_before="$(git_status_snapshot)"

  prompt_file="${TMP_DIR}/prompt-${task_id}.md"
  last_msg_file="${TMP_DIR}/last-message-${task_id}.txt"
  render_prompt "${task_id}" "${prompt_file}"

  if [[ "${DRY_RUN}" -eq 1 ]]; then
    ended_at="$(current_utc)"
    append_progress_entry \
      "${started_at}" "${ended_at}" "${iteration}" "${task_id}" "dry_run" "0" \
      "${head_before}" "${head_before}" "${task_status_before}" "${task_status_before}" \
      "Dry run only; codex was not invoked." "" "dry_run" "${git_before}" "${git_before}"
    echo "Dry run: rendered prompt for ${task_id} at ${prompt_file}"
    return 0
  fi

  local -a cmd
  cmd=("${CODEX_BIN}" "exec" "-C" "${REPO_ROOT}" "-a" "never" "-s" "workspace-write" "-o" "${last_msg_file}")
  if [[ -n "${MODEL}" ]]; then
    cmd+=("-m" "${MODEL}")
  fi
  cmd+=("-")

  set +e
  "${cmd[@]}" < "${prompt_file}"
  codex_exit=$?
  set -e

  ended_at="$(current_utc)"
  head_after="$(git_head)"
  git_after="$(git_status_snapshot)"
  task_status_after="$(task_status "${task_id}")"
  summary="$(parse_summary "${last_msg_file}")"
  blocker="$(parse_blocker "${last_msg_file}")"

  [[ "${head_before}" != "${head_after}" ]] && head_changed=1
  [[ "${git_before}" != "${git_after}" ]] && worktree_changed=1
  [[ "${task_status_after}" == "done" ]] && task_done=1
  has_explicit_progress_marker "${last_msg_file}" && explicit_progress=1

  progress_signal="none"
  if [[ "${task_done}" -eq 1 ]]; then
    progress_signal="task_done"
  elif [[ "${head_changed}" -eq 1 ]]; then
    progress_signal="head_changed"
  elif [[ "${worktree_changed}" -eq 1 ]]; then
    progress_signal="worktree_changed"
  elif [[ "${explicit_progress}" -eq 1 ]]; then
    progress_signal="explicit_progress"
  fi

  status="ok"
  if [[ "${codex_exit}" -ne 0 ]]; then
    status="codex_error"
  elif [[ -n "${blocker}" ]]; then
    status="blocked"
  elif git_has_bad_state; then
    status="bad_git_state"
  elif [[ "${progress_signal}" == "none" ]]; then
    status="no_progress"
  fi

  append_progress_entry \
    "${started_at}" "${ended_at}" "${iteration}" "${task_id}" "${status}" "${codex_exit}" \
    "${head_before}" "${head_after}" "${task_status_before}" "${task_status_after}" \
    "${summary}" "${blocker}" "${progress_signal}" "${git_before}" "${git_after}"

  echo "Iteration ${iteration} task=${task_id} status=${status} codex_exit=${codex_exit}"

  case "${status}" in
    ok)
      return 0
      ;;
    *)
      return 2
      ;;
  esac
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

  local i=1 task_id
  while [[ "${i}" -le "${MAX_ITERATIONS}" ]]; do
    if [[ -n "${FORCE_TASK_ID}" ]]; then
      task_id="${FORCE_TASK_ID}"
      FORCE_TASK_ID=""
      if ! task_is_eligible "${task_id}"; then
        echo "Forced task is not eligible (must be pending with satisfied deps): ${task_id}" >&2
        exit 2
      fi
    else
      task_id="$(select_next_parent_task)"
      if [[ -z "${task_id}" ]]; then
        echo "No eligible pending parent task found. Stopping."
        exit 0
      fi
    fi

    if ! run_codex_iteration "${task_id}" "${i}"; then
      echo "Stopping loop on task ${task_id} due to stop condition."
      exit 2
    fi

    i=$((i + 1))
  done

  echo "Reached max iterations (${MAX_ITERATIONS}). Stopping."
}

main "$@"

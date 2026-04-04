# Ralph-lite Codex Loop (Ubuntu)

## Purpose
Local script-driven execution loop for bounded Codex runs:
- one `codex exec` run per parent task
- restart-friendly via repo/backlog/progress state
- no daemon, no queue worker, no endless live session dependency

## Files
- Script: `scripts/ralph-lite.sh`
- Prompt template: `scripts/ralph-lite-prompt.md`
- Progress log: `logs/ralph-lite-progress.md` (append-only)

## Prerequisites
- Ubuntu + `bash`
- `git`, `jq`, `codex` available in `PATH`
- run from repo root (or any subdir inside the same git repo)

## Task Selection Model
`scripts/ralph-lite.sh` reads `tasks.json` and selects the first parent task where:
- `status == "pending"`
- every dependency status is `done`

It skips:
- `done`
- `blocked`
- pending tasks with unmet/missing dependencies

If `--task-id <id>` is used, the same eligibility checks are enforced.

## Slice Handling
For the selected parent task, the loop pulls all related slices from `task_slices.json` where:
- `parent_task_id == <selected_task_id>`

Slices are included in the generated prompt context for one bounded parent-task run.

## Invocation Model
Command used per iteration:

```bash
codex exec -C <repo_root> -a never -s workspace-write -o <tmp_last_message_file> -
```

Optional:
- `--model <name>` adds `-m <name>`

Prompt is rendered from `scripts/ralph-lite-prompt.md` into a temp file and fed to stdin.

## Stop Conditions
Loop stops immediately when any of the following occurs:
- codex exit code is non-zero
- final message contains `BLOCKER: ...`
- git enters bad state (conflicts/rebase/merge/cherry-pick in progress)
- no progress signal detected

`no progress` means all are false:
- parent task moved to `done`
- git `HEAD` changed
- git worktree snapshot changed
- final message contains `PROGRESS: ...`

If no eligible parent tasks exist, loop exits cleanly.

## Resume Behavior
Restart is file/state-based:
- task selection re-read from `tasks.json`
- slices re-read from `task_slices.json`
- git state re-read from repository
- prior execution memory is in append-only `logs/ralph-lite-progress.md`

No in-memory session id is required for continuation.

## Progress File Contract
Each iteration appends one markdown block with:
- timestamps: `started_at`, `ended_at`
- `iteration`, `task`, `status`, `codex_exit_code`
- task status before/after
- progress signal type
- git head before/after
- result summary and blocker text (if any)
- git status snapshots before/after

Status values used by loop:
- `ok`
- `dry_run`
- `blocked`
- `codex_error`
- `bad_git_state`
- `no_progress`

## Usage
Dry run (selection + prompt render + progress append, no codex call):

```bash
bash scripts/ralph-lite.sh --dry-run --max-iterations 1
```

Run loop normally:

```bash
bash scripts/ralph-lite.sh
```

Run one specific eligible parent task:

```bash
bash scripts/ralph-lite.sh --task-id GATEWAY-008 --max-iterations 1
```

Custom progress file:

```bash
bash scripts/ralph-lite.sh --progress-file logs/ralph-lite-progress-dev.md
```

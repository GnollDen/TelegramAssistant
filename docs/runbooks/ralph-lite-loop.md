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
- Current-unit state marker: `logs/ralph-lite-current.json`
- Git recovery policy: `docs/runbooks/ralph-lite-git-recovery.md`

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

## Current Unit Model
- Current unit is persisted in `logs/ralph-lite-current.json`.
- Parent-first rule: once a parent is assigned, that parent stays current until backlog marks it `done`.
- Slice hint rule: script keeps/chooses one unfinished ready slice inside that parent:
  - reuse persisted slice if still open
  - otherwise choose first unfinished ready slice for that parent
- On restart, loop resolves current unit from state + backlog + git status; no live-session resume is used.

## Slice Handling
For the selected parent task, the loop pulls all related slices from `task_slices.json` where:
- `parent_task_id == <selected_task_id>`

Slices are included in the generated prompt context for one bounded parent-task run.  
Current slice is tracked as a recovery hint to avoid jumping across unfinished units after interruptions.

## Invocation Model
Command used per iteration:

```bash
codex --search exec -C <repo_root> --dangerously-bypass-approvals-and-sandbox -o <tmp_last_message_file> -
```

Optional:
- `--model <name>` adds `-m <name>`
- `--max-retries <n>` caps retries for same unfinished unit (default `3`)
- `--state-file <path>` overrides current-unit marker path

Prompt is rendered from `scripts/ralph-lite-prompt.md` into a temp file and fed to stdin.

Ubuntu dev environment notes:
- Ralph-lite stays on fresh bounded `codex exec`; it does not switch to `codex resume`.
- Legacy `-a never` is not used because this CLI profile does not support `-a`.
- `workspace-write` sandbox is not used in this environment because it routes through `bwrap` and fails with `Operation not permitted` / `Failed RTM_NEWADDR: Operation not permitted`.
- `--dangerously-bypass-approvals-and-sandbox` is used intentionally as the environment-compatible `exec` profile for this repo-local loop.
- `--search` is passed as a top-level `codex` flag (before `exec`) to match this CLI profile in Ubuntu.

Compared with a standard desktop/full-sandbox path:
- this loop does not rely on Codex-managed sandbox isolation
- filesystem and approval safety come from the bounded one-run-per-parent model, repo-local state marker, and operator-controlled dev environment
- recovery remains file/state-based even though sandboxing is bypassed for compatibility

## Stop Conditions
Loop stops immediately when any of the following occurs:
- git is in bad state (conflicts/rebase/merge/cherry-pick)
- codex run outcome is `blocked`
- codex run outcome is `interrupted`
- codex run outcome is `failed`
- codex run outcome is `no_progress`
- retry threshold for current unit is reached

`no_progress` means all are false:
- parent task moved to `done`
- git `HEAD` changed
- git worktree snapshot changed
- final message contains `PROGRESS: ...`

`failed` includes missing completion marker (`RESULT:`) or invalid post-run state.
`interrupted` includes non-zero `codex exec` exit or interrupted wording in captured output.

If no eligible parent tasks exist, loop exits cleanly.

## Resume Behavior
Restart is file/state-based:
- task selection re-read from `tasks.json`
- slices re-read from `task_slices.json`
- git state re-read from repository
- current unit + retry count re-read from `logs/ralph-lite-current.json`
- prior execution evidence in append-only `logs/ralph-lite-progress.md`

No in-memory session id is required for continuation.

Startup algorithm:
1. Fail fast if git has bad state.
2. If current-unit marker exists and parent is still pending/eligible, reuse same parent (and open slice hint).
3. If marker parent is `done`, clear marker and proceed to normal selection.
4. If marker parent is blocked/missing/not eligible, stop for manual attention.
5. If no marker exists:
   - if worktree is dirty: stop (safe recovery cannot assign a new unit confidently)
   - else assign first eligible pending parent and persist marker.

## Progress File Contract
Each iteration appends one markdown block with:
- `iteration_id`
- target parent + target slice
- timestamps: `started_at`, `ended_at`
- `outcome` (`success|failed|interrupted|blocked|no_progress`)
- `retry_count`
- `codex_exit_code`
- task status before/after
- git head before/after
- summary, blocker, progress_note
- git status snapshots before/after

State marker contract (`logs/ralph-lite-current.json`):
- `current_parent_task_id`
- `current_slice_id` (optional)
- `retry_count`
- `last_outcome`
- `last_iteration_id`
- `updated_at`
- `head`
- `worktree_dirty`

## Usage
Dry run (selection + prompt render + progress append, no codex call):

```bash
bash scripts/ralph-lite.sh --dry-run --max-iterations 1
```

Dry run with explicit retry threshold and state file:

```bash
bash scripts/ralph-lite.sh --dry-run --max-retries 3 --state-file logs/ralph-lite-current.json --max-iterations 1
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

Retry behavior:
- On `interrupted|failed|no_progress`, loop records failed iteration, increments retry counter in state file, and stops.
- Next start retries the same current unit from state file.
- When `retry_count >= max_retries`, loop stops immediately with manual-attention message.

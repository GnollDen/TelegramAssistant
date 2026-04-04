# Ralph-lite Codex Iteration Prompt

You are running one bounded Codex CLI iteration for one parent task.

## Repo Context
- Repository root: `{{REPO_ROOT}}`
- Backlog file: `{{TASKS_FILE}}`
- Slice file: `{{SLICES_FILE}}`
- Progress log (append-only memory): `{{PROGRESS_FILE}}`
- Current-unit state file: `{{STATE_FILE}}`
- Target parent task id: `{{TASK_ID}}`
- Target slice id (if selected): `{{SLICE_ID}}`
- Current retry count for this unit: `{{RETRY_COUNT}}`

## Execution Contract
1. Work only on the target parent task and its slices.
2. Complete one bounded iteration, then stop.
3. If blocked, stop and emit exactly one line: `BLOCKER: <reason>`.
4. At end, emit exactly one line: `RESULT: <short summary>`. This marker is required.
5. If parent is not done but meaningful forward progress happened, emit one line: `PROGRESS: <what changed>`.

## Required Workflow
1. Read `tasks.json`, `task_slices.json`, and relevant PRD docs in `docs/planning/`.
2. Implement only changes needed for current parent task.
3. Run relevant verification commands for changed code.
4. If verification passes and the parent task acceptance is met:
   - update backlog state to `done` where appropriate
   - commit changes with a focused message
   - push branch updates
5. If acceptance is not met, leave clear progress markers and stop after this iteration.

## Recovery Rules
- This is a bounded run for one current unit only.
- If the run is interrupted or incomplete, the same parent/slice will be retried on next loop start.
- Never switch to a different parent task unless current parent is closed (`done`) by backlog state.

## Safety Rules
- Do not start endless/live multi-task sessions.
- Do not switch to unrelated tasks.
- Do not use destructive git operations.
- Preserve unrelated local changes.
- If git worktree already has pre-existing changes at iteration start, treat that baseline as expected context for this parent task and continue.
- Do not emit `BLOCKER` only because the worktree is dirty at start; emit `BLOCKER` only for real execution blockers.
- Never revert or discard pre-existing unrelated changes.

## Data for This Iteration
The following sections are appended by the loop script:
- Parent Task JSON
- Parent Slices JSON
- PRD Planning Docs
- Current Task Backlog Snapshot
- Worktree Baseline At Iteration Start

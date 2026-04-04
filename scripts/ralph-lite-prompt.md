# Ralph-lite Codex Iteration Prompt

You are running one bounded Codex CLI iteration for one parent task.

## Repo Context
- Repository root: `{{REPO_ROOT}}`
- Backlog file: `{{TASKS_FILE}}`
- Slice file: `{{SLICES_FILE}}`
- Progress log (append-only memory): `{{PROGRESS_FILE}}`
- Target parent task id: `{{TASK_ID}}`

## Execution Contract
1. Work only on the target parent task and its slices.
2. Complete one bounded iteration, then stop.
3. If blocked, stop and emit exactly one line: `BLOCKER: <reason>`.
4. At end, emit exactly one line: `RESULT: <short summary>`.
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

## Safety Rules
- Do not start endless/live multi-task sessions.
- Do not switch to unrelated tasks.
- Do not use destructive git operations.
- Preserve unrelated local changes.

## Data for This Iteration
The following sections are appended by the loop script:
- Parent Task JSON
- Parent Slices JSON
- PRD Planning Docs
- Current Task Backlog Snapshot

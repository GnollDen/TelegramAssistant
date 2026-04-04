# Ralph-lite Git Recovery Policy

## Safe Continue
- Continue is allowed when git is dirty but not in conflict/rebase/merge/cherry-pick state.
- Dirty worktree is preserved; loop never resets/reverts by default.
- Dirty worktree is only allowed for retries of an already assigned current unit.

## Mandatory Stop
- Stop if any unmerged/conflict state exists.
- Stop if merge/rebase/cherry-pick metadata is present.
- Stop if no current-unit marker exists and worktree is dirty (cannot safely assign a new unit).

## Commands Used by Loop
- Bad state detection:
  - `git diff --name-only --diff-filter=U`
  - `git rev-parse --git-dir` + checks for:
    - `MERGE_HEAD`
    - `REBASE_HEAD`
    - `rebase-merge/`
    - `rebase-apply/`
    - `CHERRY_PICK_HEAD`
- Dirty snapshot:
  - `git status --porcelain=v1`
- Commit pointer:
  - `git rev-parse HEAD`

## Recovery Intent
- Preserve partial work for current unit.
- Retry same unfinished unit deterministically.
- Require manual attention once retry threshold is reached or git enters bad state.

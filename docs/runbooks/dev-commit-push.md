# Dev Commit Push Runbook

Use this after a meaningful implementation pass.

## Goal

End code work in a clean state:

1. verified
2. committed
3. pushed

## Standard Flow

1. Check repo state.
   - `git status --short --branch`
   - `git branch --show-current`
2. Verify the changed scope.
   - C#: `dotnet build TelegramAssistant.sln`
   - MCP TypeScript: `cd src/TgAssistant.Mcp && npm run build`
   - runtime/startup changes: add targeted `--liveness-check`, `--readiness-check`, and `--runtime-wiring-check` (`--healthcheck` remains readiness alias for Sprint 1)
3. Stage only the intended batch.
   - do not include unrelated files
   - if the tree is mixed, split the batch first
4. Create one clean commit.
   - message in English, imperative mood
5. Push immediately.
   - preferred safe command: `scripts/safe_push.sh origin <branch>`
   - direct fallback: `git push origin <branch>`

## Workflow Push Guard

`scripts/safe_push.sh` blocks pushes that include `.github/workflows/*` by default.
This prevents common PAT failures when token scope does not include `workflow`.

If workflow push is intentional, use:
- `ALLOW_WORKFLOW_PUSH=1 scripts/safe_push.sh origin <branch>`

Safe preview without actual push:
- `DRY_RUN=1 scripts/safe_push.sh origin <branch>`

## Completion Rule

For TelegramAssistant, "done" after code changes normally means:

- local verification done
- commit created
- push completed

If push is blocked by auth/network/policy, report it explicitly and treat the task as partially complete.

## Do Not

- do not leave long-lived local-only commits by default
- do not push a mixed batch just to get something remote
- do not amend or rebase without explicit instruction

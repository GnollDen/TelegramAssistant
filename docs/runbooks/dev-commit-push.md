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
   - runtime/startup changes: add targeted `--healthcheck` or `--runtime-wiring-check`
3. Stage only the intended batch.
   - do not include unrelated files
   - if the tree is mixed, split the batch first
4. Create one clean commit.
   - message in English, imperative mood
5. Push immediately.
   - `git push origin <branch>`

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

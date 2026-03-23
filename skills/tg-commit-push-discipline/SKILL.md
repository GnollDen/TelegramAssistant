---
name: tg-commit-push-discipline
description: Use for TelegramAssistant when a coding pass is effectively complete and repository changes must be finalized safely. Enforce verify, commit, and push discipline after code changes so work does not remain as drifting local-only branch state.
---

# TG Commit Push Discipline

Use this skill when code, config, compose, migrations, or operational docs were changed and the task is ready to be finalized.

## Read Order

Read these first:

1. `AGENTS.md`
2. `docs/DOCS_INDEX.md`
3. current sprint task pack and acceptance doc, if the change belongs to a sprint
4. `docs/runbooks/dev-commit-push.md`
5. `docs/runbooks/post-change-verification.md`

If production verification matters for the task, also read:

6. `docs/runbooks/production-stability-check.md`

## Purpose

This skill exists to prevent:

- long-lived local-only commits
- dirty branches drifting away from remote
- partially verified code being left in working trees
- mixed batches being pushed accidentally

## Non-Negotiable Rule

After a meaningful code change, do not stop at "files edited".

Default completion path is:

1. verify
2. commit
3. push

If push cannot be completed:

- say so explicitly
- report the exact blocker
- do not present the work as fully finished

## Workflow

### 1. Inspect git state

Check:

- current branch
- `git status --short --branch`
- whether unrelated changes exist

Do not absorb unrelated work into the commit.

### 2. Verify the changed scope

Use the minimum required verification for the actual change:

- any C# change: `dotnet build TelegramAssistant.sln`
- any TypeScript change in `src/TgAssistant.Mcp/`: `npm run build`
- startup/runtime/compose changes: add targeted startup or runtime checks
- production-impacting changes: use the production stability runbook when appropriate

If verification is partial, record exactly what was and was not checked.

### 3. Build a clean commit

Commit only the coherent scope that was actually implemented.

If the tree is mixed:

- separate the intended batch first
- do not commit a broad accidental bundle

Commit messages must be in English, imperative mood.

### 4. Push immediately

Push the branch after a successful commit.

Do not leave finished work as unpushed local history unless the user explicitly asks to stop before push.

### 5. Report completion

Final report should include:

1. branch
2. commit hash
3. pushed or not pushed
4. verification run
5. any remaining follow-up

## Safety Rules

- Do not rewrite history unless explicitly requested.
- Do not amend old commits unless explicitly requested.
- Do not push mixed unrelated changes just to "save progress".
- If remote/auth/network blocks push, stop and report the blocker clearly.

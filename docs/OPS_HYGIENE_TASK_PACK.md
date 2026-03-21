# Ops Hygiene Task Pack

## Name

Operational Hygiene Stabilization

## Goal

Reduce obvious operational blind spots in the current VPS/dev environment before deeper feature work continues.

This is not a product sprint.

It is a small maintenance pack intended to improve:

- observability
- runtime confidence
- migration/operator clarity
- worktree awareness

## Why This Exists

Current baseline identified these risks:

- very dirty git worktree
- app container has no healthcheck
- monitoring stack is not up
- migration naming is confusing
- bot polling timeout appears in logs

Not all of them need full fixes right now, but the environment should become less opaque.

## Read First

Read:

1. `docs/VPS_BASELINE_2026-03-21.md`
2. `docs/PRODUCT_DECISIONS.md`
3. `docs/CODEX_TASK_PACKS.md`
4. `skills/tg-project-executor/SKILL.md`
5. `skills/tg-review-gate/SKILL.md`

## Scope

In scope:

- app healthcheck
- migration/operator note
- runtime error review for Telegram bot polling timeout
- worktree hygiene snapshot
- minimal observability note

Out of scope:

- full monitoring rollout
- deep refactor of deployment topology
- major CI/CD work
- large docker-compose rewrite

## Tasks

### 1. Add App Healthcheck

Implement a meaningful healthcheck path for the app container.

Preferred outcome:

- `docker compose ps` can show health meaningfully
- app is not considered healthy only because process exists

If no proper app health endpoint exists yet:

- implement the smallest reasonable health endpoint or health command
- then wire it into compose

### 2. Review Telegram Bot Polling Timeout

Investigate whether the observed Telegram bot polling timeout is:

- expected intermittent noise
- retryable benign behavior
- or an actual operational issue

Deliverable:

- short written finding
- no need for heavy redesign unless a clear issue is found

### 3. Add Migration Naming Note

Create a short operator-facing note documenting the migration naming situation and what ordering rule is authoritative.

Goal:

- reduce operator confusion
- avoid mistakes during future deploy/review

This can be a short doc note, not a full migration renaming project.

### 4. Worktree Hygiene Snapshot

Produce a concise categorized snapshot of the current dirty worktree:

- generated/runtime files
- docs-only changes
- code changes
- unknown files

Goal:

- make future sprint review easier

Do not reset or destroy changes.

### 5. Monitoring Recommendation

Do not fully deploy monitoring unless trivial.

Instead:

- document whether current work can proceed safely without it
- list the minimum recommended next ops step

## Deliverables

By the end of this task pack, the repo should contain:

- app healthcheck support
- compose healthcheck wiring
- short migration naming/operator note
- short worktree hygiene note
- short Telegram timeout assessment

## Definition of Done

This task pack is complete only if:

1. app health can be checked meaningfully
2. Telegram timeout issue is classified, not just mentioned
3. migration naming confusion is documented
4. dirty worktree is categorized into a readable snapshot
5. no destructive cleanup was performed without explicit instruction

## Verification Required

Codex must verify and report:

- healthcheck behavior
- compose/runtime health visibility
- existence of the new notes/docs
- classification of Telegram timeout issue

## Final Report Required

Return:

1. what changed
2. files changed
3. healthcheck verification result
4. Telegram timeout conclusion
5. worktree hygiene summary
6. remaining ops risks

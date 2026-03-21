# Ops Hygiene Acceptance

## Purpose

Use this checklist after the VPS hygiene task pack is completed.

## Acceptance Checklist

- app now has a meaningful healthcheck path
- docker compose or equivalent runtime shows health beyond mere process liveness
- Telegram bot polling timeout is classified as benign / intermittent / actionable
- migration naming/operator confusion is documented
- current dirty worktree is categorized in a readable note
- no destructive cleanup was done without explicit approval

## Pass Condition

Pass if:

- runtime observability is meaningfully improved
- operator confusion is reduced
- no accidental regressions are introduced

## Hold Condition

Hold if:

- healthcheck is fake/useless
- timeout issue remains unexamined
- worktree remains opaque

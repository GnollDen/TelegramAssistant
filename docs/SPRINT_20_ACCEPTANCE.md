# Sprint 20 Acceptance

## Purpose

Validate that risky Stage 5/backfill operations now have hard phase boundaries and a usable backup safety rail.

## Acceptance Checklist

## Phase Guards

- backfill-active chat cannot be sliced or Stage 5 processed
- slicing-active chat cannot be Stage 5 processed
- tail-only reopen policy exists and is enforced
- blocked transitions are explicit and visible

## Backup Guardrail

- risky operations require fresh backup metadata or explicit override
- backup state is operator-visible
- no risky destructive path starts silently without backup evidence

## Integrity Preflight

- duplicate/overlap/hole checks exist for risky repair paths
- preflight can distinguish clean vs unsafe execution scope

## Verification

- build passes
- startup/runtime wiring passes
- relevant smoke/verification checks pass

## Hold Conditions

- mixed per-chat phase execution is still possible
- backup gate can be bypassed silently
- tail-only reopen is not enforced

## Pass Condition

- Stage 5/backfill coordination is now protected by explicit phase guards and a practical backup safety rail

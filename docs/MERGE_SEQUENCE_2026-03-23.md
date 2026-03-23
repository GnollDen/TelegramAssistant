# Merge Sequence 2026-03-23

## Goal

Take the current mixed working tree and merge it in the only safe sequence:

1. G1 + G2
2. Sprint 21
3. Sprint 20
4. Sprint 22

## Why This Order

### Batch 1

Production-used runtime and repair changes:

- coordination/runtime fixes
- Stage5 repair utility
- budget upsert hardening

This must be stabilized first because later batches build on top of it.

### Batch 2

Sprint 21 composition-root decomposition:

- modular DI
- role-aware startup
- Program.cs cleanup

This is already close to accepted and should be isolated from runtime hotfix logic.

### Batch 3

Sprint 20 runtime hardening:

- phase guards
- backup guardrail
- integrity preflight

This should land only after the earlier runtime/repair and composition-root layers are separated.

### Batch 4

Sprint 22 modular split prep:

- docs
- preview compose
- workload split prep only

This is last because it depends on the previous structure becoming reviewable.

## Rule

Do not review or merge `Program.cs` as one giant undifferentiated file.

Always separate:

- G1/G2 command/runtime hotfix hunks
- Sprint 21 decomposition hunks
- Sprint 20 phase-guard hooks

## Expected Outcome

At the end of this sequence:

- runtime hotfixes are preserved
- composition root is reviewable
- hardening changes are isolated
- modular split prep stays clean and non-invasive

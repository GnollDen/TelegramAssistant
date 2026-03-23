# Near-Term Backlog

## Context

Stage 5 and backfill coordination have gone through multiple repair passes.

The immediate goal is no longer "add major new product layers", but:

- make ingestion and Stage 5 phase transitions safe
- guarantee recoverability before risky ops
- reduce composition-root complexity
- prepare a clean modular split without destabilizing the current runtime

## Priority Order

### P0

- Sprint 20: Stage 5 phase guards + backup guardrail + integrity preflight

### P1

- Sprint 21: Program.cs / DI decomposition + role-based startup registration

### P2

- Sprint 22: Modular split M1 into separate deployable workloads

## Why This Order

Sprint 20 prevents the exact class of failures already observed:

- wide reopen after backfill/reseed
- mixed processing phases
- risky destructive repair without a fresh restore point

Sprint 21 reduces the cost of future operational and architectural changes by shrinking the giant composition root.

Sprint 22 then becomes a controlled deployment/runtime split, not a blind architecture rewrite.

## Parallel Work Guidance

Safe to do now while the current Stage 5 tail is still finishing:

- Sprint 21 implementation work
- Sprint 22 design/prep work
- Sprint 20 docs/runbook/preflight design

Wait until the current Stage 5 tail is fully finished before:

- deploying Sprint 20 phase-guard runtime changes
- deploying Sprint 21 runtime-wiring changes
- deploying Sprint 22 container/workload split

## Success Condition

After these three sprints:

- risky backfill/recompute flows have hard safety rails
- composition root is modular and role-aware
- the system can move toward separated workloads without ingestion/state chaos

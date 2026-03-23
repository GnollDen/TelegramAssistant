# Parallel Agent Plan

## Goal

Speed up the next architecture/hardening wave without destabilizing the currently running Stage 5 tail.

## Start Now

### Agent 1

Sprint 21 implementation:

- decompose `Program.cs`
- modularize DI
- add role-aware startup registration

### Agent 2

Sprint 20 design + implementation prep:

- phase guard model
- backup guardrail model
- integrity preflight checks
- identify safe insertion points in current runtime

### Agent 3

Sprint 22 design/prep only:

- define workload boundaries
- define compose/runtime split plan
- identify shared ownership constraints

## Wait Until Stage 5 Tail Finishes

- production rollout of Sprint 20 runtime changes
- production rollout of Sprint 21 startup changes
- production rollout of Sprint 22 workload split

## Recommended Order After Tail Finish

1. Ship Sprint 20
2. Ship Sprint 21
3. Ship Sprint 22

## Notes

- Sprint 21 is the safest coding track to start immediately
- Sprint 22 should start as design/prep, not deployment
- Sprint 20 should be designed now, but prod activation should wait for the current run to finish

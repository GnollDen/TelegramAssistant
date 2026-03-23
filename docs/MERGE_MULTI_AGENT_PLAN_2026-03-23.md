# Merge Multi-Agent Plan 2026-03-23

## Goal

Use one orchestrator and a few scoped workers to prepare merge-ready batches without reintroducing mixed-scope chaos.

## Worker Split

### Worker A

Batch 1 merge execution prep

Ownership:

- G1/G2 runtime/repair layer only
- path + hunk filtering for `Program.cs`

### Worker B

Batch 2 merge execution prep

Ownership:

- Sprint 21 clean changeset only
- `Program.cs` Sprint 21 hunks + `Startup/*`

### Worker C

Batch 4 follow-up cleanup + merge prep

Ownership:

- prep-only docs/preview compose cleanup
- no production runtime changes

## Main Rollout Logic

- Batch 3 is already closed and can be treated as the easiest mergeable runtime-hardening batch after Batch 1/2 prep
- Batch 4 should be cleaned while A/B are working
- orchestrator must prevent file-scope collisions

## No-Go Rules

- do not let Worker A and Worker B both edit the same `Program.cs` hunk range blindly
- do not let Worker C touch runtime code
- do not start production rollout from this pass

## Expected Deliverables

- clean merge prep notes for Batch 1
- clean merge prep notes for Batch 2
- cleaned Batch 4 follow-up files
- orchestrator summary of what is merge-ready now

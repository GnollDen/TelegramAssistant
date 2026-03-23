# Merge Execution Plan 2026-03-23

## Goal

Move from review/repair loops to a clean merge sequence with explicit batch ownership.

## Final Batch Status

- Batch 1: ready
- Batch 2: pass with follow-up
- Batch 3: closed
- Batch 4: pass with follow-up

## Merge Order

1. Batch 1
2. Batch 2
3. Batch 3
4. Batch 4

## Why This Order

### Batch 1

Production-used runtime/repair layer must be stabilized first.

### Batch 2

Composition-root decomposition must land after Batch 1 so `Program.cs` changes stay understandable and scoped.

### Batch 3

Hardening layer depends on earlier runtime and startup foundations being separated cleanly.

### Batch 4

Prep-only modular split artifacts should land last, after runtime-impacting layers are clean.

## Execution Rules

- do not merge `Program.cs` as one undifferentiated blob
- Batch 1 and Batch 2 must be hunk-separated
- Batch 4 remains prep-only; no accidental rollout activation
- follow-up fixes that are tiny and local may be folded into their own batch only if they do not blur boundaries

## Batch Notes

### Batch 1

- use clean changeset only
- include unavoidable dependency `0020`
- exclude Sprint 21 decomposition hunks

### Batch 2

- keep Sprint 21 as a pure composition-root batch
- runtime evidence follow-up can remain post-merge if clearly recorded

### Batch 3

- merge as the clean Sprint 20 hardening layer

### Batch 4

- fix follow-up items before merge if possible:
  - `tga-stage6` ownership drift
  - prep-only vs rollout wording
  - repo-relative links

## Desired Outcome

At the end of this plan:

- runtime hotfixes are preserved
- startup decomposition is clean
- hardening layer is merged
- modular split prep is documented without rollout confusion

# Merge Batch 4 Follow-Up

## Goal

Close the remaining prep-only inconsistencies so Batch 4 can merge cleanly.

## Required Fixes

1. Resolve workload ownership drift:
- `tga-stage6` role mapping must be consistent between docs and preview compose

2. Separate prep-only vs rollout wording:
- Batch 4 docs should not imply that modular split is already deployed
- acceptance should match prep-only phase

3. Fix repo-relative reviewability:
- replace non-repo/local-machine path assumptions in Sprint 22 docs

## Scope

- docs only
- preview compose only

## Out Of Scope

- real workload split rollout
- runtime code changes

## Acceptance

- docs are internally consistent
- preview compose matches stated ownership model
- Batch 4 remains clearly prep-only

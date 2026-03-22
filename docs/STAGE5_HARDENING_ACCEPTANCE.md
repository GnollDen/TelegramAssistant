# Stage 5 Hardening Acceptance

## Purpose

Validate that Stage 5 became more explicit, predictable, and cost-controlled without breaking ingestion.

## Acceptance Checklist

## Expensive Pass

- expensive pass state is explicit
- expensive pass is not silently active as a normal default path
- logs/config make its state understandable

## Embeddings

- embedding model routing is config-driven where expected
- no important hardcoded embedding model remains in the key Stage 5 path without justification

## Summary Behavior

- summary generation behavior is understandable
- summary path is not operationally misleading
- summary cost surface is clearer

## Edit-Diff

- edit-diff path is clearly optional
- it does not look like an accidental always-on token sink

## Verification

- build passes
- startup passes
- stage5 smoke passes

## Hold Conditions

Hold if any of these are true:

- expensive pass behavior is still ambiguous
- key embedding routing is still hardcoded incorrectly
- summary behavior is still confusing enough to hide cost
- the hardening pass breaks Stage 5 startup or expected ingestion behavior

## Pass Condition

This pass succeeds if:

- Stage 5 is materially clearer and safer to operate
- ready for future cost optimization and A/B work

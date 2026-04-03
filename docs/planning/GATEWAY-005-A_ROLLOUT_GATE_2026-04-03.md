# GATEWAY-005-A Rollout Gate

## Date

2026-04-03

## Scope Analyzed

- Gateway foundation through `GATEWAY-004-B`
- Success and failure smoke coverage
- Replay A/B evidence for the safe `stage5_edit_diff` text path
- Pilot parity and governance validation for the same path

## Evidence Reviewed

### 1. Smoke coverage status

- `--llm-gateway-success-smoke` passed on 2026-04-03 and confirms normalized success-path handling for `codex-lb` text/chat plus `openrouter` embeddings and audio.
- `--llm-gateway-failure-smoke` passed on 2026-04-03 and confirms retryable fallback, fail-fast non-retryable failures, and normalized gateway exception mapping.

### 2. Replay A/B evidence

Source: [gateway_text_replay_ab_report.json](/home/codex/projects/TelegramAssistant/artifacts/llm-gateway/gateway_text_replay_ab_report.json)

- Baseline branch: `4/4` success, `0.00` error rate, `1.00` schema-valid rate, `1.00` expected-behavior match rate.
- Candidate branch: `3/4` success, `0.25` error rate, `0.50` schema-valid rate, `0.25` expected-behavior match rate.
- Cross-branch comparison: `1/4` parity, `1/4` diverged, `1/4` schema mismatch, `1/4` error.

Confirmed finding:
- The candidate branch is not behaviorally ready for broader text rollout. This is evidenced by both lower schema validity and low normalized-output parity on the fixed replay set.

### 3. Pilot parity and governance evidence

Source: [edit_diff_pilot_validation_report.json](/home/codex/projects/TelegramAssistant/artifacts/llm-gateway/edit_diff_pilot_validation_report.json)

- Legacy path: `4/4` success, `0.75` expected-behavior match rate, telemetry visible `4/4`.
- Gateway pilot path: `4/4` success, `1.00` schema-valid rate, telemetry visible `4/4`, usage logged `4/4`.
- Gateway pilot parity versus legacy: `1/4` parity, `3/4` diverged, `0` schema mismatch, `0` hard errors.
- Gateway pilot fallback rate: `1.00` (`4/4` requests fell back from `codex-lb` to `openrouter`).
- Budget semantics probe: quota registration remained compatible for `stage5_edit_diff`.

Confirmed findings:
- The pilot path is rollback-safe and governance-compatible at the budget/telemetry level.
- The pilot path does not currently prove primary-provider readiness because runtime evidence shows fallback-only execution.
- Normalized-output parity versus the legacy path is too low for a broader rollout decision.

## Gate Decision

`NO-GO` for broader gateway rollout beyond the bounded `edit_diff` pilot.

### Rationale

- Primary `codex-lb` did not successfully serve the validated pilot traffic in the collected runtime evidence.
- Replay A/B evidence showed material quality regression in candidate behavior.
- Pilot parity remained at `0.25`, which is below an acceptable threshold for expanding text-path migration.
- Budget and telemetry compatibility reduce governance risk, but they do not offset output-quality and primary-provider readiness gaps.

## Deferred Until Revalidation

- Broader text-path migration beyond `stage5_edit_diff`
- Any migration of embeddings or audio as part of a broad gateway-adoption claim
- Any claim that `codex-lb` is production-ready as the primary text provider for this track

## Minimal Required Conditions For Re-Opening Go Decision

- Demonstrate successful pilot execution from primary `codex-lb` without fallback on the safe replay and pilot validation corpus.
- Raise replay and pilot parity to a materially higher level, with schema-validity regressions removed.
- Re-run the smoke suite, replay A/B, and pilot validation after provider remediation or routing/model policy changes.

## Residual Risk

- The gateway foundation itself appears operational.
- The dominant remaining risk is behavioral: provider output quality and primary-provider availability are not yet strong enough to justify broader cutover.

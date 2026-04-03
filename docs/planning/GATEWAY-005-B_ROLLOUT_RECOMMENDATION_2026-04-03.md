# GATEWAY-005-B Rollout Recommendation

## Date

2026-04-03

## Scope

This recommendation follows the `GATEWAY-005-A` rollout gate decision.

Inputs reviewed:

- [GATEWAY-005-A_ROLLOUT_GATE_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/GATEWAY-005-A_ROLLOUT_GATE_2026-04-03.md)
- [gateway_text_replay_ab_report.json](/home/codex/projects/TelegramAssistant/artifacts/llm-gateway/gateway_text_replay_ab_report.json)
- [edit_diff_pilot_validation_report.json](/home/codex/projects/TelegramAssistant/artifacts/llm-gateway/edit_diff_pilot_validation_report.json)

## Recommendation Summary

The gateway foundation should stay in place, but broader migration should remain paused.

Current recommendation:

1. Keep the gateway contracts, adapters, routing, smokes, A/B harness, and bounded `stage5_edit_diff` pilot path in the repository.
2. Keep broader text migration disabled beyond the existing pilot flag.
3. Defer any embeddings and audio adoption claims until the text primary-provider path is proven stable enough to justify renewed rollout work.

## What Can Proceed Safely Now

- Maintain the existing gateway foundation as the integration seam for future provider work.
- Keep running `--llm-gateway-success-smoke` and `--llm-gateway-failure-smoke` as part of gateway health verification.
- Keep the `stage5_edit_diff` pilot available behind `Analysis:EditDiffGatewayEnabled` for targeted validation and provider remediation testing.
- Use the replay A/B harness and pilot validation runner to measure the impact of provider, model, prompt-shaping, or routing changes before any broader cutover attempt.

## What Must Remain Deferred

- Any broader Stage5 text-path migration beyond `stage5_edit_diff`
- Any claim that `codex-lb` is ready as the primary production text provider for this track
- Any broader embeddings migration decision under the gateway rollout banner
- Any broader audio migration decision under the gateway rollout banner

These remain deferred because the current evidence set shows both fallback-only pilot execution and materially weak normalized parity versus the legacy path.

## Recommended Re-Entry Sequence

1. Stabilize the primary text provider path.
   Evidence target:
   `codex-lb` must serve the pilot corpus directly without fallback for a sustained validation run.
2. Improve normalized output parity on the safe pilot path.
   Evidence target:
   replay A/B and pilot validation should show materially better parity and no schema-validity regression.
3. Re-run the bounded pilot gate.
   Evidence target:
   success/failure smokes stay green, budget semantics stay compatible, telemetry remains complete, and fallback becomes exceptional rather than universal.
4. Only after the text pilot is revalidated should broader text-path candidates be reconsidered.
5. Embeddings and audio should remain on their current provider path until the text primary-provider problem is no longer open and a new explicit migration decision is recorded.

## Concrete Re-Open Signals

Broader migration can be reconsidered only when all of the following are true:

- Pilot validation shows primary-provider success from `codex-lb` instead of `100%` fallback to `openrouter`.
- Replay A/B no longer shows the current candidate regression profile (`0.25` error rate, `0.50` schema-valid rate, `0.25` expected-behavior match rate).
- Pilot parity materially exceeds the current `1/4` result and no longer shows divergence as the dominant outcome.
- Gateway telemetry and budget signals remain complete under the improved provider behavior.
- The updated evidence is captured in the same auditable artifacts and reviewed through a new gate note before any broader rollout task starts.

## Immediate Follow-Up Priority

Priority order for the next gateway work after this slice:

1. Provider remediation on `codex-lb` availability and output behavior for the safe pilot corpus
2. Replay and pilot revalidation against the remediated provider path
3. Only then, a new rollout gate review

## Final Position

The correct next move is not rollback of the gateway foundation.

The correct next move is controlled revalidation:

- keep the gateway seam
- keep the pilot bounded
- fix primary-provider readiness
- reopen broader rollout only after fresh evidence shows the current no-go conditions have been removed

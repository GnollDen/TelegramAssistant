# Sprint 22 Acceptance

## Purpose

Validate that Sprint 22 prep artifacts are internally consistent, rollout-gated, and ready for controlled post-tail execution.

## Acceptance Checklist

## Workload Split

- ingest/Stage 5/Stage 6/web/MCP workload boundaries are explicitly defined in prep artifacts
- preview compose reflects the same ownership mapping as docs
- active production deployment path remains unchanged during prep phase

## Ownership

- ingestion owns Telegram connectivity/session use
- Stage 5 does not accidentally run Stage 6/web roles
- Stage 6 iteration no longer risks ingest uptime directly

## Verification

- build passes
- runtime-role wiring checks are documented for post-tail execution
- preview compose/startup validation is staged for isolated non-prod environment
- no double-running worker ownership in prep mapping

## Hold Conditions

- roles still overlap in unsafe ways
- ingestion/session ownership becomes ambiguous
- docs/preview artifacts imply active production rollout during prep phase

## Pass Condition

- Sprint 22 prep artifacts are internally consistent, rollout-gated, and ready for a controlled post-tail cutover window

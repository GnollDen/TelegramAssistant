# Pre-Sprint 0 Runtime Role and Health Contract

## Date

2026-03-25

## Purpose

This note fixes PS0-2 and PS0-3 contracts for sprint start:
- runtime role meanings and allowed combinations
- startup/readiness/liveness/degraded behavior rules

Until Sprint 1 exits, this note is mandatory deploy policy.

## Runtime Role Matrix (V0)

| Role | Meaning Today | Required Dependencies | Deployable Now |
| --- | --- | --- | --- |
| `Ingest` | Telegram intake and enqueue only | Telegram API, queue/Redis, DB connectivity for intake state | Yes (with pinned role) |
| `Stage5` | substrate processing/extraction/summaries | queue/Redis, DB, model provider where needed | Yes (with pinned role) |
| `Stage6` | reasoning/artifact/case generation | Stage5 substrate availability, DB, model provider | Partial (controlled use) |
| `Web` | operator review surface | DB, Stage6 read paths, auth/session config | Partial |
| `Ops` | health/runtime control signals | host runtime config, health endpoints, dependency probes | Yes (required for operations) |

## Allowed Role Combinations (Pre-Sprint 0)

- Default safe mode: one pinned runtime role per instance.
- Allowed exception: `Ops` co-located with one functional role.
- Not allowed in deploy-now baseline:
  - implicit multi-role fallback
  - empty or unknown role configuration
  - runtime auto-expansion to broader role sets

## Startup Contract

Startup must fail fast when:
- runtime role is missing, unknown, or ambiguous
- role-required dependencies are not configured
- critical secrets/config for the selected role are missing

Startup may proceed only when selected role config is explicit and valid.

## Readiness Contract

Readiness means "safe to accept new work for this role."

Readiness must be `false` when:
- role-required queue/DB/model dependency is unavailable
- Stage5 critical path is unavailable for roles that depend on it
- internal role configuration drifts from pinned startup role

## Liveness Contract

Liveness means "process is running and can self-report health."

- Liveness must be process-oriented.
- Liveness does not imply downstream dependency readiness.

## Degraded Behavior Contract

When required dependencies fail after startup:
- process may stay live
- readiness must flip to `false`
- no new work should be admitted for that role
- in-flight work should follow role-safe cancellation/drain behavior
- quota/cooldown guard states must be persisted in shared operational state (not process-memory only)
- Stage 6 paid chat/tool paths must propagate caller cancellation end-to-end so canceled turns stop downstream paid work

## Soft vs Hard Status

- Hard/deployable now: explicit role pinning, startup/readiness/liveness semantics, degraded not-ready behavior.
- Soft/not yet hardened: strong isolation guarantees across all role combinations and full fail-closed coverage in every path.

Those soft areas are Sprint 1 hardening scope, not assumed complete at sprint start.

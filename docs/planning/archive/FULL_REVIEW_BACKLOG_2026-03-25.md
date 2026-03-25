# Full Review Backlog

## Date

2026-03-25

## Purpose

This backlog captures the must-fix and follow-up work from the full multi-agent review of the project.

It is broader than the narrow data/prompt/database backlog and focuses on:
- control-plane safety
- runtime fail-closed behavior
- Stage 5 processing semantics
- database consistency guarantees
- AI execution discipline
- observability gaps
- ops/security posture

## Review Outcome Summary

Overall review verdict:
- `risky`

Why:
- The project is functionally mature, but still carries systemic risks in:
  - runtime control plane
  - Stage 5 processing semantics
  - DB consistency
  - AI execution control
  - observability for silent regressions

## P0 Must Fix

### P0.1 Make runtime-role parsing fail-closed

Risk:
- Invalid or empty runtime role config can fall back to overly broad workload activation.
- This increases blast radius and makes misconfiguration dangerous.

Primary owners/files:
- [RuntimeRoleSelection.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Host/Startup/RuntimeRoleSelection.cs)
- startup/runtime config readers in host layer

Required outcome:
- invalid/empty runtime role config must fail safely
- no silent fallback to `all` or equivalent wide role
- startup error or explicit block instead of permissive behavior

Control points:
- runtime role parse failures
- startup blocked by invalid role config
- role mismatch incidents

### P0.2 Split readiness from liveness

Risk:
- A process may be alive while critical Stage 5/queue/coordination paths are broken.
- Current health semantics do not protect against degraded-but-running states.

Primary owners/files:
- healthcheck/readiness code in host/runtime layer
- relevant hosted service registration and runtime health paths

Required outcome:
- liveness proves process viability only
- readiness covers critical operational path:
  - queue
  - phase guards
  - Stage 5 critical path
  - essential dependencies

Control points:
- readiness failures vs liveness success
- queue/phase anomalies while process stays alive

### P0.3 Remove Stage 5 silent-loss processing window

Risk:
- Stage 5 may mark work complete before durable extraction apply.
- This can create silent loss without immediate visible failure.

Primary owners/files:
- [AnalysisWorkerService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage5/AnalysisWorkerService.cs)
- [MessageRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/MessageRepository.cs)

Required outcome:
- no message reaches durable processed state before effective apply succeeds
- clear retry/requeue path on failure

Control points:
- `processed_messages_without_effective_extraction_apply`

### P0.4 Add DB-safe identity and conflict semantics

Risk:
- Facts/relationships still rely on weak read-then-write semantics.
- Duplicate or inconsistent rows can still be created under concurrency.

Primary owners/files:
- facts/relationships repositories
- relevant migrations and constraints

Required outcome:
- unique constraints for natural identities
- conflict-safe upserts
- aligned repository and DB semantics

Control points:
- duplicate-rate on facts
- duplicate-rate on relationships

### P0.5 Make analysis_state watermarks monotonic/CAS-safe

Risk:
- Blind overwrite of watermarks can cause progress regressions and silent corruption of control state.

Primary owners/files:
- [AnalysisStateRepository.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Infrastructure/Database/AnalysisStateRepository.cs)

Required outcome:
- monotonic/CAS-safe writes
- alarms on regressions

Control points:
- watermark monotonicity
- backward movement alarms

### P0.6 Propagate CancellationToken through Stage 6 AI paths

Risk:
- Expensive or paid Stage 6 calls may continue after caller cancellation.
- This causes waste and weak execution control.

Primary owners/files:
- [BotChatService.cs](/home/codex/projects/TelegramAssistant/src/TgAssistant.Intelligence/Stage6/BotChatService.cs)
- downstream tool/AI call paths in Stage 6

Required outcome:
- end-to-end `CancellationToken` propagation
- no `CancellationToken.None` in expensive AI/tool paths

Control points:
- cancelled turn but paid calls still completed
- cancellation leakage counters

### P0.7 Remove insecure runtime defaults

Risk:
- Compose and env posture still allow risky defaults and weak startup assumptions.

Primary owners/files:
- [docker-compose.yml](/home/codex/projects/TelegramAssistant/docker-compose.yml)
- env/config startup guards

Required outcome:
- safer defaults
- explicit startup guard for unsafe/incomplete config
- no permissive insecure boot by accident

Control points:
- startup guard failures
- config posture drift
- MCP bind/auth drift

## P1 Should Fix Next

### P1.1 Prompt versioning and drift control

Risk:
- Prompt templates can drift between code, DB, and runtime.

Required outcome:
- prompt id/version/checksum
- update-if-changed semantics
- drift visibility

### P1.2 Shared persisted cooldown state

Risk:
- Cooldown living only in memory weakens degrade behavior under restart or multi-instance conditions.

Required outcome:
- shared persisted cooldown contract
- restart-safe and multi-instance-safe behavior

### P1.3 Decompose composition root by workload profiles

Risk:
- Current host/runtime shape remains broad and hard to reason about.

Required outcome:
- clearer workload ownership
- smaller blast radius
- easier reasoning about active services

### P1.4 Add AI quality gates

Risk:
- Quality regressions may ship silently even if code stays healthy.

Required outcome:
- gates for:
  - tool-call completion
  - synthesis completeness
  - extraction coverage consistency
  - scenario regression packs

## P2 Hardening and Tooling

### P2.1 CI guard for migration naming/order

Risk:
- Migration ordering remains fragile.

Required outcome:
- CI check for order collisions and naming ambiguity

### P2.2 Fault-injection recovery testing

Risk:
- App/Redis/Postgres restart behavior may regress without explicit test coverage.

Required outcome:
- restart and recovery drills
- reclaim/retry/crash tests

### P2.3 Separate read-only vs materializing operator APIs

Risk:
- Operator surfaces may accidentally mix review/read with state-mutating/materializing paths.

Required outcome:
- explicit separation of read-only vs write/materialization contracts

### P2.4 Expand silent-regression observability

Risk:
- Some of the highest-severity issues are silent until too late.

Required outcome:
- stronger signals on:
  - processed-without-apply
  - phase guard anomalies
  - Redis PEL aging
  - prompt drift
  - duplicate-rate regressions

## Recommended Execution Order

1. Fail-closed runtime roles
2. Readiness vs liveness split
3. Stage 5 silent-loss fix
4. DB identity/conflict semantics
5. Watermark CAS safety
6. Stage 6 cancellation propagation
7. Insecure default removal and startup guard
8. Prompt versioning
9. Shared cooldown state
10. AI quality gates
11. Migration CI guard
12. Fault-injection and recovery tooling
13. Operator API separation
14. Silent-regression observability expansion

## Control Points To Keep Watching

- `processed_messages_without_effective_extraction_apply`
- monotonicity of `analysis_state`
- duplicate-rate SLOs for messages/facts/relationships
- Redis PEL:
  - pending_count
  - max_idle_ms
  - reclaim spikes
- Stage 6 cancellation leakage
- prompt version/checksum drift
- soft-limit/degrade behavior vs hard-stop rate
- MCP auth/bind posture

## Agent Execution Guidance

When delegating this backlog:
- treat P0 as safety and correctness work, not polish
- keep fixes narrow and verifiable
- require concrete evidence for each control point affected
- do not combine control-plane remediation with unrelated feature work

## Current Priority Statement

The highest-value next work is control-plane and correctness hardening.
Do not treat current project maturity as proof that silent-failure risks are acceptable.

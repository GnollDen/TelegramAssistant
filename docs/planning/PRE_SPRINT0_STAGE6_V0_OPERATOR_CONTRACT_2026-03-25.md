# Pre-Sprint 0 Stage 6 V0 Operator Contract

## Date

2026-03-25

## Purpose

This note fixes PS0-4 for sprint start.
It defines first-wave operator contract terms that must not be inferred during Sprint 1+ implementation.

## `dossier` Meaning (First Wave)

`dossier` is a synthesized operator-facing artifact for one bounded scope.
It must include:
- current understanding
- evidence basis
- uncertainty/open questions
- action relevance

`dossier` must not default to raw payload dumps or internal reasoning traces.

## Internal/Raw vs Operator-Facing

Internal/raw includes:
- raw message/extraction payloads
- intermediate reasoning traces
- low-confidence working state

Operator-facing includes:
- `dossier`
- `current_state`
- `strategy`
- `draft`
- `review`
- `clarification_state`

Internal/raw data may support outputs but is non-default for operator UI/bot responses.

## Scope Contract

### Chat Scope

- Default scope for artifacts and cases is one bounded Telegram chat.
- If scope is ambiguous, choose narrower single-chat scope and request clarification.

### Case Scope

- Case scope is the actionable unit attached to one chat by default.
- Cross-chat case scope is allowed only when:
  - operator explicitly asks for cross-chat synthesis, or
  - case type explicitly requires cross-chat evidence.

## One-Queue Rule

- Exactly one canonical case queue exists.
- Bot and web expose filtered views of that same queue.
- Urgent/ready/needs-input are queue filters, not separate queues.

## Deployable Now vs Soft

- Deployable now:
  - first-wave artifact set and scope defaults
  - one canonical queue rule
  - bot/web split by filtered views
- Still soft:
  - richer multi-scope automation
  - advanced autonomous queue orchestration
  - non-default surfacing of internal/raw traces

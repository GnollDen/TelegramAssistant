# OPINT-001-C Auth, Session, and Bounded Action Contract

## Date

2026-04-03

## Status

Completed governance artifact for `OPINT-001-C`.

This note defines the operator auth/session and bounded-action policy contract for Telegram and Web surfaces. It is planning authority for `OPINT-002+` implementation slices only. It does not add runtime features, does not depend on MCP, and does not reopen legacy Stage6, legacy web, or legacy tgbot workflows.

## Authority Inputs

- [OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md)
- [OPINT-001-B_EXECUTION_DEFAULTS_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/OPINT-001-B_EXECUTION_DEFAULTS_2026-04-03.md)
- [tasks.json](/home/codex/projects/TelegramAssistant/tasks.json)
- [task_slices.json](/home/codex/projects/TelegramAssistant/task_slices.json)

## Operational Boundary Analyzed

Service boundary:

- operator-authenticated Telegram and Web surfaces that can read bounded operator context or submit operator actions into the clean operator-layer contracts

Control-plane boundary:

- operator authentication
- operator authorization/allowlist resolution
- operator session state
- surface handoff between Telegram and Web
- bounded action admission

Data-plane boundary:

- active tracked person context
- active resolution item or other active scope item
- active mode
- unfinished workflow step
- action and audit records emitted by operator interaction

Dependency edges included:

- operator identity source or allowlist used by Telegram/Web
- shared session store or equivalent durable/ephemeral state boundary
- audit/event persistence used by `OPINT-002+`

Dependency edges explicitly excluded:

- MCP
- legacy Stage6 case/artifact/profile/strategy flows
- legacy web routes and legacy tgbot workflow as execution authority

## Confirmed Facts and Working Assumptions

Confirmed from current authority:

- P0 actions are `approve`, `reject`, `defer`, `clarify`, `evidence`, and `open-web`.
- P0 stays bounded to one active tracked person at a time.
- `reject`, `defer`, and `clarify` require explanation.
- Telegram and Web are the intended operator surfaces.

Assumptions this note makes explicit for implementation:

- Telegram and Web may use different transport-specific authentication mechanics, but both must resolve to one canonical operator identity contract before any read/write action is accepted.
- `open-web` is a bounded handoff/navigation action, not an authorization bypass.
- `OPINT-002+` will persist action and audit data through clean operator-layer contracts rather than legacy Stage6 stores.

## Risk Statement

Without a shared auth/session contract, `OPINT-002+` can diverge across Telegram and Web in ways that widen blast radius:

- actions may be attributed to transport identities instead of a stable operator identity
- stale or cross-person session state may allow writes against the wrong tracked person or item
- Web handoff may inherit Telegram context without fresh authorization on the target surface
- audit records may omit the active mode or unfinished step that explains why an action was allowed

## Contract

### 1. Canonical operator identity

Every authenticated interaction must resolve to one canonical operator identity before any operator-scoped read or write is admitted.

Required identity fields:

- `operator_id`: stable internal identifier used across Telegram and Web
- `operator_display`: human-readable label for audit surfaces
- `surface_subject`: transport-specific subject such as Telegram chat/user binding or Web principal id
- `auth_source`: allowlist or identity provider source used for admission
- `auth_time_utc`: timestamp of the authentication event or validated session restoration

Policy:

- tracked person identity is never used as operator identity
- transport subject alone is not sufficient audit identity; it must map to `operator_id`
- unauthorized or unmapped subjects may not perform operator actions and may not receive scoped data beyond an access-denied outcome

### 2. Shared operator session model

Telegram and Web must use the same logical session envelope even if transport mechanics differ.

Required session fields:

- `operator_session_id`: unique session identifier
- `operator_id`
- `surface`: `telegram` or `web`
- `authenticated_at_utc`
- `last_seen_at_utc`
- `expires_at_utc` or equivalent explicit invalidation rule
- `active_tracked_person_id`
- `active_scope_item_key`: nullable when the operator is at person-level context only
- `active_mode`
- `unfinished_step`: nullable structured object describing the current bounded workflow step

`unfinished_step` minimum fields:

- `step_kind`
- `step_state`
- `started_at_utc`
- `bound_tracked_person_id`
- `bound_scope_item_key`

Policy:

- a session is valid for exactly one active tracked person at a time
- switching tracked person is an explicit session transition and clears `active_scope_item_key` plus any incompatible `unfinished_step`
- `unfinished_step` is valid only for the tracked person and scope item that created it
- a mutating action may not implicitly switch tracked person, item, or mode

### 3. Active mode contract

`active_mode` is mandatory session state and must explain the bounded workflow currently in progress.

P0-approved mode families:

- `resolution_queue`
- `resolution_detail`
- `clarification`
- `evidence`

P1+ modes may extend this set through later authority notes, but implementations may not invent silent free-form modes that change authorization semantics.

Policy:

- mode changes are auditable session transitions
- mode alone does not grant authority; it only narrows what the operator is doing inside the already bounded scope
- action handlers must validate that the requested action is allowed from the current mode

### 4. Bounded action admission policy

Every operator action must be authenticated, auditable, and scope-bounded before execution.

Required action envelope fields:

- `operator_id`
- `operator_session_id`
- `surface`
- `tracked_person_id`
- `scope_item_key`
- `active_mode`
- `unfinished_step` reference or null
- `action_type`
- `explanation` or explicit null when not required
- `submitted_at_utc`

Admission rules:

- mutating actions `approve`, `reject`, `defer`, and `clarify` require a live authenticated session plus exact match between request scope and session scope
- `reject`, `defer`, and `clarify` must satisfy the explanation rule already fixed by `OPINT-001-B`
- `evidence` and `open-web` remain authenticated actions and must stay bounded to the same tracked person and relevant scope item
- if `scope_item_key` is absent, only person-level reads/navigation may proceed; no item mutation is allowed
- no global queue, cross-person bulk action, or multi-item mutation is permitted under this contract

### 5. Telegram/Web handoff rule

Telegram-to-Web and Web-to-Telegram handoff may carry a bounded context hint, but the target surface must authenticate the operator on that surface before admitting any action.

Policy:

- handoff context may include `tracked_person_id`, `scope_item_key`, and intended `active_mode`
- handoff context must not be treated as sufficient proof of operator identity
- target-surface authentication failure or identity mismatch invalidates the handoff
- handoff into a different tracked person or item than the source session is denied unless the operator performs an explicit context switch after authentication

### 6. Audit minimum

All operator actions and security-relevant session transitions must be auditable.

Minimum audit fields:

- `audit_event_id`
- `operator_id`
- `operator_session_id`
- `surface`
- `surface_subject`
- `tracked_person_id`
- `scope_item_key`
- `active_mode`
- `unfinished_step_kind`
- `action_type` or `session_event_type`
- `decision_outcome`
- `auth_source`
- `event_time_utc`

Required auditable events:

- successful authentication or validated session restoration
- session expiry or explicit invalidation
- tracked-person switch
- mode switch
- bounded action accepted
- bounded action denied

Control objective:

- every accepted write in `OPINT-002+` can be tied back to an authenticated operator, a concrete bounded scope, and the session state that allowed it

### 7. Deny-safe behavior

Smallest safe behavior on contract violation:

- deny the action
- do not widen scope automatically
- do not persist a partial operator decision
- require explicit re-authentication, re-selection, or step restart as appropriate

Mandatory deny cases:

- missing or unmapped operator identity
- expired or invalidated session
- tracked person mismatch between request and session
- scope item mismatch between request and `unfinished_step`
- missing required explanation
- audit write failure for mutating actions

## Why This Contract Is Preferred

This is the smallest coherent policy that restores safety for `OPINT-002+`:

- one canonical identity model prevents Telegram/Web drift
- one session envelope keeps person, mode, and unfinished-step scope explicit
- one bounded action envelope gives backend slices a reusable admission and audit contract
- deny-safe behavior preserves rollback options because rejected requests do not mutate state

Broader redesign is intentionally deferred. This note does not choose concrete identity provider, token format, or storage technology.

## Downstream Use

`OPINT-002+` should treat this note as binding for:

- action persistence contracts
- read-model/session lookup boundaries
- Telegram command or callback admission checks
- Web action/postback admission checks
- audit schema and audit assertions

## Validation For This Slice

Validated in planning artifacts:

- contract is recorded in a dedicated `docs/planning` note
- contract aligns with `OPINT-001-B` defaults for explanation policy and bounded tracked-person scope
- contract keeps Telegram/Web reuse explicit without introducing MCP or legacy Stage6/web/tgbot dependencies

Still requires live implementation verification in later slices:

- actual authentication mechanism selection and allowlist wiring
- session expiry and restoration behavior under real transports
- audit persistence failure handling
- handoff denial behavior across Telegram/Web

## Residual Risk and Follow-Up

Residual risk after this slice:

- implementation may still drift if `OPINT-002+` does not reuse the exact identity/session/action fields above
- final assurance depends on runtime tests for expiry, replay, and cross-surface mismatch handling

Prioritized follow-up actions:

1. `OPINT-002` should implement action and audit envelopes using this contract verbatim where possible.
2. `OPINT-004` and `OPINT-005` should enforce the same bounded session semantics on Telegram and Web entrypoints.
3. Later validation slices should test one normal action path, one denied path, and one recovery path after session expiry or context mismatch.

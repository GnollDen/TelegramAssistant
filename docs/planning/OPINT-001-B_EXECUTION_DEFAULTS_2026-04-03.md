# OPINT-001-B Execution Defaults

## Date

2026-04-03

## Status

Completed governance artifact for `OPINT-001-B`.

This note records defaults-for-now required to execute the operator-layer waves without reopening product ambiguity. It is authority for backlog execution only. It does not implement runtime behavior, and it does not replace the PRD.

## Authority Inputs

- [OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md)
- [tasks.json](/home/codex/projects/TelegramAssistant/tasks.json)
- [task_slices.json](/home/codex/projects/TelegramAssistant/task_slices.json)

## Clarified Problem Statement

`OPINT-001` needs explicit execution defaults so `OPINT-002` through `OPINT-005` can build the P0 resolution system without waiting on final product-policy decisions that the PRD still leaves open.

The immediate user outcome is narrower than full product completion:

- engineering can implement P0 resolution read/write/recompute and Telegram/Web surfaces against one stable contract
- product ambiguity is captured as explicit defaults or deferred decisions instead of being resolved ad hoc during implementation
- no legacy Stage6/web/tgbot flow is reopened by ambiguity

## Normalized Scope

### In Scope

- record wave-safe defaults for ambiguous P0 decisions already referenced by `OPINT-001` acceptance criteria
- define success and failure boundaries for those defaults
- identify downstream slices affected if a default changes later

### Out of Scope

- runtime feature implementation
- auth/session mechanism design beyond what `OPINT-001-C` will formalize
- redefining the PRD delivery order
- adding new operator actions, new resolution taxonomy types, or new pilot surfaces

### Deferred

- final long-term product policy for evidence depth beyond MVP
- multi-person or global operator workflow outside the bounded pilot
- richer explanation-policy variants beyond the P0 minimum

## Confirmed P0 Contract Inputs

These are already fixed by existing backlog authority and are not new assumptions in this note:

- P0 resolution item taxonomy: `clarification`, `review`, `contradiction`, `missing_data`, `blocked_branch`
- P0 action set: `approve`, `reject`, `defer`, `clarify`, `evidence`, `open-web`
- P0 delivery order remains resolution-first across Telegram and Web

## Defaults For Now

### 1. Mandatory explanation policy

Default:

- `reject`, `defer`, and `clarify` require a non-empty operator explanation in both Telegram and Web flows
- `approve` may accept an optional explanation but does not require one for P0
- `evidence` and `open-web` are navigation/inspection actions only and do not create explanation-bearing decisions

Reason this is the default:

- it matches the explicit `OPINT-001` acceptance requirement
- it gives `OPINT-002-B`, `OPINT-004-C`, and `OPINT-005-D` one shared validation rule

Success boundary:

- no P0 decision write for `reject`, `defer`, or `clarify` is accepted without explanation
- explanation policy is identical across Telegram and Web

Failure boundary:

- surface-specific exceptions
- silent acceptance of empty `reject`, `defer`, or `clarify` submissions

### 2. Clarify payload minimum

Default:

- `clarify` carries the same mandatory explanation requirement as above
- when follow-up questions are asked, the answer set is persisted as structured clarification payload
- P0 clarification stays item-local and bounded to the current resolution workflow, not a free-form multi-item investigation thread

Reason this is the default:

- the PRD requires structured clarification payload and bounded follow-up behavior
- it unblocks command-contract design without preempting the fuller auth/session policy work in `OPINT-001-C`

Success boundary:

- `clarify` always produces auditable intent plus structured payload when follow-up input exists

Failure boundary:

- clarify implemented as unstructured chat text only
- clarification flow expands into unbounded cross-item or cross-person sessions under P0

### 3. Evidence drilldown depth for MVP

Default:

- MVP evidence drilldown is summarized-only
- Telegram `Evidence` shows compact summarized evidence entries and hands deep inspection off to Web
- Web evidence panel shows summarized evidence, provenance, trust context, and bounded drilldown links, but not a full raw message/media viewer in P0

Reason this is the default:

- the PRD says Telegram stays compact and Web is the deep-analysis workspace, but it leaves raw-viewer depth open
- summarized drilldown is the smallest scope that satisfies `OPINT-001` acceptance without expanding P0 into a full evidence browser

Success boundary:

- operators can understand why an item exists, what evidence families support it, and where to continue analysis

Failure boundary:

- P0 depends on building raw transcript/media browsing before resolution workflows can ship
- Telegram grows into a deep-analysis evidence surface

### 4. Fixed tracked-person selector for bounded pilot rollout

Default:

- P0 pilot surfaces operate against a fixed tracked-person set prepared for the bounded rollout
- the operator chooses one active tracked person at a time from that fixed set
- if the bounded pilot has only one tracked person, the system auto-selects it
- operator identity is never used as the tracked person target

Reason this is the default:

- it matches the PRD scope model and `OPINT-001` acceptance wording
- it gives `OPINT-002-C`, `OPINT-004-A`, and `OPINT-005` one stable context-selection rule

Success boundary:

- every P0 read, action, and recompute path can be tied to one active tracked-person context

Failure boundary:

- ad hoc tracked-person creation during P0 resolution work
- implicit switching between tracked persons without explicit operator action

### 5. Bounded pilot scope rule

Default:

- P0 resolution queue, detail, actions, and recompute feedback stay bounded to the currently active tracked person
- no cross-person bulk actions, global triage queue, or multi-person action submission is assumed in P0
- broader person-navigation and workspace expansion stays sequenced to later OPINT tasks

Reason this is the default:

- it is the smallest safe scope consistent with the bounded pilot language already present in the backlog
- it prevents silent expansion of `OPINT-004` and `OPINT-005` into a broader operations console

Success boundary:

- downstream slices can implement one-person-at-a-time resolution workflows without inventing cross-person policies

Failure boundary:

- P0 implementation requires a global queue or multi-person action semantics not yet approved

## Downstream Dependency Impact

If these defaults change later, the first expected rework points are:

- `OPINT-001-C` for auth/session policy alignment
- `OPINT-002-B` and `OPINT-002-C` for write-contract and selection-contract changes
- `OPINT-004-A` and `OPINT-004-C` for Telegram context and evidence behavior
- `OPINT-005-C` and `OPINT-005-D` for Web evidence and clarification behavior

## Non-Blocking Risks

- `OPINT-001-C` still needs to formalize operator auth/session policy; this note does not choose identity storage, session expiry, or authorization mechanics
- if product later wants raw evidence viewers in MVP, `OPINT-005-C` scope will expand materially
- if product later wants global or multi-person resolution triage in P0, both backend contracts and UI sequencing will need to be reopened

## Open Decisions Requiring Product/Owner Resolution

These are explicitly not blockers for P0 execution under the defaults above:

1. Whether `approve` should ever require explanation in a later wave
2. Whether Web evidence drilldown later includes raw message/media viewers, summarized-plus-raw hybrid, or another depth model
3. What the final persons-list information model is for P1
4. Whether Web assistant mode stays post-P0 or moves earlier
5. How much offline-event content can be edited after capture

## Recommended Engineering Handoff

Treat this note as the temporary product-policy default for `OPINT-002` through `OPINT-005`.

Engineering should proceed with:

- one shared explanation validator for `reject`, `defer`, and `clarify`
- summary-first evidence contracts
- one-active-tracked-person context selection for the bounded pilot
- no cross-person workflow assumptions unless a later planning note explicitly changes them

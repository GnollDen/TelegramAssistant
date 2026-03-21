# Clarification Link Conventions

## Purpose

This note freezes the intended semantics of clarification dependency links so they do not drift across future sprints.

## Scope

These conventions apply to:

- clarification questions
- dependency links between questions
- recompute planning references where dependency meaning matters

## Core Rule

`link_type` defines the structural relationship.

`link_reason` explains the concrete business reason in the current case.

Do not invert these roles.

## Allowed `link_type` Meanings

### `depends_on`

Meaning:

- question B should not be interpreted independently until question A is answered or resolved

Typical effect:

- question B may be deprioritized until A is resolved

Use when:

- B needs factual or contextual clarification that A may provide

### `duplicate_of`

Meaning:

- question B is materially the same clarification intent as question A

Typical effect:

- B can be resolved/collapsed under A

Use when:

- the answer to A would make B redundant

Do not use when:

- B is merely related but still distinct

### `blocks`

Meaning:

- unresolved question A blocks safe interpretation of question B or a downstream output

Typical effect:

- B may remain visible, but A carries stronger priority

Use when:

- A is a hard precondition for a higher-confidence interpretation

## `link_reason` Usage

`link_reason` should be short and concrete.

Good examples:

- `same_transition_root_cause`
- `same_offline_event_unknown`
- `same_status_ambiguity`
- `prerequisite_for_state_update`
- `prerequisite_for_period_interpretation`

Bad examples:

- long prose explanations
- generic labels like `related`
- labels that duplicate `link_type`

## Modeling Rules

- Prefer one clear dependency over many weak dependencies.
- Do not create dense graph noise for loosely related questions.
- If two questions share evidence but are not functionally redundant, do not mark them `duplicate_of`.
- If resolution of A only partially informs B, prefer `depends_on` over `duplicate_of`.

## Resolution Rules

Preferred behavior:

- `duplicate_of` may collapse child into resolved/redundant state
- `depends_on` may downgrade or defer child priority
- `blocks` should increase parent importance, not necessarily delete child

## Future Extension Rule

If a new `link_type` is needed later:

- add it explicitly
- document it here
- do not overload existing meanings

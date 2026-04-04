# OPINT-006-A Assistant Response Contract

## Date

2026-04-04

## Status

Completed contract artifact for `OPINT-006-A`.

This note defines the deterministic, machine-checkable assistant response contract for Telegram assistant mode on bounded tracked-person scope. It is implementation authority for `OPINT-006-B1`, `OPINT-006-B2`, and `OPINT-006-C`. It does not add runtime behavior by itself.

## Authority Inputs

- [OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/OPERATOR_INTERACTION_LAYER_PRD_2026-04-03.md)
- [OPINT-001-C_AUTH_SESSION_BOUNDARY_CONTRACT_2026-04-03.md](/home/codex/projects/TelegramAssistant/docs/planning/OPINT-001-C_AUTH_SESSION_BOUNDARY_CONTRACT_2026-04-03.md)
- [OPINT-004-D_VALIDATION_2026-04-04.md](/home/codex/projects/TelegramAssistant/docs/planning/OPINT-004-D_VALIDATION_2026-04-04.md)
- [OPINT-005-F_VALIDATION_2026-04-04.md](/home/codex/projects/TelegramAssistant/docs/planning/OPINT-005-F_VALIDATION_2026-04-04.md)
- [tasks.json](/home/codex/projects/TelegramAssistant/tasks.json)
- [task_slices.json](/home/codex/projects/TelegramAssistant/task_slices.json)

## Contract Goals

- force one response shape for assistant outputs
- force explicit truth labels in every assistant answer
- force explicit trust percent rendering in every assistant answer
- keep context assembly bounded to the active tracked person
- keep deep-analysis handoff bounded and auditable

## Deterministic Response Envelope

Assistant responses must be produced in this canonical envelope before Telegram rendering:

```json
{
  "contract_version": "opint_assistant_v1",
  "surface": "telegram_assistant",
  "tracked_person_id": "00000000-0000-0000-0000-000000000000",
  "scope_key": "chat:885574984",
  "operator_session_id": "telegram:885574984",
  "question": "string",
  "generated_at_utc": "2026-04-04T00:00:00Z",
  "sections": {
    "short_answer": {
      "text": "string",
      "truth_label": "Inference",
      "trust_percent": 73
    },
    "what_is_known": [
      {
        "truth_label": "Fact",
        "text": "string",
        "trust_percent": 88,
        "evidence_refs": [
          "evidence:123"
        ]
      }
    ],
    "what_it_means": [
      {
        "truth_label": "Inference",
        "text": "string",
        "trust_percent": 71,
        "evidence_refs": [
          "evidence:123"
        ]
      },
      {
        "truth_label": "Hypothesis",
        "text": "string",
        "trust_percent": 52,
        "evidence_refs": []
      }
    ],
    "recommendation": {
      "truth_label": "Recommendation",
      "text": "string",
      "trust_percent": 69,
      "evidence_refs": [
        "evidence:123"
      ]
    }
  },
  "trust_percent": 69,
  "open_in_web": {
    "enabled": true,
    "target_api": "/api/operator/resolution/detail/query",
    "tracked_person_id": "00000000-0000-0000-0000-000000000000",
    "scope_item_key": "resolution:clarification:abc",
    "active_mode": "resolution_detail",
    "handoff_token": "string"
  },
  "guardrails": {
    "scope_bounded": true,
    "mcp_dependent": false
  }
}
```

## Machine-Check Rules

The following checks are mandatory and deterministic:

1. `contract_version` must equal `opint_assistant_v1`.
2. `surface` must equal `telegram_assistant`.
3. `tracked_person_id` must match the active tracked person in operator session context.
4. `scope_key` must match the active tracked-person scope in operator session context.
5. `sections` must contain all required keys:
   - `short_answer`
   - `what_is_known`
   - `what_it_means`
   - `recommendation`
6. `sections.short_answer.truth_label` must be one of `Fact`, `Inference`, `Hypothesis`.
7. Every element under `what_is_known` must use truth label `Fact`.
8. Every element under `what_it_means` must use truth label `Inference` or `Hypothesis`.
9. `sections.recommendation.truth_label` must equal `Recommendation`.
10. All `trust_percent` values must be integer `0..100`.
11. Top-level `trust_percent` must be integer `0..100` and must always be rendered as `Trust: NN%`.
12. `open_in_web.tracked_person_id` must equal top-level `tracked_person_id`.
13. `open_in_web` must not include cross-person or cross-scope identifiers.
14. `guardrails.scope_bounded` must be `true`.
15. `guardrails.mcp_dependent` must be `false`.

## Telegram Rendering Contract

Telegram text rendering must use this exact section order and headings:

1. `Short Answer`
2. `What Is Known`
3. `What It Means`
4. `Recommendation`
5. `Trust: NN%`

Deterministic text shape:

```text
Short Answer
[Inference | 73%] <text>

What Is Known
- [Fact | 88%] <text>

What It Means
- [Inference | 71%] <text>
- [Hypothesis | 52%] <text>

Recommendation
[Recommendation | 69%] <text>

Trust: 69%
```

Rendering rules:

- every rendered bullet/line in section content must include `[Label | NN%]`
- labels must be exactly `Fact`, `Inference`, `Hypothesis`, or `Recommendation`
- `Trust: NN%` line is mandatory, always present, and always last
- if `open_in_web.enabled=true`, Telegram adds `Open in Web` action control

## Open in Web Bounded Handoff Contract

Assistant-mode handoff must follow the same bounded-session principles already enforced in OPINT-004/005:

- handoff is a bounded context hint, not authorization bypass
- target surface must re-authenticate operator session
- handoff payload must preserve:
  - `tracked_person_id`
  - `scope_item_key`
  - `operator_session_id`
  - `active_mode`
  - `handoff_token`
- if handoff scope and active session scope diverge, target surface must deny action
- legacy web routes are out of contract

## Fallback and Failure Semantics

If bounded context is insufficient:

- still render full section shape
- keep explicit labels
- include low-confidence hypothesis or recommendation for clarification
- keep `Trust: NN%` present

If scope validation fails:

- do not render unbounded answer
- return deterministic bounded failure reason to caller:
  - `tracked_person_scope_mismatch`
  - `session_scope_item_mismatch`
  - `missing_active_tracked_person`

## Bounded Examples

### Example A: Normal bounded answer

```text
Short Answer
[Inference | 76%] The tracked person is currently avoiding direct calls and prefers asynchronous messages.

What Is Known
- [Fact | 91%] In the last 14 days, 11 of 13 replies arrived as text after missed calls.
- [Fact | 84%] Response delay dropped when messages contained concrete next-step options.

What It Means
- [Inference | 74%] Call-first outreach is likely to reduce response probability in the current state.
- [Hypothesis | 57%] Current workload pressure may be driving channel preference.

Recommendation
[Recommendation | 72%] Send one concise text with two time-window options instead of calling first.

Trust: 72%
```

### Example B: Low-context bounded answer

```text
Short Answer
[Hypothesis | 49%] Available evidence is limited for a reliable communication recommendation.

What Is Known
- [Fact | 63%] Recent interaction evidence for this tracked person is sparse in the active scope.

What It Means
- [Inference | 51%] Any communication strategy suggestion has elevated uncertainty.

Recommendation
[Recommendation | 58%] Ask one clarification question about preferred contact timing before acting.

Trust: 58%
```

## Testability Mapping for OPINT-006

This contract enables deterministic checks for parent acceptance criteria:

- structure check: required sections always present in order
- truth-label check: all rendered lines include explicit label category
- trust check: section trust and terminal `Trust: NN%` always present
- scope check: envelope and handoff fields bound to active tracked person/session
- MCP-independence check: `guardrails.mcp_dependent=false`

## Implementation Handoff

`OPINT-006-B1` should implement envelope construction + validation from this note.

`OPINT-006-B2` should implement bounded retrieval/context assembly and scope/audit guardrails that populate contract guardrail fields.

`OPINT-006-C` should implement Telegram rendering + assistant mode controls using this section order and label format, plus bounded `Open in Web` handoff.

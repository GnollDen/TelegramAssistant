# AI Conflict Resolution Session UX Detail

Date: `2026-04-06`
Status: implementation-ready UX slice
Scope: `/operator/resolution` detail panel and clarification drawer in the current vanilla JS shell

## Current Interaction Evidence

Evidence from the current web shell:

- Clarify is implemented as a multi-field payload editor with `summary`, `explanation`, `question`, `answer`, `answer kind`, and `notes`.
- Detail actions expose four peer CTAs: `Approve`, `Reject`, `Defer`, `Clarify`.
- There is no explicit AI verdict state between operator answer entry and durable apply.
- Error handling exists, but recovery is mostly technical and action-specific.

Primary code anchors:

- `Clarification Panel`: `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs:2670`
- `Bounded Actions` block: `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs:3462`
- `Clarify submit` validation + payload: `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs:3659`
- `Detail/queue load states`: `src/TgAssistant.Host/OperatorWeb/OperatorWebEndpointExtensions.cs:3790`

## Top UX Problems

### P0. One-question session is over-modeled as form filling

Evidence:

- Current clarify drawer asks for six inputs before submit.
- For the target flow, only one AI question and one operator answer are essential.

Impact:

- Increases decision time.
- Makes operators invent structure the system should already know.
- Blurs the mental model: "answer the conflict question" vs "author a clarification payload".

### P0. Missing verdict checkpoint before apply

Evidence:

- Current flow goes from answer entry or direct action click straight into `/api/operator/resolution/actions`.
- No dedicated state communicates how AI interpreted the operator answer.

Impact:

- Operators cannot confirm whether the system understood the answer.
- Apply feels risky because verdict and commit are conflated.

### P1. Recovery paths are under-explained

Evidence:

- Current messages like `Follow-up answer is required.` and `Clarify submission failed` are valid but do not provide the next best path.
- Scope/item invalidation is handled, but the answer draft is not explicitly preserved.

Impact:

- Users must infer whether to retry, switch to manual action, or refresh scope.

## Target Flow

Keep the current detail page and drawer. Replace the clarify interaction with a strict four-step session:

1. `Question shown`
2. `Operator answers`
3. `AI returns verdict`
4. `Operator applies verdict`

Do not add a new route or multi-step wizard.

## Minimal UI Structure

Reuse the current clarification drawer, but simplify the information architecture:

- Keep `clarification-context` as session context.
- Replace editable `clarification-question` with a read-only question block.
- Keep only one editable answer field using `clarification-answer`.
- Reuse `clarification-state` as the async state/status banner.
- Add a small verdict block between answer and footer buttons.
- Change the primary footer CTA depending on state:
  - before verdict: `Check answer`
  - after verdict: `Apply verdict`

Hide by default in this flow:

- `clarification-summary`
- `clarification-explanation`
- `clarification-answer-kind`
- `clarification-notes`

These can still be filled programmatically for backward-compatible payload construction.

## Copy

### Drawer title

`AI conflict check`

### Context line

`Conflict: {title} · Scope: {scopeItemKey}`

### Question block

Label:

`Question from AI`

Helper:

`Answer once. The system will suggest the resolution outcome before anything is applied.`

Fallback when unavailable:

`AI question is unavailable for this conflict. Use manual actions below.`

### Answer input

Label:

`Your answer`

Placeholder:

`Answer briefly and factually. If you do not know, say what is missing.`

Helper:

`1-3 short sentences. No need to retell the full chat.`

### Verdict block

Idle:

`After your answer, AI will suggest one outcome: apply, reject, or defer.`

Loading:

`AI is checking your answer against the current conflict...`

Approve verdict:

`Suggested verdict: apply resolution. Your answer is sufficient to resolve this conflict now.`

Reject verdict:

`Suggested verdict: reject current conflict interpretation. Your answer conflicts with the current hypothesis.`

Defer verdict:

`Suggested verdict: defer. Your answer is useful, but not strong enough to resolve the conflict safely.`

Fallback verdict:

`AI verdict is unavailable. You can still use manual actions for this item.`

### Apply area

Primary button before verdict:

`Check answer`

Primary button after verdict:

`Apply verdict`

Secondary button after verdict:

`Edit answer`

Manual escape hatch link/button:

`Use manual actions instead`

### Success copy

Verdict applied:

`Verdict applied. Queue and related conflicts were refreshed.`

## State Model

### 1. Idle

- Question is visible.
- Answer field is enabled.
- Verdict block shows idle copy.
- Primary CTA is `Check answer`.

### 2. Checking

- Answer field and footer buttons are disabled.
- Verdict block shows loading copy.
- Drawer stays open.

### 3. Verdict ready

- Verdict block shows `apply`, `reject`, or `defer`.
- Answer remains visible.
- Primary CTA becomes `Apply verdict`.
- Secondary CTA becomes `Edit answer`.

### 4. Applying

- Controls are disabled.
- State banner shows `Applying verdict...`

### 5. Applied

- Drawer closes.
- Detail panel shows success feedback.
- Queue reloads and selection is refreshed.

### 6. Fallback/manual

- AI question or verdict is unavailable.
- Operator can exit to existing manual `Approve/Reject/Defer` actions.

## Error And Fallback Behavior

### Missing answer

Trigger:

- operator clicks `Check answer` with empty input

Behavior:

- inline field error on answer
- keep focus in answer field

Copy:

`Enter one answer before checking the verdict.`

### Verdict request failed

Trigger:

- session/respond request fails or times out

Behavior:

- preserve typed answer
- keep drawer open
- show retry path and manual escape hatch

Copy:

`AI could not return a verdict. Retry, or use manual actions for this conflict.`

### Apply failed

Trigger:

- apply call fails after verdict is shown

Behavior:

- keep verdict visible
- do not clear answer
- enable `Apply verdict` retry
- allow switch to manual actions

Copy:

`Verdict was not applied. Retry apply, or use manual actions instead.`

### Item changed or disappeared

Trigger:

- `scope_item_not_found`, scope mismatch, or refresh invalidates the item

Behavior:

- close drawer
- refresh detail/queue
- preserve last typed answer in memory until next item selection

Copy:

`This conflict changed while you were answering. The page was refreshed before apply.`

### Handoff/session/auth failure

Behavior:

- reuse existing route-level error surface
- do not open the AI session drawer

Copy:

`This session is no longer valid. Refresh scope and open the conflict again.`

## Implementation Notes

Minimal implementation path for the current shell:

- keep existing queue/detail rendering unchanged
- keep existing manual action buttons as fallback
- repurpose the clarification drawer for the AI session
- use a local JS state object like:
  - `conflictSessionId`
  - `questionText`
  - `operatorAnswer`
  - `verdict`
  - `verdictActionType`
  - `verdictRevision`
- map `Apply verdict` back into existing `/api/operator/resolution/actions` with optional verdict linkage
- keep current `.state.empty|loading|error|success` classes for visual consistency

## Validation Slice

Validate with 5-7 operator runs on real conflict items.

Success signals:

- lower time-to-apply vs current clarify flow
- fewer abandoned clarify attempts
- fewer manual explanation-only retries
- operators can correctly predict what `Apply verdict` will do before clicking

Open question for next slice:

- whether `defer` should remain an AI verdict in v1 or be manual-only until calibration is stable

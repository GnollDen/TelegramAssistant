# Sprint 11 Task Pack

## Name

Web Review / Edit Layer

## Goal

Add the first usable review-and-edit workflows to the web interface so the product can move from read-only inspection to controlled correction and confirmation.

This sprint should make the web layer operational for review without attempting to finish every workflow or polish the full UX.

## Why This Sprint

The product now has:

- persisted analytical artifacts
- bot command access
- read-first web views
- reviewable outputs across clarifications, periods, profiles, strategy, drafts, and conflicts

The next critical step is web review/edit capability.

Without it:

- the user can inspect the system but cannot reliably steer it
- review remains fragmented
- manual correction and confirmation loops stay too weak

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_11_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_11_TASK_PACK.md)
4. [SPRINT_11_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_11_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)
6. [TIMELINE_REVISION_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\TIMELINE_REVISION_POLICY.md)

Also inspect:

- existing review/event persistence
- inbox/conflict models
- web read layer
- prior review thresholds decisions in product docs

## Scope

In scope:

- web review cards
- confirm / reject / defer actions
- limited inline edit flows where practical
- review handling for clarifications, periods, transitions, profiles, strategy, drafts, conflicts
- reason capture
- impacted-area preview where available
- verification paths

Out of scope:

- full design polish
- advanced batch operations UX
- full search integration
- graph editing UI
- full timeline revision system
- GraphRAG

## Product Rules To Respect

Review should use a consistent pattern:

- object summary
- provenance
- suggested interpretation or change
- actions:
  - confirm
  - reject
  - defer
  - edit

Review must:

- preserve audit trail
- not silently mutate high-impact objects
- capture reason where needed

Critical/high-impact items should remain reviewable before major interpretation changes are adopted.

Edit mode should be:

- inline where small
- drawer/modal or explicit form where bigger

This sprint should not over-build the perfect UI.
It should make review operational and safe.

## Required Deliverables

### 1. Review Web Surface

Add web routes/components/pages that surface reviewable objects and let the user:

- confirm
- reject
- defer
- edit where supported

### 2. Object Coverage

Support review/edit workflows for the most important artifact types:

- clarification questions/answers where review applies
- periods
- transitions
- profile snapshots/traits
- strategy records/options
- draft artifacts where review metadata exists
- conflicts

You do not need to finish every object equally deeply, but the web layer must support a real multi-object review flow.

### 3. Review Action Handling

Implement action handling for:

- confirm
- reject
- defer

And a limited but real edit path for selected artifacts where practical.

Actions must:

- persist through existing review/audit mechanisms
- avoid destructive silent rewrites

### 4. Reason Capture

Where applicable, capture:

- reason
- actor
- affected object

### 5. Impact / Context Visibility

Where available, show:

- provenance
- confidence
- linked period/state/strategy context

If full impact preview is not available for all objects, support it at least for one meaningful high-impact class.

### 6. Verification Path

Add a verification path such as:

- `--web-review-smoke`

That proves:

- review routes resolve
- a reviewable object can be confirmed
- a reviewable object can be rejected
- a reviewable object can be deferred
- at least one edit path works
- audit trail is created

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. web review smoke success
4. confirm path works
5. reject path works
6. defer path works
7. at least one edit path works
8. review/audit trail persists

## Definition of Done

Sprint 11 is complete only if:

1. the web interface now supports operational review
2. review actions are persisted safely
3. the user can start steering the system through web workflows
4. the product is ready for later inbox/history/search refinement

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how web review/edit now works
4. what verification was run
5. remaining limitations before Sprint 12

# Sprint 15 Task Pack

## Name

Outcome / Learning Layer

## Goal

Implement the first usable outcome and learning layer so the product can track:

- what strategy was recommended
- what draft was generated
- what action was actually taken
- what outcome was observed

This sprint should create the causal chain needed for later adaptation and A/B work.

## Why This Sprint

The product now has:

- state
- profiles
- strategy
- draft generation
- draft review
- bot and web operational surfaces
- network context

The next critical step is outcome linkage.

Without it:

- strategy cannot learn from reality
- draft quality cannot be judged over time
- A/B testing remains shallow

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_15_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_15_TASK_PACK.md)
4. [SPRINT_15_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_15_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- strategy records/options
- draft records/outcomes
- current message/session storage
- review events and history

## Scope

In scope:

- outcome service layer
- strategy-to-action linkage
- draft-to-actual-message linkage
- observed outcome recording
- basic learning signals
- outcome web/bot visibility where practical
- verification paths

Out of scope:

- deep automatic adaptation
- full reinforcement loop
- advanced experimentation platform
- sentiment science overhaul
- GraphRAG

## Product Rules To Respect

The system should track a causal chain:

- strategy recommendation
- draft artifact
- actual user action
- observed consequence

Outcome should support labels such as:

- positive
- neutral
- negative
- mixed
- unclear

Learning should begin as:

- structured recording
- matching
- light interpretation

Not:

- uncontrolled self-retraining

User feedback and observed outcomes should remain distinguishable.

## Required Deliverables

### 1. Outcome Service Layer

Implement dedicated services for:

- outcome recording
- draft/action matching
- observed consequence interpretation
- learning signal extraction

You may structure this into services such as:

- `OutcomeService`
- `DraftActionMatcher`
- `ObservedOutcomeRecorder`
- `LearningSignalBuilder`

### 2. Strategy / Draft / Actual Action Linkage

Support recording links between:

- strategy record
- draft record
- actual sent or selected action
- observed follow-up

### 3. Outcome Recording

Support:

- user-labeled outcome
- system-observed outcome
- confidence or uncertainty on outcome interpretation

### 4. Matching Logic

Implement a practical MVP for:

- matching a generated draft to an actual message where possible
- recording partial match when exact match is not possible

### 5. Learning Signals

Generate basic learning signals such as:

- strategy helpful / not helpful
- draft sendable / not sendable
- style fit likely / poor
- action escalated too early / appropriately

Keep this conservative and reviewable.

### 6. Visibility

Expose outcome data where practical through existing bot/web surfaces or read models.

It is enough in this sprint to make outcomes inspectable and linked, not fully polished.

### 7. Verification Path

Add a verification path such as:

- `--outcome-smoke`

That proves:

- strategy can be linked to draft
- draft can be linked to actual action
- outcome can be recorded
- learning signals are produced
- persisted outcome chain is inspectable

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. outcome smoke success
4. outcome records persist
5. matching path works
6. learning signals exist

## Definition of Done

Sprint 15 is complete only if:

1. the system can link recommendations to real-world outcomes
2. outcomes are persistable and inspectable
3. the product is ready for later A/B and budget-aware optimization work

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how outcome/learning now works
4. what verification was run
5. remaining limitations before Sprint 16

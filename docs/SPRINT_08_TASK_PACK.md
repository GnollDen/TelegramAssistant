# Sprint 08 Task Pack

## Name

Draft Review Engine

## Goal

Implement the first usable pre-send draft review layer so the system can:

- assess a candidate message
- identify the main risks
- produce a safer rewrite
- produce a more natural rewrite

This sprint should create a real review layer on top of the draft engine without expanding into bot/web UX polish.

## Why This Sprint

The product now has:

- current state
- profiles
- strategy
- draft generation

The next critical layer is draft review.

Without it:

- the system can generate text, but cannot yet evaluate user-written or generated drafts before sending
- `/review` remains unavailable as a meaningful capability
- strategy-to-draft safety loop remains incomplete

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_08_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_08_TASK_PACK.md)
4. [SPRINT_08_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_08_ACCEPTANCE.md)
5. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- strategy outputs
- draft engine outputs
- conflict model
- risk taxonomy in product decisions

## Scope

In scope:

- review service layer
- risk assessment of a proposed message
- strategy-fit checking
- safer rewrite generation
- more-natural rewrite generation
- review artifact persistence where appropriate
- verification paths

Out of scope:

- final bot `/review` UX
- web review UI
- sent-message outcome learning
- full conversation simulator
- GraphRAG

## Product Rules To Respect

Review mode must support:

- assessing user-provided draft text
- assessing system-generated draft text
- identifying the most relevant risks
- offering a safer rewrite
- offering a more natural rewrite

Review outputs must be:

- strategy-aware
- state-aware
- profile-aware
- restrained

The main risk layer should use the agreed risk taxonomy where relevant, including:

- `overpressure`
- `overdisclosure`
- `premature_escalation`
- `friendship_misread`
- `neediness_signal`
- `ambiguity_increase`
- `withdrawal_trigger`
- `timing_mismatch`

Review should not:

- produce long essays
- invent deep psychological claims
- ignore current ambiguity or fragility

## Required Deliverables

### 1. Review Engine Service Layer

Implement dedicated services for:

- review orchestration
- risk detection
- strategy-fit evaluation
- safer rewrite generation
- more-natural rewrite generation

You may structure this into services such as:

- `DraftReviewEngine`
- `DraftRiskAssessor`
- `DraftStrategyFitChecker`
- `SaferRewriteGenerator`
- `NaturalRewriteGenerator`

### 2. Review Input Handling

Support review of:

- a free-form user draft
- a generated draft record where relevant

Use context from:

- strategy
- current state
- current period
- profiles
- recent messages

### 3. Risk Assessment

Produce:

- short assessment
- 1-2 main risks
- risk labels

Risk logic should be grounded in current state/strategy, not generic linting only.

### 4. Rewrite Generation

Generate:

- one safer rewrite
- one more natural rewrite

They should:

- remain close enough to the intended meaning
- improve strategic fit
- avoid obvious risky escalation

### 5. Strategy-Fit Logic

If a reviewed draft materially conflicts with current strategy:

- say so briefly
- reflect that in risk assessment
- make the safer rewrite align better with strategy

### 6. Persistence

Persist review artifacts in a minimal usable way.

It is acceptable to:

- add a dedicated review record if needed
- or persist through existing review/history artifacts if that fits current architecture better

Do not add unnecessary duplicate truth objects.

### 7. Verification Path

Add a verification path such as:

- `--review-smoke`

That proves:

- a draft can be reviewed
- assessment is produced
- 1-2 main risks are produced
- safer rewrite is produced
- more-natural rewrite is produced
- at least one strategy-conflict scenario is demonstrated

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. review smoke success
4. assessment exists
5. risk labels exist
6. safer rewrite exists
7. more-natural rewrite exists
8. strategy-conflict path is demonstrated

## Definition of Done

Sprint 8 is complete only if:

1. the system can review a draft before sending
2. review is grounded in current analytical context
3. safer and natural rewrites are both available
4. outputs are ready for later bot `/review` integration

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how draft review now works
4. what verification was run
5. remaining limitations before Sprint 9

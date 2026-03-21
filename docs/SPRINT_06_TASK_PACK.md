# Sprint 06 Task Pack

## Name

Strategy Engine

## Goal

Implement the first usable strategy engine so the system can produce:

- multiple valid next-move options
- risks and conditions for each option
- micro-step guidance
- a short horizon when confidence allows

This sprint should turn analysis outputs into a real action layer without collapsing truth and strategy into one thing.

## Why This Sprint

The product now has:

- clarification orchestration
- periodization
- current state
- profiles

The next critical reasoning layer is strategy.

Without it:

- `/next` remains shallow
- draft generation has no strategic substrate
- state/profile insights cannot yet become actionable guidance

## Read First

Read these first:

1. [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
2. [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CODEX_TASK_PACKS.md)
3. [SPRINT_06_TASK_PACK.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_06_TASK_PACK.md)
4. [SPRINT_06_ACCEPTANCE.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\SPRINT_06_ACCEPTANCE.md)
5. [CLARIFICATION_LINK_CONVENTIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CLARIFICATION_LINK_CONVENTIONS.md)
6. [CLARIFICATION_CONTRADICTION_RULES.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CLARIFICATION_CONTRADICTION_RULES.md)
7. [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\CASE_ID_POLICY.md)

Also inspect:

- current state outputs
- profile outputs
- periodization outputs
- strategy schemas from Sprint 1 foundation

## Scope

In scope:

- strategy synthesis services
- multiple action options
- action ranking
- strategy confidence
- why-not-other-options explanation
- risk tagging
- micro-step generation
- short horizon generation when confidence allows
- strategy record persistence
- verification paths

Out of scope:

- draft text generation
- draft review engine
- bot `/next` UX
- web strategy UI
- outcome learning loop optimization
- GraphRAG

## Product Rules To Respect

Strategy must be based on:

- current state
- current period
- profile and pair-pattern context

Strategy must produce:

- multiple valid moves
- risks
- conditions

The action taxonomy must support at least:

- `wait`
- `warm_reply`
- `hold_rapport`
- `check_in`
- `light_test`
- `clarify`
- `invite`
- `deepen`
- `repair`
- `boundaries`
- `deescalate`

Each option must include:

- purpose
- risk
- when to use
- success signs
- failure signs

Strategy should also support:

- micro-step guidance
- why rejected alternatives were not preferred
- short horizon only when confidence is sufficient

When uncertainty is high:

- narrow the option set
- shift toward softer moves and clarification

User style should matter:

- but not at the cost of safety

## Required Deliverables

### 1. Strategy Engine Service Layer

Implement dedicated services for:

- strategy synthesis
- option generation
- option ranking
- risk tagging
- strategy confidence evaluation
- horizon planning

You may structure this into services such as:

- `StrategyEngine`
- `StrategyOptionGenerator`
- `StrategyRanker`
- `StrategyRiskEvaluator`
- `StrategyConfidenceEvaluator`
- `MicroStepPlanner`

### 2. Action Option Generation

Generate multiple candidate options from:

- current state
- latest period
- self/other/pair profiles
- clarifications and conflicts where relevant

Support the agreed action taxonomy.

### 3. Ranking and Filtering

Rank options using:

- state fit
- ambiguity level
- profile fit
- pair-pattern fit
- risk

When uncertainty is high:

- reduce aggressive options
- prefer softer moves
- prefer clarification-compatible moves

### 4. Option Content

Each option should include:

- action type
- short summary
- purpose
- risk labels
- when to use
- success signs
- failure signs
- whether it is the primary option

### 5. Strategy Confidence

Compute strategy confidence from:

- state confidence
- option separation / ranking clarity
- conflict level
- ambiguity

### 6. Why-Not Logic

Add compact explanation for:

- why lower-ranked or rejected options were not preferred

This should be short by default.

### 7. Micro-Step and Horizon

Generate:

- one micro-step recommendation
- short horizon only when confidence is sufficient

Short horizon should be:

- immediate next move
- plus 1-2 likely follow-up steps

Not a long speculative plan.

### 8. Persistence

Persist strategy records and options into the existing strategy schema.

Include:

- confidence
- option details
- why-not notes
- micro-step
- horizon when available

### 9. Verification Path

Add a verification path such as:

- `--strategy-smoke`

That proves:

- strategy record created
- multiple options generated
- risks populated
- primary option selected
- micro-step generated
- high-uncertainty scenario narrows/softens options
- horizon appears only when confidence is sufficient

## Verification Required

Codex must verify:

1. build success
2. runtime startup success
3. strategy smoke success
4. strategy record persisted
5. multiple options persisted
6. risk tags exist
7. micro-step exists
8. uncertainty behavior is demonstrated

## Definition of Done

Sprint 6 is complete only if:

1. the system can turn analysis into structured actionable strategy
2. more than one valid move can be proposed
3. risks and conditions are visible
4. uncertainty softens or narrows strategy appropriately
5. outputs are ready for later draft generation and bot integration

## Final Report Required

Final report must include:

1. what changed
2. files changed
3. how strategy synthesis now works
4. what verification was run
5. remaining limitations before Sprint 7

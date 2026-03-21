---
name: tg-review-gate
description: Use for TelegramAssistant when reviewing a sprint result, validating risky changes, checking acceptance criteria, or deciding whether a change is ready to ship to the next sprint. Focus on regressions, architecture drift, reviewability, provenance, and acceptance against task-pack goals.
---

# TG Review Gate

Use this skill when reviewing completed sprint work or risky changes in TelegramAssistant.

## Read Order

Always read:

1. `docs/PRODUCT_DECISIONS.md`
2. `docs/IMPLEMENTATION_BACKLOG.md`
3. current sprint task pack, for example `docs/SPRINT_01_TASK_PACK.md`
4. current sprint acceptance file, for example `docs/SPRINT_01_ACCEPTANCE.md`
5. `docs/SPRINT_AB_TESTS.md`

Then inspect changed code and verification results.

## Review Priorities

Prioritize:

- regressions
- architecture drift
- truth-layer violations
- reviewability loss
- provenance loss
- missing verification
- mismatch against sprint DoD

Do not focus first on style polish or optional refactors.

## Non-Negotiable Checks

- Facts, hypotheses, overrides, and review history remain separate.
- Provenance is preserved.
- High-risk interpretations are not silently auto-applied.
- Existing ingestion/runtime behavior is not accidentally broken.
- New schema is actually usable from code.
- Verification evidence matches the claimed changes.

## Findings Format

Review findings should focus on:

- behavioral regressions
- missing acceptance criteria
- unsafe shortcuts
- schema/design issues that will block next sprint

If no critical findings exist, say so explicitly and mention residual risks.

## Acceptance Gate

A sprint should pass only if:

- core acceptance items are satisfied
- no major regression is visible
- no immediate redesign is required for the next sprint

If in doubt:

- prefer holding the sprint and fixing foundation issues first

## Output Contract

Final review should include:

1. pass / hold recommendation
2. findings ordered by severity
3. acceptance items confirmed
4. residual risks
5. what should be corrected before the next sprint

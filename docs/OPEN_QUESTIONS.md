# Open Questions

## Purpose

This file tracks only the questions that remain materially unresolved after the current product-definition pass.

Most core product questions are already settled in `PRODUCT_DECISIONS.md`.

## What Is Already Closed

Closed at high confidence:

- product identity and scope
- truth layers and provenance model
- timeline and period principles
- social graph principles
- offline audio workflow
- state/profile/strategy foundations
- bot interaction foundations
- bot response formatting
- web IA foundations
- evaluation principles
- deployment and ops foundations
- taxonomy foundations and core label catalogs
- high-level schema structure

## Remaining Open Questions

## 1. Exact Field-Level Schema Finalization

Still needed:

- exact SQL-level field names and types
- nullable vs non-null rules
- index strategy
- which fields become separate child tables versus JSON
- exact enum representation strategy

This is now an implementation design task more than a product task.

## 2. Exact Score-to-Label Mapping

Still needed:

- how hidden score dimensions map to dynamic labels
- how relationship status is inferred from mixed evidence
- exact ambiguity thresholds
- exact confidence thresholds for showing alternative status

This is a logic calibration task.

## 3. Exact Review Thresholds

Still needed:

- what auto-applies
- what always requires review
- what enters inbox automatically
- what becomes blocking
- what confidence levels trigger manual review

This is important for keeping review load sane.

## 4. Exact Prompt and Model Routing

Still needed:

- which models are used for which stages
- where cheap vs expensive paths split
- where audio-specific models are used
- where GraphRAG retrieval joins the prompt path

This is mostly implementation and cost architecture.

## 5. GraphRAG Rollout Timing

Still needed:

- whether GraphRAG MVP lands before or after first full usable release
- whether graph stays optional through early releases
- which features are allowed to depend on graph

This remains a roadmap decision.

## 6. Exact Eval Dataset and Thresholds

Still needed:

- exact golden cases
- exact expected outputs or review notes
- exact thresholds for ship/hold
- exact regression limits per surface

This is a concrete operations/evaluation task.

## 7. Security and Retention Defaults

Still needed:

- default retention for raw audio
- default retention for transcripts
- default retention for derived snippets
- default log retention
- default purge policy for sensitive materials

This is a policy and ops decision.

## 8. Bot Response Microcopy

Still needed:

- exact Russian wording style for headings
- final short labels for states/risks/actions in bot UI
- exact help/menu wording

This is mostly polish, but affects usability.

## 9. Web Screen-Level Layout Polish

Still needed:

- exact visual layout of each screen
- responsive behavior
- exact card hierarchy
- exact diff rendering
- exact graph interaction behavior

This is UI execution detail, not core product logic.

## 10. Launch Scope

Still needed:

- what is truly in first usable release
- what is feature-flagged
- what is explicitly post-v1

This is the final scoping gate before implementation begins in earnest.

## Recommended Next Order

Best next order:

1. launch scope
2. review thresholds
3. score-to-label mapping
4. exact schema finalization
5. eval thresholds
6. GraphRAG rollout timing
7. security/retention defaults

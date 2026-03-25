# Pre-Sprint 9.5 Behavioral Profile Introduction Plan

## Purpose

Define a safe, bounded first-wave behavioral profile contract for Sprint 10-13 integration.

## First-Wave Output Definition

`artifact_type`: `behavioral_profile` (planned first-wave contract; not yet a mandatory persisted Stage 6 artifact type)

First-wave outputs are descriptive and operational:
- observed pattern summary
- soft hypotheses with uncertainty
- strategy implications for communication behavior

## Allowed Inputs

`input_source_types[]`:
- observed evidence from messages/sessions/timeline
- clarification answers
- user-supplied context (source-typed)
- existing profile snapshots/traits

## Guardrails

- no diagnostic or pseudo-clinical labeling
- no hard personality verdicts from sparse evidence
- hypotheses must be labeled soft and revisable
- confidence must be explicit and bounded

Required output fields:
- `case_id`
- `subject_refs[]`
- `profile_statement_types[]`
- `evidence_refs[]`
- `confidence_label`
- `uncertainty_notes[]`
- `disallowed_claims[]`

## Refresh Triggers

`refresh_trigger_rules[]`:
- material new evidence in the active chat scope
- clarification answer that changes interpretation basis
- user-supplied correction superseding prior context
- manual operator refresh request

## First Consumers

`consumer_surfaces[]`:
- strategy generation context
- draft style/strategy calibration hints
- dossier/current_state explanatory sections (non-diagnostic framing only)

## Evaluation Hooks

`evaluation_dimensions[]`:
- usefulness for strategy/draft quality
- source-separation correctness
- uncertainty/overreach discipline
- operator trust and correction frequency

## Deferred Work

- advanced behavioral taxonomy expansion
- autonomous behavioral interventions
- any diagnostic/clinical interpretations

## Sprint Hand-off

Sprint 10-13 may integrate behavioral profile only as a bounded, non-diagnostic support layer with explicit confidence and source separation.

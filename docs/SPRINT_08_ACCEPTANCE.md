# Sprint 08 Acceptance

## Purpose

Validate Sprint 8 as `dossier` + `current_state` quality uplift for Stage 6 operator baseline.

## Acceptance Checklist

## Dossier Coverage

- dossier is synthesized (not raw payload replay)
- sections are explicit:
  - observed facts
  - likely interpretation
  - uncertainties / alternative readings
  - missing information
  - practical interpretation
- relationship read and notable events are visible

## Current State Coverage

- current_state remains stable and readable
- observed facts are separated from interpretation
- uncertainty is explicit, not hidden behind confidence scalar
- signal strength is presented in scoped outputs

## Signal and Uncertainty Discipline

- signal scale exists and is used:
  - `strong`
  - `medium`
  - `weak`
  - `contradictory`
- uncertainty and alternative readings are explicit
- anti-dump behavior is preserved

## Persistence

- dossier/current_state artifacts persist in a usable way
- audit/history path exists

## Verification

- build passes
- state smoke passes
- web/search smoke passes with Sprint 8 sections and signal markers
- manual review notes confirm operator usability uplift

## Hold Conditions

Hold Sprint 8 if any of these are true:

- dossier still behaves like a dump
- current_state still mixes facts and interpretation in one blob
- uncertainty is hidden behind confident tone
- signal-strength markers are absent
- critical factual failures are present in review samples

## Pass Condition

Sprint 8 passes if:

- dossier/current_state are operator-usable analytical outputs
- fact/interpretation/uncertainty separation is explicit
- uncertainty framing and signal-strength presentation are present

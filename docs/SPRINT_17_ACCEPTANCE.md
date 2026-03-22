# Sprint 17 Acceptance

## Purpose

Validate that Sprint 17 turned external archive ingestion from sidecar foundation into a usable integrated capability.

## Acceptance Checklist

## Ingestion Coverage

- external archive input can be validated
- ingestion persistence works
- idempotent behavior exists

## Provenance and Weighting

- source class is preserved
- provenance is visible
- weighting is stored

## Linkage Readiness

- linkage artifacts toward graph/timeline/clarification context exist
- integration remains controlled and non-silent

## Verification

- build passes
- startup passes
- external archive smoke passes

## Hold Conditions

Hold Sprint 17 if any of these are true:

- ingestion is not really integrated
- provenance is not preserved clearly
- idempotency is absent
- linkage outputs are too weak to use later

## Pass Condition

Sprint 17 passes if:

- the product now has a usable external archive ingestion capability ready for later special-case extensions

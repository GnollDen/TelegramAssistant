# Sprint 02 Acceptance

## Purpose

Validate that clarification orchestration is truly usable as a domain layer and not just a set of extra methods.

## Acceptance Checklist

## Queue and Priority

- questions can be created and stored
- priority levels exist and are usable
- queue ordering reflects impact and priority

## Dependencies

- dependency links are used meaningfully
- child/dependent questions can be suppressed, downgraded, or resolved
- duplicate-like questions do not remain unmanaged

## Answer Apply Flow

- answers are applied through service logic
- question status updates happen correctly
- audit/review history is preserved
- source class for answer is handled

## Contradictions

- contradictory answers create or update conflict records
- contradictions are reviewable, not silently overwritten

## Recompute Planning

- affected outputs are identified after answer application
- at minimum the planner can target:
  - periods
  - state
  - profiles
  - strategy artifacts

## Verification

- build passes
- startup passes
- dependency scenario is demonstrated
- contradiction scenario is demonstrated
- recompute target planning is demonstrated

## Hold Conditions

Hold Sprint 2 if any of these are true:

- orchestration still lives only in repositories
- dependencies exist in schema but are not actually used
- contradictory answers do not create conflicts
- recompute planning is absent or hand-waved

## Pass Condition

Sprint 2 passes if:

- clarification logic is now a real service layer
- queue/dependency/conflict/recompute behavior is proven
- next sprint can build timeline/state logic on top of it

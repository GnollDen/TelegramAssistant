# Sprint 08 Acceptance

## Purpose

Validate that Sprint 8 produced a real draft review layer rather than generic rewrite helpers.

## Acceptance Checklist

## Review Coverage

- draft review can run on candidate text
- assessment is produced
- safer rewrite is produced
- more-natural rewrite is produced

## Risk Quality

- 1-2 main risks are identified
- risk labels are grounded in current state/strategy
- review is not generic text linting only

## Strategy Fit

- strategy-conflict detection exists
- safer rewrite aligns better with strategy where needed

## Persistence

- review artifacts persist in a usable way
- audit/history path exists

## Verification

- build passes
- startup passes
- review smoke passes
- strategy-conflict scenario is demonstrated

## Hold Conditions

Hold Sprint 8 if any of these are true:

- review is generic and not tied to state/strategy
- safer rewrite is missing
- natural rewrite is missing
- risk assessment is vague or empty
- strategy-conflict handling is absent

## Pass Condition

Sprint 8 passes if:

- the system now has a usable pre-send review layer
- ready for bot `/review` and later live messaging workflows

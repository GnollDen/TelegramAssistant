# Sprint 09 Acceptance

## Purpose

Validate that Sprint 9 exposed the implemented engines through a usable bot command layer.

## Acceptance Checklist

## Command Coverage

- `/state` works
- `/next` works
- `/draft` works
- `/review` works
- `/gaps` works
- `/answer` works
- `/timeline` works
- `/offline` is wired in a usable way

## Engine Integration

- commands use real engines
- outputs are not placeholders
- command results are grounded in current product state

## Formatting

- responses are concise
- response structure matches product expectations
- uncertainty is not hidden

## Clarification Flow

- a top clarification question can be surfaced
- answer flow works
- clarification flow is not broken by command integration

## Verification

- build passes
- startup passes
- bot smoke passes

## Hold Conditions

Hold Sprint 9 if any of these are true:

- commands are stubs
- main commands fail end-to-end
- outputs are too raw or unusable in Telegram
- clarification flow is broken

## Pass Condition

Sprint 9 passes if:

- the product now has a usable command-level bot interface over the implemented reasoning stack

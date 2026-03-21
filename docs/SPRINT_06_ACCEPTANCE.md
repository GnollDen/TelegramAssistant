# Sprint 06 Acceptance

## Purpose

Validate that Sprint 6 produced a real strategy layer rather than generic advice text.

## Acceptance Checklist

## Strategy Coverage

- strategy record is generated
- multiple options are generated
- one primary option is selected
- micro-step is generated

## Option Quality

- options are based on real state/period/profile inputs
- each option has purpose
- each option has risk
- each option has when-to-use guidance
- success/failure signs exist

## Uncertainty Behavior

- high uncertainty narrows or softens the option set
- strategy is not overconfident under ambiguity
- risky/aggressive options are not preferred by default when evidence is weak

## Explanation Layer

- strategy confidence exists
- why-not-other-options explanation exists
- horizon appears only when confidence is sufficient

## Persistence

- strategy record persists correctly
- strategy options persist correctly
- micro-step and confidence are stored

## Verification

- build passes
- startup passes
- strategy smoke passes
- persistence works

## Hold Conditions

Hold Sprint 6 if any of these are true:

- only one option is generated
- options are generic and not grounded in state/profile context
- uncertainty does not affect strategy shape
- risks are missing or trivial
- micro-step is absent

## Pass Condition

Sprint 6 passes if:

- the system now has a usable structured strategy layer
- ready to support the later draft and bot `/next` layers

# Clarification Contradiction Rules

## Purpose

This note freezes the initial contradiction policy for clarification answers so future sprints do not reinterpret conflict behavior inconsistently.

## Core Principle

Contradictions should be surfaced, not silently overwritten.

The system should prefer:

- conflict creation
- reviewability
- preserved audit trail

Over:

- destructive replacement
- silent reinterpretation

## Current Contradiction Sources

Contradiction checks currently matter against:

1. previous clarification answers
2. strong current state interpretation
3. strong linked hypothesis

These are the minimum protected surfaces for Sprint 2+.

## Strong Interpretation Rule

A contradiction against interpretation should be taken seriously only when the target interpretation is strong enough.

In practice this means:

- high-confidence state snapshot
- high-confidence linked hypothesis
- or reviewed/confirmed prior answer

Weak internal candidates should not create noisy conflicts by default.

## Contradiction Outcomes

When contradiction is detected, preferred behavior is:

- create a new conflict record
- or reopen an existing relevant conflict

Do not:

- silently replace the prior answer
- silently rewrite state/history

## Review Rule

Contradictions are reviewable objects.

They should remain visible until:

- resolved
- explicitly rejected
- or superseded through review logic

## User Answer Priority

User answers usually carry strong weight.

But if a new user answer conflicts with:

- an earlier user answer
- or a strong reviewed interpretation

the system should still create a conflict, not assume the latest answer is always correct.

## Scope Rule

A contradiction should be attached as locally as possible:

- to the related question
- to the related period
- to the affected output(s)

Avoid creating vague global conflicts when the issue is local.

## Non-Goals

This policy does not yet define:

- advanced semantic contradiction detection
- probabilistic contradiction scoring
- cross-case contradiction semantics

Those can be added later, but must not change the current visible behavior silently.

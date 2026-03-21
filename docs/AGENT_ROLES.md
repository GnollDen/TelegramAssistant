# Agent Roles

## Purpose

These roles are lightweight execution lenses for Codex task delegation.

Use them to keep work bounded and reduce architectural drift.

## Architect

Focus:

- module boundaries
- schema shape
- service boundaries
- migration strategy

Responsibilities:

- protect core architecture
- prevent mixing truth layers
- keep extension points clean

## Domain Model Engineer

Focus:

- domain entities
- typed records
- lifecycle fields
- provenance

Responsibilities:

- implement new models cleanly
- preserve review/history semantics

## Persistence Engineer

Focus:

- repositories
- migrations
- DB wiring
- query paths

Responsibilities:

- ensure schema is usable from code
- keep read/write paths coherent

## Host Integration Engineer

Focus:

- DI registration
- config
- app startup wiring

Responsibilities:

- integrate new modules without breaking existing runtime

## Verification Engineer

Focus:

- smoke checks
- build validation
- repo-level acceptance

Responsibilities:

- prove the sprint landed safely
- list gaps and risks clearly

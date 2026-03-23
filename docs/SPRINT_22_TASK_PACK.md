# Sprint 22 Task Pack

## Name

Modular Split M1

## Goal

Prepare the first practical modular workload split artifacts without changing the active production runtime path.

## Target Workloads

- `tga-ingest`
- `tga-stage5`
- `tga-stage6`
- `tga-web`
- `tga-mcp`

## Why This Sprint

The project already has clear runtime pain:

- ingestion should not depend on Stage 6 experimentation
- Stage 5 should not compete with web/MCP/process restarts
- web and MCP should be operationally isolated

## Read First

1. [NEAR_TERM_BACKLOG_2026-03-23.md](NEAR_TERM_BACKLOG_2026-03-23.md)
2. [SPRINT_21_TASK_PACK.md](SPRINT_21_TASK_PACK.md)
3. [LAUNCH_READINESS.md](LAUNCH_READINESS.md)

## Product Rules

- ingestion owns Telegram session usage
- history catch-up and realtime remain inside ingest, not separate competing services
- one repo, one DB, one migration lineage
- workload split must not change semantic truth layers

## In Scope

- workload roles and startup configuration prep
- preview compose/service split template for M1 workloads (non-default path)
- health/readiness separation plan per workload
- runtime verification plan for post-tail rollout window

## Out Of Scope

- separate databases
- broad event-bus redesign
- repo split
- deep microservice rewrite

## Deliverables

- workload-specific startup roles
- preview compose/service definitions for M1 split prep
- verification path for each workload in isolated non-prod environment

## Verification Minimum

- each workload role mapping is explicit and internally consistent in prep artifacts
- ingestion ownership remains explicit for Telegram session/connectivity
- Stage 5 isolation expectations are documented without active production cutover
- runtime wiring and healthcheck commands are documented for staged post-tail execution

## Final Report

Report strictly:

1. What changed
2. Which files changed
3. What workloads now exist
4. How runtime ownership is split
5. What was verified
6. What limitations remain

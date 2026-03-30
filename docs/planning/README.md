# Planning Pack

## Purpose

This folder is the clean entry point for the current implementation planning set.

It consolidates the recent backlog, product, and sprint-planning work into one place instead of scattering it across the root `docs` folder.

## Recommended Reading Order

1. [FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md)
- primary execution plan
- the main source of truth for implementation order

2. [STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md)
- current product framing
- single-operator product contract

3. [PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md)
- missing product decisions that were closed in interview form
- artifact, case, operator, and reasoning contracts

4. [RUNTIME_TOPOLOGY_NOTE_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\RUNTIME_TOPOLOGY_NOTE_2026-03-25.md)
- short sprint-start runtime map
- clarifies what is actually deployable now vs still soft

5. [STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md)
- broader Stage 6 implementation roadmap
- now treated as supporting planning context

6. [WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md)
- separate web delivery track
- use when planning the internal operator web app

7. [INITIAL_CHRONOLOGY_RECONSTRUCTION_PLAN_2026-03-26.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\INITIAL_CHRONOLOGY_RECONSTRUCTION_PLAN_2026-03-26.md)
- chronology-first reconstruction track
- use when planning initial historical passes, pilot-year runs, and chronology-driven case provenance

8. [STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md)
- separate Stage 6 remediation track
- use when planning post-audit Stage 6 fixes, rebuild prep, and Stage 6 rebuild hardening

9. [STAGE6_VERIFIED_BACKLOG_2026-03-30.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_VERIFIED_BACKLOG_2026-03-30.md)
- code-verified Stage 6 backlog snapshot
- use when deciding what is actually true, what is partially true, and what is not a real blocker

10. [STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md)
- agent-ready execution cards for the current Stage 6 remediation entry sprints
- use when delegating `S6-R0` and `S6-R1` without allowing broad redesign drift
## Current Source-of-Truth Rule

For current implementation planning, use:
- [FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md)

For product framing, use:
- [STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md)

For supporting decision context, use:
- [PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md)
- [RUNTIME_TOPOLOGY_NOTE_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\RUNTIME_TOPOLOGY_NOTE_2026-03-25.md)
- [STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_FULL_IMPLEMENTATION_PLAN_2026-03-24.md)
- [WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\WEB_DELIVERY_SPRINT_PLAN_2026-03-25.md)
- [INITIAL_CHRONOLOGY_RECONSTRUCTION_PLAN_2026-03-26.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\INITIAL_CHRONOLOGY_RECONSTRUCTION_PLAN_2026-03-26.md)
- [STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_REMEDIATION_SPRINT_PLAN_2026-03-26.md)
- [STAGE6_VERIFIED_BACKLOG_2026-03-30.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_VERIFIED_BACKLOG_2026-03-30.md)
- [STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_AGENT_EXECUTION_CARDS_2026-03-30.md)

Do not use `docs/BACKLOG_STATUS.md` as an equal execution authority for sprint start.
It is useful as historical status framing, but the active planning pack is authoritative for current implementation sequencing.

## Archived Inputs

Older intermediate planning inputs were moved to:
- [archive](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\archive)

These remain useful as source material, but should not be treated as the first entry point:
- `FULL_REVIEW_BACKLOG_2026-03-25.md`
- `PROJECT_REVIEW_BACKLOG_2026-03-25.md`
- `WORKING_SPRINTS_2026-03-25.md`

## Pre-Sprint 9.5 Execution Notes (2026-03-25)

These notes freeze the Stage 6 baseline and contracts immediately before Sprint 10:

- [PRE_SPRINT_9_5_CONTEXT_REFRESH_PACKET_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRE_SPRINT_9_5_CONTEXT_REFRESH_PACKET_2026-03-25.md)
- [PRE_SPRINT_9_5_CLARIFICATION_CASE_CONTRACT_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRE_SPRINT_9_5_CLARIFICATION_CASE_CONTRACT_2026-03-25.md)
- [PRE_SPRINT_9_5_USER_SUPPLIED_CONTEXT_CONTRACT_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRE_SPRINT_9_5_USER_SUPPLIED_CONTEXT_CONTRACT_2026-03-25.md)
- [PRE_SPRINT_9_5_BEHAVIORAL_PROFILE_INTRO_PLAN_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRE_SPRINT_9_5_BEHAVIORAL_PROFILE_INTRO_PLAN_2026-03-25.md)
- [PRE_SPRINT_9_5_SPRINT_10_13_FIT_CHECK_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRE_SPRINT_9_5_SPRINT_10_13_FIT_CHECK_2026-03-25.md)

## Sprint Implementation Notes

- [SPRINT_10_CASE_MODEL_IMPLEMENTATION_NOTES_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\SPRINT_10_CASE_MODEL_IMPLEMENTATION_NOTES_2026-03-25.md)
- [SPRINT_11_AUTO_CASE_GENERATION_IMPLEMENTATION_NOTES_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\SPRINT_11_AUTO_CASE_GENERATION_IMPLEMENTATION_NOTES_2026-03-25.md)

## Intent

This folder should reduce planning sprawl and make it obvious:
- what the product is
- what the current execution order is
- which planning docs are active
- which ones are archival inputs

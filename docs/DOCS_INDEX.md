# Docs Index

## Source of Truth

These files should be treated as the current primary reference set:

- [PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\PRODUCT_DECISIONS.md)
- [CODEX_TASK_PACKS.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\CODEX_TASK_PACKS.md)
- [IMPLEMENTATION_BACKLOG.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\IMPLEMENTATION_BACKLOG.md)
- [SPRINT_AB_TESTS.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\SPRINT_AB_TESTS.md)
- [CASE_ID_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\CASE_ID_POLICY.md)
- [BACKLOG_STATUS.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\BACKLOG_STATUS.md)

## Active Sprint Docs

Active only while the sprint is in progress:

- current `SPRINT_*_TASK_PACK.md`
- current `SPRINT_*_ACCEPTANCE.md`

After acceptance, these remain useful as historical execution records, not as primary product doctrine.

## Stable Policies

These remain active until explicitly replaced:

- [CLARIFICATION_LINK_CONVENTIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\CLARIFICATION_LINK_CONVENTIONS.md)
- [CLARIFICATION_CONTRADICTION_RULES.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\CLARIFICATION_CONTRADICTION_RULES.md)
- [TIMELINE_REVISION_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\TIMELINE_REVISION_POLICY.md)
- [TESTING_EXPANSION_NOTE.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\TESTING_EXPANSION_NOTE.md)
- [EXTERNAL_ARCHIVE_INGESTION_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\EXTERNAL_ARCHIVE_INGESTION_POLICY.md)
- [COMPETING_RELATIONSHIP_CONTEXT_POLICY.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\COMPETING_RELATIONSHIP_CONTEXT_POLICY.md)

## Historical Execution Docs

These are useful records, but not the best entry point for understanding the current system:

- completed `SPRINT_01` through `SPRINT_18` task packs and acceptance files
- `SPRINT_01_1_*`
- `OPS_HYGIENE_*`
- `VPS_BASELINE_2026-03-21.md`
- dated `OPS_*_2026-03-21.md`

## Legacy / Narrow-Scope References

These are still useful, but only for a narrow slice of the system:

- [README.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\README.md)
  - still useful for local run/deploy basics
  - outdated as a full architecture map
- [CODEX_BACKLOG.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\CODEX_BACKLOG.md)
  - still useful for old Stage5/media backlog context
  - not the main full-product backlog anymore
- [stage5_product_backlog.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\stage5_product_backlog.md)
  - Stage5-specific reference only

## Documentation Hygiene

Use this rule set going forward:

1. Product truth goes into `PRODUCT_DECISIONS.md`.
2. Current status and remaining work goes into `BACKLOG_STATUS.md`.
3. Current execution details go into the active sprint docs only.
4. Finished sprint docs remain historical, not normative.
5. Narrow subsystem docs should say clearly when they are Stage5-only or legacy-only.

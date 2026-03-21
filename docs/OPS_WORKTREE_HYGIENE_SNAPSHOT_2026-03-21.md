# Worktree Hygiene Snapshot

Date: 2026-03-21
Snapshot source: `git status --short`

## generated/runtime

- No runtime-generated files are currently tracked in git from this snapshot.
- Runtime artifacts exist outside repo (example: `/opt/tgassistant/logs/*`) and remain unmanaged by git.

## docs

- Multiple untracked docs files under `docs/` including sprint/task-pack/acceptance artifacts and ops docs.

## code

- Modified tracked code:
  - `src/TgAssistant.Core/Interfaces/IRepositories.cs`
  - `src/TgAssistant.Host/Program.cs`
  - `src/TgAssistant.Infrastructure/Database/Ef/DbRows.cs`
  - `src/TgAssistant.Infrastructure/Database/Ef/TgAssistantDbContext.cs`
- Multiple untracked code files in `src/TgAssistant.Core/Models/*` and `src/TgAssistant.Infrastructure/Database/*`.

## unknown/untracked

- `skills/` subtree is untracked.
- Several migration SQL files are untracked in `src/TgAssistant.Infrastructure/Database/Migrations/`.

## hygiene note

No destructive cleanup was performed in this pass.

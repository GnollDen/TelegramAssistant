# Migration Naming / Ordering Operator Note

Date: 2026-03-21

## Why this note exists

Migration files currently include duplicate numeric prefixes (`0003_*`, `0004_*`, and baseline historically referenced duplicate `0011_*` ids in DB state). This can confuse manual operators.

## Authoritative ordering rule

For this project, migration apply order is authoritative by embedded resource filename sorted lexicographically (`StringComparer.Ordinal`), implemented in `DatabaseInitializer.LoadEmbeddedMigrations()`.

Operationally this means:

1. Do not infer order from numeric prefix only.
2. Use full migration filename/id from `schema_migrations.id`.
3. Never edit already applied migration SQL (checksum lock). Add a new migration file instead.

## Operator checks before deploy

1. Compare `src/TgAssistant.Infrastructure/Database/Migrations/*.sql` with `schema_migrations.id`.
2. Ensure no checksum mismatch errors in startup logs.
3. If a migration must adjust previous behavior, append a new `00NN_*` file; do not rewrite history.

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

## Guard usage

Use the guard before merge, before deploy, and after adding a migration:

```bash
scripts/verify_migration_order.sh
```

The guard is intentionally fail-closed. It exits non-zero when:

1. a filename does not match `^[0-9]{4}_[a-z0-9]+(_[a-z0-9]+)*\.sql$`
2. an ambiguous 4-digit prefix appears that is not in `scripts/migration_prefix_collisions.allowlist`
3. an allowlisted collision set drifts from the actual migration file set

This mirrors the operational risk: runtime loads by full filename, and historical collisions must remain explicitly tracked while preventing new ambiguous insertions.

## Operator checks before deploy

1. Run `scripts/verify_migration_order.sh` and treat any non-zero exit as a deploy blocker.
2. Compare `src/TgAssistant.Infrastructure/Database/Migrations/*.sql` with `schema_migrations.id`.
3. Ensure no checksum mismatch errors in startup logs.
4. If a migration must adjust previous behavior, append a new unused `00NN_*` file; do not rewrite history.

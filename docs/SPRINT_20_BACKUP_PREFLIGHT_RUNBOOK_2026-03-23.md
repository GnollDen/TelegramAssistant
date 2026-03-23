# Sprint 20 Backup + Integrity Preflight Runbook (Prep Only)

## Purpose

Operator runbook for blocking risky Stage5/backfill actions unless backup evidence and integrity checks are clean.

## Backup evidence contract

Required fields:
- `backup_id`
- `created_at_utc`
- `scope`
- `artifact_uri`
- `checksum`

Freshness policy (prep default):
- backup age must be <= 6 hours before risky apply path.

## Integrity preflight outcomes

- `clean`: run may proceed (subject to approval)
- `warning`: run requires explicit operator review/ack
- `unsafe`: run is blocked (fail-closed)

## Preflight checklist

1. duplicate/overlap check in target chat scope
2. hole detection in expected message/session range
3. dual-source conflict check for repair scope
4. write-volume sanity check vs configured threshold

## Override policy

Override is allowed only with:
- operator identity,
- reason,
- approval token,
- explicit audit trail id.

No silent bypass is allowed.

## Hold policy while Stage5 tail active

During active Stage5 tail:
- design and dry-run checks are allowed,
- apply path with destructive side effects is deferred.

## Dry-run bundle

1. capture backup metadata snapshot,
2. run integrity queries (`scripts/stage5_integrity_preflight_preview.sql`),
3. archive output with timestamped artifact id,
4. request operator sign-off before rollout window.

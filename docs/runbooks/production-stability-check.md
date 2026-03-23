# Production Stability Check

Use this after deploy or when a risky runtime change must be validated in production.

## Core Checks

1. container/process status
2. application healthcheck
3. runtime wiring check
4. role-specific wiring checks when relevant
5. pipeline liveliness in logs

## Typical Evidence

- `docker compose ps`
- app logs over a recent window
- `dotnet TgAssistant.Host.dll --healthcheck`
- `dotnet TgAssistant.Host.dll --runtime-wiring-check`
- role-specific runtime checks
- Redis queue/group sanity
- Stage5/backfill/listener liveliness

## Acceptance Lens

Confirm:

- latest messages are being processed
- slicing/Stage5 paths are active or correctly idle
- no startup exception loops
- no obvious race symptoms
- no growing lag that indicates hidden stall

## Reporting

Final production status should be one of:

- stable and healthy
- healthy with warnings
- degraded
- hold

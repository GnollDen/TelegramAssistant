# Container-First Testing Policy

Mandatory testing policy for this repository.

## Rule

1. Any application-behavior verification must run inside Docker Compose containers.
2. Before behavior tests, rebuild the `app` image.
3. Local host runs (without container) are allowed only for code-level checks, not for runtime-behavior claims.

## Allowed Without Container

- `dotnet build TelegramAssistant.sln`
- static code inspection/review
- diff validation

These checks do not confirm real runtime behavior.

## Required For Runtime/Behavior Verification

Use this sequence:

```bash
docker compose build app
docker compose run --rm app --list-smokes
docker compose run --rm app --<smoke-flag>
```

Examples:

```bash
docker compose run --rm app --opint-004-a-smoke
docker compose run --rm app --opint-006-c-smoke
docker compose run --rm app --opint-009-b-smoke
```

## Reporting Contract

When reporting test results:

1. State the exact container command(s) used.
2. State pass/fail per command.
3. Attach artifact/log path.
4. Do not claim application behavior from non-container runs.

## Fail-Closed

If container test was not run, status must be reported as:

- `not verified in container runtime`


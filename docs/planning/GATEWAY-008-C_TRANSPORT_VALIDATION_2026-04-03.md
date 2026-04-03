# GATEWAY-008-C Transport Validation

## Scope

Bounded transport-backed gateway validation for one safe text/chat path using loopback-only HTTP provider stubs. This is evidence for GATEWAY-008 hardening only; it is not a rollout broadening mechanism.

## Repeatable Command

```bash
dotnet run --project src/TgAssistant.Host -- --llm-gateway-transport-validate
```

Optional explicit artifact target:

```bash
dotnet run --project src/TgAssistant.Host -- --llm-gateway-transport-validate --llm-gateway-transport-validate-output=artifacts/llm-gateway/transport_validation_report.json
```

## Evidence

- Machine-readable artifact: [transport_validation_report.json](/home/codex/projects/TelegramAssistant/artifacts/llm-gateway/transport_validation_report.json)

The validation proves:

- Real `HttpClient` transport is exercised against bounded loopback endpoints instead of in-memory handlers only.
- Retryable primary failure accounting remains visible while fallback success still records the final outcome.
- Bounded `RouteOverride` works within configured provider bounds and rejects out-of-bounds overrides before network calls.
- Gateway readiness validation still fails fast with actionable diagnostics for incomplete timeout/credential/path settings.

## Recommendation

Continue bounded selective rollout under the existing provider policy boundaries only; do not open broad rollout.

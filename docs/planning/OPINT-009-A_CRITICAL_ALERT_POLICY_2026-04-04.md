# OPINT-009-A: Critical Alert Taxonomy and Default Suppression Policy

Date: `2026-04-04`  
Status: done  
Parent: `OPINT-009`  
Slice: `OPINT-009-A`

## Scope

This slice defines the policy foundation only:

- alert source classes
- default suppression policy
- escalation boundaries between suppression, web visibility, and Telegram push

Delivery surfaces (Telegram push flow, web alerts UI, control/analytics widgets) remain in later OPINT-009 slices.

## Alert Source Classes

- `resolution_blocker`
- `runtime_defect`
- `runtime_control_state`
- `materialization_failure`
- `state_transition`

## Default Policy

Policy is workflow-critical by default:

- only critical/blocking workflow conditions are promoted to alert surfaces
- non-critical state churn is suppressed by default
- state-transition-only noise is suppressed

## Escalation Boundaries

- `suppressed`: no web alert, no Telegram push
- `web_only`: critical context visible in web alerts without Telegram push
- `telegram_push_acknowledge`: Telegram push + required acknowledgement + bounded entry into resolution context

## Rule Matrix (Current)

Telegram push (`telegram_push_acknowledge`):

- `critical_clarification_block`
- `critical_blocking_review`
- `critical_blocking_resolution`
- `runtime_degraded_active_workflow`
- `materialization_failure_stops_progression`
- `control_plane_stop_active_scope`

Web-only (`web_only`):

- `critical_workflow_blocker_web_only`

Suppressed (`suppressed`):

- `suppressed_state_churn`
- `suppressed_non_critical_default`

## Testability

Policy test coverage is provided by runtime smoke:

- `dotnet run --project src/TgAssistant.Host/TgAssistant.Host.csproj -- --opint-009-a-smoke`

Smoke verifies:

- all required critical classes are mapped
- Telegram push boundaries require acknowledgement
- web-only boundary is explicit
- non-critical and state-churn scenarios are suppressed by default

Primary implementation:

- `src/TgAssistant.Core/Models/OperatorAlertPolicyModels.cs`
- `src/TgAssistant.Infrastructure/Database/OperatorAlertPolicyService.cs`
- `src/TgAssistant.Host/Launch/Opint009AlertPolicySmokeRunner.cs`

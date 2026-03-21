# Monitoring Minimum Recommendation

Date: 2026-03-21

## Can work continue safely without full monitoring stack now?

Yes, for short incremental maintenance and low-risk changes, provided manual checks are run (app healthcheck, error log scan, DB/Redis container health).

## Minimum next ops step

Enable a minimal always-on visibility loop before deeper sprint work:

1. Keep app compose healthcheck enabled and monitored.
2. Bring up at least `postgres-exporter`, `redis-exporter`, and `prometheus` from existing compose definitions.
3. Add a simple daily operator check (container health + recent error scan) until dashboards/alerts are fully operational.

This is the smallest practical step to reduce blind spots without a full observability rollout.

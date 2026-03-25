ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS pending_sessions_queue BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS reanalysis_backlog BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS quarantine_total BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS quarantine_stuck BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS duplicate_message_business_key_groups BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS duplicate_message_business_key_rows BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS duplicate_message_business_key_row_rate NUMERIC(12,6) NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS processed_without_extraction BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS processed_without_apply_evidence_count BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS processed_without_apply_evidence_rate NUMERIC(12,6) NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS watermark_regression_blocked_1h BIGINT NOT NULL DEFAULT 0;

ALTER TABLE IF EXISTS stage5_metrics_snapshots
    ADD COLUMN IF NOT EXISTS watermark_monotonic_regression_count BIGINT NOT NULL DEFAULT 0;

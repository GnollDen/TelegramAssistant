ALTER TABLE facts ADD COLUMN IF NOT EXISTS decay_class TEXT NOT NULL DEFAULT 'slow';
CREATE INDEX IF NOT EXISTS idx_facts_decay_class ON facts(decay_class, is_current);

CREATE TABLE IF NOT EXISTS intelligence_observations (
    id BIGSERIAL PRIMARY KEY,
    message_id BIGINT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    entity_id UUID REFERENCES entities(id) ON DELETE SET NULL,
    subject_name TEXT NOT NULL,
    observation_type TEXT NOT NULL,
    object_name TEXT,
    value TEXT,
    evidence TEXT,
    confidence REAL NOT NULL DEFAULT 0.5,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_intelligence_observations_message ON intelligence_observations(message_id);
CREATE INDEX IF NOT EXISTS idx_intelligence_observations_entity_time ON intelligence_observations(entity_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_intelligence_observations_type ON intelligence_observations(observation_type, created_at DESC);

CREATE TABLE IF NOT EXISTS intelligence_claims (
    id BIGSERIAL PRIMARY KEY,
    message_id BIGINT NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    entity_id UUID REFERENCES entities(id) ON DELETE SET NULL,
    entity_name TEXT NOT NULL,
    claim_type TEXT NOT NULL,
    category TEXT NOT NULL,
    key TEXT NOT NULL,
    value TEXT NOT NULL,
    evidence TEXT,
    status SMALLINT NOT NULL DEFAULT 2,
    confidence REAL NOT NULL DEFAULT 0.5,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_intelligence_claims_message ON intelligence_claims(message_id);
CREATE INDEX IF NOT EXISTS idx_intelligence_claims_entity_key ON intelligence_claims(entity_id, category, key);
CREATE INDEX IF NOT EXISTS idx_intelligence_claims_type ON intelligence_claims(claim_type, created_at DESC);

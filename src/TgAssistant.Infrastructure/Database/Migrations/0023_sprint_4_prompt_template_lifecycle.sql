ALTER TABLE prompt_templates
    ADD COLUMN IF NOT EXISTS version TEXT NOT NULL DEFAULT 'v1';

ALTER TABLE prompt_templates
    ADD COLUMN IF NOT EXISTS checksum TEXT NOT NULL DEFAULT '';

UPDATE prompt_templates
SET checksum = upper(encode(digest(trim(system_prompt), 'sha256'), 'hex'))
WHERE checksum = '';

-- 0003: name-based duplicate key, Telugu/Devanagari letters in survey numbers, audit log protected
-- from TRUNCATE. Applied migrations are never edited; this file changes 0001's objects forward.

-- Survey numbers may now contain Telugu (U+0C00-U+0C7F) and Devanagari (U+0900-U+097F) letters,
-- kept exactly as printed on the land record.
ALTER TABLE parcels DROP CONSTRAINT parcels_survey_number_check;
ALTER TABLE parcels ADD CONSTRAINT parcels_survey_number_check
    CHECK (survey_number ~ '^[0-9A-Zఀ-౿ऀ-ॿ]+(/[0-9A-Zఀ-౿ऀ-ॿ]+)*$');

-- name_key is the state|district|mandal|village|survey key built from names, kept even when an LGD
-- village code gives the primary parcel_key, so leaving out the code cannot create a duplicate.
ALTER TABLE parcels ADD COLUMN name_key text;
UPDATE parcels
SET name_key = state_code
    || '|' || lower(regexp_replace(btrim(district), '\s+', ' ', 'g'))
    || '|' || lower(regexp_replace(btrim(mandal), '\s+', ' ', 'g'))
    || '|' || lower(regexp_replace(btrim(village), '\s+', ' ', 'g'))
    || '|' || survey_number;
ALTER TABLE parcels ALTER COLUMN name_key SET NOT NULL;
CREATE UNIQUE INDEX parcels_name_key_idx ON parcels (name_key);

-- TRUNCATE bypasses row triggers, so block it with a statement trigger as well.
CREATE TRIGGER audit_log_no_truncate BEFORE TRUNCATE ON audit_log
    FOR EACH STATEMENT EXECUTE FUNCTION audit_log_block_change();

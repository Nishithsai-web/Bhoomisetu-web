-- 0003: name-based duplicate key, Telugu/Devanagari letters in survey numbers, audit log protected
-- from TRUNCATE. Applied migrations are never edited; this file changes 0001's objects forward.
-- (Corrected once before any shared database applied it, after QA found it failed on existing duplicates;
-- see docs/decisions.)

-- Survey numbers may contain Telugu (U+0C00-U+0C7F) and Devanagari (U+0900-U+097F) letters, kept exactly as
-- printed on the land record. The Devanagari danda (। ॥) is punctuation, not a letter, so it is excluded.
ALTER TABLE parcels DROP CONSTRAINT parcels_survey_number_check;
ALTER TABLE parcels ADD CONSTRAINT parcels_survey_number_check
    CHECK (survey_number ~ '^[0-9A-Zఀ-౿ऀ-ॿ]+(/[0-9A-Zఀ-౿ऀ-ॿ]+)*$'
           AND survey_number !~ '[।॥]');

-- Same normalisation as ParcelLocation.Key in C#: trim, collapse any whitespace (including the non-breaking
-- space U+00A0, which PostgreSQL's \s does not match) to one space, lower case.
CREATE FUNCTION parcel_name_part(value text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    SELECT lower(regexp_replace(regexp_replace(value, '^[\s ]+|[\s ]+$', '', 'g'), '[\s ]+', ' ', 'g'));
$$;

-- name_key is the state|district|mandal|village|survey key built from names, kept even when an LGD village
-- code gives the primary parcel_key, so leaving out the code cannot create a duplicate.
ALTER TABLE parcels ADD COLUMN name_key text;
UPDATE parcels
SET name_key = state_code
    || '|' || parcel_name_part(district)
    || '|' || parcel_name_part(mandal)
    || '|' || parcel_name_part(village)
    || '|' || survey_number;

-- Land listed twice before this migration (once with, once without an LGD code) would break the unique
-- index below. Keep the oldest record's key, set the later ones aside with a marker, and list them for a
-- trust reviewer to merge. Nothing is deleted.
CREATE TABLE parcel_duplicate_candidates (
    parcel_id      uuid        PRIMARY KEY REFERENCES parcels (id) ON DELETE CASCADE,
    kept_parcel_id uuid        NOT NULL REFERENCES parcels (id) ON DELETE CASCADE,
    name_key       text        NOT NULL,
    found_at       timestamptz NOT NULL DEFAULT now(),
    resolved_at    timestamptz
);

WITH ranked AS (
    SELECT id, name_key,
           first_value(id) OVER (PARTITION BY name_key ORDER BY created_at, id) AS kept_id,
           row_number()    OVER (PARTITION BY name_key ORDER BY created_at, id) AS position
    FROM parcels
)
INSERT INTO parcel_duplicate_candidates (parcel_id, kept_parcel_id, name_key)
SELECT id, kept_id, name_key FROM ranked WHERE position > 1;

UPDATE parcels p
SET name_key = p.name_key || '|duplicate-of:' || d.kept_parcel_id
FROM parcel_duplicate_candidates d
WHERE d.parcel_id = p.id;

ALTER TABLE parcels ALTER COLUMN name_key SET NOT NULL;
CREATE UNIQUE INDEX parcels_name_key_idx ON parcels (name_key);

-- TRUNCATE bypasses row triggers, so block it with a statement trigger as well.
CREATE TRIGGER audit_log_no_truncate BEFORE TRUNCATE ON audit_log
    FOR EACH STATEMENT EXECUTE FUNCTION audit_log_block_change();

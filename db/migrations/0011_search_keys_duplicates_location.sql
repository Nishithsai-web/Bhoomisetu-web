-- 0011: search in any script, fast search, and who set the map pin.
--
-- 1. Sound-alike keys for village, mandal and district (PlaceKey in the Land module), so "మోకిల", "मोकिला" and
--    "Mokila" find the same land. The API fills them on every save and backfills existing rows at start-up.
-- 2. A stored flag for records set aside as duplicates. Search used NOT LIKE '%|duplicate-of:%', which reads
--    every row; the flag plus a partial index keeps search fast as listings grow.
-- 3. The map pin (parcels.centroid from 0002) gets its source, who set it and when.

ALTER TABLE parcels
    ADD COLUMN village_key  text,
    ADD COLUMN mandal_key   text,
    ADD COLUMN district_key text,
    ADD COLUMN is_hidden_duplicate boolean GENERATED ALWAYS AS (name_key LIKE '%|duplicate-of:%') STORED,
    ADD COLUMN centroid_source text CHECK (centroid_source IN ('lister_gps', 'lister_map', 'reviewer')),
    ADD COLUMN centroid_set_by uuid REFERENCES users (id),
    ADD COLUMN centroid_set_at timestamptz;

CREATE INDEX parcels_village_key_trgm_idx  ON parcels USING gin (village_key gin_trgm_ops);
CREATE INDEX parcels_mandal_key_trgm_idx   ON parcels USING gin (mandal_key gin_trgm_ops);
CREATE INDEX parcels_district_key_trgm_idx ON parcels USING gin (district_key gin_trgm_ops);
CREATE INDEX parcels_visible_created_idx   ON parcels (created_at DESC) WHERE NOT is_hidden_duplicate;

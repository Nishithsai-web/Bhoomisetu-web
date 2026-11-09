-- 0012: the visible-parcels index matches the search order exactly (created_at DESC, id), so listing land is an
-- index scan instead of a full read and sort. Replaces the 0011 index on created_at alone.
DROP INDEX parcels_visible_created_idx;
CREATE INDEX parcels_visible_created_id_idx ON parcels (created_at DESC, id) WHERE NOT is_hidden_duplicate;

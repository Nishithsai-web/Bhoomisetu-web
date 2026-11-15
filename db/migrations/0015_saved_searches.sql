-- 0015: saved searches. A signed-in buyer saves a place search (and optionally a land class). The page shows how many
-- pieces of land were listed there since the buyer last looked. No message is sent yet: there is no SMS or email provider.
CREATE TABLE saved_searches (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id       uuid        NOT NULL REFERENCES users (id),
    place_text    text        NOT NULL CHECK (char_length(place_text) BETWEEN 2 AND 80),
    land_class    text        CHECK (land_class IN ('agricultural', 'non_agricultural', 'converted', 'unknown')),
    created_at    timestamptz NOT NULL DEFAULT now(),
    last_seen_at  timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX saved_searches_unique_idx ON saved_searches (user_id, lower(place_text), coalesce(land_class, 'any'));
CREATE INDEX saved_searches_user_idx ON saved_searches (user_id, created_at DESC);

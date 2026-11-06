-- 0004: corrections to parcel facts, proposed by people involved with the land and decided by a trust reviewer.
-- Facts are never edited silently: the old value, the new value, who asked, why, and who decided are all kept.

CREATE TABLE parcel_change_requests (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    parcel_id      uuid        NOT NULL REFERENCES parcels (id) ON DELETE CASCADE,
    requested_by   uuid        NOT NULL REFERENCES users (id),
    field          text        NOT NULL CHECK (field IN ('land_class', 'frontage_m', 'depth_m', 'facing', 'road_width_m',
                                   'road_type', 'access_right', 'distance_to_main_road_m')),
    current_value  text        CHECK (char_length(current_value) <= 200),
    proposed_value text        NOT NULL CHECK (char_length(proposed_value) BETWEEN 1 AND 200),
    reason         text        NOT NULL CHECK (char_length(reason) BETWEEN 3 AND 1000),
    status         text        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'rejected')),
    decided_by     uuid        REFERENCES users (id),
    decided_at     timestamptz,
    decision_note  text        CHECK (char_length(decision_note) <= 1000),
    created_at     timestamptz NOT NULL DEFAULT now(),
    CHECK ((status = 'pending') = (decided_at IS NULL)),
    CHECK ((status = 'pending') = (decided_by IS NULL)),
    CHECK (decided_by IS NULL OR decided_by <> requested_by)
);

-- One open request per person per fact; a second one would only confuse the reviewer.
CREATE UNIQUE INDEX parcel_change_requests_one_pending_idx
    ON parcel_change_requests (parcel_id, field, requested_by) WHERE status = 'pending';
CREATE INDEX parcel_change_requests_queue_idx ON parcel_change_requests (status, created_at);

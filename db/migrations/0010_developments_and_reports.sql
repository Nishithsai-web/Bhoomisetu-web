-- 0010: nearby developments (metro, highways, road widening, corridors) and "report this listing".
--
-- Developments are entered by trust reviewers from an official source with its date, then linked to land with
-- a distance. They are information only: the site never says a project will raise a price, and a project whose
-- alignment may touch the land is flagged so the buyer checks it with the authority.

CREATE TABLE development_projects (
    id            uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name          text        NOT NULL CHECK (char_length(name) BETWEEN 2 AND 200),
    kind          text        NOT NULL CHECK (kind IN ('metro', 'highway', 'ring_road', 'road_widening', 'new_road', 'railway',
                                                       'airport', 'industrial_corridor', 'it_corridor', 'other')),
    status        text        NOT NULL CHECK (status IN ('proposed_unverified', 'approved', 'under_construction', 'completed', 'cancelled')),
    authority     text        NOT NULL CHECK (char_length(authority) BETWEEN 2 AND 200),
    source_url    text        CHECK (source_url ~ '^https://[^\s]{4,500}$'),
    source_date   date        NOT NULL,
    created_by    uuid        NOT NULL REFERENCES users (id),
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE parcel_development_links (
    parcel_id         uuid         NOT NULL REFERENCES parcels (id) ON DELETE CASCADE,
    project_id        uuid         NOT NULL REFERENCES development_projects (id) ON DELETE CASCADE,
    distance_km       numeric(7,2) CHECK (distance_km >= 0 AND distance_km <= 500),
    may_affect_parcel boolean      NOT NULL DEFAULT false,
    created_by        uuid         NOT NULL REFERENCES users (id),
    created_at        timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (parcel_id, project_id)
);
CREATE INDEX parcel_development_links_project_idx ON parcel_development_links (project_id);

CREATE TABLE listing_reports (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    parcel_id        uuid        NOT NULL REFERENCES parcels (id) ON DELETE CASCADE,
    reporter_id      uuid        NOT NULL REFERENCES users (id),
    reason           text        NOT NULL CHECK (reason IN ('wrong_details', 'already_sold', 'fraud_suspected', 'duplicate', 'ownership_dispute', 'other')),
    details          text        CHECK (char_length(details) <= 1000),
    status           text        NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'resolved', 'dismissed')),
    resolution_note  text        CHECK (char_length(resolution_note) <= 1000),
    resolved_by      uuid        REFERENCES users (id),
    resolved_at      timestamptz,
    created_at       timestamptz NOT NULL DEFAULT now(),
    CHECK ((status = 'open') = (resolved_by IS NULL)),
    CHECK (resolved_by IS NULL OR resolved_by <> reporter_id)
);
-- One open report per person per piece of land.
CREATE UNIQUE INDEX listing_reports_one_open_idx ON listing_reports (parcel_id, reporter_id) WHERE status = 'open';
CREATE INDEX listing_reports_queue_idx ON listing_reports (created_at) WHERE status = 'open';
CREATE INDEX listing_reports_reporter_idx ON listing_reports (reporter_id, created_at DESC);

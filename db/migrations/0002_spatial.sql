-- 0002_spatial: parcel boundaries and road projects. Needs PostGIS 3.x.

CREATE EXTENSION IF NOT EXISTS postgis;

ALTER TABLE parcels
    ADD COLUMN boundary  geography(Polygon, 4326),
    ADD COLUMN centroid  geography(Point, 4326);

CREATE INDEX parcels_boundary_gix ON parcels USING gist (boundary);
CREATE INDEX parcels_centroid_gix ON parcels USING gist (centroid);

-- Area of the drawn boundary in square metres, for comparison with the recorded area.
CREATE FUNCTION parcel_boundary_area_sqm(p parcels) RETURNS numeric LANGUAGE sql STABLE AS $$
    SELECT CASE WHEN p.boundary IS NULL THEN NULL ELSE round(ST_Area(p.boundary)::numeric, 2) END;
$$;

-- Planned road widening and new roads, entered by the data team from authority notices.
CREATE TABLE road_projects (
    id             uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    name           text        NOT NULL CHECK (char_length(name) BETWEEN 1 AND 200),
    authority      text        NOT NULL CHECK (char_length(authority) BETWEEN 1 AND 120),
    kind           text        NOT NULL CHECK (kind IN ('widening', 'new_road', 'ring_road', 'bypass', 'master_plan_road')),
    status         text        NOT NULL CHECK (status IN ('proposed', 'notified', 'under_construction', 'complete')),
    planned_width_m numeric(6, 2) CHECK (planned_width_m > 0),
    source_url     text        NOT NULL CHECK (source_url ~ '^https?://'),
    source_date    date        NOT NULL,
    alignment      geography(Geometry, 4326) NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX road_projects_alignment_gix ON road_projects USING gist (alignment);

-- Parcels whose boundary lies within 50 m of a road project alignment. A prompt to check, not a legal finding.
CREATE VIEW parcel_road_project_flags AS
SELECT p.id AS parcel_id,
       r.id AS road_project_id,
       r.name,
       r.status,
       round(ST_Distance(p.boundary, r.alignment)::numeric, 1) AS distance_m
FROM parcels p
JOIN road_projects r ON ST_DWithin(p.boundary, r.alignment, 50)
WHERE p.boundary IS NOT NULL;

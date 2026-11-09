-- 0014: the public position of a home for rent is a grid point (0.001 degree, about 110 m), computed by the
-- database. Every public query (map area, distances, nearby) uses only this point, so the exact position cannot
-- be worked out from distances or by shrinking the map area. The exact point stays for the lister.
ALTER TABLE rental_listings
    ADD COLUMN public_point geography(Point, 4326)
        GENERATED ALWAYS AS (ST_SnapToGrid(centroid::geometry, 0.001)::geography) STORED;
CREATE INDEX rental_listings_public_point_gix ON rental_listings USING gist (public_point) WHERE status = 'active';
DROP INDEX rental_listings_centroid_gix;

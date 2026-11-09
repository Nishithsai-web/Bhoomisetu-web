-- 0013: homes for rent. One row per home offered for rent, always with a map point, so renters can find homes
-- on the map and compare rent per square foot with homes nearby. Contact follows the same rules as land:
-- signed-in renters, listers with an active subscription, limited reveals per day.
CREATE TABLE rental_listings (
    id                         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    lister_user_id             uuid        NOT NULL REFERENCES users (id),
    lister_kind                text        NOT NULL CHECK (lister_kind IN ('owner', 'agent')),
    property_type              text        NOT NULL CHECK (property_type IN ('apartment', 'independent_house', 'villa', 'portion', 'room')),
    bedrooms                   smallint    NOT NULL CHECK (bedrooms BETWEEN 0 AND 10),
    area_sqft                  integer     NOT NULL CHECK (area_sqft BETWEEN 50 AND 20000),
    rent_rupees                integer     NOT NULL CHECK (rent_rupees BETWEEN 500 AND 10000000),
    deposit_rupees             integer     NOT NULL CHECK (deposit_rupees BETWEEN 0 AND 100000000),
    furnishing                 text        NOT NULL CHECK (furnishing IN ('unfurnished', 'semi', 'full')),
    preferred_tenants          text        NOT NULL CHECK (preferred_tenants IN ('any', 'family', 'bachelors')),
    parking                    boolean     NOT NULL DEFAULT false,
    available_from             date        NOT NULL,
    locality                   text        NOT NULL CHECK (char_length(locality) BETWEEN 2 AND 120),
    mandal                     text        NOT NULL CHECK (char_length(mandal) BETWEEN 2 AND 80),
    district                   text        NOT NULL CHECK (char_length(district) BETWEEN 2 AND 80),
    centroid                   geography(Point, 4326) NOT NULL,
    status                     text        NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'rented', 'withdrawn')),
    availability_confirmed_at  timestamptz NOT NULL DEFAULT now(),
    created_at                 timestamptz NOT NULL DEFAULT now(),
    updated_at                 timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX rental_listings_centroid_gix ON rental_listings USING gist (centroid) WHERE status = 'active';
CREATE INDEX rental_listings_lister_idx ON rental_listings (lister_user_id, created_at DESC);

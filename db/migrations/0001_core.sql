-- 0001_core: accounts, OTP, parcels, offers, checks and audit.
-- Runs on PostgreSQL 16+ without PostGIS; spatial columns are added in 0002_spatial.

CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ---------------------------------------------------------------- users
CREATE TABLE users (
    id                 uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    phone_e164         text        NOT NULL UNIQUE CHECK (phone_e164 ~ '^\+91[6-9][0-9]{9}$'),
    phone_verified_at  timestamptz,
    display_name       text        CHECK (char_length(display_name) <= 120),
    preferred_locale   text        NOT NULL DEFAULT 'en' CHECK (preferred_locale IN ('en', 'te', 'hi')),
    is_trust_reviewer  boolean     NOT NULL DEFAULT false,
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now()
);

-- Checks a person has passed. A row exists only once the check succeeded.
CREATE TABLE user_checks (
    user_id     uuid        NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    check_type  text        NOT NULL CHECK (check_type IN ('phone_otp', 'identity', 'rera_agent')),
    verified_at timestamptz NOT NULL,
    reference   text        CHECK (char_length(reference) <= 200), -- e.g. RERA agent registration number
    PRIMARY KEY (user_id, check_type)
);

-- ---------------------------------------------------------------- OTP
-- Only an HMAC of the code is stored, never the code itself.
CREATE TABLE otp_challenges (
    id           uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    phone_e164   text        NOT NULL CHECK (phone_e164 ~ '^\+91[6-9][0-9]{9}$'),
    code_hash    bytea       NOT NULL CHECK (octet_length(code_hash) = 32),
    created_at   timestamptz NOT NULL DEFAULT now(),
    expires_at   timestamptz NOT NULL,
    attempts     integer     NOT NULL DEFAULT 0 CHECK (attempts >= 0),
    consumed_at  timestamptz,
    CHECK (expires_at > created_at)
);
CREATE INDEX otp_challenges_phone_created_idx ON otp_challenges (phone_e164, created_at DESC);

-- ---------------------------------------------------------------- parcels
-- One row per piece of land. Many listings and agent offers point at one parcel.
CREATE TABLE parcels (
    id                        uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    parcel_key                text        NOT NULL UNIQUE,
    state_code                text        NOT NULL CHECK (state_code ~ '^[A-Z]{2}$'),
    district                  text        NOT NULL CHECK (char_length(district) BETWEEN 1 AND 100),
    mandal                    text        NOT NULL CHECK (char_length(mandal) BETWEEN 1 AND 100),
    village                   text        NOT NULL CHECK (char_length(village) BETWEEN 1 AND 100),
    lgd_village_code          text        CHECK (lgd_village_code ~ '^[0-9]{1,10}$'),
    survey_number_raw         text        NOT NULL CHECK (char_length(survey_number_raw) BETWEEN 1 AND 60),
    survey_number             text        NOT NULL CHECK (survey_number ~ '^[0-9A-Z]+(/[0-9A-Z]+)*$'),
    land_class                text        NOT NULL CHECK (land_class IN ('agricultural', 'non_agricultural', 'converted', 'unknown')),
    recorded_area_sqm         numeric(14, 4) NOT NULL CHECK (recorded_area_sqm > 0),
    entered_area_value        numeric(14, 4) NOT NULL CHECK (entered_area_value > 0),
    entered_area_unit         text        NOT NULL CHECK (entered_area_unit IN
                                  ('square_metre', 'square_foot', 'square_yard', 'gunta', 'acre', 'cent', 'hectare', 'ankanam')),
    area_source               text        NOT NULL CHECK (area_source IN ('record', 'deed', 'survey', 'lister')),
    frontage_m                numeric(8, 2) CHECK (frontage_m > 0),
    depth_m                   numeric(8, 2) CHECK (depth_m > 0),
    facing                    text        CHECK (facing IN ('north', 'east', 'south', 'west', 'north_east', 'north_west', 'south_east', 'south_west')),
    road_width_m              numeric(6, 2) CHECK (road_width_m > 0),
    road_width_source         text        CHECK (road_width_source IN ('approved_layout', 'measured_on_map', 'lister')),
    road_type                 text        CHECK (road_type IN ('national_highway', 'state_highway', 'main_road', 'layout_road', 'village_road', 'cart_track')),
    access_right              text        CHECK (access_right IN ('direct_public_road', 'through_private_land_with_right', 'through_private_land_no_record', 'unknown')),
    distance_to_main_road_m   integer     CHECK (distance_to_main_road_m >= 0),
    created_by                uuid        NOT NULL REFERENCES users (id),
    created_at                timestamptz NOT NULL DEFAULT now(),
    updated_at                timestamptz NOT NULL DEFAULT now(),
    CHECK ((road_width_m IS NULL) = (road_width_source IS NULL))
);
CREATE INDEX parcels_village_trgm_idx ON parcels USING gin (village gin_trgm_ops);
CREATE INDEX parcels_location_idx ON parcels (state_code, district, mandal);

-- Who stands behind a parcel. The owner of record is set only by a trust reviewer after document review;
-- authorised agents are confirmed by that owner.
CREATE TABLE parcel_parties (
    parcel_id      uuid        NOT NULL REFERENCES parcels (id) ON DELETE CASCADE,
    user_id        uuid        NOT NULL REFERENCES users (id),
    role           text        NOT NULL CHECK (role IN ('owner_of_record', 'authorised_agent')),
    confirmed_at   timestamptz NOT NULL DEFAULT now(),
    confirmed_by   uuid        NOT NULL REFERENCES users (id),
    PRIMARY KEY (parcel_id, user_id, role)
);
-- At most one owner of record per parcel for now (joint owners come with the documents module).
CREATE UNIQUE INDEX parcel_parties_one_owner_idx ON parcel_parties (parcel_id) WHERE role = 'owner_of_record';

-- ---------------------------------------------------------------- offers
CREATE TABLE offers (
    id                         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    parcel_id                  uuid        NOT NULL REFERENCES parcels (id) ON DELETE CASCADE,
    lister_user_id             uuid        NOT NULL REFERENCES users (id),
    lister_kind                text        NOT NULL CHECK (lister_kind IN ('owner', 'agent')),
    asking_price_paise         bigint      NOT NULL CHECK (asking_price_paise > 0),
    buyer_brokerage_percent    numeric(5, 2) CHECK (buyer_brokerage_percent >= 0 AND buyer_brokerage_percent <= 10),
    brokerage_terms            text        CHECK (char_length(brokerage_terms) <= 500),
    availability_confirmed_at  timestamptz NOT NULL DEFAULT now(),
    status                     text        NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'withdrawn', 'sold')),
    created_at                 timestamptz NOT NULL DEFAULT now(),
    updated_at                 timestamptz NOT NULL DEFAULT now(),
    -- An owner offer never carries brokerage.
    CHECK (lister_kind = 'agent' OR coalesce(buyer_brokerage_percent, 0) = 0)
);
-- One active offer per lister per parcel; a new price replaces the old offer.
CREATE UNIQUE INDEX offers_one_active_per_lister_idx ON offers (parcel_id, lister_user_id) WHERE status = 'active';
CREATE INDEX offers_parcel_active_idx ON offers (parcel_id) WHERE status = 'active';

-- Response statistics, maintained by a background job from lead events (later phase).
CREATE TABLE lister_stats (
    user_id                  uuid        PRIMARY KEY REFERENCES users (id) ON DELETE CASCADE,
    median_response_minutes  integer     CHECK (median_response_minutes >= 0),
    sample_size              integer     NOT NULL DEFAULT 0 CHECK (sample_size >= 0),
    updated_at               timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------- audit
CREATE TABLE audit_log (
    id             bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at    timestamptz NOT NULL DEFAULT now(),
    actor_user_id  uuid        REFERENCES users (id),
    action         text        NOT NULL CHECK (char_length(action) BETWEEN 1 AND 80),
    entity_type    text        NOT NULL CHECK (char_length(entity_type) BETWEEN 1 AND 40),
    entity_id      text        NOT NULL,
    details        jsonb       NOT NULL DEFAULT '{}'::jsonb
);
CREATE INDEX audit_log_entity_idx ON audit_log (entity_type, entity_id, occurred_at DESC);

-- The audit log is append-only.
CREATE FUNCTION audit_log_block_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'audit_log is append-only';
END;
$$;
CREATE TRIGGER audit_log_no_update BEFORE UPDATE OR DELETE ON audit_log
    FOR EACH ROW EXECUTE FUNCTION audit_log_block_change();

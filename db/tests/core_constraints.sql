-- Constraint tests for the core schema (0001 + 0003). Run against a fresh database after all migrations:
--   psql -v ON_ERROR_STOP=1 -f db/tests/core_constraints.sql
-- Every block raises an exception (and stops the script) if a rule is not enforced.
\set QUIET on
BEGIN;

-- Passes only when the statement fails with the expected SQLSTATE, so a typo or an unrelated error
-- (foreign key, syntax) cannot make a test pass by accident.
--   23514 check_violation · 23505 unique_violation · 23502 not_null_violation · P0001 raised by a trigger
CREATE FUNCTION pg_temp.expect_failure(sql text, label text, expected_state text) RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    actual text;
BEGIN
    BEGIN
        EXECUTE sql;
    EXCEPTION WHEN others THEN
        GET STACKED DIAGNOSTICS actual = RETURNED_SQLSTATE;
        IF actual <> expected_state THEN
            RAISE EXCEPTION 'FAIL % : expected SQLSTATE %, got % (%)', label, expected_state, actual, SQLERRM;
        END IF;
        RAISE NOTICE 'ok   %', label;
        RETURN;
    END;
    RAISE EXCEPTION 'FAIL % : statement was accepted', label;
END;
$$;

\o /dev/null
INSERT INTO users (id, phone_e164) VALUES
    ('00000000-0000-0000-0000-000000000001', '+919876543210'),
    ('00000000-0000-0000-0000-000000000002', '+918765432109'),
    ('00000000-0000-0000-0000-000000000003', '+917654321098');

SELECT pg_temp.expect_failure($$INSERT INTO users (phone_e164) VALUES ('+915876543210')$$, 'mobile must start with 6-9', '23514');
SELECT pg_temp.expect_failure($$INSERT INTO users (phone_e164) VALUES ('9876543210')$$, 'phone must be E.164 with +91', '23514');
SELECT pg_temp.expect_failure($$INSERT INTO users (phone_e164) VALUES ('+919876543210')$$, 'phone is unique', '23505');
SELECT pg_temp.expect_failure($$INSERT INTO users (phone_e164, preferred_locale) VALUES ('+919999999999', 'fr')$$, 'locale limited to en, te, hi', '23514');

INSERT INTO parcels (id, parcel_key, name_key, state_code, district, mandal, village, survey_number_raw, survey_number,
                     land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by)
VALUES ('10000000-0000-0000-0000-000000000001', 'TG|rangareddy|shankarpalle|mokila|123/A/1', 'TG|rangareddy|shankarpalle|mokila|123/A/1', 'TG', 'Rangareddy',
        'Shankarpalle', 'Mokila', '123/a/1', '123/A/1', 'agricultural', 4046.8564, 1, 'acre', 'lister',
        '00000000-0000-0000-0000-000000000001');

SELECT pg_temp.expect_failure($$INSERT INTO parcels (parcel_key, name_key, state_code, district, mandal, village, survey_number_raw, survey_number,
    land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by)
    VALUES ('TG|rangareddy|shankarpalle|mokila|123/A/1', 'other-name-key', 'TG', 'Rangareddy', 'Shankarpalle', 'Mokila', '123/A/1', '123/A/1',
    'agricultural', 10, 10, 'square_metre', 'lister', '00000000-0000-0000-0000-000000000001')$$, 'parcel key is unique (duplicate land)', '23505');
SELECT pg_temp.expect_failure($$UPDATE parcels SET survey_number = '123 A' WHERE id = '10000000-0000-0000-0000-000000000001'$$, 'survey number must be normalised', '23514');
SELECT pg_temp.expect_failure($$UPDATE parcels SET recorded_area_sqm = 0 WHERE id = '10000000-0000-0000-0000-000000000001'$$, 'area must be positive', '23514');
SELECT pg_temp.expect_failure($$UPDATE parcels SET road_width_m = 9 WHERE id = '10000000-0000-0000-0000-000000000001'$$, 'road width needs its source', '23514');
SELECT pg_temp.expect_failure($$UPDATE parcels SET entered_area_unit = 'bigha' WHERE id = '10000000-0000-0000-0000-000000000001'$$, 'unsupported unit rejected', '23514');

SELECT pg_temp.expect_failure($$INSERT INTO parcels (parcel_key, name_key, state_code, district, mandal, village, survey_number_raw, survey_number,
    land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by)
    VALUES ('TG|lgd:574123|123/A/1', 'TG|rangareddy|shankarpalle|mokila|123/A/1', 'TG', 'Rangareddy', 'Shankarpalle', 'Mokila', '123/A/1', '123/A/1',
    'agricultural', 10, 10, 'square_metre', 'lister', '00000000-0000-0000-0000-000000000001')$$, 'name key is unique (same land with an LGD code)', '23505');
SELECT pg_temp.expect_failure($$INSERT INTO parcels (parcel_key, state_code, district, mandal, village, survey_number_raw, survey_number,
    land_class, recorded_area_sqm, entered_area_value, entered_area_unit, area_source, created_by)
    VALUES ('k-no-name', 'TG', 'd', 'm', 'v', '1', '1', 'agricultural', 10, 10, 'square_metre', 'lister', '00000000-0000-0000-0000-000000000001')$$,
    'name key is required', '23502');
UPDATE parcels SET survey_number = '45/ఆ' WHERE id = '10000000-0000-0000-0000-000000000001';
UPDATE parcels SET survey_number = '7/क' WHERE id = '10000000-0000-0000-0000-000000000001';
UPDATE parcels SET survey_number = '123/A/1' WHERE id = '10000000-0000-0000-0000-000000000001';

INSERT INTO parcel_parties (parcel_id, user_id, role, confirmed_by)
VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001', 'owner_of_record', '00000000-0000-0000-0000-000000000003');
SELECT pg_temp.expect_failure($$INSERT INTO parcel_parties (parcel_id, user_id, role, confirmed_by)
    VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 'owner_of_record', '00000000-0000-0000-0000-000000000003')$$,
    'only one owner of record', '23505');

INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise, buyer_brokerage_percent)
VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 'agent', 500000000, 1.5);
SELECT pg_temp.expect_failure($$INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise)
    VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 'agent', 400000000)$$,
    'one active offer per lister per parcel', '23505');
SELECT pg_temp.expect_failure($$INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise, buyer_brokerage_percent)
    VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000001', 'owner', 400000000, 1)$$,
    'owner offers carry no brokerage', '23514');
SELECT pg_temp.expect_failure($$INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise)
    VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000003', 'agent', 0)$$, 'price must be positive', '23514');

-- A withdrawn offer frees the slot for a new one.
UPDATE offers SET status = 'withdrawn' WHERE lister_user_id = '00000000-0000-0000-0000-000000000002';
INSERT INTO offers (parcel_id, lister_user_id, lister_kind, asking_price_paise)
VALUES ('10000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 'agent', 480000000);

SELECT pg_temp.expect_failure($$INSERT INTO otp_challenges (phone_e164, code_hash, expires_at) VALUES ('+919876543210', '\x00', now() + interval '5 min')$$,
    'OTP hash must be 32 bytes', '23514');
SELECT pg_temp.expect_failure($$INSERT INTO otp_challenges (phone_e164, code_hash, expires_at) VALUES ('+919876543210', decode(repeat('00', 32), 'hex'), now() - interval '1 min')$$,
    'OTP expiry after creation', '23514');

INSERT INTO audit_log (action, entity_type, entity_id) VALUES ('test', 'parcel', '1');
SELECT pg_temp.expect_failure($$UPDATE audit_log SET action = 'x'$$, 'audit log cannot be updated', 'P0001');
SELECT pg_temp.expect_failure($$DELETE FROM audit_log$$, 'audit log cannot be deleted', 'P0001');
SELECT pg_temp.expect_failure($$TRUNCATE audit_log$$, 'audit log cannot be truncated', 'P0001');

\o
\echo 'core constraint tests passed'
ROLLBACK;

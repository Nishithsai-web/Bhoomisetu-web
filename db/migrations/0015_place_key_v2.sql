-- 0015: the sound-alike place key now also leaves out the first syllable's vowel ("Kurnool" and కర్నూలు match).
-- Keys are made by the API, not the database, so the old ones are cleared here and the start-up backfill makes
-- them again in batches. Until it finishes, search still finds land by its written name.
UPDATE parcels SET village_key = NULL, mandal_key = NULL, district_key = NULL WHERE village_key IS NOT NULL;

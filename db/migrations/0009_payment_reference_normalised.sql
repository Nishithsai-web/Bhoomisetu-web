-- 0009: stricter "one payment, one period". 0008 compared references case-insensitively but let "UTR 123" and
-- "UTR123" through as different, and exempted free periods so a receipt could be reused for a free period.
-- Now only letters and digits are compared (Telugu and Devanagari digits as 0-9), for every period.
--
-- Rows that 0008 allowed but this rule calls the same keep the first one; later ones are cancelled and get a
-- visible marker ("DUPLICATE-<id>"), so the migration never fails on existing data, a repeated receipt grants no
-- extra access, and a reviewer can find them. (Edited before any shared database applied it: see decision 36.)
DROP INDEX subscriptions_payment_reference_once_idx;

UPDATE subscriptions s
SET payment_reference = left(s.payment_reference, 50) || ' DUPLICATE-' || replace(s.id::text, '-', ''),
    cancelled_at = COALESCE(s.cancelled_at, now())
WHERE EXISTS (
    SELECT 1 FROM subscriptions e
    WHERE regexp_replace(translate(lower(e.payment_reference), '౦౧౨౩౪౫౬౭౮౯०१२३४५६७८९', '01234567890123456789'), '[^a-z0-9]', '', 'g')
        = regexp_replace(translate(lower(s.payment_reference), '౦౧౨౩౪౫౬౭౮౯०१२३४५६७८९', '01234567890123456789'), '[^a-z0-9]', '', 'g')
      AND (e.created_at, e.id) < (s.created_at, s.id));

CREATE UNIQUE INDEX subscriptions_payment_reference_once_idx ON subscriptions (
    regexp_replace(translate(lower(payment_reference), '౦౧౨౩౪౫౬౭౮౯०१२३४५६७८९', '01234567890123456789'), '[^a-z0-9]', '', 'g'));

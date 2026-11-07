-- 0009: stricter "one payment, one period". 0008 compared references case-insensitively but let "UTR 123" and
-- "UTR123" through as different, and exempted free periods so a receipt could be reused for a free period.
-- Now letters and digits only are compared, for every period.
DROP INDEX subscriptions_payment_reference_once_idx;
CREATE UNIQUE INDEX subscriptions_payment_reference_once_idx
    ON subscriptions (regexp_replace(lower(payment_reference), '[^a-z0-9]', '', 'g'));

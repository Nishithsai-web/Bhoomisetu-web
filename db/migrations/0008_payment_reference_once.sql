-- 0008: a payment reference (UPI transaction ID or receipt number) can start only one paid period, so a
-- double-submitted form or a reused receipt cannot create a period nobody paid for. Free periods (amount 0) are exempt.
CREATE UNIQUE INDEX subscriptions_payment_reference_once_idx ON subscriptions (lower(payment_reference)) WHERE amount_paise > 0;

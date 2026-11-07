-- 0005: who asked for each OTP (so one person's requests cannot use up another's quota) and a token
-- version per user (so signing out, or removing a reviewer, ends existing sessions on the server).

-- Rate-limit key of the requester (IPv4 address or IPv6 /64). Kept only as long as the challenge row.
ALTER TABLE otp_challenges ADD COLUMN requester_key text CHECK (char_length(requester_key) <= 64);
CREATE INDEX otp_challenges_phone_requester_idx ON otp_challenges (phone_e164, requester_key, created_at DESC);

-- Tokens carry this number; the API rejects tokens whose number is lower than the user's current one.
ALTER TABLE users ADD COLUMN token_version integer NOT NULL DEFAULT 0 CHECK (token_version >= 0);

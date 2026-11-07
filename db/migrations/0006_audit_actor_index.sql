-- 0006: index for "how many times did this person do X recently" checks on the audit log, such as the
-- per-reviewer limit on account lookups. Without it each check scans the whole (append-only) table.
CREATE INDEX audit_log_actor_action_idx ON audit_log (actor_user_id, action, occurred_at DESC);

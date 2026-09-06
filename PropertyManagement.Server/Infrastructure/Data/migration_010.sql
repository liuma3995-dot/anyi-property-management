-- ============================================================
-- migration_010.sql（M5 CHG-INF-02）
-- t_owner_property_rel 增强：份额/到期日/关系状态（PG-INF-03）
-- 版本：010（2026-09-03）
-- ============================================================
ALTER TABLE t_owner_property_rel ADD COLUMN share NUMERIC NOT NULL DEFAULT 0;
ALTER TABLE t_owner_property_rel ADD COLUMN expire_at TEXT;
ALTER TABLE t_owner_property_rel ADD COLUMN rel_status INTEGER NOT NULL DEFAULT 0;

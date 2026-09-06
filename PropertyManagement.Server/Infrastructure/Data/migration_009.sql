-- ============================================================
-- migration_009.sql（M5 CHG-INF-01）
-- t_owner 增强：证件类型/常住地址/紧急联系人/入住日期（PG-INF-02）
-- 版本：009（2026-09-03）
-- ============================================================
ALTER TABLE t_owner ADD COLUMN id_card_type INTEGER NOT NULL DEFAULT 0;
ALTER TABLE t_owner ADD COLUMN resident_address TEXT;
ALTER TABLE t_owner ADD COLUMN emergency_contact_name TEXT;
ALTER TABLE t_owner ADD COLUMN emergency_contact_phone TEXT;
ALTER TABLE t_owner ADD COLUMN check_in_date TEXT;

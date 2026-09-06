-- ============================================================
-- migration_012.sql（M5 CHG-INF-04）
-- t_base_change_log 增强：变更字段/旧值/新值/操作人/渠道（PG-INF-02 变更历史 6 列）
-- 版本：012（2026-09-03）
-- ============================================================
ALTER TABLE t_base_change_log ADD COLUMN field_name TEXT;
ALTER TABLE t_base_change_log ADD COLUMN old_value TEXT;
ALTER TABLE t_base_change_log ADD COLUMN new_value TEXT;
ALTER TABLE t_base_change_log ADD COLUMN operator TEXT;
ALTER TABLE t_base_change_log ADD COLUMN channel TEXT;

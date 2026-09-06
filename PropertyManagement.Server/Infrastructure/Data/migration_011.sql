-- ============================================================
-- migration_011.sql（M5 CHG-INF-03）
-- t_parking_space 增强：区域/状态/月租金/租期至（PG-INF-04）
-- 版本：011（2026-09-03）
-- ============================================================
ALTER TABLE t_parking_space ADD COLUMN area TEXT;
ALTER TABLE t_parking_space ADD COLUMN status INTEGER NOT NULL DEFAULT 2;
ALTER TABLE t_parking_space ADD COLUMN monthly_rent NUMERIC;
ALTER TABLE t_parking_space ADD COLUMN rent_to TEXT;

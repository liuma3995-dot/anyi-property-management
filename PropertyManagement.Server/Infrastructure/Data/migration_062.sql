-- ============================================================
-- migration_062.sql（v2.1.0：车位时间周期）
-- 背景：车位新增/编辑需要可选起始日期、终止日期，并在列表展示。
-- 处置：新增独立 period_start / period_end；不回填历史 rent_to。
-- 版本：062（2026-10-18）
-- ============================================================

ALTER TABLE t_parking_space ADD COLUMN period_start TEXT;
ALTER TABLE t_parking_space ADD COLUMN period_end TEXT;

-- ============================================================
-- migration_007.sql（M4 CHG-M4-16）
-- t_bill_generate_log 增加 del_flag：批次软删（草稿/发布失败批次删除后保留轨迹，
-- 账单明细级联软删，已发布/部分缴/已缴批次禁止删除）
-- 版本：007（2026-09-02）
-- ============================================================
ALTER TABLE t_bill_generate_log ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;
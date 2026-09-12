-- ============================================================
-- migration_007.sql（M4 CHG-M4-16）
-- t_bill_generate_log del_flag：批次软删（草稿/发布失败批次删除后保留轨迹，
-- 账单明细级联软删，已发布/部分缴/已缴批次禁止删除）
-- 版本：007（2026-09-02）
-- 全新安装说明（2026-09-06 全新重放修复）：schema.sql 的 t_bill_generate_log
-- 已内置 del_flag 列，本迁移对全新库为幂等空操作；存量库（v6 前升级）历史上
-- 已应用过该列，此处不再 ALTER（重复执行会报 duplicate column）。
-- ============================================================
UPDATE t_bill_generate_log SET del_flag = 0 WHERE del_flag IS NULL;

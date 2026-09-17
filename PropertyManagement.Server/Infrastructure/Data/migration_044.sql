-- ============================================================
-- migration_044.sql（v1.1.0 第 21 轮：发布失败批次「一键重推」闭环）
-- 1) t_bill_generate_log 新增 retried_at / retried_count：
--    记录「失败对象重推」的时间与成功户数；重推后失败清单收敛为「仍未成功」的对象，
--    全部成功时 fail=0 且 retried_at 非空 → 批次状态由「发布失败」转为「已重推」（不再计入失败卡片）。
-- 2) 清单与状态口径：失败卡片只统计「仍存在失败对象」的批次，重推闭环后自动出列。
-- 版本：044（2026-09-17）
-- ============================================================

ALTER TABLE t_bill_generate_log ADD COLUMN retried_at TEXT;
ALTER TABLE t_bill_generate_log ADD COLUMN retried_count INTEGER NOT NULL DEFAULT 0;

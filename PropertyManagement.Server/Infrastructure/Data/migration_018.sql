-- EQP 设备台账（PG-EQP-01 品牌/型号）：t_device 补 brand_model。
-- 版本：018（2026-09-06）
ALTER TABLE t_device ADD COLUMN brand_model TEXT;

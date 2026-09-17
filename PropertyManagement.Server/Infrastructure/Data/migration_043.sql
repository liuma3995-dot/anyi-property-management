-- ============================================================
-- migration_043.sql（v1.1.0 第 18 轮：自定义缴费对象出账 ＋ 同项目重复出账放行）
-- 1) t_bill 新增 payer_name：自定义缴费对象（租户/广告商/外部单位）手工填写的缴费人名称，
--    与 property_id / parking_id / owner_id 互斥（三者均为 NULL 时按 payer_name 展示与收款）。
-- 2) 移除「同项目 + 同周期 + 同对象」唯一索引：负责人裁定 —— 同对象同周期同项目**允许重复出账**
--    （如补开、重开账单），重复出账不再拦截，也不写失败清单。
-- 3) 新增 payer_name 索引，供收款登记按「自定义缴费对象名称」聚合与筛选。
-- 版本：043（2026-09-17）
-- ============================================================

ALTER TABLE t_bill ADD COLUMN payer_name TEXT;

DROP INDEX IF EXISTS ux_bill_property_cycle;
DROP INDEX IF EXISTS ux_bill_parking_cycle;
DROP INDEX IF EXISTS ux_bill_owner_cycle;

CREATE INDEX IF NOT EXISTS ix_bill_payer_name ON t_bill (payer_name, del_flag);

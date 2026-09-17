-- ============================================================
-- migration_039.sql（v1.1.0 第 10 轮：缴费对象新增「业主」维度）
-- 1) t_bill 新增 owner_id：业主直缴账单（办卡费/清理费/维修费等面向业主本人的收费项目）
--    口径：与 property_id / parking_id 三者互斥，一张账单只挂一个缴费对象。
-- 2) 部分唯一索引：同项目 + 同周期 + 同业主 不得重复（BR-FIN-01 口径扩展到业主维度）
-- 3) 存量数据不变（现有账单 owner_id 为 NULL）
-- 版本：039（2026-09-17）
-- ============================================================

ALTER TABLE t_bill ADD COLUMN owner_id INTEGER;

CREATE UNIQUE INDEX IF NOT EXISTS ux_bill_owner_cycle
    ON t_bill (charge_item_id, cycle_id, owner_id)
    WHERE owner_id IS NOT NULL AND del_flag = 0;

CREATE INDEX IF NOT EXISTS ix_bill_owner ON t_bill (owner_id, del_flag);

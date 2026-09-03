-- ============================================================
-- migration_003.sql（M4 CHG-M4-09）
-- t_charge_item 增加 object_type：收费项目适用对象（0房产/1车位），
-- 账单生成"全部对象"按此取候选，避免物业费生成到车位、停车费生成到房产。
-- 版本：003（2026-09-01）
-- ============================================================
ALTER TABLE t_charge_item ADD COLUMN object_type INTEGER NOT NULL DEFAULT 0;
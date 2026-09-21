-- ============================================================
-- migration_050.sql（v1.1.2：存量收费项目搬迁为「收费标准 + 规格」）
-- 背景：新增的收费项目表单不再直接维护价格，价格改由「收费标准 + 规格」承载；
--       存量收费项目必须可继续出账，因此逐条搬迁。
-- 处置：每条 t_charge_item（standard_id 为空者）→
--       1 条 t_charge_standard（沿用原 id，保证映射唯一且可重复执行）
--       + 1 条 t_charge_standard_spec（规格名「统一价」、is_fallback = 1、继承单价 / 公式 / 单位 / 周期）
--       + 回填 t_charge_item.standard_id。
-- 说明：全程不删除、不修改既有列；按 standard_id IS NULL 过滤，可重复执行；
--       历史账单金额已固化，搬迁不影响任何已出账数据。
-- 版本：050（2026-09-19）
-- ============================================================

INSERT INTO t_charge_standard (id, name, category, remark, status, del_flag, created_at, updated_at)
SELECT i.id, i.name, COALESCE(i.category, ''), '由存量收费项目自动搬迁（migration_050）', 0, 0,
       COALESCE(i.created_at, datetime('now','localtime')), COALESCE(i.updated_at, datetime('now','localtime'))
FROM t_charge_item i
WHERE i.standard_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM t_charge_standard s WHERE s.id = i.id);

INSERT INTO t_charge_standard_spec
    (id, standard_id, spec_name, is_fallback, unit_price, formula, price_unit, cycle_name, status, del_flag, remark, created_at, updated_at)
SELECT i.id, i.id, '统一价', 1, COALESCE(i.unit_price, 0), i.formula, i.price_unit, i.cycle_name, 0, 0,
       '由存量收费项目自动搬迁（migration_050）',
       COALESCE(i.created_at, datetime('now','localtime')), COALESCE(i.updated_at, datetime('now','localtime'))
FROM t_charge_item i
WHERE i.standard_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM t_charge_standard_spec s WHERE s.id = i.id);

UPDATE t_charge_item
SET standard_id = id
WHERE standard_id IS NULL;

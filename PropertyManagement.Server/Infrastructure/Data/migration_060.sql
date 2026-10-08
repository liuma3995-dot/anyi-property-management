-- ============================================================
-- migration_060.sql（v1.4.0：无关联账单的调整记录可删除）
-- 背景（负责人 2026-10-08 反馈）：在「财务调整」页签登记的无关联账单补收/冲正记录
--       （t_payment_refund.bill_id IS NULL）没有任何删除通道 ——
--       该表自 migration_046 重建起就没有 del_flag，也不在「一键清理残余数据」的
--       悬空清理范围（那条规则只覆盖 bill_id NOT IN t_bill），
--       于是记录永远留在退款列表 / 财务报表 / 收支明细流水里，成为噪点。
-- 处置（CHG-v1.4.0-01，负责人 2026-10-08 裁定 A）：
--       t_payment_refund 增加 del_flag；删除 = 软删留痕 + 下游读数过滤；
--       留痕行随「系统设置 → 备份与恢复 → 一键清理残余数据」物理回收
--       （清理器按表名遍历有 del_flag 的表，无需额外登记）。
-- 幂等：执行由 schema_version 记录保证仅一次；后句 CREATE INDEX 可重复执行。
-- 版本：060（2026-10-08）
-- ============================================================

ALTER TABLE t_payment_refund ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_payment_refund_del_flag ON t_payment_refund (del_flag);

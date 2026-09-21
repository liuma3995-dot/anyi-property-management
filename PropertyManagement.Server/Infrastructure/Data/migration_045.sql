-- ============================================================
-- migration_045.sql（v1.1.2：导入覆盖计数 / 欠费台账移出 / 收费项目计价公式）
-- 1) t_import_log 新增 updated_count：导入的「新增」与「覆盖」分开统计，
--    批次列表与导入结果据此提示「新增 N 条 / 覆盖 M 条」。
-- 2) t_charge_item 新增 formula：用户自定义计价公式。
--    为空 = 沿用内置计价方式口径（按建筑面积 = 单价 × 面积，其余 = 单价），存量零回归。
-- 3) 新增 t_arrear_dismiss：欠费台账「移出台账」记录。
--    负责人 2026-09-19 裁定 —— 台账内删除只影响台账可见性，不再软删 t_bill，
--    因而不再连带影响账单工作台 / 收款登记 / 退款·减免·调整 / 财务报表 / 收支流水 / 业主档案；
--    删除剔除记录即「恢复台账」。
-- 版本：045（2026-09-19）
-- ============================================================

ALTER TABLE t_import_log ADD COLUMN updated_count INTEGER NOT NULL DEFAULT 0;

ALTER TABLE t_charge_item ADD COLUMN formula TEXT;

CREATE TABLE IF NOT EXISTS t_arrear_dismiss (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id    INTEGER NOT NULL UNIQUE,
    reason     TEXT,
    operator   TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (bill_id) REFERENCES t_bill (id)
);

CREATE INDEX IF NOT EXISTS ix_arrear_dismiss_bill ON t_arrear_dismiss (bill_id);

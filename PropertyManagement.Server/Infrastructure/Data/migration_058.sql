-- ============================================================
-- migration_058.sql（v1.2.0：收款登记「已结清记录归档」t_bill_archive）
-- 背景（负责人 2026-09-22 反馈）：收款登记「应缴明细」按缴费对象列出其全部账单，
--       已结清记录会长期累积，用户有「清理记录 / 管理列表长度」的诉求；
--       但账单删除若走软删 t_bill（CHG-v1.1.2-02），会连带影响收款记录、财务报表、
--       收支明细流水与业主档案缴费概况 —— 与负责人口径冲突。
-- 处置（CHG-v1.2.0-31）：新增「归档」语义 —— 只写本表标记，账单与全部资金记录完全不变；
--       收款登记「应缴明细」查询按本表排除已归档行，其它模块（账单工作台 / 台账 /
--       收款 / 退款 / 财报 / 流水 / 业主档案）不受任何影响。
-- 口径：仅「已结清」账单可归档（未结清记录一律拒绝，服务端逐条校验）。
-- 幂等：CREATE TABLE / INDEX IF NOT EXISTS，可重复执行。
-- 版本：058（2026-09-22）
-- ============================================================

CREATE TABLE IF NOT EXISTS t_bill_archive (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id    INTEGER NOT NULL UNIQUE,   -- t_bill.id
    reason     TEXT,                      -- 归档来源说明（如「收款登记·已结清记录清理」）
    operator   TEXT,                      -- 操作人
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (bill_id) REFERENCES t_bill (id)
);

CREATE INDEX IF NOT EXISTS ix_bill_archive_bill ON t_bill_archive (bill_id);

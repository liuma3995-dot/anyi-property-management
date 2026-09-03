-- ============================================================
-- migration_002.sql（M4 CHG-M4-03）
-- 新增 t_arrear_remind_log：欠费催缴渠道记录（UC-FIN-007）
-- 仅对已初始化到 v1 的存量库执行；全新安装由 schema.sql 直接建表。
-- 版本：002（2026-09-01）
-- ============================================================
CREATE TABLE IF NOT EXISTS t_arrear_remind_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id    INTEGER NOT NULL,
    channel    TEXT    NOT NULL,
    note       TEXT,
    remind_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    created_by INTEGER,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (bill_id) REFERENCES t_bill (id),
    FOREIGN KEY (created_by) REFERENCES t_user (id)
);
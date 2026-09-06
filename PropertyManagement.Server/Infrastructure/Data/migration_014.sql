-- ============================================================
-- migration_014.sql（M5 CHG-INF-06）
-- t_import_log 增强：批次状态/操作人 + 新增 t_import_error（错误行明细，错误清单导出）
-- 版本：014（2026-09-03）
-- ============================================================
ALTER TABLE t_import_log ADD COLUMN status INTEGER NOT NULL DEFAULT 0;
ALTER TABLE t_import_log ADD COLUMN created_by TEXT;

CREATE TABLE IF NOT EXISTS t_import_error (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    import_id  INTEGER NOT NULL,
    row_no     INTEGER NOT NULL,
    field      TEXT,
    content    TEXT,
    reason     TEXT,
    suggestion TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (import_id) REFERENCES t_import_log (id)
);

CREATE INDEX IF NOT EXISTS ix_import_error_import ON t_import_error (import_id);

-- ============================================================
-- migration_024.sql（处理与结案：调解协议扫描件附件，F1）
-- 1) 新建 t_dispute_attachment：案件附件（原始文件名 / 相对存储路径 / MIME / 字节数 / 上传人 / 时间）
-- 2) 删除策略：软删置 del_flag=1，仅管理员可删，且连物理文件一并删除
-- 3) 物理文件：%ProgramData%\PropertyManagement\data\attachments\dispute\{caseId}\{guid}{ext}
-- 版本：024（2026-09-10）
-- 说明：本迁移仅新增表，无重建需求。
-- ============================================================

CREATE TABLE IF NOT EXISTS t_dispute_attachment (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id      INTEGER NOT NULL,
    file_name    TEXT    NOT NULL,
    stored_path  TEXT    NOT NULL,
    content_type TEXT,
    size_bytes   INTEGER,
    uploaded_by  TEXT,
    uploaded_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    del_flag     INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (case_id) REFERENCES t_dispute_case (id)
);

CREATE INDEX IF NOT EXISTS ix_dispute_attachment_case ON t_dispute_attachment (case_id, del_flag);

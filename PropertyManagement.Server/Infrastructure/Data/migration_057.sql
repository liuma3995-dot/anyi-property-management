-- ============================================================
-- migration_057.sql（v1.2.0：导入回执逐行结果 t_import_row）
-- 背景（负责人 2026-09-21 反馈）：重复数据已改为「覆盖处理」，但覆盖后**拿不到回执**，
--       同名业主/重复房产被覆盖后无法追溯，后续查找同名条件变得困难。
-- 处置（CHG-v1.2.0-01）：每次导入把逐行处理结果落库 —— 新增 / 覆盖（覆盖了谁、改了什么）/ 失败，
--       批次列表「下载回执」导出 Excel；回执行随批次删除、一键清理残余数据一并物理清理。
-- 幂等：CREATE TABLE / INDEX IF NOT EXISTS，可重复执行。
-- 版本：057（2026-09-21）
-- ============================================================

CREATE TABLE IF NOT EXISTS t_import_row (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    import_id      INTEGER NOT NULL,          -- t_import_log.id
    row_no         INTEGER NOT NULL,          -- Excel 行号（含表头，从 1 起）
    result         INTEGER NOT NULL,          -- 0 新增 / 1 覆盖 / 2 失败
    object_key     TEXT,                      -- 业务对象标识（房产/业主/车位/关系）
    change_summary TEXT,                      -- 覆盖明细：字段：旧值 → 新值
    reason         TEXT,                      -- 失败原因
    suggestion     TEXT,                      -- 处理建议
    created_at     TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (import_id) REFERENCES t_import_log (id)
);

CREATE INDEX IF NOT EXISTS ix_import_row_import ON t_import_row (import_id);

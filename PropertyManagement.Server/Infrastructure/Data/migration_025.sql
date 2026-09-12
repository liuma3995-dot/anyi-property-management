-- ============================================================
-- migration_025.sql（设备台账：保养/年检登记类型自定义维护）
-- 1) 新建 t_maintain_type：登记类型（kind 0 保养 / 1 年检，决定保存路由），软删 del_flag
-- 2) t_maintenance_record / t_inspection_record 新增 record_type（登记类型名称快照，
--    删除类型后历史记录仍可读，与纠纷类型删除口径一致）
-- 3) 初始类型 = 原型登记类型下拉（月度/季度/半年/年度保养 + 年检登记）
-- 版本：025（2026-09-11）
-- 说明：本迁移仅新增表 / 新增列，无重建需求。
-- ============================================================

CREATE TABLE IF NOT EXISTS t_maintain_type (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    kind       INTEGER NOT NULL DEFAULT 0,
    sort       INTEGER NOT NULL DEFAULT 0,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE INDEX IF NOT EXISTS ix_maintain_type_del ON t_maintain_type (del_flag, kind);

ALTER TABLE t_maintenance_record ADD COLUMN record_type TEXT;
ALTER TABLE t_inspection_record  ADD COLUMN record_type TEXT;

-- 初始登记类型（BR-EQP-02：保养按周期 + 年检按年度）
INSERT INTO t_maintain_type (name, kind, sort)
SELECT '月度保养', 0, 10 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '月度保养');
INSERT INTO t_maintain_type (name, kind, sort)
SELECT '季度保养', 0, 20 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '季度保养');
INSERT INTO t_maintain_type (name, kind, sort)
SELECT '半年保养', 0, 30 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '半年保养');
INSERT INTO t_maintain_type (name, kind, sort)
SELECT '年度保养', 0, 40 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '年度保养');
INSERT INTO t_maintain_type (name, kind, sort)
SELECT '年检登记', 1, 90 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '年检登记');

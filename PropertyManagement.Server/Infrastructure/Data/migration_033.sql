-- ============================================================
-- migration_033.sql（软删留痕后的唯一性口径统一 —— v1.1.0 T9 / CHG-v1.1.0-04）
--
-- 背景：t_property / t_bill 已在 migration_031 收敛为「含 del_flag = 0 的部分唯一索引」，
--       但 t_building / t_unit 仍是建表期的整表 UNIQUE 约束（含软删留痕行）：
--       软删楼栋「X」后重建同名 → UNIQUE constraint failed: t_building.community_id, t_building.building_no
--       → 接口只回「数据服务暂不可用」，用户看不懂，也与 BR-INF-01「软删留痕后可重建」口径打架。
--
-- 处置：沿用 migration_023 的「重建表」方式，把两处唯一约束改写为部分唯一索引（WHERE del_flag = 0）。
--       现有数据在新索引下必然唯一（旧约束更强），无需去重。
-- 版本：033（2026-09-16，v1.1.0）
-- 说明：本工程 SQLite 未开启 foreign_keys（DatabaseInitializer 显式 PRAGMA foreign_keys = OFF），
--       重建表安全；重建保留 id 不变，t_property.building_id / unit_id 等外部引用不受影响。
-- ============================================================

-- 1) t_building：去掉整表 UNIQUE(community_id, building_no)，改为 del_flag = 0 部分唯一索引
CREATE TABLE IF NOT EXISTS t_building_new (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    community_id  INTEGER NOT NULL,
    building_no   TEXT    NOT NULL,
    floors        INTEGER,
    del_flag      INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (community_id) REFERENCES t_community (id)
);

INSERT OR IGNORE INTO t_building_new (id, community_id, building_no, floors, del_flag, created_at, updated_at)
SELECT id, community_id, building_no, floors, del_flag, created_at, updated_at FROM t_building;

DROP TABLE t_building;
ALTER TABLE t_building_new RENAME TO t_building;

CREATE UNIQUE INDEX IF NOT EXISTS ux_building_no_unique
    ON t_building (community_id, building_no) WHERE del_flag = 0;

-- 2) t_unit：去掉整表 UNIQUE(building_id, unit_no)，改为 del_flag = 0 部分唯一索引
CREATE TABLE IF NOT EXISTS t_unit_new (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    building_id INTEGER NOT NULL,
    unit_no     TEXT    NOT NULL,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (building_id) REFERENCES t_building (id)
);

INSERT OR IGNORE INTO t_unit_new (id, building_id, unit_no, del_flag, created_at, updated_at)
SELECT id, building_id, unit_no, del_flag, created_at, updated_at FROM t_unit;

DROP TABLE t_unit;
ALTER TABLE t_unit_new RENAME TO t_unit;

CREATE UNIQUE INDEX IF NOT EXISTS ux_unit_no_unique
    ON t_unit (building_id, unit_no) WHERE del_flag = 0;
CREATE INDEX IF NOT EXISTS ix_unit_building_del ON t_unit (building_id, del_flag);

-- ============================================================
-- migration_034.sql（软删留痕后的唯一性口径统一 · 第二批 —— v1.1.0 第 4 轮 / CHG-v1.1.0-06）
--
-- 背景（现场实测根因）：业主-房产关系导入失败，报「数据重复」。
--   实测：楼栋「1栋」的 202 房与该业主存在一条**已解除（del_flag = 1）的留痕关系**，
--   生效日期与本次导入相同（同一天），而 t_owner_property_rel 的唯一键仍是建表期内联
--   UNIQUE (property_id, owner_id, effective_at)（**未含 del_flag**）→ 留痕行占用唯一键
--   → 解除后无法再次绑定同一业主到同一房产（同一天），UI 绑定同样会被挡住。
--
-- 同类隐患审计（本迁移一并修）：
--   * t_dict_item      UNIQUE (type_code, item_code)  → 软删留痕后无法重建同编码字典项
--   * t_parking_space  UNIQUE (space_no)              → 软删留痕后无法重建同编号车位
--
-- 处置：三张表统一重建为「del_flag = 0 的**部分唯一索引**」，与既有口径对齐
--   （migration_031：t_property / t_bill；migration_033：t_building / t_unit）。
--   留痕行完整保留；旧约束更强 ⇒ 现网数据在新索引下必然合法，无需去重。
-- 版本：034（2026-09-16，v1.1.0）
-- 说明：本工程 SQLite 未开启 foreign_keys（DatabaseInitializer 显式 PRAGMA foreign_keys = OFF），
--       重建表安全；重建保留 id 不变，t_bill.parking_id 等外部引用不受影响。
-- ============================================================

-- 1) t_owner_property_rel：内联 UNIQUE → 部分唯一索引（del_flag = 0）
CREATE TABLE IF NOT EXISTS t_owner_property_rel_new (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    property_id  INTEGER NOT NULL,
    owner_id     INTEGER NOT NULL,
    rel_type     INTEGER,
    share        NUMERIC NOT NULL DEFAULT 0,
    effective_at TEXT,
    expire_at    TEXT,
    rel_status   INTEGER NOT NULL DEFAULT 0,
    del_flag     INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (property_id) REFERENCES t_property (id),
    FOREIGN KEY (owner_id) REFERENCES t_owner (id)
);

INSERT OR IGNORE INTO t_owner_property_rel_new
    (id, property_id, owner_id, rel_type, share, effective_at, expire_at, rel_status, del_flag, created_at, updated_at)
SELECT id, property_id, owner_id, rel_type, COALESCE(share, 0), effective_at, expire_at,
       COALESCE(rel_status, 0), COALESCE(del_flag, 0), created_at, updated_at
FROM t_owner_property_rel;

DROP TABLE t_owner_property_rel;
ALTER TABLE t_owner_property_rel_new RENAME TO t_owner_property_rel;

CREATE UNIQUE INDEX IF NOT EXISTS ux_relation_unique
    ON t_owner_property_rel (property_id, owner_id, effective_at) WHERE del_flag = 0;
CREATE INDEX IF NOT EXISTS ix_relation_property_del
    ON t_owner_property_rel (property_id, del_flag);

-- 2) t_dict_item：内联 UNIQUE (type_code, item_code) → 部分唯一索引（del_flag = 0）
CREATE TABLE IF NOT EXISTS t_dict_item_new (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    type_code     TEXT    NOT NULL,
    item_code     TEXT    NOT NULL,
    item_name     TEXT    NOT NULL,
    sort          INTEGER NOT NULL DEFAULT 0,
    status        INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    remark        TEXT,
    display_value TEXT,
    updated_by    TEXT,
    updated_at    TEXT,
    del_flag      INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO t_dict_item_new
    (id, type_code, item_code, item_name, sort, status, created_at, remark, display_value, updated_by, updated_at, del_flag)
SELECT id, type_code, item_code, item_name, sort, status, created_at, remark, display_value, updated_by, updated_at,
       COALESCE(del_flag, 0)
FROM t_dict_item;

DROP TABLE t_dict_item;
ALTER TABLE t_dict_item_new RENAME TO t_dict_item;

CREATE UNIQUE INDEX IF NOT EXISTS ux_dict_item_code
    ON t_dict_item (type_code, item_code) WHERE del_flag = 0;
CREATE INDEX IF NOT EXISTS ix_dict_item_type_code ON t_dict_item (type_code, status);
CREATE INDEX IF NOT EXISTS ix_dict_item_type_del  ON t_dict_item (type_code, del_flag, status);

-- 3) t_parking_space：内联 UNIQUE (space_no) → 部分唯一索引（del_flag = 0）
CREATE TABLE IF NOT EXISTS t_parking_space_new (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    space_no     TEXT    NOT NULL,
    space_type   INTEGER,
    property_id  INTEGER,
    owner_id     INTEGER,
    del_flag     INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    area         TEXT,
    status       INTEGER NOT NULL DEFAULT 2,
    monthly_rent NUMERIC,
    rent_to      TEXT,
    rent_mode    INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (property_id) REFERENCES t_property (id),
    FOREIGN KEY (owner_id) REFERENCES t_owner (id)
);

INSERT OR IGNORE INTO t_parking_space_new
    (id, space_no, space_type, property_id, owner_id, del_flag, created_at, updated_at, area, status, monthly_rent, rent_to, rent_mode)
SELECT id, space_no, space_type, property_id, owner_id, COALESCE(del_flag, 0), created_at, updated_at, area,
       COALESCE(status, 2), monthly_rent, rent_to, COALESCE(rent_mode, 0)
FROM t_parking_space;

DROP TABLE t_parking_space;
ALTER TABLE t_parking_space_new RENAME TO t_parking_space;

CREATE UNIQUE INDEX IF NOT EXISTS ux_parking_no
    ON t_parking_space (space_no) WHERE del_flag = 0;
CREATE INDEX IF NOT EXISTS ix_parking_property_del
    ON t_parking_space (property_id, del_flag);

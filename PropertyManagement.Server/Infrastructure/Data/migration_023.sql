-- ============================================================
-- migration_023.sql（房产：楼栋与单元解耦；单元改为可选）
-- 1) t_property 新增 building_id（楼栋必填，与单元解耦）
-- 2) t_property.unit_id 允许为空（部分楼栋无单元）
-- 3) 由既有「单元 -> 楼栋」回填 building_id
-- 4) 统一房号口径：楼栋(原值) + 单元(原值) + 房号
-- 版本：023（2026-09-10）
-- 说明：本工程 SQLite 未开启 foreign_keys（无 PRAGMA），可安全重建表；
--       重建保留 id 不变，外部以 t_property(id) 引用的数据不受影响。
-- ============================================================

CREATE TABLE IF NOT EXISTS t_property_new (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    building_id INTEGER,
    unit_id     INTEGER,
    room_no     TEXT    NOT NULL,
    area        NUMERIC,
    usage       INTEGER,
    status      INTEGER,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (building_id) REFERENCES t_building (id),
    FOREIGN KEY (unit_id) REFERENCES t_unit (id)
);

INSERT OR IGNORE INTO t_property_new
    (id, building_id, unit_id, room_no, area, usage, status, del_flag, created_at, updated_at)
SELECT p.id,
       u.building_id,
       p.unit_id,
       p.room_no, p.area, p.usage, p.status, p.del_flag, p.created_at, p.updated_at
FROM t_property p
LEFT JOIN t_unit u ON u.id = p.unit_id;

DROP TABLE t_property;
ALTER TABLE t_property_new RENAME TO t_property;

CREATE INDEX IF NOT EXISTS ix_property_unit_del ON t_property (unit_id, del_flag);
CREATE INDEX IF NOT EXISTS ix_property_building  ON t_property (building_id, del_flag);
CREATE INDEX IF NOT EXISTS ix_property_room      ON t_property (room_no);

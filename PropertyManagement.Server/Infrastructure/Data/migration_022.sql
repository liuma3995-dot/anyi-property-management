-- 电话簿：新增类型实体 t_phone_type（支持自定义新增/删除），条目改为引用 type_id。
CREATE TABLE IF NOT EXISTS t_phone_type (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    sort       INTEGER NOT NULL DEFAULT 0,
    status     INTEGER,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

INSERT INTO t_phone_type (name, sort) VALUES ('紧急', 0), ('普通', 1), ('员工通讯录', 2);

ALTER TABLE t_phone_entry ADD COLUMN type_id INTEGER;
UPDATE t_phone_entry SET type_id = entry_type + 1 WHERE type_id IS NULL;

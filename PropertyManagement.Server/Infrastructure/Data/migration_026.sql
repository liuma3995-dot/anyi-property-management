-- ============================================================
-- migration_026：设备台账 R6 改造
-- 1) t_device 新增 next_maintenance_at：用户自定义下次保养日期（空=按周期派生）
-- 2) t_reminder 新增 del_flag（批量删除软删）、handled_at（已处理时间，保留已处理记录展示）
-- 3) t_maintain_type 新增 is_system：登记类型固定为系统两类（保养/年检），历史自定义类型全部软删
-- 4) 新建 t_device_custom_record：设备维度自定义类型记录（登记类型解耦）
-- 版本：026（2026-09-11）
-- 说明：仅新增表/新增列 + 数据标记，无表重建；历史催办/处置流水与 record_type 文本快照保留不改写。
-- ============================================================

-- 1) 自定义下次保养日期（yyyy-MM-dd 文本，空=按"最近保养+周期"派生）
ALTER TABLE t_device ADD COLUMN next_maintenance_at TEXT;

-- 2) 到期提醒：软删 + 已处理时间（列表保留已处理记录）
ALTER TABLE t_reminder ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;
ALTER TABLE t_reminder ADD COLUMN handled_at TEXT;

-- 已处理提醒回填处置时间：优先取 handled 流水时间，缺失回退 created_at（仅业务提醒行，流水行不参与）
UPDATE t_reminder
   SET handled_at = COALESCE(
        (SELECT MAX(u.created_at) FROM t_reminder u WHERE u.type = 'handled' AND u.target_id = t_reminder.id),
        created_at)
 WHERE status = 1
   AND type NOT IN ('urge', 'handled')
   AND (handled_at IS NULL OR handled_at = '');

-- 3) 登记类型固定化：系统两类 保养(kind=0) / 年检(kind=1)；历史自定义类型软删
ALTER TABLE t_maintain_type ADD COLUMN is_system INTEGER NOT NULL DEFAULT 0;

INSERT INTO t_maintain_type (name, kind, sort, del_flag, is_system)
SELECT '保养', 0, 10, 0, 1
 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '保养' AND kind = 0);

INSERT INTO t_maintain_type (name, kind, sort, del_flag, is_system)
SELECT '年检', 1, 20, 0, 1
 WHERE NOT EXISTS (SELECT 1 FROM t_maintain_type WHERE name = '年检' AND kind = 1);

-- 历史自定义类型（含 migration_025 预置的 月度/季度/半年/年度保养、年检登记）软删下线；
-- 历史记录 t_maintenance_record/t_inspection_record.record_type 文本快照不改写。
UPDATE t_maintain_type SET del_flag = 1 WHERE is_system = 0;

CREATE INDEX IF NOT EXISTS ix_maintain_type_system ON t_maintain_type (del_flag, is_system, kind);

-- 4) 设备自定义类型记录（设备查看浮层"自定义类型记录"页签）
CREATE TABLE IF NOT EXISTS t_device_custom_record (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id  INTEGER NOT NULL,
    type_name  TEXT    NOT NULL,   -- 自定义类型名称（管理员输入）
    r_date     TEXT    NOT NULL,   -- 记录日期 yyyy-MM-dd
    content    TEXT,               -- 记录内容
    result     TEXT,               -- 结果（可空）
    cost       REAL,               -- 费用（可空）
    vendor_id  INTEGER,            -- 执行方（复用 t_vendor）
    operator   TEXT,               -- 记录人
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (device_id) REFERENCES t_device (id),
    FOREIGN KEY (vendor_id) REFERENCES t_vendor (id)
);

CREATE INDEX IF NOT EXISTS ix_device_custom_record_device
    ON t_device_custom_record (device_id, del_flag, r_date);

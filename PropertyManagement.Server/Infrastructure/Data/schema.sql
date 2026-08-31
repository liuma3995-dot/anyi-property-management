-- ============================================================
-- 澜庭物业管理系统 - schema.sql（M2 D2-1）
-- 依据：DM-04 数据库设计（60 张表）；SQLite 单文件
-- 约定：id INTEGER 主键自增；枚举存 INTEGER；金额 NUMERIC；
--       时间 TEXT（ISO 8601 本地，datetime('now','localtime')）；
--       布尔/软删 INTEGER 0/1；核心业务表统一 del_flag。
-- 版本：v1（2026-08-31）
-- ============================================================

-- 版本表（首次建库后写入当前版本）
CREATE TABLE IF NOT EXISTS schema_version (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    version    INTEGER NOT NULL,
    applied_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- ------------------------------------------------------------
-- 2.1 公共支撑（8 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_user (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    username         TEXT    NOT NULL UNIQUE,
    password_hash    TEXT    NOT NULL,
    status           INTEGER NOT NULL DEFAULT 0,   -- UserStatus: 0正常 1锁定 2停用
    last_login_at    TEXT,
    login_fail_count INTEGER NOT NULL DEFAULT 0,   -- M2-D4：P-02 连续失败计数
    locked_until     TEXT,                          -- M2-D4：P-02 锁定截止时间
    created_at       TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at       TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_param (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    param_key   TEXT NOT NULL UNIQUE,
    param_value TEXT NOT NULL,
    remark      TEXT,
    updated_at  TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_dict_type (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    type_code  TEXT NOT NULL UNIQUE,
    type_name  TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_dict_item (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    type_code  TEXT    NOT NULL,
    item_code  TEXT    NOT NULL,
    item_name  TEXT    NOT NULL,
    sort       INTEGER NOT NULL DEFAULT 0,
    status     INTEGER NOT NULL DEFAULT 0,   -- DictItemStatus: 0启用 1停用
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (type_code, item_code)
);

CREATE TABLE IF NOT EXISTS t_audit_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id     INTEGER,
    action      TEXT NOT NULL,
    target_type TEXT,
    target_id   TEXT,
    detail      TEXT,
    created_at  TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (user_id) REFERENCES t_user (id)
);

CREATE TABLE IF NOT EXISTS t_backup (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path  TEXT    NOT NULL,
    size       INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_reminder (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    type       TEXT    NOT NULL,
    target_id  INTEGER NOT NULL DEFAULT 0,
    due_at     TEXT    NOT NULL,
    status     INTEGER NOT NULL DEFAULT 0,   -- ReminderStatus: 0待处理 1已处理
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_export_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    module     TEXT NOT NULL,
    format     INTEGER NOT NULL,             -- ExportFormat: 0 Excel 1 PDF
    file_path  TEXT,
    created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

-- ------------------------------------------------------------
-- 2.2 基础信息（8 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_community (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    address    TEXT,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_building (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    community_id  INTEGER NOT NULL,
    building_no   TEXT    NOT NULL,
    floors        INTEGER,
    del_flag      INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (community_id, building_no),
    FOREIGN KEY (community_id) REFERENCES t_community (id)
);

CREATE TABLE IF NOT EXISTS t_unit (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    building_id INTEGER NOT NULL,
    unit_no     TEXT    NOT NULL,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (building_id, unit_no),
    FOREIGN KEY (building_id) REFERENCES t_building (id)
);

CREATE TABLE IF NOT EXISTS t_property (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    unit_id    INTEGER NOT NULL,
    room_no    TEXT    NOT NULL,
    area       NUMERIC,
    usage      INTEGER,                      -- PropertyUsage: 0住宅 1商铺
    status     INTEGER,                      -- PropertyStatus: 0空置 1入住
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (unit_id, room_no),               -- BR-INF-01
    FOREIGN KEY (unit_id) REFERENCES t_unit (id)
);

CREATE TABLE IF NOT EXISTS t_owner (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    id_card    TEXT,
    phone      TEXT,
    status     INTEGER,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_owner_property_rel (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    property_id  INTEGER NOT NULL,
    owner_id     INTEGER NOT NULL,
    rel_type     INTEGER,                    -- OwnerRelType: 0自住 1出租
    effective_at TEXT,
    del_flag     INTEGER NOT NULL DEFAULT 0,
    created_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (property_id, owner_id, effective_at),  -- BR-INF-02 关系唯一+历史
    FOREIGN KEY (property_id) REFERENCES t_property (id),
    FOREIGN KEY (owner_id) REFERENCES t_owner (id)
);

CREATE TABLE IF NOT EXISTS t_parking_space (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    space_no    TEXT    NOT NULL,
    space_type  INTEGER,                     -- ParkingSpaceType: 0固定 1临时
    property_id INTEGER,
    owner_id    INTEGER,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (space_no),                       -- BR-INF-03 固定车位唯一绑定
    FOREIGN KEY (property_id) REFERENCES t_property (id),
    FOREIGN KEY (owner_id) REFERENCES t_owner (id)
);

CREATE TABLE IF NOT EXISTS t_base_change_log (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    object_type    INTEGER NOT NULL,         -- BaseChangeObjectType
    object_id      INTEGER NOT NULL,
    change_content TEXT,
    changed_at     TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- ------------------------------------------------------------
-- 2.3 人员组织（8 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_department (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    parent_id  INTEGER,
    status     INTEGER,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_position (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    dept_id  INTEGER NOT NULL,
    name     TEXT    NOT NULL,
    FOREIGN KEY (dept_id) REFERENCES t_department (id)
);

CREATE TABLE IF NOT EXISTS t_employee (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    dept_id     INTEGER NOT NULL,
    position_id INTEGER NOT NULL,
    name        TEXT    NOT NULL,
    phone       TEXT,
    hire_date   TEXT,
    status      INTEGER,                     -- EmployeeStatus: 0在职 1离岗 2离职
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (dept_id) REFERENCES t_department (id),
    FOREIGN KEY (position_id) REFERENCES t_position (id)
);

CREATE TABLE IF NOT EXISTS t_employee_status_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_id INTEGER NOT NULL,
    status     INTEGER NOT NULL,
    changed_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (employee_id) REFERENCES t_employee (id)
);

CREATE TABLE IF NOT EXISTS t_shift (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT NOT NULL,
    start_time TEXT,
    end_time   TEXT
);

CREATE TABLE IF NOT EXISTS t_schedule (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_id INTEGER NOT NULL,
    shift_id    INTEGER NOT NULL,
    work_date   TEXT    NOT NULL,
    status      INTEGER NOT NULL DEFAULT 0,  -- ScheduleStatus: 0草稿 1已发布 2已取消
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (employee_id) REFERENCES t_employee (id),
    FOREIGN KEY (shift_id) REFERENCES t_shift (id)
);

CREATE TABLE IF NOT EXISTS t_attendance (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    employee_id INTEGER NOT NULL,
    work_date   TEXT    NOT NULL,
    check_in    TEXT,
    check_out   TEXT,
    result      INTEGER,                     -- AttendanceResult: 0已记录 1正常 2异常 3已审核
    review_by   INTEGER,
    review_note TEXT,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (employee_id) REFERENCES t_employee (id)
);

CREATE TABLE IF NOT EXISTS t_schedule_conflict_log (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    schedule_id   INTEGER NOT NULL,
    conflict_type TEXT,
    detail        TEXT,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (schedule_id) REFERENCES t_schedule (id)
);

-- ------------------------------------------------------------
-- 2.4 财务收费（13 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_charge_item (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    name        TEXT    NOT NULL,
    pay_mode    INTEGER NOT NULL,            -- ChargePayMode: 0按年 1按月 2临时
    unit_price  NUMERIC,
    cycle_type  INTEGER,                     -- BillingCycleType: 0按年 1按月
    status      INTEGER,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_billing_cycle (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    cycle_type  INTEGER NOT NULL,
    start_date  TEXT,
    end_date    TEXT
);

CREATE TABLE IF NOT EXISTS t_bill (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    charge_item_id    INTEGER NOT NULL,
    property_id       INTEGER,
    parking_id        INTEGER,
    cycle_id          INTEGER,
    amount            NUMERIC NOT NULL,
    paid_amount       NUMERIC NOT NULL DEFAULT 0,
    status            INTEGER NOT NULL DEFAULT 0,  -- BillStatus: 0待缴 1部分缴 2逾期 3已缴 4已冲正
    due_at            TEXT,
    generate_batch_id INTEGER,
    del_flag          INTEGER NOT NULL DEFAULT 0,
    created_at        TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at        TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (charge_item_id) REFERENCES t_charge_item (id),
    FOREIGN KEY (property_id) REFERENCES t_property (id),
    FOREIGN KEY (parking_id) REFERENCES t_parking_space (id),
    FOREIGN KEY (cycle_id) REFERENCES t_billing_cycle (id)
);
-- BR-FIN-01：同一缴费对象同一计费周期同一收费项目不得重复（部分唯一索引，规避 NULL 判重失效）
CREATE UNIQUE INDEX IF NOT EXISTS ux_bill_property_cycle ON t_bill (charge_item_id, cycle_id, property_id) WHERE property_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_bill_parking_cycle  ON t_bill (charge_item_id, cycle_id, parking_id)  WHERE parking_id  IS NOT NULL;

CREATE TABLE IF NOT EXISTS t_payment (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id       INTEGER NOT NULL,
    amount        NUMERIC NOT NULL,
    pay_method    INTEGER NOT NULL,          -- PayMethod: 0现金 1转账
    paid_at       TEXT    NOT NULL,
    status        INTEGER NOT NULL DEFAULT 0,  -- PaymentStatus: 0正常 1冲正
    to_pre_deposit NUMERIC NOT NULL DEFAULT 0, -- P-06 转预存款
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (bill_id) REFERENCES t_bill (id)
);

CREATE TABLE IF NOT EXISTS t_receipt (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    payment_id  INTEGER NOT NULL,
    receipt_no  TEXT    NOT NULL UNIQUE,     -- BR-FIN-08 收据号唯一
    print_count INTEGER NOT NULL DEFAULT 0,
    printed_at  TEXT,
    FOREIGN KEY (payment_id) REFERENCES t_payment (id)
);

CREATE TABLE IF NOT EXISTS t_payment_refund (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id     INTEGER NOT NULL,
    refund_type INTEGER NOT NULL,            -- RefundType: 0退款 1减免 2调整
    amount      NUMERIC NOT NULL,
    reason      TEXT    NOT NULL,            -- BR-FIN-06 必须填写原因
    ref_no      TEXT,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (bill_id) REFERENCES t_bill (id)
);

CREATE TABLE IF NOT EXISTS t_pre_deposit (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    owner_id   INTEGER NOT NULL UNIQUE,
    balance    NUMERIC NOT NULL DEFAULT 0,
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (owner_id) REFERENCES t_owner (id)
);

CREATE TABLE IF NOT EXISTS t_expense_category (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    name          TEXT NOT NULL,
    category_type TEXT,
    status        INTEGER,
    del_flag      INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT  NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at    TEXT  NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_expense (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id  INTEGER NOT NULL,
    amount       NUMERIC NOT NULL,
    expense_date TEXT    NOT NULL,
    note         TEXT,
    status       INTEGER,
    del_flag     INTEGER NOT NULL DEFAULT 0, -- BR-FIN-10 软删除
    created_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (category_id) REFERENCES t_expense_category (id)
);

CREATE TABLE IF NOT EXISTS t_expense_object_rel (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    expense_id  INTEGER NOT NULL,
    object_type INTEGER NOT NULL,            -- ExpenseObjectType: 0员工 1设备 2维保单位
    object_id   INTEGER NOT NULL,
    FOREIGN KEY (expense_id) REFERENCES t_expense (id)
);

CREATE TABLE IF NOT EXISTS t_bill_generate_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    generate_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    total       INTEGER,
    success     INTEGER,
    fail        INTEGER,
    fail_detail TEXT
);

CREATE TABLE IF NOT EXISTS t_report_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    report_type TEXT,
    period      TEXT,
    format      INTEGER,                     -- ExportFormat
    file_path   TEXT,
    created_at  TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_bill_status_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id    INTEGER NOT NULL,
    old_status INTEGER,
    new_status INTEGER NOT NULL,
    changed_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    reason     TEXT,
    FOREIGN KEY (bill_id) REFERENCES t_bill (id)
);

-- ------------------------------------------------------------
-- 2.5 应急处置（7 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_emergency_scene (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    category   TEXT,
    status     INTEGER,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_emergency_step (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    scene_id   INTEGER NOT NULL,
    step_no    INTEGER NOT NULL,
    content    TEXT    NOT NULL,
    version_no TEXT    NOT NULL,             -- BR-EMG-05 步骤版本化
    status     INTEGER,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (scene_id) REFERENCES t_emergency_scene (id)
);

CREATE TABLE IF NOT EXISTS t_emergency_event (
    id             INTEGER PRIMARY KEY AUTOINCREMENT,
    scene_id       INTEGER NOT NULL,         -- BR-EMG-01 必须关联场景
    event_time     TEXT    NOT NULL,
    location       TEXT,
    description    TEXT,
    status         INTEGER NOT NULL DEFAULT 0,  -- EmergencyEventStatus: 0已发起 1处置中 2已结案 3已复盘
    step_version   TEXT,
    record_pending INTEGER NOT NULL DEFAULT 0,
    del_flag       INTEGER NOT NULL DEFAULT 0,
    created_at     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at     TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (scene_id) REFERENCES t_emergency_scene (id)
);

CREATE TABLE IF NOT EXISTS t_emergency_assign (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id    INTEGER NOT NULL,
    employee_id INTEGER NOT NULL,
    assign_type INTEGER NOT NULL,            -- EmergencyAssignType: 0责任人 1值班人
    assigned_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (event_id) REFERENCES t_emergency_event (id),
    FOREIGN KEY (employee_id) REFERENCES t_employee (id)
);

CREATE TABLE IF NOT EXISTS t_emergency_record (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id     INTEGER NOT NULL,
    record_time  TEXT    NOT NULL,
    content      TEXT    NOT NULL,           -- BR-EMG-04 时间/操作/结果
    result       TEXT,
    recorder     TEXT,
    is_supplement INTEGER NOT NULL DEFAULT 0, -- 补录留痕（BR-EMG-06）
    created_at   TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (event_id) REFERENCES t_emergency_event (id)
);

CREATE TABLE IF NOT EXISTS t_emergency_review (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id   INTEGER NOT NULL,
    cause      TEXT,
    measure    TEXT,
    finish_at  TEXT,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (event_id) REFERENCES t_emergency_event (id)
);

CREATE TABLE IF NOT EXISTS t_event_status_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id   INTEGER NOT NULL,
    old_status INTEGER,
    new_status INTEGER NOT NULL,
    changed_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (event_id) REFERENCES t_emergency_event (id)
);

-- ------------------------------------------------------------
-- 2.6 便民电话簿（2 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_phone_category (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    sort       INTEGER NOT NULL DEFAULT 0,
    status     INTEGER,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_phone_entry (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    category_id INTEGER NOT NULL,
    entry_type  INTEGER NOT NULL,            -- PhoneEntryType: 0紧急 1普通 2员工通讯录
    name        TEXT    NOT NULL,
    phone       TEXT    NOT NULL,
    note        TEXT,
    is_top      INTEGER NOT NULL DEFAULT 0,  -- BR-TEL-03 紧急置顶
    status      INTEGER NOT NULL DEFAULT 0,  -- PhoneEntryStatus: 0启用 1停用
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (category_id) REFERENCES t_phone_category (id)
);

-- ------------------------------------------------------------
-- 2.7 纠纷调解（5 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_dispute_type (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    status     INTEGER,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_dispute_case (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    type_id     INTEGER NOT NULL,
    occur_time  TEXT    NOT NULL,
    location    TEXT,
    detail      TEXT,
    status      INTEGER NOT NULL DEFAULT 0,  -- DisputeCaseStatus: 0已登记 1处理中 2已结案
    mediator_id INTEGER,                     -- BR-DIS-03 调解员引用员工
    close_type  INTEGER,                     -- DisputeCloseType（P-08）
    closed_at   TEXT,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (type_id) REFERENCES t_dispute_type (id),
    FOREIGN KEY (mediator_id) REFERENCES t_employee (id)
);

CREATE TABLE IF NOT EXISTS t_dispute_party (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id    INTEGER NOT NULL,
    party_type INTEGER,
    owner_id   INTEGER,                      -- BR-DIS-05 可引用业主或外部登记
    name       TEXT,
    phone      TEXT,
    FOREIGN KEY (case_id) REFERENCES t_dispute_case (id),
    FOREIGN KEY (owner_id) REFERENCES t_owner (id)
);

CREATE TABLE IF NOT EXISTS t_dispute_record (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id       INTEGER NOT NULL,
    record_time   TEXT    NOT NULL,
    content       TEXT    NOT NULL,
    recorder      TEXT,
    is_supplement INTEGER NOT NULL DEFAULT 0, -- BR-DIS-04 补录留痕
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (case_id) REFERENCES t_dispute_case (id)
);

CREATE TABLE IF NOT EXISTS t_dispute_status_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id    INTEGER NOT NULL,
    old_status INTEGER,
    new_status INTEGER NOT NULL,
    changed_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (case_id) REFERENCES t_dispute_case (id)
);

-- ------------------------------------------------------------
-- 2.8 设备资产台账（7 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_device_type (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    name             TEXT NOT NULL,
    maintenance_cycle TEXT,                  -- P-04 类型默认保养周期
    status           INTEGER,
    del_flag         INTEGER NOT NULL DEFAULT 0,
    created_at       TEXT   NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at       TEXT   NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_device (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    type_id     INTEGER NOT NULL,
    name        TEXT    NOT NULL,
    location    TEXT,
    status      INTEGER NOT NULL DEFAULT 0,  -- DeviceStatus: 0在用 1维修中 2停用 3报废
    enable_date TEXT,
    del_flag    INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (type_id) REFERENCES t_device_type (id)
);

CREATE TABLE IF NOT EXISTS t_device_status_log (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id  INTEGER NOT NULL,
    old_status INTEGER,
    new_status INTEGER NOT NULL,
    reason     TEXT,
    changed_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (device_id) REFERENCES t_device (id)
);

CREATE TABLE IF NOT EXISTS t_maintenance_record (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id  INTEGER NOT NULL,
    vendor_id  INTEGER,
    m_date     TEXT    NOT NULL,
    content    TEXT,
    result     TEXT,                         -- 不合格转维修（BR-EQP-03）
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (device_id) REFERENCES t_device (id),
    FOREIGN KEY (vendor_id) REFERENCES t_vendor (id)
);

CREATE TABLE IF NOT EXISTS t_inspection_record (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id  INTEGER NOT NULL,
    vendor_id  INTEGER,
    i_date     TEXT    NOT NULL,
    result     TEXT,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (device_id) REFERENCES t_device (id),
    FOREIGN KEY (vendor_id) REFERENCES t_vendor (id)
);

CREATE TABLE IF NOT EXISTS t_fault_record (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id  INTEGER NOT NULL,
    event_id   INTEGER,                      -- 弱关联应急事件（可空）
    f_time     TEXT    NOT NULL,
    symptom    TEXT,
    cause      TEXT,
    handle     TEXT,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (device_id) REFERENCES t_device (id),
    FOREIGN KEY (event_id) REFERENCES t_emergency_event (id)
);

CREATE TABLE IF NOT EXISTS t_vendor (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    contact    TEXT,
    phone      TEXT,
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- ------------------------------------------------------------
-- 2.9 导入打印（2 张）
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS t_import_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    module      INTEGER NOT NULL,            -- ImportModule
    file_name   TEXT,
    total       INTEGER,
    success     INTEGER,
    fail        INTEGER,
    error_file  TEXT,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_print_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    biz_type    TEXT    NOT NULL,
    biz_id      INTEGER NOT NULL,
    print_count INTEGER NOT NULL DEFAULT 0,
    printed_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- ------------------------------------------------------------
-- 常用查询索引
-- ------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_audit_log_created_at   ON t_audit_log (created_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_action       ON t_audit_log (action);
CREATE INDEX IF NOT EXISTS ix_dict_item_type_code    ON t_dict_item (type_code, status);
CREATE INDEX IF NOT EXISTS ix_bill_property_id       ON t_bill (property_id);
CREATE INDEX IF NOT EXISTS ix_bill_parking_id        ON t_bill (parking_id);
CREATE INDEX IF NOT EXISTS ix_bill_due_at            ON t_bill (due_at, status);
CREATE INDEX IF NOT EXISTS ix_payment_bill_id        ON t_payment (bill_id);
CREATE INDEX IF NOT EXISTS ix_payment_paid_at        ON t_payment (paid_at);
CREATE INDEX IF NOT EXISTS ix_expense_date           ON t_expense (expense_date);
CREATE INDEX IF NOT EXISTS ix_reminder_status        ON t_reminder (status, due_at);
CREATE INDEX IF NOT EXISTS ix_owner_name             ON t_owner (name);
CREATE INDEX IF NOT EXISTS ix_property_room          ON t_property (room_no);
CREATE INDEX IF NOT EXISTS ix_emergency_event_time   ON t_emergency_event (event_time);
CREATE INDEX IF NOT EXISTS ix_dispute_case_occur     ON t_dispute_case (occur_time);
CREATE INDEX IF NOT EXISTS ix_device_status          ON t_device (status);
CREATE INDEX IF NOT EXISTS ix_schedule_work_date     ON t_schedule (work_date, status);
CREATE INDEX IF NOT EXISTS ix_attendance_work_date   ON t_attendance (work_date, result);

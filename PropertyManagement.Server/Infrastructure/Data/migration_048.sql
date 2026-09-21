-- ============================================================
-- migration_048.sql（v1.1.2：收费项目「价目表 + 计量变量」重构 —— 结构）
-- 背景：负责人 2026-09-19 裁定 —— 收费项目表单由 8 项收敛为「选收费标准 → 选缴费对象 → 保存」，
--       差异化收费由「多建项目」改为「同项目多规格」，计量单位与计算规则统一收拢到「变量」。
-- 处置：新增 4 张表（收费标准 / 规格明细 / 计量变量 / 收费标准引用变量），并为
--       t_charge_item 补 standard_id 与 allow_price_override，
--       t_bill 补账单快照（规格 + 计量取值 + 单价 + 公式），金额可复算。
-- 说明：本次全部为「加表 + 加列」，既有列一律保留，旧版本数据零回归。
-- 版本：048（2026-09-19）
-- ============================================================

-- 1) 收费标准（价目表主体）
CREATE TABLE IF NOT EXISTS t_charge_standard (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    name       TEXT    NOT NULL,
    category   TEXT    NOT NULL DEFAULT '',
    remark     TEXT,
    status     INTEGER NOT NULL DEFAULT 0,   -- 0 启用 1 停用
    del_flag   INTEGER NOT NULL DEFAULT 0,
    created_at TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE INDEX IF NOT EXISTS ix_charge_standard_name ON t_charge_standard (name);

-- 2) 规格明细（同一收费标准下的多条价目表条目）
CREATE TABLE IF NOT EXISTS t_charge_standard_spec (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    standard_id      INTEGER NOT NULL,
    spec_name        TEXT    NOT NULL,
    match_usage      INTEGER,                    -- 房产用途：0 住宅 1 商铺；NULL = 不限
    match_status     INTEGER,                    -- 房产状态：0 空置 1 入住 2 装修中；NULL = 不限
    match_space_type INTEGER,                    -- 车位类型：0 产权 1 人防 2 临时；NULL = 不限
    match_building   TEXT,                       -- 楼栋号；NULL = 不限、空串 = 任意楼栋
    is_fallback      INTEGER NOT NULL DEFAULT 0, -- 1 = 兜底规格（未命中其它规格时使用）
    unit_price       NUMERIC NOT NULL DEFAULT 0,
    formula          TEXT,                       -- 计算公式（变量以 {v:ID} 记号引用）
    formula_vars     TEXT,                       -- 变量快照：[{"id":1,"name":"台数","unit":"台"}]
    price_unit       TEXT,                       -- 单价单位（由公式变量推导，如 元/台·月）
    cycle_name       TEXT,                       -- 计费周期名（每月 / 每季 / 一次性 / 自定义）
    effective_from   TEXT,                       -- 生效日期
    remark           TEXT,
    status           INTEGER NOT NULL DEFAULT 0,
    del_flag         INTEGER NOT NULL DEFAULT 0,
    created_at       TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at       TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (standard_id) REFERENCES t_charge_standard (id)
);

CREATE INDEX IF NOT EXISTS ix_charge_spec_standard ON t_charge_standard_spec (standard_id);

-- 3) 计量变量字典（系统预置 + 用户自建）
CREATE TABLE IF NOT EXISTS t_charge_variable (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    var_code      TEXT    NOT NULL UNIQUE,        -- MQ-01 起顺序编号
    var_name      TEXT    NOT NULL,
    unit          TEXT    NOT NULL DEFAULT '',    -- ㎡ / 台 / 桶 / 人次 / 度 …
    value_type    INTEGER NOT NULL DEFAULT 0,     -- 0 整数 1 小数
    source        INTEGER NOT NULL DEFAULT 2,     -- 0 档案自动 1 周期派生 2 手填 3 固定值
    default_value NUMERIC,
    object_scope  INTEGER NOT NULL DEFAULT 4,     -- 0 房产 1 车位 2 业主 3 自定义 4 不限
    field_key     TEXT,                           -- 档案自动类变量的档案字段（内置专用）
    is_builtin    INTEGER NOT NULL DEFAULT 0,     -- 1 = 内置（可停用、不可删除）
    remark        TEXT,
    status        INTEGER NOT NULL DEFAULT 0,
    del_flag      INTEGER NOT NULL DEFAULT 0,
    sort          INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);

-- 4) 收费标准引用的变量（内置不占额度，用户自建 ≤ 5 个）
CREATE TABLE IF NOT EXISTS t_charge_standard_variable (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    standard_id INTEGER NOT NULL,
    variable_id INTEGER NOT NULL,
    is_custom   INTEGER NOT NULL DEFAULT 0,
    sort        INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE (standard_id, variable_id),
    FOREIGN KEY (standard_id) REFERENCES t_charge_standard (id),
    FOREIGN KEY (variable_id) REFERENCES t_charge_variable (id)
);

CREATE INDEX IF NOT EXISTS ix_charge_std_var_standard ON t_charge_standard_variable (standard_id);

-- 5) 收费项目：绑定收费标准 + 出账可改价开关（默认关闭，仅一次性 / 自定义项目可开）
ALTER TABLE t_charge_item ADD COLUMN standard_id INTEGER;
ALTER TABLE t_charge_item ADD COLUMN allow_price_override INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_charge_item_standard ON t_charge_item (standard_id);

-- 6) 账单快照：命中的规格 + 计量取值 + 单价 + 公式（历史账单不回填，保持为空）
ALTER TABLE t_bill ADD COLUMN charge_spec_id INTEGER;
ALTER TABLE t_bill ADD COLUMN measure_snapshot TEXT;
ALTER TABLE t_bill ADD COLUMN unit_price_snapshot NUMERIC;
ALTER TABLE t_bill ADD COLUMN formula_snapshot TEXT;

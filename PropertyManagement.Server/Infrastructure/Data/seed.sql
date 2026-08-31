-- ============================================================
-- 澜庭物业管理系统 - seed.sql（M2 D2-2）
-- 首次启动种子数据：字典类型/项、系统参数 P-01~P-09、单小区
-- 约定：INSERT OR IGNORE 幂等；管理员账号由 DatabaseInitializer
--       以 BCrypt 运行时写入（密码不落明文 SQL）。
-- 版本：v1（2026-08-31）
-- ============================================================

-- 单小区（多项目预留，单项目仅初始化一条）
INSERT OR IGNORE INTO t_community (name, address) VALUES ('澜庭小区', '');

-- ------------------------------------------------------------
-- 字典类型（BR-COM-05：缴费模式/设备类型/应急场景/支出分类/电话分类/纠纷类型）
-- ------------------------------------------------------------
INSERT OR IGNORE INTO t_dict_type (type_code, type_name) VALUES
 ('pay_mode',          '缴费模式'),
 ('equipment_type',    '设备类型'),
 ('emergency_scene',   '应急场景'),
 ('expense_category',  '支出分类'),
 ('phone_category',    '电话分类'),
 ('dispute_type',      '纠纷类型');

-- 缴费模式
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('pay_mode', 'yearly',   '按年',   1, 0),
 ('pay_mode', 'monthly',  '按月',   2, 0),
 ('pay_mode', 'temporary','临时',   3, 0);

-- 设备类型（BR-EQP-05 可扩展）
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('equipment_type', 'elevator',   '电梯',   1, 0),
 ('equipment_type', 'cctv',       '监控',   2, 0),
 ('equipment_type', 'pump',       '水泵',   3, 0),
 ('equipment_type', 'power_room', '配电房', 4, 0);

-- 应急场景（六大场景）
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('emergency_scene', 'fire',          '火警',     1, 0),
 ('emergency_scene', 'elevator_trap', '电梯困人', 2, 0),
 ('emergency_scene', 'pipe_burst',    '水管爆裂', 3, 0),
 ('emergency_scene', 'power_outage',  '停电',     4, 0),
 ('emergency_scene', 'security',      '治安事件', 5, 0);

-- 支出分类（工资/维护/水电/外包/杂项）
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('expense_category', 'salary',     '工资',   1, 0),
 ('expense_category', 'maintenance','维修维护',2, 0),
 ('expense_category', 'utilities',  '水电',   3, 0),
 ('expense_category', 'outsourcing','外包',   4, 0),
 ('expense_category', 'other',      '其他',   5, 0);

-- 电话分类
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('phone_category', 'emergency', '紧急', 1, 0),
 ('phone_category', 'repair',    '维修', 2, 0),
 ('phone_category', 'medical',   '医疗', 3, 0),
 ('phone_category', 'other',     '其他', 4, 0);

-- 纠纷类型
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('dispute_type', 'noise',      '噪音',   1, 0),
 ('dispute_type', 'water_leak', '漏水',   2, 0),
 ('dispute_type', 'parking',    '停车',   3, 0),
 ('dispute_type', 'fee',        '物业费', 4, 0),
 ('dispute_type', 'other',      '其他',   5, 0);

-- ------------------------------------------------------------
-- 系统参数 P-01~P-09（异常路径核对清单确认默认值，可界面调整）
-- ------------------------------------------------------------
INSERT OR IGNORE INTO t_param (param_key, param_value, remark) VALUES
 ('arrear.grace.days',               '0',      'P-01 欠费逾期宽限天数（0=到期日即逾期）'),
 ('auth.fail.limit',                 '5',      'P-02 连续登录失败锁定阈值（次）'),
 ('auth.lock.minutes',               '30',     'P-02 登录锁定时长（分钟）'),
 ('emergency.review.deadline.workdays','3',    'P-03 事故复盘时限（工作日）'),
 ('equipment.cycle.default',         '{"elevator_maintenance":"15d","annual_inspection":"1y"}', 'P-04 设备保养/年检周期类型默认值（单台可覆盖）'),
 ('receipt.reprint.policy',          'keep-original-no', 'P-05 收据补打保留原收据号并标注补打次数'),
 ('payment.overpay.mode',            'pre-deposit', 'P-06 收款多缴转预存款（多缴转存+自动抵扣+余额退还）'),
 ('emergency.no-match.policy',       'non-blocking-initiator-first', 'P-07 应急无人匹配不阻塞，发起人默认第一处置人'),
 ('dispute.close.types',             'mediated,settled,transferred', 'P-08 纠纷结案类型：调解成功/自行和解/转办'),
 ('backup.retain.count',             '30',     'P-09 备份保留份数'),
 ('backup.auto.daily',               'true',   'P-09 每日自动备份开关');

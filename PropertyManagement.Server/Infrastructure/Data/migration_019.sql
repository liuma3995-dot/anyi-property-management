-- M6 评审缺陷修复：契约增补整体落地（CHG-M6-EMG/ORG/TEL/DIS/EQP/COM）。
-- 版本：019（2026-09-06）
-- 说明：全部使用 ADD COLUMN / CREATE TABLE IF NOT EXISTS，可重复执行。

-- ===== EMG 应急处置 =====
ALTER TABLE t_emergency_event ADD COLUMN close_summary TEXT;      -- 结案处置结果与物资消耗（BR-EMG-02）
ALTER TABLE t_emergency_event ADD COLUMN closed_at TEXT;          -- 结案时间（P-03 复盘时限/统计口径）
ALTER TABLE t_emergency_review ADD COLUMN review_no TEXT;         -- 复盘编号 FP-yyMM-序号
ALTER TABLE t_emergency_review ADD COLUMN host_name TEXT;         -- 主持人
ALTER TABLE t_emergency_review ADD COLUMN plan_date TEXT;         -- 复盘日期
ALTER TABLE t_emergency_review ADD COLUMN completion_rate INTEGER;-- 完成度 0~100
ALTER TABLE t_event_status_log ADD COLUMN operator TEXT;          -- 操作人留痕
ALTER TABLE t_event_status_log ADD COLUMN action TEXT;            -- 动作类型（发起/处置/结案/复盘/撤销/补录）
CREATE TABLE IF NOT EXISTS t_emergency_review_item (              -- 复盘改进措施清单（PG-EMG-04）
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    review_id  INTEGER NOT NULL,
    content    TEXT    NOT NULL,        -- 措施
    owner      TEXT,                   -- 责任人
    due_date   TEXT,                   -- 期限
    status     INTEGER NOT NULL DEFAULT 0, -- 0待开展 1进行中 2已完成
    sort       INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (review_id) REFERENCES t_emergency_review (id)
);

-- ===== ORG 人员组织 =====
ALTER TABLE t_employee ADD COLUMN emp_no TEXT;                    -- 工号（唯一业务键）
ALTER TABLE t_shift ADD COLUMN min_required INTEGER NOT NULL DEFAULT 1; -- 班次需求人数（缺员判定 BR-ORG-04）
ALTER TABLE t_attendance ADD COLUMN review_at TEXT;               -- 审核时间留痕
ALTER TABLE t_attendance ADD COLUMN abnormal_type TEXT;           -- 异常类型（迟到/缺卡）

-- ===== TEL 便民电话簿 =====
ALTER TABLE t_phone_entry ADD COLUMN employee_id INTEGER;         -- 员工弱关联（同步幂等）
ALTER TABLE t_phone_entry ADD COLUMN name_pinyin TEXT;            -- 全拼（排序/搜索）
ALTER TABLE t_phone_entry ADD COLUMN name_initials TEXT;          -- 拼音首字母（搜索）
ALTER TABLE t_phone_entry ADD COLUMN disable_source INTEGER NOT NULL DEFAULT 0; -- 0手工 1离职联动
CREATE TABLE IF NOT EXISTS t_phone_call_log (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    entry_id      INTEGER,
    called_phone  TEXT    NOT NULL,
    caller        TEXT,
    duration_sec  INTEGER NOT NULL DEFAULT 0,
    result        TEXT,
    note          TEXT,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS ix_phone_call_entry ON t_phone_call_log (entry_id, created_at);

-- ===== DIS 民事纠纷调解 =====
ALTER TABLE t_dispute_case ADD COLUMN level INTEGER;              -- 纠纷等级 0一般 1较大 2重大
ALTER TABLE t_dispute_case ADD COLUMN close_summary TEXT;         -- 结案报告（必填留档）
ALTER TABLE t_dispute_record ADD COLUMN method TEXT;              -- 调解方式
ALTER TABLE t_dispute_record ADD COLUMN plan_summary TEXT;        -- 方案摘要
ALTER TABLE t_dispute_record ADD COLUMN party_opinion TEXT;       -- 当事人意见
ALTER TABLE t_dispute_record ADD COLUMN result TEXT;              -- 结果
ALTER TABLE t_dispute_record ADD COLUMN supplement_reason TEXT;   -- 补录原因（BR-DIS-04）

-- ===== EQP 设备资产台账 =====
ALTER TABLE t_device ADD COLUMN warranty_end TEXT;                -- 质保到期（PG-EQP-04 范围页签）
ALTER TABLE t_device ADD COLUMN contract_end TEXT;                -- 合同到期
ALTER TABLE t_device ADD COLUMN maintenance_cycle_override INTEGER; -- 单台保养周期覆盖（P-04）
ALTER TABLE t_maintenance_record ADD COLUMN cost DECIMAL;         -- 费用（元）
ALTER TABLE t_maintenance_record ADD COLUMN fail_reason TEXT;     -- 不合格说明（必填）
ALTER TABLE t_inspection_record ADD COLUMN cost DECIMAL;
ALTER TABLE t_inspection_record ADD COLUMN fail_reason TEXT;
ALTER TABLE t_fault_record ADD COLUMN fault_no TEXT;              -- 故障单号 WX-yyMM-序号（PG-EQP-03）
ALTER TABLE t_fault_record ADD COLUMN level INTEGER;              -- 故障级别 0一般 1重大
ALTER TABLE t_fault_record ADD COLUMN reporter TEXT;              -- 发现人
ALTER TABLE t_fault_record ADD COLUMN status INTEGER NOT NULL DEFAULT 0; -- 0待处理 1维修中 2已修复

-- ===== COM 系统设置 =====
ALTER TABLE t_dict_item ADD COLUMN display_value TEXT;            -- 显示值（PG-COM-01）
ALTER TABLE t_dict_item ADD COLUMN updated_by TEXT;               -- 修改人
ALTER TABLE t_dict_item ADD COLUMN updated_at TEXT;               -- 修改时间
ALTER TABLE t_audit_log ADD COLUMN user_name TEXT;                -- 操作人（冗余名，免 JOIN）
ALTER TABLE t_audit_log ADD COLUMN role TEXT;                     -- 角色
ALTER TABLE t_audit_log ADD COLUMN module TEXT;                   -- 模块
ALTER TABLE t_audit_log ADD COLUMN result TEXT;                   -- 结果（成功/失败）
ALTER TABLE t_audit_log ADD COLUMN ip_addr TEXT;                  -- IP 地址
ALTER TABLE t_backup ADD COLUMN backup_type TEXT;                 -- auto/manual（P-09）
ALTER TABLE t_backup ADD COLUMN period TEXT;                      -- 计划周期
ALTER TABLE t_backup ADD COLUMN operator TEXT;                    -- 操作人
ALTER TABLE t_backup ADD COLUMN reviewer TEXT;                    -- 复核人
ALTER TABLE t_backup ADD COLUMN result TEXT;                      -- 上次结果
ALTER TABLE t_backup ADD COLUMN kind TEXT;                        -- backup/restore（恢复/演练记录）
ALTER TABLE t_backup ADD COLUMN source_point TEXT;                -- 目标备份点
ALTER TABLE t_backup ADD COLUMN target TEXT;                      -- 目标位置
ALTER TABLE t_user ADD COLUMN password_changed_at TEXT;           -- 90 天强制改密
ALTER TABLE t_user ADD COLUMN must_change_password INTEGER NOT NULL DEFAULT 0; -- 首登强制改密
CREATE TABLE IF NOT EXISTS t_password_history (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id       INTEGER NOT NULL,
    password_hash TEXT    NOT NULL,
    created_at    TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
);
CREATE INDEX IF NOT EXISTS ix_password_history_user ON t_password_history (user_id, created_at);

CREATE INDEX IF NOT EXISTS ix_employee_emp_no ON t_employee (emp_no);
CREATE INDEX IF NOT EXISTS ix_audit_action_time ON t_audit_log (action, created_at);
CREATE INDEX IF NOT EXISTS ix_audit_user_time ON t_audit_log (user_id, created_at);

-- ===== M6 补充种子（迁移后执行，避免 seed.sql 先于迁移缺列） =====
-- ------------------------------------------------------------
-- M6 评审补充种子（2026-09-06）：补齐原型所需基础数据
-- ------------------------------------------------------------

-- 字典类型补齐（PG-COM-01 八类：缺 计费方式/证件类型/车位类型/班次定义）
INSERT OR IGNORE INTO t_dict_type (type_code, type_name) VALUES
 ('charge_mode',       '计费方式'),
 ('id_card_type',      '证件类型'),
 ('parking_type',      '车位类型'),
 ('shift_define',      '班次定义');

INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('charge_mode',   'prepay',   '预付费', 1, 0),
 ('charge_mode',   'postpay',  '后付费', 2, 0),
 ('id_card_type',  'id_card',  '居民身份证', 1, 0),
 ('id_card_type',  'passport', '护照',     2, 0),
 ('parking_type',  'owned',    '产权车位', 1, 0),
 ('parking_type',  'rent',     '租赁车位', 2, 0),
 ('parking_type',  'civil',    '人防车位', 3, 0),
 ('shift_define',  'morning',  '早班 08:00-16:00', 1, 0),
 ('shift_define',  'middle',   '中班 16:00-24:00', 2, 0),
 ('shift_define',  'night',    '晚班 00:00-08:00', 3, 0),
 ('shift_define',  'rest',     '休',             4, 0);

-- 便民电话簿分类（PG-TEL-01 五分类，BR-COM-05 落到业务表）
INSERT OR IGNORE INTO t_phone_category (name, sort, status, del_flag) VALUES
 ('物业服务中心', 1, 0, 0),
 ('工程维修',     2, 0, 0),
 ('紧急电话',     3, 0, 0),
 ('政府 / 市政',  4, 0, 0),
 ('商圈服务',     5, 0, 0);

-- 紧急电话默认置顶（BR-TEL-03：119/120/110 默认置顶禁删）
INSERT INTO t_phone_entry (category_id, entry_type, name, phone, note, is_top, status, name_pinyin, name_initials)
SELECT c.id, 0, v.name, v.phone, v.note, 1, 0, v.py, v.pyi
FROM (SELECT '119' AS phone, '火警' AS name, '全国火警报警电话' AS note, 'huojing' AS py, 'hj' AS pyi UNION ALL
      SELECT '120', '急救', '全国医疗急救电话', 'jijiu', 'jj' UNION ALL
      SELECT '110', '报警', '全国报警电话', 'baojing', 'bj') v
JOIN t_phone_category c ON c.name = '紧急电话'
WHERE NOT EXISTS (SELECT 1 FROM t_phone_entry e WHERE e.phone = v.phone);

-- 应急场景与处置步骤（PG-EMG-01 六大场景）
INSERT OR IGNORE INTO t_emergency_scene (name, category, status, del_flag) VALUES
 ('火灾',       '消防', 0, 0),
 ('电梯困人',   '设备', 0, 0),
 ('水浸爆管',   '设施', 0, 0),
 ('燃气泄漏',   '安全', 0, 0),
 ('治安事件',   '秩序', 0, 0),
 ('人员受伤',   '救护', 0, 0);

INSERT INTO t_emergency_step (scene_id, step_no, content, version_no, status, role, time_limit, action)
SELECT s.id, v.step_no, v.content, 'v1', 0, v.role, v.time_limit, v.action
FROM (SELECT '火灾' AS scene, 1 AS step_no, '确认火情位置与火势级别' AS content, '全体值班' AS role, '1 分钟' AS time_limit, '广播' AS action UNION ALL
      SELECT '火灾', 2, '拨打 119 并上报负责人', '秩序班长', '2 分钟', '拨打' UNION ALL
      SELECT '火灾', 3, '启动消防泵与排烟', '工程值班', '3 分钟', '推送' UNION ALL
      SELECT '火灾', 4, '疏散着火单元住户', '客服/秩序', '5 分钟', '广播' UNION ALL
      SELECT '火灾', 5, '设置警戒并引导消防车', '秩序岗', '5 分钟', NULL UNION ALL
      SELECT '火灾', 6, '清点人数并复盘', '客服主管', '15 分钟', NULL UNION ALL
      SELECT '电梯困人', 1, '确认被困楼层与人数', '监控中心', '1 分钟', '广播' UNION ALL
      SELECT '电梯困人', 2, '安抚被困人员', '客服', '2 分钟', '推送' UNION ALL
      SELECT '电梯困人', 3, '通知电梯维保到场', '工程值班', '10 分钟', '拨打' UNION ALL
      SELECT '电梯困人', 4, '盘车放人（持证）', '维保单位', '20 分钟', NULL UNION ALL
      SELECT '电梯困人', 5, '故障排查后恢复运行', '工程值班', '30 分钟', NULL UNION ALL
      SELECT '水浸爆管', 1, '关闭爆管阀门', '工程值班', '2 分钟', '推送' UNION ALL
      SELECT '水浸爆管', 2, '通知受影响住户', '客服', '5 分钟', '广播' UNION ALL
      SELECT '水浸爆管', 3, '排水与设施保护', '工程/保洁', '10 分钟', NULL UNION ALL
      SELECT '水浸爆管', 4, '评估损失并拍照留档', '客服主管', '20 分钟', NULL UNION ALL
      SELECT '水浸爆管', 5, '修复管路并恢复供水', '工程值班', '当日', NULL UNION ALL
      SELECT '燃气泄漏', 1, '禁明火禁电梯禁电器', '监控中心', '立即', '广播' UNION ALL
      SELECT '燃气泄漏', 2, '关闭燃气总阀', '工程值班', '2 分钟', '推送' UNION ALL
      SELECT '燃气泄漏', 3, '拨打 119 与燃气公司', '秩序班长', '3 分钟', '拨打' UNION ALL
      SELECT '燃气泄漏', 4, '疏散泄漏单元及周边', '客服/秩序', '5 分钟', '广播' UNION ALL
      SELECT '燃气泄漏', 5, '开窗通风并检测浓度', '工程值班', '10 分钟', NULL UNION ALL
      SELECT '燃气泄漏', 6, '燃气公司检测合格后恢复', '工程主管', '即时', NULL UNION ALL
      SELECT '治安事件', 1, '保护现场并劝阻升级', '秩序岗', '立即', NULL UNION ALL
      SELECT '治安事件', 2, '拨打 110 并上报', '秩序班长', '2 分钟', '拨打' UNION ALL
      SELECT '治安事件', 3, '调取监控留存证据', '监控中心', '5 分钟', '推送' UNION ALL
      SELECT '治安事件', 4, '配合警方处置', '秩序班长', '即时', NULL UNION ALL
      SELECT '人员受伤', 1, '现场急救与隔离危险源', '秩序岗', '立即', NULL UNION ALL
      SELECT '人员受伤', 2, '拨打 120 视伤情送医', '客服', '2 分钟', '拨打' UNION ALL
      SELECT '人员受伤', 3, '通知家属并留档', '客服', '10 分钟', '推送' UNION ALL
      SELECT '人员受伤', 4, '走工伤/保险流程', '客服主管', '当日', NULL) v
JOIN t_emergency_scene s ON s.name = v.scene
WHERE NOT EXISTS (
    SELECT 1 FROM t_emergency_step x
    WHERE x.scene_id = s.id AND x.step_no = v.step_no AND x.status = 0);

-- 纠纷类型业务表（PG-DIS-01，BR-DIS-01）
INSERT OR IGNORE INTO t_dispute_type (name, status) VALUES
 ('漏水',       0),
 ('噪音',       0),
 ('装修',       0),
 ('邻里',       0),
 ('宠物扰邻',   0);

-- 人员组织基础数据（PG-ORG-01/02 演示基线：部门/岗位/班次/员工）
INSERT OR IGNORE INTO t_department (name, status) VALUES
 ('客服部', 0), ('工程部', 0), ('安保部', 0), ('保洁部', 0), ('物业办', 0);

INSERT INTO t_position (dept_id, name)
SELECT d.id, v.name FROM (SELECT '客服部' AS dept, '客服主管' AS name UNION ALL
                          SELECT '客服部', '客服专员' UNION ALL
                          SELECT '工程部', '工程主管' UNION ALL
                          SELECT '工程部', '工程值班' UNION ALL
                          SELECT '安保部', '保安班长' UNION ALL
                          SELECT '安保部', '保安员' UNION ALL
                          SELECT '保洁部', '保洁领班' UNION ALL
                          SELECT '物业办', '负责人') v
JOIN t_department d ON d.name = v.dept
WHERE NOT EXISTS (SELECT 1 FROM t_position p WHERE p.dept_id = d.id AND p.name = v.name);

INSERT INTO t_employee (dept_id, position_id, name, phone, hire_date, status, emp_no)
SELECT d.id, p.id, v.name, v.phone, '2024-03-01', 0, 'YG-' || printf('%03d', v.seq)
FROM (SELECT 1 AS seq, '张强' AS name, '13800002233' AS phone, '安保部' AS dept, '保安班长' AS pos UNION ALL
      SELECT 2, '李伟', '13800003457', '安保部', '保安员' UNION ALL
      SELECT 3, '刘芳', '13800004567', '客服部', '客服主管' UNION ALL
      SELECT 4, '赵敏', '13800005678', '客服部', '客服专员' UNION ALL
      SELECT 5, '王工', '13800006789', '工程部', '工程主管' UNION ALL
      SELECT 6, '陈勇', '13800007890', '工程部', '工程值班' UNION ALL
      SELECT 7, '孙浩', '13800008901', '保洁部', '保洁领班' UNION ALL
      SELECT 8, '周婷', '13800009012', '物业办', '负责人') v
JOIN t_department d ON d.name = v.dept
JOIN t_position p ON p.dept_id = d.id AND p.name = v.pos
WHERE NOT EXISTS (SELECT 1 FROM t_employee e WHERE e.name = v.name AND e.del_flag = 0);

INSERT OR IGNORE INTO t_shift (name, start_time, end_time, min_required) VALUES
 ('早班', '08:00', '16:00', 2),
 ('中班', '16:00', '24:00', 1),
 ('晚班', '00:00', '08:00', 1),
 ('休',   '',       '',      0);

-- 维保单位（PG-EQP 演示基线）
INSERT OR IGNORE INTO t_vendor (name, contact, phone, del_flag) VALUES
 ('迅达电梯维保', '马师傅', '13900001122', 0),
 ('消防设施维保', '高工',   '13900002233', 0);

-- 设备类型（PG-EQP 演示基线，P-04 默认保养周期）
INSERT OR IGNORE INTO t_device_type (name, maintenance_cycle, status, del_flag) VALUES
 ('客梯',   '30', 0, 0),
 ('消防设施', '90', 0, 0),
 ('水泵',   '30', 0, 0),
 ('监控系统', '30', 0, 0);

-- ============================================================
-- dev-seed.sql（M4 T4F-1-7 演示造数 v2）
-- 独立于正式 seed.sql；仅当 App.config DevSeedEnabled=true 时执行；
-- 幂等可重复执行（INSERT OR IGNORE / NOT EXISTS 存在性检查）。
-- 用途：对齐 quanju.pen PG-FIN-01 原型 7 行收费项目示例 + 财务全流程演示主数据
--       （主数据录入界面属 M5，M4 期间以直插方式造数，不污染正式 seed）。
-- 版本：v2（2026-09-01）
-- ============================================================

-- 楼栋/单元（UNIQUE 幂等）
INSERT OR IGNORE INTO t_building (community_id, building_no, floors)
SELECT id, '1号楼', 6 FROM t_community WHERE name = '安怡小区';

INSERT OR IGNORE INTO t_unit (building_id, unit_no)
SELECT id, '1单元' FROM t_building WHERE building_no = '1号楼';

-- 房产 8 套（幂等：unit_id + room_no 去重；building_id 由单元所属楼栋带出）
INSERT OR IGNORE INTO t_property (building_id, unit_id, room_no, area, usage, status)
SELECT u.building_id, u.id, r.room, r.area, 0, 1
FROM t_unit u
JOIN (
    SELECT '101' room, 88.50 area UNION ALL SELECT '102', 82.30
    UNION ALL SELECT '201', 88.50 UNION ALL SELECT '202', 82.30
    UNION ALL SELECT '301', 88.50 UNION ALL SELECT '302', 82.30
    UNION ALL SELECT '401', 88.50 UNION ALL SELECT '402', 82.30
) r ON 1 = 1
WHERE u.unit_no = '1单元';

-- 业主 6 户（存在性检查幂等）
INSERT INTO t_owner (name, id_card, phone, status)
SELECT '张伟', '110101198501011234', '13800000001', 0 WHERE NOT EXISTS (SELECT 1 FROM t_owner WHERE name = '张伟');
INSERT INTO t_owner (name, id_card, phone, status)
SELECT '李娜', '110101198702022345', '13800000002', 0 WHERE NOT EXISTS (SELECT 1 FROM t_owner WHERE name = '李娜');
INSERT INTO t_owner (name, id_card, phone, status)
SELECT '陈强', '110101198903033456', '13800000003', 0 WHERE NOT EXISTS (SELECT 1 FROM t_owner WHERE name = '陈强');
INSERT INTO t_owner (name, id_card, phone, status)
SELECT '王芳', '110101199001014567', '13800000004', 0 WHERE NOT EXISTS (SELECT 1 FROM t_owner WHERE name = '王芳');
INSERT INTO t_owner (name, id_card, phone, status)
SELECT '刘洋', '110101199202025678', '13800000005', 0 WHERE NOT EXISTS (SELECT 1 FROM t_owner WHERE name = '刘洋');
INSERT INTO t_owner (name, id_card, phone, status)
SELECT '赵敏', '110101199303036789', '13800000006', 0 WHERE NOT EXISTS (SELECT 1 FROM t_owner WHERE name = '赵敏');

-- 业主-房产关系（UNIQUE(property_id, owner_id, effective_at) 幂等）
INSERT OR IGNORE INTO t_owner_property_rel (property_id, owner_id, rel_type, effective_at)
SELECT p.id, o.id, 0, date('now','localtime')
FROM t_property p JOIN t_owner o ON o.name = '张伟' WHERE p.room_no = '101' AND p.del_flag = 0;
INSERT OR IGNORE INTO t_owner_property_rel (property_id, owner_id, rel_type, effective_at)
SELECT p.id, o.id, 0, date('now','localtime')
FROM t_property p JOIN t_owner o ON o.name = '李娜' WHERE p.room_no = '102' AND p.del_flag = 0;
INSERT OR IGNORE INTO t_owner_property_rel (property_id, owner_id, rel_type, effective_at)
SELECT p.id, o.id, 0, date('now','localtime')
FROM t_property p JOIN t_owner o ON o.name = '陈强' WHERE p.room_no = '201' AND p.del_flag = 0;
INSERT OR IGNORE INTO t_owner_property_rel (property_id, owner_id, rel_type, effective_at)
SELECT p.id, o.id, 0, date('now','localtime')
FROM t_property p JOIN t_owner o ON o.name = '王芳' WHERE p.room_no = '202' AND p.del_flag = 0;
INSERT OR IGNORE INTO t_owner_property_rel (property_id, owner_id, rel_type, effective_at)
SELECT p.id, o.id, 0, date('now','localtime')
FROM t_property p JOIN t_owner o ON o.name = '刘洋' WHERE p.room_no = '301' AND p.del_flag = 0;
INSERT OR IGNORE INTO t_owner_property_rel (property_id, owner_id, rel_type, effective_at)
SELECT p.id, o.id, 0, date('now','localtime')
FROM t_property p JOIN t_owner o ON o.name = '赵敏' WHERE p.room_no = '302' AND p.del_flag = 0;

-- 车位 2 个（UNIQUE(space_no) 幂等）
INSERT OR IGNORE INTO t_parking_space (space_no, space_type, property_id, owner_id)
SELECT 'B1-01', 0, p.id, o.id FROM t_property p JOIN t_owner o ON o.name = '张伟' WHERE p.room_no = '101';
INSERT OR IGNORE INTO t_parking_space (space_no, space_type, property_id, owner_id)
SELECT 'B1-02', 0, p.id, o.id FROM t_property p JOIN t_owner o ON o.name = '李娜' WHERE p.room_no = '102';

-- 演示自定义字典项：计价方式「按卡」（remark=张；T4F-1-5）
INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, remark, sort, status)
VALUES ('charge_method', 'card', '按卡', '张', 7, 0);

-- 收费项目 7 行（T4F-1-7，对齐原型 XM-001~007；pay_mode 为兼容遗留字段，表单已改用计价方式字典）
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '物业服务费', '物业费', 'area', '按建筑面积', 2.80, '㎡', 1, '', 0, 0, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '物业服务费' AND del_flag = 0);
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '生活垃圾处理费', '代收代缴', 'house', '按户', 8.00, '户', 1, '', 0, 0, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '生活垃圾处理费' AND del_flag = 0);
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '地下车位管理费', '车位费', 'parking', '按车位', 50.00, '车位', 1, '', 1, 0, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '地下车位管理费' AND del_flag = 0);
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '水电公摊费', '公摊分摊', 'share', '按户均摊', 36.00, '户', 3, '每季', 0, 0, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '水电公摊费' AND del_flag = 0);
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '装修管理费', '一次性', 'onetime', '一次性', 800.00, '户', 2, '一次性', 0, 0, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '装修管理费' AND del_flag = 0);
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '门禁卡工本费', '一次性', 'card', '按卡', 20.00, '张', 2, '一次性', 0, 1, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '门禁卡工本费' AND del_flag = 0);
INSERT INTO t_charge_item (name, category, method_code, method_name, unit_price, price_unit, cycle_type, cycle_name, object_type, status, pay_mode)
SELECT '二次供水清洗费', '代收代缴', 'house', '按户', 15.00, '户', 3, '每半年', 0, 1, 1
WHERE NOT EXISTS (SELECT 1 FROM t_charge_item WHERE name = '二次供水清洗费' AND del_flag = 0);

-- 计费周期（cycle_type: 0按年 1按月 2一次性 3自定义；幂等）
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 1, '2026-05-01', '2026-05-31' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 1 AND start_date = '2026-05-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 1, '2026-06-01', '2026-06-30' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 1 AND start_date = '2026-06-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 1, '2026-07-01', '2026-07-31' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 1 AND start_date = '2026-07-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 1, '2026-08-01', '2026-08-31' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 1 AND start_date = '2026-08-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 1, '2026-09-01', '2026-09-30' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 1 AND start_date = '2026-09-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 0, '2026-01-01', '2026-12-31' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 0 AND start_date = '2026-01-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 3, '2026-04-01', '2026-06-30' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 3 AND start_date = '2026-04-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 3, '2026-01-01', '2026-06-30' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 3 AND start_date = '2026-01-01');
INSERT INTO t_billing_cycle (cycle_type, start_date, end_date)
SELECT 2, '2026-08-01', '2026-08-31' WHERE NOT EXISTS (SELECT 1 FROM t_billing_cycle WHERE cycle_type = 2 AND start_date = '2026-08-01');

-- 支出分类（与字典口径一致，存在性检查幂等）
INSERT INTO t_expense_category (name, category_type, status)
SELECT '工资', 'salary', 0 WHERE NOT EXISTS (SELECT 1 FROM t_expense_category WHERE name = '工资');
INSERT INTO t_expense_category (name, category_type, status)
SELECT '维修维护', 'maintenance', 0 WHERE NOT EXISTS (SELECT 1 FROM t_expense_category WHERE name = '维修维护');
INSERT INTO t_expense_category (name, category_type, status)
SELECT '水电', 'utilities', 0 WHERE NOT EXISTS (SELECT 1 FROM t_expense_category WHERE name = '水电');
INSERT INTO t_expense_category (name, category_type, status)
SELECT '外包', 'outsourcing', 0 WHERE NOT EXISTS (SELECT 1 FROM t_expense_category WHERE name = '外包');
INSERT INTO t_expense_category (name, category_type, status)
SELECT '其他', 'other', 0 WHERE NOT EXISTS (SELECT 1 FROM t_expense_category WHERE name = '其他');

-- 预存款示例（owner_id 唯一幂等）：王芳预存 300 元
INSERT OR IGNORE INTO t_pre_deposit (owner_id, balance)
SELECT o.id, 300.00 FROM t_owner o WHERE o.name = '王芳';

-- 历史欠费账单（演示欠费台账 + 账龄>90 天标红；按面积计费金额 = 单价 × 面积；状态 2=逾期）
INSERT INTO t_bill (charge_item_id, property_id, parking_id, cycle_id, amount, paid_amount, status, due_at, generate_batch_id, del_flag)
SELECT ci.id, p.id, NULL, cy.id, ROUND(ci.unit_price * p.area, 2), 0, 2, '2026-05-31', NULL, 0
FROM t_charge_item ci
JOIN t_property p ON p.room_no IN ('101', '102', '201')
JOIN t_billing_cycle cy ON cy.cycle_type = 1 AND cy.start_date = '2026-05-01'
WHERE ci.name = '物业服务费'
  AND p.del_flag = 0
  AND NOT EXISTS (
      SELECT 1 FROM t_bill b
      WHERE b.charge_item_id = ci.id AND b.property_id = p.id AND b.cycle_id = cy.id AND b.del_flag = 0
  );

-- 车位欠费（B1-01 地下车位管理费 2026-05，账龄 93 天）
INSERT INTO t_bill (charge_item_id, property_id, parking_id, cycle_id, amount, paid_amount, status, due_at, generate_batch_id, del_flag)
SELECT ci.id, NULL, ps.id, cy.id, ci.unit_price, 0, 2, '2026-05-31', NULL, 0
FROM t_charge_item ci
JOIN t_parking_space ps ON ps.space_no = 'B1-01'
JOIN t_billing_cycle cy ON cy.cycle_type = 1 AND cy.start_date = '2026-05-01'
WHERE ci.name = '地下车位管理费'
  AND ps.del_flag = 0
  AND NOT EXISTS (
      SELECT 1 FROM t_bill b
      WHERE b.charge_item_id = ci.id AND b.parking_id = ps.id AND b.cycle_id = cy.id AND b.del_flag = 0
  );

-- 历史已缴账单（101 户 2026-06 物业服务费已缴：2.80 × 88.50 = 247.80）
INSERT INTO t_bill (charge_item_id, property_id, parking_id, cycle_id, amount, paid_amount, status, due_at, generate_batch_id, del_flag)
SELECT ci.id, p.id, NULL, cy.id, ROUND(ci.unit_price * p.area, 2), ROUND(ci.unit_price * p.area, 2), 3, '2026-06-30', NULL, 0
FROM t_charge_item ci
JOIN t_property p ON p.room_no = '101'
JOIN t_billing_cycle cy ON cy.cycle_type = 1 AND cy.start_date = '2026-06-01'
WHERE ci.name = '物业服务费'
  AND p.del_flag = 0
  AND NOT EXISTS (
      SELECT 1 FROM t_bill b
      WHERE b.charge_item_id = ci.id AND b.property_id = p.id AND b.cycle_id = cy.id AND b.del_flag = 0
  );

-- 对应缴费记录 + 收据（幂等：按收据号）
INSERT INTO t_payment (bill_id, amount, pay_method, paid_at, status, to_pre_deposit)
SELECT b.id, b.amount, 0, '2026-06-28 10:00:00', 0, 0
FROM t_bill b
WHERE b.status = 3 AND b.paid_amount > 0 AND b.generate_batch_id IS NULL
  AND NOT EXISTS (SELECT 1 FROM t_payment p WHERE p.bill_id = b.id);

INSERT INTO t_receipt (payment_id, receipt_no, print_count, printed_at)
SELECT p.id, 'RC-DEMO-20260628-001', 1, '2026-06-28 10:00:00'
FROM t_payment p
WHERE p.paid_at = '2026-06-28 10:00:00'
  AND NOT EXISTS (SELECT 1 FROM t_receipt r WHERE r.receipt_no = 'RC-DEMO-20260628-001');

-- 内置计价方式默认单价单位（与 migration_004 口径一致，幂等）
UPDATE t_dict_item SET remark = '㎡'   WHERE type_code = 'charge_method' AND item_code = 'area';
UPDATE t_dict_item SET remark = '户'   WHERE type_code = 'charge_method' AND item_code IN ('house','share','onetime');
UPDATE t_dict_item SET remark = '车位' WHERE type_code = 'charge_method' AND item_code = 'parking';
UPDATE t_dict_item SET remark = ''    WHERE type_code = 'charge_method' AND item_code = 'step';

-- ============================================================
-- 设备资产台账（PG-EQP-01~04）演示基线
-- 目的：设备列表 / 保养年检登记 / 故障登记 / 到期提醒 四页具备可演示、可验收数据
-- 说明：全部幂等（NOT EXISTS 守卫）；仅 DevSeedEnabled=true 的开发机执行
-- ============================================================

-- 1) 设备类型：原型 8 大类（BR-EQP-05 类型可扩展；含 P-04 默认保养周期）
INSERT INTO t_device_type (name, maintenance_cycle, status, del_flag)
SELECT v.name, v.cycle, 0, 0 FROM (
    SELECT '电梯' AS name, 90 AS cycle UNION ALL
    SELECT '消防', 90 UNION ALL
    SELECT '给排水', 90 UNION ALL
    SELECT '门禁', 180 UNION ALL
    SELECT '安防', 180 UNION ALL
    SELECT '供配电', 180 UNION ALL
    SELECT '暖通', 180 UNION ALL
    SELECT '照明', 365) v
WHERE NOT EXISTS (SELECT 1 FROM t_device_type t WHERE t.name = v.name);

-- migration_019 遗留演示类型若无在册设备则停用，保持类别下拉与原型 8 大类一致
UPDATE t_device_type SET status = 1
WHERE del_flag = 0 AND status = 0 AND name IN ('客梯', '消防设施', '水泵', '监控系统')
  AND NOT EXISTS (SELECT 1 FROM t_device d WHERE d.type_id = t_device_type.id AND d.del_flag = 0);

-- 2) 维保单位（BR-EQP-06 可复用，与设备/支出关联）
INSERT INTO t_vendor (name, contact, phone, del_flag)
SELECT v.name, v.contact, v.phone, 0 FROM (
    SELECT '迅达电梯维保' AS name, '马师傅' AS contact, '13900001122' AS phone UNION ALL
    SELECT '消防设施维保', '高工', '13900002233' UNION ALL
    SELECT '安泰消防', '陈工', '13900003344' UNION ALL
    SELECT '水务设备维保', '刘师傅', '13900004455' UNION ALL
    SELECT '物业工程部', '王工', '13900006789') v
WHERE NOT EXISTS (SELECT 1 FROM t_vendor t WHERE t.name = v.name AND t.del_flag = 0);

-- 3) 设备台账（显式编号对齐原型 EQP-xxxx；品牌型号/投运日期/质保/合同到期）
INSERT INTO t_device (id, type_id, name, location, status, enable_date, brand_model, warranty_end, contract_end, maintenance_cycle_override, del_flag, created_at, updated_at)
SELECT v.id, (SELECT id FROM t_device_type WHERE name = v.type_name LIMIT 1), v.name, v.location, v.status, v.enable_date, v.brand_model,
       CASE WHEN v.warranty_days IS NULL THEN NULL ELSE date('now', 'localtime', '+' || v.warranty_days || ' days') END,
       CASE WHEN v.contract_days IS NULL THEN NULL ELSE date('now', 'localtime', '+' || v.contract_days || ' days') END,
       v.cycle_override, 0, datetime('now', 'localtime'), datetime('now', 'localtime')
FROM (
    SELECT  9 AS id, '电梯' AS type_name, '老扶梯（已封存）' AS name, '商业裙房' AS location, 2 AS status, '2012-04-10' AS enable_date, '三菱 / J 系列' AS brand_model, NULL AS warranty_days, NULL AS contract_days, NULL AS cycle_override UNION ALL
    SELECT 42, '门禁', '东门道闸', '小区东门', 0, '2021-07-01', '捷顺 / JSD40', NULL, NULL, NULL UNION ALL
    SELECT 63, '供配电', '发电机组', '配电房 B1', 0, '2019-05-18', '康明斯 / C90', NULL, NULL, NULL UNION ALL
    SELECT 87, '电梯', '3 单元客梯', '3栋3单元', 0, '2019-05-20', '迅达 / S3300', 120, NULL, 180 UNION ALL
    SELECT 102, '消防', '消防泵组', '泵房 B1', 0, '2019-05-18', '正压 / XBD6', NULL, 45, NULL UNION ALL
    SELECT 115, '给排水', '二次供水泵 2#', '泵房 B1', 1, '2020-03-11', '南方 / CDL32', 33, NULL, NULL UNION ALL
    SELECT 128, '安防', '监控摄像头 N23', '2栋周界', 1, '2022-01-15', '海康 / DS-2CD3', NULL, NULL, NULL UNION ALL
    SELECT 140, '照明', '园区路灯回路 A', '园区主干道', 0, '2021-09-01', '亚明 / LED-120W', NULL, NULL, NULL UNION ALL
    SELECT 151, '暖通', '中央空调机组', '裙房屋顶', 1, '2021-03-15', '格力 / LSBLG', NULL, NULL, NULL) v
WHERE NOT EXISTS (SELECT 1 FROM t_device d WHERE d.id = v.id);

-- 4) 设备状态留痕（BR-EQP-01 只追加）
INSERT INTO t_device_status_log (device_id, old_status, new_status, reason)
SELECT v.device_id, v.old_status, v.new_status, v.reason FROM (
    SELECT  9 AS device_id, 0 AS old_status, 2 AS new_status, '设备封存停用' AS reason UNION ALL
    SELECT 102, -1, 0, '登记' UNION ALL
    SELECT 115, 0, 1, '年检不合格转维修' UNION ALL
    SELECT 128, 0, 1, '故障未修复' UNION ALL
    SELECT 151, 0, 1, '故障未修复') v
WHERE EXISTS (SELECT 1 FROM t_device d WHERE d.id = v.device_id)
  AND NOT EXISTS (SELECT 1 FROM t_device_status_log l
                  WHERE l.device_id = v.device_id AND l.new_status = v.new_status AND COALESCE(l.reason, '') = v.reason);

-- 5) 保养记录（P-04：下次保养 = 最近保养日期 + 周期，登记即顺延）
INSERT INTO t_maintenance_record (device_id, vendor_id, m_date, content, result, cost)
SELECT v.device_id, (SELECT id FROM t_vendor WHERE name = v.vendor AND del_flag = 0 LIMIT 1), date('now', 'localtime', v.offset), v.content, '合格', v.cost
FROM (
    SELECT 102 AS device_id, '安泰消防' AS vendor, '-79 days' AS offset, '主备泵切换正常，压力表读数 0.6MPa，阀门无渗漏，更换润滑脂 1 支。' AS content, 350.00 AS cost UNION ALL
    SELECT 87, '迅达电梯维保', '-162 days', '季度保养：门机与层门间隙调整，曳引机润滑，平层精度复核合格。', 900.00 UNION ALL
    SELECT 42, '物业工程部', '-155 days', '道闸起落杆限位校准，齿轮油补充，遥控与地感联动测试正常。', 120.00 UNION ALL
    SELECT 63, '物业工程部', '-75 days', '发电机组空载试机 30 分钟，更换机油与滤芯，蓄电池电压正常。', 480.00 UNION ALL
    SELECT 140, '物业工程部', '-165 days', '园区路灯回路 A 绝缘检测，更换故障灯头 3 只。', 260.00 UNION ALL
    SELECT 151, '物业工程部', '-70 days', '冷媒压力检测与补充，冷凝器清洗，风机皮带张紧度调整。', 420.00) v
WHERE EXISTS (SELECT 1 FROM t_device d WHERE d.id = v.device_id AND d.del_flag = 0)
  AND NOT EXISTS (SELECT 1 FROM t_maintenance_record m WHERE m.device_id = v.device_id);

-- 6) 年检记录（BR-EQP-02：年检按年度，下次年检 = 最近年检 + 1 年）
INSERT INTO t_inspection_record (device_id, vendor_id, i_date, result, cost)
SELECT v.device_id, (SELECT id FROM t_vendor WHERE name = v.vendor AND del_flag = 0 LIMIT 1), date('now', 'localtime', v.offset), '合格', v.cost
FROM (
    SELECT 102 AS device_id, '消防设施维保' AS vendor, '-103 days' AS offset, 800.00 AS cost UNION ALL
    SELECT 9, '消防设施维保', '-383 days', 600.00 UNION ALL
    SELECT 42, '物业工程部', '-150 days', 200.00 UNION ALL
    SELECT 63, '物业工程部', '-150 days', 300.00 UNION ALL
    SELECT 87, '迅达电梯维保', '-200 days', 1200.00 UNION ALL
    SELECT 115, '水务设备维保', '-150 days', 300.00 UNION ALL
    SELECT 128, '物业工程部', '-150 days', 150.00 UNION ALL
    SELECT 140, '物业工程部', '-150 days', 150.00 UNION ALL
    SELECT 151, '物业工程部', '-100 days', 400.00) v
WHERE EXISTS (SELECT 1 FROM t_device d WHERE d.id = v.device_id AND d.del_flag = 0)
  AND NOT EXISTS (SELECT 1 FROM t_inspection_record i WHERE i.device_id = v.device_id);

-- 7) 应急事件（PG-EQP-03 关联应急下拉 + 故障单 EM 弱关联）
INSERT INTO t_emergency_event (scene_id, event_time, location, description, status, step_version, record_pending, del_flag, event_no, level, detail_location)
SELECT (SELECT id FROM t_emergency_scene WHERE name = v.scene LIMIT 1), v.event_time, v.location, v.description,
       v.status, 'v1', 0, 0, v.event_no, v.level, v.location
FROM (
    SELECT '电梯困人' AS scene, date('now', 'localtime', '-14 days') || ' 06:40' AS event_time, '3栋3单元' AS location,
           '客梯 3 单元运行中急停，2 人被困 6 层，维保到场盘车放人。' AS description, 2 AS status,
           'EM-' || replace(substr(date('now', 'localtime'), 3, 5), '-', '') || '-10' AS event_no, 1 AS level UNION ALL
    SELECT '火灾', date('now', 'localtime', '-30 days') || ' 14:26', '1栋2单元',
           '巡逻发现楼梯间有烟雾，疑似配电井过热，无明火。', 2,
           'EM-' || replace(substr(date('now', 'localtime'), 3, 5), '-', '') || '-11', 2) v
WHERE EXISTS (SELECT 1 FROM t_emergency_scene WHERE name = v.scene)
  AND NOT EXISTS (SELECT 1 FROM t_emergency_event e WHERE e.event_no = v.event_no);

-- 8) 故障记录（BR-EQP-04；状态 0 待接单 / 1 维修中 / 2 已修复；WX-yyMM-序号）
INSERT INTO t_fault_record (device_id, event_id, f_time, symptom, cause, handle, fault_no, level, reporter, status)
SELECT v.device_id, (SELECT id FROM t_emergency_event WHERE event_no = v.event_no LIMIT 1), v.f_time, v.symptom, v.cause, v.handle,
       'WX-' || replace(substr(date('now', 'localtime'), 3, 5), '-', '') || v.seq, v.level, v.reporter, v.status
FROM (
    SELECT 128 AS device_id, NULL AS event_no, date('now', 'localtime', '-14 days') || ' 06:55' AS f_time,
           '摄像头画面黑屏，重启无效，初步判断供电线路或机芯故障，影响 2 栋东南侧周界监控。' AS symptom,
           '' AS cause, '' AS handle, '-05' AS seq, 0 AS level, '保安 · 李伟（巡查）' AS reporter, 0 AS status UNION ALL
    SELECT 151, NULL, date('now', 'localtime', '-19 days') || ' 10:12',
           '中央空调机组压缩机异响，制冷量下降，疑似冷媒泄漏或压缩机故障。', '', '', '-03', 0, '工程值班 · 陈勇', 1 UNION ALL
    SELECT 115, NULL, date('now', 'localtime', '-43 days') || ' 15:03',
           '二次供水泵 2# 压力波动，运行噪声偏大。', '轴承磨损', '更换轴承并调试，运行恢复正常。', '-11', 0, '工程值班 · 陈勇', 2 UNION ALL
    SELECT 87, 'EM-' || replace(substr(date('now', 'localtime'), 3, 5), '-', '') || '-10', date('now', 'localtime', '-61 days') || ' 08:47',
           '客梯 3 单元运行中急停，2 人被困 6 层。', '门锁触点氧化接触不良', '维保到场盘车放人，更换门锁触点，试运行正常。', '-08', 1, '监控中心 · 刘芳', 2) v
WHERE EXISTS (SELECT 1 FROM t_device d WHERE d.id = v.device_id AND d.del_flag = 0)
  AND NOT EXISTS (SELECT 1 FROM t_fault_record f WHERE f.device_id = v.device_id);

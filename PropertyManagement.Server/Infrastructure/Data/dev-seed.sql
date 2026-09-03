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
SELECT id, '1号楼', 6 FROM t_community WHERE name = '澜庭小区';

INSERT OR IGNORE INTO t_unit (building_id, unit_no)
SELECT id, '1单元' FROM t_building WHERE building_no = '1号楼';

-- 房产 8 套（UNIQUE(unit_id, room_no) 幂等）
INSERT OR IGNORE INTO t_property (unit_id, room_no, area, usage, status)
SELECT u.id, r.room, r.area, 0, 1
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
-- ============================================================
-- migration_031.sql（M7 阶段③ T7-11-0：GAP-01 闭环）
-- 背景（根因）：migration_023 重建 t_property 时，schema.sql 中的
--   UNIQUE (unit_id, room_no) 随 DROP TABLE 一并丢失，而 dev-seed.sql 的
--   「INSERT OR IGNORE INTO t_property」依赖该唯一键做幂等判重；
--   失去约束后每次启动（DevSeedEnabled=true）都会重复插入同一房号，
--   并派生重复的业主关系与账单。
-- 本迁移：
--   1) 按 BR-INF-01 现代口径（楼栋 + 单元 + 房号；无单元按楼栋 + 房号）
--      对存量重复行去重：保留最早一行（MIN(id)），其余行软删（del_flag=1）；
--   2) 子记录先归并再软删，避免悬空引用与重复记账：
--      · t_owner_property_rel：同业主同生效日的重复关系软删留痕；
--      · t_bill：与保留房产「同收费项目 + 同计费周期 + 同缴费对象」的重复账单，
--                收款/退款/状态日志/催办日志改挂保留账单，已缴金额累加并按
--                BR-FIN-05 口径重算状态，重复账单软删；无对应保留账单的
--                （防御分支）直接改挂保留房产；
--      · t_dispute_case / t_parking_space → 改挂保留房产；
--   3) 补「等效唯一索引」ux_property_room_unique（COALESCE 覆盖 unit_id 为空的
--      无单元口径；部分索引 WHERE del_flag=0，与 BR-INF-01 服务层判重及软删口径一致），
--      恢复库级约束并使 dev-seed 的 INSERT OR IGNORE 重新幂等。
-- 说明：全程只软删（del_flag=1）不物理删除业务行；无表重建，id 不变。
-- 版本：031（2026-09-14）
-- ============================================================

-- Step 1：重复组与保留行（同 楼栋+单元+房号 且在用的最早一行）
CREATE TEMP TABLE _m031_keep AS
SELECT MIN(id)                   AS keep_id,
       COALESCE(building_id, -1) AS key_building,
       COALESCE(unit_id, -1)     AS key_unit,
       room_no                   AS key_room
FROM t_property
WHERE del_flag = 0
GROUP BY COALESCE(building_id, -1), COALESCE(unit_id, -1), room_no
HAVING COUNT(*) > 1;

CREATE TEMP TABLE _m031_dup AS
SELECT p.id AS dup_id, k.keep_id AS keep_id
FROM t_property p
JOIN _m031_keep k
  ON k.key_building = COALESCE(p.building_id, -1)
 AND k.key_unit     = COALESCE(p.unit_id, -1)
 AND k.key_room     = p.room_no
WHERE p.del_flag = 0 AND p.id <> k.keep_id;

-- Step 2：业主-房产关系归并（同 业主 + 生效日 的重复关系软删留痕）
UPDATE OR IGNORE t_owner_property_rel
   SET property_id = (SELECT d.keep_id FROM _m031_dup d WHERE d.dup_id = t_owner_property_rel.property_id),
       updated_at  = datetime('now','localtime')
 WHERE property_id IN (SELECT dup_id FROM _m031_dup);

UPDATE t_owner_property_rel
   SET del_flag = 1, updated_at = datetime('now','localtime')
 WHERE property_id IN (SELECT dup_id FROM _m031_dup);

-- Step 3：账单级归并映射（重复房产账单 ↔ 保留房产同键账单）
CREATE TEMP TABLE _m031_bill_dup AS
SELECT b.id AS dup_bill_id, k.id AS keep_bill_id
FROM t_bill b
JOIN _m031_dup d ON d.dup_id = b.property_id
JOIN t_bill k ON k.charge_item_id = b.charge_item_id
             AND k.cycle_id       = b.cycle_id
             AND k.property_id    = d.keep_id
             AND IFNULL(k.parking_id, -1) = IFNULL(b.parking_id, -1)
             AND k.del_flag = 0
WHERE b.del_flag = 0;

-- Step 4：收款流水 / 退款 / 账单状态日志 / 催办日志改挂保留账单
UPDATE t_payment
   SET bill_id = (SELECT m.keep_bill_id FROM _m031_bill_dup m WHERE m.dup_bill_id = t_payment.bill_id)
 WHERE bill_id IN (SELECT dup_bill_id FROM _m031_bill_dup);

UPDATE t_payment_refund
   SET bill_id = (SELECT m.keep_bill_id FROM _m031_bill_dup m WHERE m.dup_bill_id = t_payment_refund.bill_id)
 WHERE bill_id IN (SELECT dup_bill_id FROM _m031_bill_dup);

UPDATE t_bill_status_log
   SET bill_id = (SELECT m.keep_bill_id FROM _m031_bill_dup m WHERE m.dup_bill_id = t_bill_status_log.bill_id)
 WHERE bill_id IN (SELECT dup_bill_id FROM _m031_bill_dup);

UPDATE t_arrear_remind_log
   SET bill_id = (SELECT m.keep_bill_id FROM _m031_bill_dup m WHERE m.dup_bill_id = t_arrear_remind_log.bill_id)
 WHERE bill_id IN (SELECT dup_bill_id FROM _m031_bill_dup);

-- Step 5：保留账单累加重复账单已缴金额，并按「已缴/部分缴」口径重算状态（BR-FIN-05）
UPDATE t_bill
   SET paid_amount = paid_amount + IFNULL((
           SELECT SUM(b2.paid_amount)
           FROM t_bill b2 JOIN _m031_bill_dup m ON m.dup_bill_id = b2.id
           WHERE m.keep_bill_id = t_bill.id), 0),
       status      = CASE
           WHEN paid_amount + IFNULL((
                    SELECT SUM(b3.paid_amount)
                    FROM t_bill b3 JOIN _m031_bill_dup m3 ON m3.dup_bill_id = b3.id
                    WHERE m3.keep_bill_id = t_bill.id), 0) >= amount THEN 3
           WHEN paid_amount + IFNULL((
                    SELECT SUM(b4.paid_amount)
                    FROM t_bill b4 JOIN _m031_bill_dup m4 ON m4.dup_bill_id = b4.id
                    WHERE m4.keep_bill_id = t_bill.id), 0) > 0 THEN 1
           ELSE status END,
       updated_at  = datetime('now','localtime')
 WHERE id IN (SELECT keep_bill_id FROM _m031_bill_dup);

-- Step 6：重复账单软删留痕（保留原 property_id，避免与 ux_bill_property_cycle 冲突）
UPDATE t_bill
   SET del_flag = 1, updated_at = datetime('now','localtime')
 WHERE id IN (SELECT dup_bill_id FROM _m031_bill_dup);

-- Step 6b：防御分支——重复房产下无同键保留账单的账单，直接改挂保留房产
UPDATE OR IGNORE t_bill
   SET property_id = (SELECT d.keep_id FROM _m031_dup d WHERE d.dup_id = t_bill.property_id),
       updated_at  = datetime('now','localtime')
 WHERE property_id IN (SELECT dup_id FROM _m031_dup)
   AND del_flag = 0
   AND id NOT IN (SELECT dup_bill_id FROM _m031_bill_dup);

-- Step 7：纠纷案件 / 车位改挂保留房产
UPDATE t_dispute_case
   SET property_id = (SELECT d.keep_id FROM _m031_dup d WHERE d.dup_id = t_dispute_case.property_id),
       updated_at  = datetime('now','localtime')
 WHERE property_id IN (SELECT dup_id FROM _m031_dup);

UPDATE t_parking_space
   SET property_id = (SELECT d.keep_id FROM _m031_dup d WHERE d.dup_id = t_parking_space.property_id),
       updated_at  = datetime('now','localtime')
 WHERE property_id IN (SELECT dup_id FROM _m031_dup);

-- Step 8：重复房产行软删留痕（保留最早一行）
UPDATE t_property
   SET del_flag = 1, updated_at = datetime('now','localtime')
 WHERE id IN (SELECT dup_id FROM _m031_dup);

-- Step 9：补等效唯一索引（BR-INF-01 库级约束；部分索引对齐 del_flag=0 判重口径）
CREATE UNIQUE INDEX IF NOT EXISTS ux_property_room_unique
    ON t_property (COALESCE(building_id, -1), COALESCE(unit_id, -1), room_no)
    WHERE del_flag = 0;

-- Step 10：账单唯一索引口径统一（BR-FIN-01）——原索引未含 del_flag 条件，
-- 与服务层 FindDuplicateBill（del_flag=0 判重）不一致，导致「软删账单后重新出账」
-- 被留痕行阻断并抛 SQLite 约束错误；重建为含 del_flag=0 的部分唯一索引。
DROP INDEX IF EXISTS ux_bill_property_cycle;
CREATE UNIQUE INDEX IF NOT EXISTS ux_bill_property_cycle
    ON t_bill (charge_item_id, cycle_id, property_id)
    WHERE property_id IS NOT NULL AND del_flag = 0;

DROP TABLE IF EXISTS _m031_bill_dup;
DROP TABLE IF EXISTS _m031_dup;
DROP TABLE IF EXISTS _m031_keep;

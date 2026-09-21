-- ============================================================
-- migration_053.sql（v1.1.2：历史「减免」按调减应收回填）
-- 背景（负责人 2026-09-20 反馈）：费用减免原与退款共用一条链路 —— 只冲减实缴（paid_amount），
--       应收（amount）不动。而「减免」的业务语义是**直接调减账单应收金额**（业主少缴这笔钱，
--       不发生任何资金流出），退款才是把钱退回去（冲减实缴）。
-- 处置（CHG-v1.1.2-40）：对历史 refund_type = 1（减免）且 bill_id > 0 的账单，在该减免累计
--       仍可合法成立（减免累计 ≤ 未收余额）时，把「冲减实缴」还原为「调减应收」：
--         amount      = amount − 减免累计
--         paid_amount = paid_amount + 减免累计
--       状态按 应收/实缴 重算（实缴 ≥ 应收 → 已缴 3；仍逾期 → 2；实缴 > 0 → 部分缴 1；否则 0），
--       并写 t_bill_status_log 留痕。
-- 不可回填者（减免累计 > 未收余额：旧口径下已把实缴冲光，按新口径不成立）：**不动金额**，
--       只写一条流水提示人工复核（多为开发期测试数据）。
-- 幂等：以状态流水 reason 前缀为闸门，重复执行不会二次扣减；单据与流水一律不删除。
-- 版本：053（2026-09-20）
-- ============================================================

-- ① 暂存本次待转换的账单（闸门＝尚无本次回填留痕 → 重跑时清单为空，天然幂等）
DROP TABLE IF EXISTS temp._m053_discount;
CREATE TEMP TABLE _m053_discount (
    bill_id    INTEGER PRIMARY KEY,
    old_status INTEGER NOT NULL,
    amt        NUMERIC NOT NULL
);

INSERT INTO _m053_discount (bill_id, old_status, amt)
SELECT b.id, b.status, d.amt
FROM t_bill b
JOIN (SELECT r.bill_id AS bill_id, SUM(r.amount) AS amt FROM t_payment_refund r
      WHERE r.refund_type = 1 AND r.bill_id IS NOT NULL AND r.bill_id > 0
      GROUP BY r.bill_id) d ON d.bill_id = b.id
WHERE b.del_flag = 0
  AND d.amt > 0
  AND d.amt <= (b.amount - b.paid_amount)
  AND NOT EXISTS (SELECT 1 FROM t_bill_status_log l
                  WHERE l.bill_id = b.id AND l.reason LIKE '历史减免口径回填（CHG-v1.1.2-40）%');

-- ② 留痕：用暂存的原状态，写清这次口径转换
INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason)
SELECT s.bill_id, s.old_status,
       CASE
           WHEN b.paid_amount + s.amt >= b.amount - s.amt THEN 3
           WHEN s.old_status = 2 THEN 2
           WHEN b.paid_amount + s.amt > 0 THEN 1
           ELSE 0
       END,
       datetime('now','localtime'),
       '历史减免口径回填（CHG-v1.1.2-40）：减免 ' || printf('%.2f', s.amt) ||
       ' 元由「冲减实缴」改为「调减应收」'
FROM _m053_discount s
JOIN t_bill b ON b.id = s.bill_id;

-- ③ 回填：减免额从实缴移出、从应收扣减
UPDATE t_bill
SET amount = amount - (SELECT s.amt FROM _m053_discount s WHERE s.bill_id = t_bill.id),
    paid_amount = paid_amount + (SELECT s.amt FROM _m053_discount s WHERE s.bill_id = t_bill.id),
    status = CASE
        WHEN paid_amount + (SELECT s.amt FROM _m053_discount s WHERE s.bill_id = t_bill.id) >=
             amount - (SELECT s.amt FROM _m053_discount s WHERE s.bill_id = t_bill.id) THEN 3
        WHEN status = 2 THEN 2
        WHEN paid_amount + (SELECT s.amt FROM _m053_discount s WHERE s.bill_id = t_bill.id) > 0 THEN 1
        ELSE 0
    END,
    updated_at = datetime('now','localtime')
WHERE id IN (SELECT s.bill_id FROM _m053_discount s);

DROP TABLE IF EXISTS temp._m053_discount;

-- ④ 无法回填者：只留痕提示人工复核（金额与状态保持原样）
INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason)
SELECT b.id, b.status, b.status, datetime('now','localtime'),
       '历史减免超出未收余额（CHG-v1.1.2-40）：减免 ' || printf('%.2f', d.amt) ||
       ' 元 > 未收 ' || printf('%.2f', b.amount - b.paid_amount) ||
       ' 元，账目保持原样，建议人工复核这张账单'
FROM t_bill b
JOIN (SELECT r.bill_id AS bill_id, SUM(r.amount) AS amt FROM t_payment_refund r
      WHERE r.refund_type = 1 AND r.bill_id IS NOT NULL AND r.bill_id > 0
      GROUP BY r.bill_id) d ON d.bill_id = b.id
WHERE b.del_flag = 0
  AND d.amt > (b.amount - b.paid_amount)
  AND NOT EXISTS (SELECT 1 FROM t_bill_status_log l
                  WHERE l.bill_id = b.id AND l.reason LIKE '历史减免超出未收余额（CHG-v1.1.2-40）%');

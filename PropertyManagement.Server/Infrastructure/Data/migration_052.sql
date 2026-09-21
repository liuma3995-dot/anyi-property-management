-- ============================================================
-- migration_052.sql（v1.1.2：历史「退款/减免/调整」按实缴冲减回填）
-- 背景（负责人 2026-09-20 反馈）：原实现对账单做退款/减免/调整时，把账单状态一律置为 4（已冲正），
--       但**没有冲减实缴金额**；而收款登记 / 欠费台账只列 未缴(0)/部分缴(1)/逾期(2) 的账单，
--       于是「还有未收金额」的账单连同剩余欠款一起从应缴明细消失，钱收不回来。
-- 处置（CHG-v1.1.2-39）：
--       1) 对历史遗留（status = 4 且存在冲减记录）的账单：paid_amount 按历史冲减额扣减（下限 0）；
--       2) 状态按「应收 / 净实缴」重算：净实缴 ≥ 应收 → 已缴(3)；> 0 → 部分缴(1)；= 0 → 待缴(0)；
--       3) 补一条状态流水留痕（说明这次口径回填）。
-- 说明：冲减额 = 该账单的退款/减免/调整记录金额合计，排除「调增补收」方向（adjust_dir = 1）；
--       以 status = 4 为闸门 → 回填后不再匹配，可重复执行（幂等）；单据与流水一律不删除。
-- 版本：052（2026-09-20）
-- ============================================================

-- ① 留痕：记录回填前的状态（必须先写，否则 UPDATE 后 status 不再是 4）
INSERT INTO t_bill_status_log (bill_id, old_status, new_status, changed_at, reason)
SELECT b.id, 4,
       CASE WHEN b.amount > 0 AND MAX(0, b.paid_amount - (
                    SELECT COALESCE(SUM(r.amount), 0) FROM t_payment_refund r
                    WHERE r.bill_id = b.id AND COALESCE(r.adjust_dir, 0) <> 1)) >= b.amount THEN 3
            WHEN MAX(0, b.paid_amount - (
                    SELECT COALESCE(SUM(r.amount), 0) FROM t_payment_refund r
                    WHERE r.bill_id = b.id AND COALESCE(r.adjust_dir, 0) <> 1)) > 0 THEN 1
            ELSE 0 END,
       datetime('now','localtime'),
       '历史冲减口径回填（CHG-v1.1.2-39）：实缴按历史退款/减免/调整冲减，账单回到收款登记'
FROM t_bill b
WHERE b.del_flag = 0
  AND b.status = 4
  AND EXISTS (SELECT 1 FROM t_payment_refund r WHERE r.bill_id = b.id AND COALESCE(r.adjust_dir, 0) <> 1);

-- ② 回填实缴与状态
UPDATE t_bill
SET paid_amount = MAX(0, paid_amount - (
        SELECT COALESCE(SUM(r.amount), 0) FROM t_payment_refund r
        WHERE r.bill_id = t_bill.id AND COALESCE(r.adjust_dir, 0) <> 1)),
    status = CASE
        WHEN amount > 0 AND MAX(0, paid_amount - (
                SELECT COALESCE(SUM(r.amount), 0) FROM t_payment_refund r
                WHERE r.bill_id = t_bill.id AND COALESCE(r.adjust_dir, 0) <> 1)) >= amount THEN 3
        WHEN MAX(0, paid_amount - (
                SELECT COALESCE(SUM(r.amount), 0) FROM t_payment_refund r
                WHERE r.bill_id = t_bill.id AND COALESCE(r.adjust_dir, 0) <> 1)) > 0 THEN 1
        ELSE 0 END,
    updated_at = datetime('now','localtime')
WHERE del_flag = 0
  AND status = 4
  AND EXISTS (SELECT 1 FROM t_payment_refund r WHERE r.bill_id = t_bill.id AND COALESCE(r.adjust_dir, 0) <> 1);

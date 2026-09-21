-- ============================================================
-- migration_054.sql（v1.1.2：退款/减免/调整单据记录经办人）
-- 背景（负责人 2026-09-20 反馈）：退款/减免/调整提交后只能看到「金额 + 原因」，
--       没有可归档、可审计追溯的单据资料 —— 导出 PDF 时也缺「经办人」这一必备要素。
-- 处置（CHG-v1.1.2-41）：t_payment_refund 增加 operator_name，登记时随单据落库；
--       历史记录按审计日志（REFUND_CREATE / REFUND_BATCH_CREATE 明细中含该单据申请编号）回填。
-- 幂等：仅在 operator_name 为空时回填，重复执行不会覆盖已有值。
-- 版本：054（2026-09-20）
-- ============================================================

ALTER TABLE t_payment_refund ADD COLUMN operator_name TEXT;

UPDATE t_payment_refund
SET operator_name = (
        SELECT a.user_name FROM t_audit_log a
        WHERE a.action IN ('REFUND_CREATE', 'REFUND_BATCH_CREATE')
          AND a.del_flag = 0
          AND a.user_name IS NOT NULL AND a.user_name <> ''
          AND t_payment_refund.ref_no IS NOT NULL AND t_payment_refund.ref_no <> ''
          AND a.detail LIKE '%' || t_payment_refund.ref_no || '%'
        ORDER BY a.id DESC LIMIT 1)
WHERE operator_name IS NULL
  AND EXISTS (
        SELECT 1 FROM t_audit_log a
        WHERE a.action IN ('REFUND_CREATE', 'REFUND_BATCH_CREATE')
          AND a.del_flag = 0
          AND a.user_name IS NOT NULL AND a.user_name <> ''
          AND t_payment_refund.ref_no IS NOT NULL AND t_payment_refund.ref_no <> ''
          AND a.detail LIKE '%' || t_payment_refund.ref_no || '%');

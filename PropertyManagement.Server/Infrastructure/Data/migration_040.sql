-- ============================================================
-- migration_040.sql（v1.1.0 第 12 轮：多个账单统一收款留痕）
-- 1) t_payment 新增 batch_no：一次「统一收款」下多条分账单收款共享同一流水号
--    口径：批量收款按账单逐条落 t_payment（各自保留账单号与收据号），
--          batch_no 仅用于把这批收款串成一笔业务（退款/减免仍按账单号跨模块引用）。
-- 2) 存量单笔收款 batch_no 为 NULL，语义不变（视为独立收款）
-- 版本：040（2026-09-17）
-- ============================================================

ALTER TABLE t_payment ADD COLUMN batch_no TEXT;

CREATE INDEX IF NOT EXISTS ix_payment_batch ON t_payment (batch_no);

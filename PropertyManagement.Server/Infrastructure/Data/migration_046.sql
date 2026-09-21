-- ============================================================
-- migration_046.sql（v1.1.2：无关联账单的账务调整）
-- 背景：负责人 2026-09-19 裁定 A —— 账务调整（冲正/补收）允许不选关联账单。
-- 原表 t_payment_refund.bill_id 为 NOT NULL，且连接串开启了 Foreign Keys=True，
-- 用哨兵值 0 会触发外键校验失败（实测 50300 数据服务暂不可用）。
-- 处置：重建 t_payment_refund，使 bill_id 允许为空（NULL = 无关联账单的调整记录）。
-- 说明：迁移在事务内执行，初始化器已设置 PRAGMA foreign_keys = OFF，可安全重建表；
--       本脚本可重复执行（先复制再改名，内容始终来自当前表）。
-- 版本：046（2026-09-19）
-- ============================================================

CREATE TABLE IF NOT EXISTS t_payment_refund_new (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    bill_id         INTEGER,
    refund_type     INTEGER NOT NULL,
    amount          NUMERIC NOT NULL,
    reason          TEXT    NOT NULL,
    ref_no          TEXT,
    attachment_name TEXT,
    attachment_path TEXT,
    created_at      TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    FOREIGN KEY (bill_id) REFERENCES t_bill (id)
);

DELETE FROM t_payment_refund_new;

INSERT INTO t_payment_refund_new
    (id, bill_id, refund_type, amount, reason, ref_no, attachment_name, attachment_path, created_at)
SELECT id, bill_id, refund_type, amount, reason, ref_no, attachment_name, attachment_path, created_at
FROM t_payment_refund;

DROP TABLE t_payment_refund;

ALTER TABLE t_payment_refund_new RENAME TO t_payment_refund;

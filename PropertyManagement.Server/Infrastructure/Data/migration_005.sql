-- CHG-M4-13（T4F-4-1）：退款/减免/调整附件（文件名 + 存储路径）
ALTER TABLE t_payment_refund ADD COLUMN attachment_name TEXT;
ALTER TABLE t_payment_refund ADD COLUMN attachment_path TEXT;
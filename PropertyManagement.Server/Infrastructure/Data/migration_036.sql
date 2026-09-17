-- ============================================================
-- migration_036.sql（v1.1.0 R1：设备删除跨模块引用闭环）
-- 1) t_device_status_log 新增 del_flag：0=正常 1=已删除（软删留痕）
--    背景：设备登记时即自动写入 1 条「登记 → 在用」状态日志，删除设备时该日志会成为孤儿引用；
--    状态日志属设备过程留痕（非独立业务历史），随设备删除一并软删，物理清理由
--    「系统设置 / 备份与恢复 → 一键清理残余数据」统一执行。
-- 2) 新增索引：按设备读取状态日志（del_flag=0）+ 一键清理扫描
-- 3) 说明：仅新增列 + 索引，历史行 del_flag 默认 0（既有数据不受影响）
-- 版本：036（2026-09-16）
-- ============================================================

ALTER TABLE t_device_status_log ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_device_status_log_del ON t_device_status_log (device_id, del_flag);

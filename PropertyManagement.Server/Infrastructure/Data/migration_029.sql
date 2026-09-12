-- ============================================================
-- migration_029：审计日志 / 备份记录 软删留痕 + 一键清理残余数据（R13）
-- 1) t_audit_log 新增 del_flag：审计日志批量删除改为软删（记录留痕，不物理删除）
-- 2) t_backup    新增 del_flag：备份/恢复记录批量删除改为软删
-- 3) 支撑「系统设置 / 备份与恢复 → 一键清理残余数据」：清理全库 del_flag=1 留痕行
-- 版本：029（2026-09-12）
-- 说明：仅新增列与索引，历史行 del_flag 默认 0（既有数据不受影响）。
-- ============================================================

ALTER TABLE t_audit_log ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;
ALTER TABLE t_backup ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_audit_log_del ON t_audit_log (del_flag, id);
CREATE INDEX IF NOT EXISTS ix_backup_del ON t_backup (del_flag, id);

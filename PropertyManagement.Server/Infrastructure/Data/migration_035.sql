-- ============================================================
-- migration_035.sql（v1.1.0-⑤：基础数据导入「导入批次记录」批量删除）
-- 1) t_import_log 新增 del_flag：0=正常 1=已删除（软删留痕）
--    口径与全仓一致：批量删除只置 del_flag=1（记录不再出现在批次列表），
--    物理清理由「系统设置 / 备份与恢复 → 一键清理残余数据」统一执行。
-- 2) 新增索引：批次列表查询（del_flag=0）+ 一键清理残余数据扫描
-- 3) 说明：仅新增列 + 索引，历史行 del_flag 默认 0（既有数据不受影响）
-- 版本：035（2026-09-16）
-- ============================================================

ALTER TABLE t_import_log ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_import_log_del ON t_import_log (del_flag, id);

-- 一次性纠正：历史库中已物理删除的批次若有残留错误行，先按孤儿清理，
-- 保证「导入错误清单」不会引用不存在的批次（新库为幂等空操作）。
DELETE FROM t_import_error
 WHERE import_id NOT IN (SELECT id FROM t_import_log);

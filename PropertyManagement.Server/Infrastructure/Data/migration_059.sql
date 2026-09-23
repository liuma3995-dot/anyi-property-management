-- ============================================================
-- migration_059.sql（v1.3.1：报表与导出留痕可清理）
-- 背景（负责人 2026-09-23）：年度/季度/月度清算需要清理"只增不减"的缓存数据 ——
--       每次导出都会在服务端落一个生成文件并写一条留痕（t_report_log / t_export_log），
--       系统此前没有任何清理入口（实测 exports\ 18 个文件 vs 留痕 8 行）。
-- 处置（CHG-v1.3.1-05）：财务报表模块新增「报表与导出留痕」清单，
--       删除 = 留痕软删（本迁移新增 del_flag）+ 物理删除服务端生成文件；
--       软删行再随「系统设置 → 备份与恢复 → 一键清理残余数据」物理回收（与全仓软删口径一致）。
-- 版本：059（2026-09-23）
-- ============================================================

ALTER TABLE t_report_log ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;
ALTER TABLE t_export_log ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_report_log_del_flag ON t_report_log (del_flag);
CREATE INDEX IF NOT EXISTS ix_export_log_del_flag ON t_export_log (del_flag);

-- ============================================================
-- migration_030.sql（R17：顶部栏与仪表盘交互能力的数据支撑）
-- 1) t_user 扩展个人信息列：display_name / phone / bio / avatar_key（全部可空，可填可不填）
--    头像为内置可选项（avatar-01 ~ avatar-08），不含文件上传
-- 2) 说明：本迁移仅新增列，无表重建、无数据改写
-- 版本：030（2026-09-13）
-- ============================================================

ALTER TABLE t_user ADD COLUMN display_name TEXT;   -- 显示名（顶栏展示；空=回退用户名）
ALTER TABLE t_user ADD COLUMN phone TEXT;          -- 手机号码（可空）
ALTER TABLE t_user ADD COLUMN bio TEXT;            -- 个人简介（可空，≤200 字）
ALTER TABLE t_user ADD COLUMN avatar_key TEXT;     -- 内置头像 key（可空）

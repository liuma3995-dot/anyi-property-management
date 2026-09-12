-- ============================================================
-- migration_028：字典项批量删除（软删）（R12 / PG-COM-01）
-- 1) t_dict_item 新增 del_flag：0=正常 1=已删除
-- 2) 批量删除仅允许作用于「已停用」字典项；未停用项被命中时整批拒绝
-- 3) 过滤条件统一为 del_flag = 0（管理端列表、编辑、启停、业务下拉）
-- 版本：028（2026-09-12）
-- 说明：仅新增列 + 索引，历史行 del_flag 默认 0（不影响既有数据）。
-- ============================================================

ALTER TABLE t_dict_item ADD COLUMN del_flag INTEGER NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS ix_dict_item_type_del ON t_dict_item (type_code, del_flag, status);

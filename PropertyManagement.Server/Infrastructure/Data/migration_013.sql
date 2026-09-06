-- ============================================================
-- migration_013.sql（M5 CHG-INF-05）
-- t_property 状态扩展（空置/入住/装修中）：status 为 INTEGER，无需改列，
-- 仅补齐常用查询索引（按单元/楼栋筛选，BR-INF-01 房号唯一依赖已有 UNIQUE）
-- 版本：013（2026-09-03）
-- ============================================================
CREATE INDEX IF NOT EXISTS ix_property_unit_del ON t_property (unit_id, del_flag);
CREATE INDEX IF NOT EXISTS ix_unit_building_del ON t_unit (building_id, del_flag);
CREATE INDEX IF NOT EXISTS ix_owner_name_del ON t_owner (name, del_flag);

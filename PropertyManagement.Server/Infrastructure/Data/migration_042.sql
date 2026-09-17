-- ============================================================
-- migration_042.sql（v1.1.0 第 17 轮：收费项目「缴费对象」纳入字典 + 自定义）
-- 1) 新增字典类型 charge_object（缴费对象），纳入「系统设置 → 参数/字典维护」：
--    房产/车位/业主 为系统固定三项（charge_object 固定编码 property/parking/owner，
--    不可停用、不可删除 —— 出账时跨模块调用基础信息，无法自定义）；
--    其余为自定义项（租户/广告商/外部单位等无基础信息档案的对象）。
-- 2) t_charge_item 新增 object_code：记录所选缴费对象对应的字典项编码；
--    object_type 口径保持 0 房产 / 1 车位 / 2 业主，新增 3 自定义（ChargeObjectType.Custom）。
-- 3) 存量收费项目按 object_type 回填固定编码，保证字典与项目口径一致（幂等）。
-- 版本：042（2026-09-17）
-- ============================================================

ALTER TABLE t_charge_item ADD COLUMN object_code TEXT;

INSERT OR IGNORE INTO t_dict_type (type_code, type_name) VALUES ('charge_object', '缴费对象');

INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status) VALUES
 ('charge_object', 'property', '房产', 1, 0),
 ('charge_object', 'parking',  '车位', 2, 0),
 ('charge_object', 'owner',    '业主', 3, 0);

UPDATE t_charge_item
   SET object_code = CASE object_type WHEN 1 THEN 'parking' WHEN 2 THEN 'owner' ELSE 'property' END,
       updated_at  = datetime('now','localtime')
 WHERE del_flag = 0
   AND (object_code IS NULL OR object_code = '');

-- ============================================================
-- migration_004.sql（M4 T4F-1-5 CHG-M4-11）
-- 收费项目维护页修复：类别/计价方式/单价单位/自定义周期 + 轻量字典扩展
-- 版本：004（2026-09-01）
-- ============================================================

-- t_charge_item：新增展示/业务字段（存量库升级；全新安装由 migration 链补齐）
ALTER TABLE t_charge_item ADD COLUMN category TEXT NOT NULL DEFAULT '';
ALTER TABLE t_charge_item ADD COLUMN method_code TEXT;
ALTER TABLE t_charge_item ADD COLUMN method_name TEXT;
ALTER TABLE t_charge_item ADD COLUMN price_unit TEXT;
ALTER TABLE t_charge_item ADD COLUMN cycle_name TEXT;

-- t_dict_item：remark 列（自定义计价方式单位文本等）
ALTER TABLE t_dict_item ADD COLUMN remark TEXT;

-- 收费项目类别/计价方式/计费周期 字典（幂等：兼容存量库 + 全新安装 seed 之后补 remark）
INSERT OR IGNORE INTO t_dict_type (type_code, type_name) VALUES
 ('charge_category', '收费项目类别'),
 ('charge_method',    '计价方式'),
 ('charge_cycle',     '计费周期');

INSERT OR IGNORE INTO t_dict_item (type_code, item_code, item_name, sort, status, remark) VALUES
 ('charge_category', 'property_fee', '物业费',   1, 0, ''),
 ('charge_category', 'agency_fee',   '代收代缴', 2, 0, ''),
 ('charge_category', 'parking_fee',  '车位费',   3, 0, ''),
 ('charge_category', 'shared_cost',  '公摊分摊', 4, 0, ''),
 ('charge_category', 'onetime',      '一次性',   5, 0, ''),
 ('charge_method', 'area',    '按建筑面积', 1, 0, '㎡'),
 ('charge_method', 'house',   '按户',       2, 0, '户'),
 ('charge_method', 'parking', '按车位',     3, 0, '车位'),
 ('charge_method', 'share',   '按户均摊',   4, 0, '户'),
 ('charge_method', 'onetime', '一次性',     5, 0, '户'),
 ('charge_method', 'step',    '阶梯单价',   6, 0, ''),
 ('charge_cycle', 'monthly',  '按月',       1, 0, ''),
 ('charge_cycle', 'yearly',   '按年',       2, 0, ''),
 ('charge_cycle', 'onetime',  '一次性',     3, 0, '');

-- 内置计价方式默认单价单位（seed 已插行的 remark 修正；幂等）
UPDATE t_dict_item SET remark = '㎡'   WHERE type_code = 'charge_method' AND item_code = 'area';
UPDATE t_dict_item SET remark = '户'   WHERE type_code = 'charge_method' AND item_code IN ('house','share','onetime');
UPDATE t_dict_item SET remark = '车位' WHERE type_code = 'charge_method' AND item_code = 'parking';
UPDATE t_dict_item SET remark = ''    WHERE type_code = 'charge_method' AND item_code = 'step';
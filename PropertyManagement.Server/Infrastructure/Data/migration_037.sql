-- ============================================================
-- migration_037.sql（v1.1.0 第 7 轮：支出登记业务链路补齐）
-- 1) 新增系统参数「月度支出预算」finance.budget.monthly（元，0=未设置）
--    用途：支出登记页「本月预算」卡片显示预算与已用进度，卡片内可直接设置。
-- 2) 支出分类默认值补齐：仅当分类表为空时写入 5 个默认分类
--    （口径：不覆盖、不覆盖用户自建分类；用户为空说明从未维护过分类）
-- 3) 说明：两条语句均幂等，可重复执行
-- 版本：037（2026-09-16）
-- ============================================================

INSERT OR IGNORE INTO t_param (param_key, param_value, remark)
VALUES ('finance.budget.monthly', '0', '月度支出预算（元，0=未设置；支出登记页卡片内可设置）');

INSERT INTO t_expense_category (name, category_type, status)
SELECT v.name, v.type, 0 FROM (
    SELECT '工资' AS name, 'salary' AS type UNION ALL
    SELECT '维修维护', 'maintenance' UNION ALL
    SELECT '水电', 'utilities' UNION ALL
    SELECT '外包', 'outsourcing' UNION ALL
    SELECT '其他', 'other') v
WHERE (SELECT COUNT(1) FROM t_expense_category) = 0;

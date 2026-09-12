-- DIS 纠纷调解（PG-DIS-02 关联房产/期望解决时间）：为 t_dispute_case 补充 property_id、expected_at。
-- 版本：016（2026-09-06）
ALTER TABLE t_dispute_case ADD COLUMN property_id INTEGER;
ALTER TABLE t_dispute_case ADD COLUMN expected_at TEXT;

-- 车位租金计费模式（PG-INF-04，BR-INF-03）：按月/按年，默认按月。
-- 版本：015（2026-09-03）
ALTER TABLE t_parking_space ADD COLUMN rent_mode INTEGER NOT NULL DEFAULT 0;

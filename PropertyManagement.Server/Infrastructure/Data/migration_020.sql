-- EMG 应急处置（场景与步骤维护）：新增场景标识图标（每个场景可自定义不同图标标识）。
-- 版本：20；2026-09-07。
ALTER TABLE t_emergency_scene ADD COLUMN icon_key TEXT;

-- 既有种子场景补齐默认图标（资源键前缀 Icon.，客户端 IconKeyConverter 解析为几何图形）。
UPDATE t_emergency_scene SET icon_key = 'Icon.Flame'      WHERE name = '火灾'       AND (icon_key IS NULL OR icon_key = '');
UPDATE t_emergency_scene SET icon_key = 'Icon.UserRound' WHERE name = '电梯困人' AND (icon_key IS NULL OR icon_key = '');
UPDATE t_emergency_scene SET icon_key = 'Icon.Droplet'   WHERE name LIKE '%水浸%' AND (icon_key IS NULL OR icon_key = '');
UPDATE t_emergency_scene SET icon_key = 'Icon.Zap'       WHERE name = '燃气泄漏'   AND (icon_key IS NULL OR icon_key = '');
UPDATE t_emergency_scene SET icon_key = 'Icon.ShieldCheck' WHERE name = '治安事件' AND (icon_key IS NULL OR icon_key = '');
UPDATE t_emergency_scene SET icon_key = 'Icon.Bell'      WHERE name = '人员受伤'   AND (icon_key IS NULL OR icon_key = '');
UPDATE t_emergency_scene SET icon_key = 'Icon.Siren'     WHERE icon_key IS NULL OR icon_key = '';

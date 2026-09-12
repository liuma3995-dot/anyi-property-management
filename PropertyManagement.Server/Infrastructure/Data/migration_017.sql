-- EMG 应急处置（PG-EMG-01/02）：步骤补 责任角色/时限/联动动作；事件补 编号/级别/详细位置。
-- 版本：017（2026-09-06）
ALTER TABLE t_emergency_step ADD COLUMN role TEXT;
ALTER TABLE t_emergency_step ADD COLUMN time_limit TEXT;
ALTER TABLE t_emergency_step ADD COLUMN action TEXT;
ALTER TABLE t_emergency_event ADD COLUMN event_no TEXT;
ALTER TABLE t_emergency_event ADD COLUMN level INTEGER;
ALTER TABLE t_emergency_event ADD COLUMN detail_location TEXT;

-- ============================================================
-- migration_021.sql（排班表模块：排班模板）
-- t_schedule_template：模板（由保存排班后的数据提取）
-- t_schedule_template_item：模板明细（员工 × 周期内 dayOffset × 班次）
-- 版本：021（2026-09-08）
-- ============================================================
CREATE TABLE IF NOT EXISTS t_schedule_template (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL,
  from_date TEXT NOT NULL,
  to_date TEXT NOT NULL,
  item_count INTEGER NOT NULL DEFAULT 0,
  del_flag INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL DEFAULT (datetime('now','localtime'))
);

CREATE TABLE IF NOT EXISTS t_schedule_template_item (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  template_id INTEGER NOT NULL,
  employee_id INTEGER NOT NULL,
  shift_id INTEGER NOT NULL,
  day_offset INTEGER NOT NULL,
  FOREIGN KEY (template_id) REFERENCES t_schedule_template(id)
);

CREATE INDEX IF NOT EXISTS idx_template_item_template ON t_schedule_template_item(template_id);

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>人员组织仓储 SQLite 实现（M6 D6-2，Dapper）。</summary>
    public class SqlOrgRepository : IOrgRepository
    {
        private const int MaxPageSize = 200; // 服务端分页上限保护（P2-4.10）

        // ===================== 部门 =====================
        public List<DepartmentDto> ListDepartments(IDbConnection connection, string keyword)
        {
            string sql = "SELECT d.id, d.name, d.parent_id AS ParentId, d.status FROM t_department d WHERE d.del_flag = 0";
            var p = new DynamicParameters();
            if (!string.IsNullOrWhiteSpace(keyword)) { sql += " AND d.name LIKE @kw"; p.Add("kw", "%" + keyword.Trim() + "%"); }
            sql += " ORDER BY d.id";
            return connection.Query<DepartmentDto>(sql, p).ToList();
        }

        public DepartmentDto GetDepartment(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<DepartmentDto>(
                "SELECT d.id, d.name, d.parent_id AS ParentId, d.status FROM t_department d WHERE d.id = @id AND d.del_flag = 0", new { id });
        }

        public int InsertDepartment(IDbConnection connection, IDbTransaction transaction, DepartmentDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_department (name, parent_id, status, del_flag) VALUES (@Name, @ParentId, @Status, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.ParentId, dto.Status }, transaction);
        }

        public void UpdateDepartment(IDbConnection connection, IDbTransaction transaction, DepartmentDto dto)
        {
            // CHG-ORG-02：部门禁删可停用，更新支持 status（0启用/1停用）
            connection.Execute(
                "UPDATE t_department SET name = @Name, parent_id = @ParentId, status = @Status, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.ParentId, dto.Status }, transaction);
        }

        public void SoftDeleteDepartment(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_department SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public int CountEmployeesByDept(IDbConnection connection, int deptId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_employee WHERE dept_id = @deptId AND del_flag = 0", new { deptId });
        }

        // ===================== 岗位 =====================
        public List<PositionDto> ListPositions(IDbConnection connection, int? deptId)
        {
            string sql = "SELECT id, dept_id AS DeptId, name FROM t_position";
            var p = new DynamicParameters();
            if (deptId.HasValue) { sql += " WHERE dept_id = @deptId"; p.Add("deptId", deptId.Value); }
            sql += " ORDER BY id";
            return connection.Query<PositionDto>(sql, p).ToList();
        }

        public int InsertPosition(IDbConnection connection, IDbTransaction transaction, PositionDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_position (dept_id, name) VALUES (@DeptId, @Name); SELECT last_insert_rowid();",
                new { dto.DeptId, dto.Name }, transaction);
        }

        public void UpdatePosition(IDbConnection connection, IDbTransaction transaction, PositionDto dto)
        {
            connection.Execute(
                "UPDATE t_position SET dept_id = @DeptId, name = @Name WHERE id = @Id",
                new { dto.Id, dto.DeptId, dto.Name }, transaction);
        }

        public void DeletePosition(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute("DELETE FROM t_position WHERE id = @id", new { id }, transaction);
        }

        public int CountEmployeesByPosition(IDbConnection connection, int positionId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_employee WHERE position_id = @positionId AND del_flag = 0", new { positionId });
        }

        // ===================== 员工 =====================
        // 注意：不再排除 status=2（离职档案保留可查，BR-ORG-02）；del_flag=1 才是从列表消失（物理删除语义）
        private const string EmployeeBaseSql =
            "SELECT e.id, e.dept_id AS DeptId, e.position_id AS PositionId, e.name, e.phone, e.hire_date AS HireDate, e.status, " +
            "COALESCE(e.emp_no, 'YG-' || printf('%03d', e.id)) AS EmpNo, " +
            "COALESCE(d.name,'') AS DeptName, COALESCE(p.name,'') AS PositionName " +
            "FROM t_employee e " +
            "LEFT JOIN t_department d ON d.id = e.dept_id " +
            "LEFT JOIN t_position p ON p.id = e.position_id";

        public EmployeeDto GetEmployee(IDbConnection connection, int id)
        {
            var row = connection.QueryFirstOrDefault<EmployeeDto>(
                EmployeeBaseSql + " WHERE e.id = @id AND e.del_flag = 0", new { id });
            return NormalizeEmployee(row, connection);
        }

        public PageResult<EmployeeDto> QueryEmployees(IDbConnection connection, EmployeeQueryRequest query, out int total)
        {
            string where = "WHERE e.del_flag = 0";
            var p = new DynamicParameters();
            if (query.DeptId.HasValue) { where += " AND e.dept_id = @deptId"; p.Add("deptId", query.DeptId.Value); }
            if (query.PositionId.HasValue) { where += " AND e.position_id = @positionId"; p.Add("positionId", query.PositionId.Value); }
            if (query.Status.HasValue) { where += " AND e.status = @status"; p.Add("status", (int)query.Status.Value); }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                // 工号（emp_no 业务键）可检索（审计 1.10/22）
                where += " AND (e.name LIKE @kw OR e.phone LIKE @kw OR e.emp_no LIKE @kw OR CAST(e.id AS TEXT) LIKE @idkw)";
                p.Add("kw", "%" + query.Keyword.Trim() + "%");
                p.Add("idkw", "%" + query.Keyword.Trim() + "%");
            }
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_employee e " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = ClampPageSize(query.PageSize);
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize); p.Add("offset", offset);
            var items = connection.Query<EmployeeDto>(
                EmployeeBaseSql + " " + where + " ORDER BY e.id LIMIT @limit OFFSET @offset", p)
                .Select(e => NormalizeEmployee(e, connection)).ToList();
            return new PageResult<EmployeeDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public int InsertEmployee(IDbConnection connection, IDbTransaction transaction, EmployeeDto dto)
        {
            int id = connection.ExecuteScalar<int>(
                "INSERT INTO t_employee (dept_id, position_id, name, phone, hire_date, status, del_flag) " +
                "VALUES (@DeptId, @PositionId, @Name, @Phone, @HireDate, @Status, 0); SELECT last_insert_rowid();",
                new { dto.DeptId, dto.PositionId, dto.Name, dto.Phone, dto.HireDate, Status = (int)dto.Status }, transaction);
            // 工号落库：YG-%03d（按新 id 生成，唯一业务键，审计 22）
            string empNo = "YG-" + id.ToString("000");
            connection.Execute(
                "UPDATE t_employee SET emp_no = @empNo WHERE id = @id AND (emp_no IS NULL OR emp_no = '')",
                new { empNo, id }, transaction);
            dto.Id = id;
            dto.EmpNo = empNo;
            return id;
        }

        public void UpdateEmployee(IDbConnection connection, IDbTransaction transaction, EmployeeDto dto)
        {
            connection.Execute(
                "UPDATE t_employee SET dept_id = @DeptId, position_id = @PositionId, name = @Name, phone = @Phone, " +
                "hire_date = @HireDate, status = @Status, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.DeptId, dto.PositionId, dto.Name, dto.Phone, dto.HireDate, Status = (int)dto.Status }, transaction);
        }

        public void SoftDeleteEmployee(IDbConnection connection, IDbTransaction transaction, int id)
        {
            // 物理删除语义（主管场景）：del_flag=1；离职走 ResignEmployee（del_flag 保持 0，BR-ORG-02）
            connection.Execute(
                "UPDATE t_employee SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public void ResignEmployee(IDbConnection connection, IDbTransaction transaction, int id)
        {
            // 离职：仅置 status=2，del_flag 保留 0（档案保留，列表仍可见，BR-ORG-02）
            connection.Execute(
                "UPDATE t_employee SET status = 2, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0",
                new { id }, transaction);
        }

        public int DisableUserAccountsByEmployee(IDbConnection connection, IDbTransaction transaction, int employeeId)
        {
            // 账号停用（BR-ORG-02）：t_user.username = 员工工号 emp_no；status=2=Disabled
            return connection.Execute(
                "UPDATE t_user SET status = 2, updated_at = datetime('now','localtime') " +
                "WHERE status <> 2 AND username = (SELECT emp_no FROM t_employee WHERE id = @employeeId AND emp_no IS NOT NULL)",
                new { employeeId }, transaction);
        }

        public int DisablePhoneEntriesByEmployee(IDbConnection connection, IDbTransaction transaction, int employeeId, string employeeName)
        {
            // 电话簿员工条目联动停用（UC-TEL-005）：优先按 employee_id（migration_019 新增列）
            int affected = connection.Execute(
                "UPDATE t_phone_entry SET status = 1, disable_source = 1, updated_at = datetime('now','localtime') " +
                "WHERE employee_id = @employeeId AND status = 0", new { employeeId }, transaction);
            if (affected == 0 && !string.IsNullOrWhiteSpace(employeeName))
            {
                // 兜底：老数据无 employee_id 关联，按 姓名 + 员工通讯录类型停用
                affected = connection.Execute(
                    "UPDATE t_phone_entry SET status = 1, disable_source = 1, updated_at = datetime('now','localtime') " +
                    "WHERE status = 0 AND entry_type = 2 AND name = @name", new { name = employeeName.Trim() }, transaction);
            }
            return affected;
        }

        public int InsertUserAccount(IDbConnection connection, IDbTransaction transaction, string userName, string passwordHash)
        {
            int exists = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_user WHERE username = @userName", new { userName }, transaction);
            if (exists > 0) { return 0; }
            // must_change_password=1：初始密码下发后首登强制改密（migration_019 列）
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_user (username, password_hash, status, must_change_password) " +
                "VALUES (@userName, @passwordHash, 0, 1); SELECT last_insert_rowid();",
                new { userName, passwordHash }, transaction);
        }

        public int? GetUserIdByUserName(IDbConnection connection, string userName)
        {
            if (string.IsNullOrWhiteSpace(userName)) { return null; }
            return connection.ExecuteScalar<int?>(
                "SELECT id FROM t_user WHERE username = @userName", new { userName = userName.Trim() });
        }

        public List<EmployeeDto> ListOnDutyEmployees(IDbConnection connection, int? deptId)
        {
            string sql = EmployeeBaseSql + " WHERE e.del_flag = 0 AND e.status = 0";
            var p = new DynamicParameters();
            if (deptId.HasValue) { sql += " AND e.dept_id = @deptId"; p.Add("deptId", deptId.Value); }
            sql += " ORDER BY e.id";
            return connection.Query<EmployeeDto>(sql, p).Select(e => NormalizeEmployee(e, connection)).ToList();
        }

        public List<EmployeeStatusLogDto> ListEmployeeStatusLogs(IDbConnection connection, int employeeId)
        {
            return connection.Query<EmployeeStatusLogDto>(
                "SELECT id, employee_id AS EmployeeId, status, changed_at AS ChangedAt " +
                "FROM t_employee_status_log WHERE employee_id = @employeeId ORDER BY id DESC", new { employeeId }).ToList();
        }

        public void InsertEmployeeStatusLog(IDbConnection connection, IDbTransaction transaction, int employeeId, EmployeeStatus status)
        {
            connection.Execute(
                "INSERT INTO t_employee_status_log (employee_id, status) VALUES (@employeeId, @status)",
                new { employeeId, status = (int)status }, transaction);
        }

        // ===================== 班次 =====================
        public List<ShiftDto> ListShifts(IDbConnection connection)
        {
            return connection.Query<ShiftDto>(
                "SELECT id, name, start_time AS StartTime, end_time AS EndTime, min_required AS MinRequired " +
                "FROM t_shift ORDER BY id").ToList();
        }

        public int InsertShift(IDbConnection connection, IDbTransaction transaction, ShiftDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_shift (name, start_time, end_time, min_required) " +
                "VALUES (@Name, @StartTime, @EndTime, @MinRequired); SELECT last_insert_rowid();",
                new { dto.Name, dto.StartTime, dto.EndTime, MinRequired = dto.MinRequired ?? 1 }, transaction);
        }

        public void UpdateShift(IDbConnection connection, IDbTransaction transaction, ShiftDto dto)
        {
            // MinRequired=null 时保留原值，避免老客户端把需求人数清零
            if (dto.MinRequired.HasValue)
            {
                connection.Execute(
                    "UPDATE t_shift SET name = @Name, start_time = @StartTime, end_time = @EndTime, min_required = @MinRequired WHERE id = @Id",
                    new { dto.Id, dto.Name, dto.StartTime, dto.EndTime, dto.MinRequired }, transaction);
            }
            else
            {
                connection.Execute(
                    "UPDATE t_shift SET name = @Name, start_time = @StartTime, end_time = @EndTime WHERE id = @Id",
                    new { dto.Id, dto.Name, dto.StartTime, dto.EndTime }, transaction);
            }
        }

        public void DeleteShift(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute("DELETE FROM t_shift WHERE id = @id", new { id }, transaction);
        }

        public int CountSchedulesByShift(IDbConnection connection, int shiftId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_schedule WHERE shift_id = @shiftId AND del_flag = 0", new { shiftId });
        }

        // ===================== 排班 =====================
        private const string ScheduleBaseSql =
            "SELECT s.id, s.employee_id AS EmployeeId, s.shift_id AS ShiftId, s.work_date AS WorkDate, s.status, " +
            "COALESCE(e.emp_no, 'YG-' || printf('%03d', s.employee_id)) AS EmpNo, " +
            "COALESCE(e.name,'') AS EmpName, COALESCE(sh.name,'') AS ShiftName, " +
            "COALESCE(sh.start_time,'') AS ShiftStart, COALESCE(sh.end_time,'') AS ShiftEnd " +
            "FROM t_schedule s " +
            "LEFT JOIN t_employee e ON e.id = s.employee_id " +
            "LEFT JOIN t_shift sh ON sh.id = s.shift_id";

        public List<ScheduleDto> QuerySchedules(IDbConnection connection, DateTime fromDate, DateTime toDate, int? employeeId)
        {
            string where = "WHERE s.del_flag = 0 AND s.work_date BETWEEN @fromDate AND @toDate";
            var p = new DynamicParameters();
            p.Add("fromDate", fromDate.ToString("yyyy-MM-dd"));
            p.Add("toDate", toDate.ToString("yyyy-MM-dd"));
            if (employeeId.HasValue) { where += " AND s.employee_id = @employeeId"; p.Add("employeeId", employeeId.Value); }
            where += " ORDER BY s.work_date, s.employee_id";
            var rows = connection.Query<ScheduleDto>(ScheduleBaseSql + " " + where, p).Select(NormalizeSchedule).ToList();
            ApplyScheduleFlags(connection, fromDate, toDate, rows);
            return rows;
        }

        public List<ScheduleDto> ListSchedulesByDateRange(IDbConnection connection, DateTime fromDate, DateTime toDate)
        {
            var rows = connection.Query<ScheduleDto>(
                ScheduleBaseSql + " WHERE s.del_flag = 0 AND s.work_date BETWEEN @fromDate AND @toDate " +
                "ORDER BY s.work_date, s.employee_id",
                new { fromDate = fromDate.ToString("yyyy-MM-dd"), toDate = toDate.ToString("yyyy-MM-dd") })
                .Select(NormalizeSchedule).ToList();
            ApplyScheduleFlags(connection, fromDate, toDate, rows);
            return rows;
        }

        public int InsertSchedule(IDbConnection connection, IDbTransaction transaction, ScheduleDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_schedule (employee_id, shift_id, work_date, status, del_flag) " +
                "VALUES (@EmployeeId, @ShiftId, @WorkDate, @Status, 0); SELECT last_insert_rowid();",
                new { dto.EmployeeId, dto.ShiftId, WorkDate = dto.WorkDate.ToString("yyyy-MM-dd"), Status = (int)dto.Status }, transaction);
        }

        public void UpdateSchedule(IDbConnection connection, IDbTransaction transaction, ScheduleDto dto)
        {
            connection.Execute(
                "UPDATE t_schedule SET shift_id = @ShiftId, status = @Status, updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.ShiftId, Status = (int)dto.Status }, transaction);
        }

        public void SoftDeleteSchedule(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_schedule SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public ScheduleDto GetSchedule(IDbConnection connection, int id)
        {
            return NormalizeSchedule(connection.QueryFirstOrDefault<ScheduleDto>(
                ScheduleBaseSql + " WHERE s.id = @id AND s.del_flag = 0", new { id }));
        }

        public List<ScheduleDto> ListSchedulesInRangeFiltered(IDbConnection connection, DateTime fromDate, DateTime toDate, List<int> shiftIds)
        {
            string where = "WHERE s.del_flag = 0 AND s.work_date BETWEEN @fromDate AND @toDate";
            var p = new DynamicParameters();
            p.Add("fromDate", fromDate.ToString("yyyy-MM-dd"));
            p.Add("toDate", toDate.ToString("yyyy-MM-dd"));
            if (shiftIds != null && shiftIds.Count > 0)
            {
                where += " AND s.shift_id IN @shiftIds";
                p.Add("shiftIds", shiftIds);
            }
            var rows = connection.Query<ScheduleDto>(
                ScheduleBaseSql + " " + where + " ORDER BY s.work_date, s.employee_id", p)
                .Select(NormalizeSchedule).ToList();
            ApplyScheduleFlags(connection, fromDate, toDate, rows);
            return rows;
        }

        public int SoftDeleteSchedulesInRange(IDbConnection connection, IDbTransaction transaction, DateTime fromDate, DateTime toDate, List<int> shiftIds)
        {
            var p = new DynamicParameters();
            p.Add("fromDate", fromDate.ToString("yyyy-MM-dd"));
            p.Add("toDate", toDate.ToString("yyyy-MM-dd"));
            string sql = "UPDATE t_schedule SET del_flag = 1, updated_at = datetime('now','localtime') " +
                         "WHERE del_flag = 0 AND work_date BETWEEN @fromDate AND @toDate";
            if (shiftIds != null && shiftIds.Count > 0)
            {
                sql += " AND shift_id IN @shiftIds";
                p.Add("shiftIds", shiftIds);
            }
            return connection.Execute(sql, p, transaction);
        }

        // ===================== 排班模板 =====================
        public List<ScheduleTemplateDto> ListScheduleTemplates(IDbConnection connection)
        {
            return connection.Query<ScheduleTemplateDto>(
                "SELECT id, name, from_date AS FromDate, to_date AS ToDate, item_count AS ItemCount, " +
                "created_at AS CreatedAtText FROM t_schedule_template WHERE del_flag = 0 ORDER BY id DESC").ToList();
        }

        public ScheduleTemplateDto GetScheduleTemplateItems(IDbConnection connection, int templateId)
        {
            var template = connection.QueryFirstOrDefault<ScheduleTemplateDto>(
                "SELECT id, name, from_date AS FromDate, to_date AS ToDate, item_count AS ItemCount, " +
                "created_at AS CreatedAtText FROM t_schedule_template WHERE id = @id AND del_flag = 0", new { id = templateId });
            if (template == null) { return null; }
            var items = connection.Query<ScheduleTemplateItemDto>(
                "SELECT i.employee_id AS EmployeeId, COALESCE(e.emp_no, 'YG-' || printf('%03d', i.employee_id)) AS EmpNo, " +
                "COALESCE(e.name,'') AS EmpName, i.shift_id AS ShiftId, COALESCE(sh.name,'') AS ShiftName, i.day_offset AS DayOffset " +
                "FROM t_schedule_template_item i " +
                "LEFT JOIN t_employee e ON e.id = i.employee_id " +
                "LEFT JOIN t_shift sh ON sh.id = i.shift_id " +
                "WHERE i.template_id = @id ORDER BY i.id", new { id = templateId }).ToList();
            template.Items = items;
            return template;
        }

        public int InsertScheduleTemplate(IDbConnection connection, IDbTransaction transaction, ScheduleTemplateDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_schedule_template (name, from_date, to_date, item_count, del_flag) " +
                "VALUES (@Name, @FromDate, @ToDate, @ItemCount, 0); SELECT last_insert_rowid();",
                new { dto.Name, FromDate = dto.FromDate.ToString("yyyy-MM-dd"), ToDate = dto.ToDate.ToString("yyyy-MM-dd"), dto.ItemCount }, transaction);
        }

        public void InsertScheduleTemplateItem(IDbConnection connection, IDbTransaction transaction, ScheduleTemplateItemDto item)
        {
            connection.Execute(
                "INSERT INTO t_schedule_template_item (template_id, employee_id, shift_id, day_offset) " +
                "VALUES (@TemplateId, @EmployeeId, @ShiftId, @DayOffset)",
                new { item.TemplateId, item.EmployeeId, item.ShiftId, item.DayOffset }, transaction);
        }

        public void SoftDeleteScheduleTemplate(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_schedule_template SET del_flag = 1 WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public void InsertScheduleConflictLog(IDbConnection connection, IDbTransaction transaction, ScheduleConflictLogDto dto)
        {
            connection.Execute(
                "INSERT INTO t_schedule_conflict_log (schedule_id, conflict_type, detail) VALUES (@ScheduleId, @ConflictType, @Detail)",
                new { dto.ScheduleId, dto.ConflictType, dto.Detail }, transaction);
        }

        public void ClearScheduleConflictLogs(IDbConnection connection, IDbTransaction transaction, DateTime fromDate, DateTime toDate)
        {
            // 重发布时清理区间内旧日志，避免重复写（审计 P2-2.8）
            connection.Execute(
                "DELETE FROM t_schedule_conflict_log WHERE schedule_id IN " +
                "(SELECT id FROM t_schedule WHERE del_flag = 0 AND work_date BETWEEN @fromDate AND @toDate)",
                new { fromDate = fromDate.ToString("yyyy-MM-dd"), toDate = toDate.ToString("yyyy-MM-dd") }, transaction);
        }

        public List<ScheduleConflictLogDto> ListScheduleConflicts(IDbConnection connection, DateTime fromDate, DateTime toDate)
        {
            // LEFT JOIN：缺员级日志（schedule_id=0，无对应排班行）也要列出（审计 P2-2.9）
            return connection.Query<ScheduleConflictLogDto>(
                "SELECT c.id, c.schedule_id AS ScheduleId, c.conflict_type AS ConflictType, c.detail, c.created_at AS CreatedAt " +
                "FROM t_schedule_conflict_log c LEFT JOIN t_schedule s ON s.id = c.schedule_id " +
                "WHERE (s.id IS NULL OR (s.del_flag = 0 AND s.work_date BETWEEN @fromDate AND @toDate)) ORDER BY c.id DESC",
                new { fromDate = fromDate.ToString("yyyy-MM-dd"), toDate = toDate.ToString("yyyy-MM-dd") }).ToList();
        }

        public List<UnderstaffedSlotDto> DetectUnderstaffedSlots(IDbConnection connection, DateTime fromDate, DateTime toDate)
        {
            var slots = new List<UnderstaffedSlotDto>();
            List<ShiftDto> shifts = ListShifts(connection);
            if (shifts.Count == 0) { return slots; }

            // date×shift 实排人数（草稿+已发布，不含已删除/取消）
            var counts = connection.Query<ShiftCountRow>(
                "SELECT s.work_date AS WorkDate, s.shift_id AS ShiftId, COUNT(1) AS Cnt " +
                "FROM t_schedule s WHERE s.del_flag = 0 AND s.status IN (0, 1) " +
                "AND s.work_date BETWEEN @fromDate AND @toDate GROUP BY s.work_date, s.shift_id",
                new { fromDate = fromDate.ToString("yyyy-MM-dd"), toDate = toDate.ToString("yyyy-MM-dd") })
                .ToDictionary(r => r.WorkDate + "|" + r.ShiftId, r => r.Cnt);

            for (DateTime d = fromDate.Date; d <= toDate.Date; d = d.AddDays(1))
            {
                foreach (ShiftDto shift in shifts)
                {
                    int required = (shift.MinRequired ?? 0);
                    if (required <= 0) { continue; } // 休等零需求班次不参与缺员判定
                    int actual = counts.TryGetValue(d.ToString("yyyy-MM-dd") + "|" + shift.Id, out int c) ? c : 0;
                    int gap = required - actual;
                    if (gap <= 0) { continue; }
                    slots.Add(new UnderstaffedSlotDto
                    {
                        Date = d,
                        ShiftId = shift.Id,
                        ShiftName = shift.Name,
                        Required = required,
                        Actual = actual,
                        Gap = gap,
                        Candidates = ListUnscheduledOnDutyEmployees(connection, d, 3)
                    });
                }
            }
            return slots;
        }

        /// <summary>推荐补班人选：当日未排班且在岗的员工前 limit 名。</summary>
        private List<EmployeeDto> ListUnscheduledOnDutyEmployees(IDbConnection connection, DateTime workDate, int limit)
        {
            return connection.Query<EmployeeDto>(
                EmployeeBaseSql + " WHERE e.del_flag = 0 AND e.status = 0 " +
                "AND NOT EXISTS (SELECT 1 FROM t_schedule s WHERE s.employee_id = e.id AND s.work_date = @workDate AND s.del_flag = 0) " +
                "ORDER BY e.id LIMIT @limit", new { workDate = workDate.ToString("yyyy-MM-dd"), limit })
                .Select(e => NormalizeEmployee(e, connection)).ToList();
        }

        /// <summary>回填 HasConflict（同人同日双班）/ IsUnderstaffed（date×shift 缺口）展示标记（审计 24）。</summary>
        private void ApplyScheduleFlags(IDbConnection connection, DateTime fromDate, DateTime toDate, List<ScheduleDto> schedules)
        {
            if (schedules == null || schedules.Count == 0) { return; }

            // 双班冲突：同人同日 >1 个不同班次 → 组内全部标记
            foreach (var g in schedules.GroupBy(s => new { s.EmployeeId, Key = s.WorkDate.ToString("yyyy-MM-dd") }))
            {
                if (g.Select(x => x.ShiftId).Distinct().Count() > 1)
                {
                    foreach (ScheduleDto s in g) { s.HasConflict = true; }
                }
            }

            // 缺员标记：按 date×shift 需求人数比对
            List<UnderstaffedSlotDto> gaps = DetectUnderstaffedSlots(connection, fromDate, toDate);
            if (gaps.Count == 0) { return; }
            var gapKeys = new HashSet<string>(gaps.Select(g => g.Date.ToString("yyyy-MM-dd") + "|" + g.ShiftId));
            foreach (ScheduleDto s in schedules)
            {
                if (gapKeys.Contains(s.WorkDate.ToString("yyyy-MM-dd") + "|" + s.ShiftId)) { s.IsUnderstaffed = true; }
            }
        }

        // ===================== 考勤 =====================
        // 班次列：关联当日排班（已发布优先），修复班次恒空（审计 18）；emp_no 列取工号
        private const string AttendanceBaseSql =
            "SELECT a.id, a.employee_id AS EmployeeId, a.work_date AS WorkDate, a.check_in AS CheckIn, a.check_out AS CheckOut, " +
            "a.result, a.review_by AS ReviewBy, a.review_note AS ReviewNote, a.abnormal_type AS AbnormalType, a.review_at AS ReviewAt, " +
            "COALESCE(e.emp_no, 'YG-' || printf('%03d', a.employee_id)) AS EmpNo, " +
            "COALESCE(e.name,'') AS EmpName, COALESCE(d.name,'') AS DeptName, " +
            "(SELECT sh.name FROM t_schedule s2 JOIN t_shift sh ON sh.id = s2.shift_id " +
            " WHERE s2.employee_id = a.employee_id AND s2.work_date = a.work_date AND s2.del_flag = 0 " +
            " ORDER BY CASE WHEN s2.status = 1 THEN 0 ELSE 1 END, s2.id LIMIT 1) AS ShiftName " +
            "FROM t_attendance a " +
            "JOIN t_employee e ON e.id = a.employee_id " +
            "LEFT JOIN t_department d ON d.id = e.dept_id";

        public PageResult<AttendanceDto> QueryAttendance(IDbConnection connection, AttendanceQueryRequest query, out int total)
        {
            string where = "WHERE 1=1";
            var p = new DynamicParameters();
            if (query.EmployeeId.HasValue) { where += " AND a.employee_id = @employeeId"; p.Add("employeeId", query.EmployeeId.Value); }
            if (query.DeptId.HasValue) { where += " AND e.dept_id = @deptId"; p.Add("deptId", query.DeptId.Value); }
            if (query.WorkDateFrom.HasValue) { where += " AND a.work_date >= @from"; p.Add("from", query.WorkDateFrom.Value.ToString("yyyy-MM-dd")); }
            if (query.WorkDateTo.HasValue) { where += " AND a.work_date <= @to"; p.Add("to", query.WorkDateTo.Value.ToString("yyyy-MM-dd")); }
            if (query.Result.HasValue) { where += " AND a.result = @result"; p.Add("result", (int)query.Result.Value); }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                where += " AND (e.name LIKE @kw OR e.phone LIKE @kw OR COALESCE(e.emp_no, 'YG-' || printf('%03d', a.employee_id)) LIKE @kw OR CAST(a.employee_id AS TEXT) LIKE @idkw)";
                p.Add("kw", "%" + query.Keyword.Trim() + "%");
                p.Add("idkw", "%" + query.Keyword.Trim() + "%");
            }
            total = connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_attendance a JOIN t_employee e ON e.id = a.employee_id " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = ClampPageSize(query.PageSize);
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize); p.Add("offset", offset);
            var items = connection.Query<AttendanceDto>(
                AttendanceBaseSql + " " + where + " ORDER BY a.work_date DESC, a.employee_id LIMIT @limit OFFSET @offset", p)
                .Select(NormalizeAttendance).ToList();
            return new PageResult<AttendanceDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public AttendanceDto GetAttendance(IDbConnection connection, int id)
        {
            return NormalizeAttendance(connection.QueryFirstOrDefault<AttendanceDto>(
                AttendanceBaseSql + " WHERE a.id = @id", new { id }));
        }

        public AttendanceDto GetAttendanceByEmployeeDate(IDbConnection connection, int employeeId, DateTime workDate)
        {
            return NormalizeAttendance(connection.QueryFirstOrDefault<AttendanceDto>(
                AttendanceBaseSql + " WHERE a.employee_id = @employeeId AND a.work_date = @workDate",
                new { employeeId, workDate = workDate.ToString("yyyy-MM-dd") }));
        }

        public ScheduleDto GetPublishedScheduleForEmployeeDate(IDbConnection connection, int employeeId, DateTime workDate)
        {
            return NormalizeSchedule(connection.QueryFirstOrDefault<ScheduleDto>(
                ScheduleBaseSql + " WHERE s.del_flag = 0 AND s.status = 1 AND s.employee_id = @employeeId AND s.work_date = @workDate " +
                "ORDER BY s.id LIMIT 1",
                new { employeeId, workDate = workDate.ToString("yyyy-MM-dd") }));
        }

        public int InsertAttendance(IDbConnection connection, IDbTransaction transaction, AttendanceDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_attendance (employee_id, work_date, check_in, check_out, result, abnormal_type) " +
                "VALUES (@EmployeeId, @WorkDate, @CheckIn, @CheckOut, @Result, @AbnormalType); SELECT last_insert_rowid();",
                new { dto.EmployeeId, WorkDate = dto.WorkDate.ToString("yyyy-MM-dd"), dto.CheckIn, dto.CheckOut,
                      Result = (int)dto.Result, dto.AbnormalType }, transaction);
        }

        public void UpdateAttendance(IDbConnection connection, IDbTransaction transaction, AttendanceDto dto)
        {
            connection.Execute(
                "UPDATE t_attendance SET check_in = @CheckIn, check_out = @CheckOut, result = @Result, " +
                "review_by = @ReviewBy, review_note = @ReviewNote, review_at = @ReviewAt, abnormal_type = @AbnormalType, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id",
                new { dto.Id, dto.CheckIn, dto.CheckOut, Result = (int)dto.Result, dto.ReviewBy, dto.ReviewNote,
                      ReviewAt = dto.ReviewAt.HasValue ? dto.ReviewAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : null,
                      dto.AbnormalType }, transaction);
        }

        /// <summary>
        /// 排班→考勤同步（跨模块投影）：考勤记录严格等于「当前生效排班」的视图。
        /// 1) 清除孤儿：无对应生效工作班次排班（含已删除、已取消、或改为休）的考勤残留；
        /// 2) 补齐缺失：为周期内「在岗、非休班次、未取消」的排班补齐考勤草稿（employee_id+work_date 非重复）。
        /// 休（start_time 为空）与已取消排班不产生考勤。
        /// </summary>
        public void SyncAttendanceFromSchedules(IDbConnection connection, IDbTransaction transaction, DateTime fromDate, DateTime toDate)
        {
            string from = fromDate.ToString("yyyy-MM-dd");
            string to = toDate.ToString("yyyy-MM-dd");
            // 1) 清孤儿：无对应生效工作班次排班的考勤记录（排班删除/变更残留）→ 保证跨模块引用不残留“瞎编”班次
            connection.Execute(
                "DELETE FROM t_attendance " +
                "WHERE work_date BETWEEN @from AND @to " +
                "  AND NOT EXISTS (SELECT 1 FROM t_schedule s JOIN t_shift sh ON sh.id = s.shift_id " +
                "                  WHERE s.del_flag = 0 AND s.status <> 2 " +
                "                    AND sh.start_time IS NOT NULL AND sh.start_time <> '' " +
                "                    AND s.employee_id = t_attendance.employee_id AND s.work_date = t_attendance.work_date)",
                new { from, to }, transaction);
            // 2) 补齐缺失
            connection.Execute(
                "INSERT INTO t_attendance (employee_id, work_date, result) " +
                "SELECT s.employee_id, s.work_date, 0 " +
                "FROM t_schedule s " +
                "JOIN t_shift sh ON sh.id = s.shift_id " +
                "JOIN t_employee e ON e.id = s.employee_id " +
                "WHERE s.del_flag = 0 AND s.status <> 2 " +
                "  AND s.work_date BETWEEN @fromDate AND @toDate " +
                "  AND sh.start_time IS NOT NULL AND sh.start_time <> '' " +
                "  AND e.del_flag = 0 " +
                "  AND NOT EXISTS (SELECT 1 FROM t_attendance a WHERE a.employee_id = s.employee_id AND a.work_date = s.work_date)",
                new { fromDate = from, toDate = to }, transaction);
        }

        /// <summary>无显式事务的排班→考勤同步（查询前补齐用，单条 INSERT...SELECT 原子）。</summary>
        public void SyncAttendanceFromSchedules(IDbConnection connection, DateTime fromDate, DateTime toDate)
        {
            SyncAttendanceFromSchedules(connection, null, fromDate, toDate);
        }

        /// <summary>删除某员工某日考勤（排班删除时的联动清空；考勤表无 del_flag，直接物理删除）。</summary>
        public void DeleteAttendanceByEmployeeDate(IDbConnection connection, IDbTransaction transaction, int employeeId, DateTime workDate)
        {
            connection.Execute("DELETE FROM t_attendance WHERE employee_id = @employeeId AND work_date = @workDate",
                new { employeeId, workDate = workDate.ToString("yyyy-MM-dd") }, transaction);
        }

        public AttendanceSummaryDto SummarizeAttendance(IDbConnection connection, int year, int month, int? deptId)
        {
            DateTime from = new DateTime(year, month, 1);
            DateTime to = from.AddMonths(1).AddDays(-1);
            string sql =
                "SELECT COUNT(1) AS TotalCount, " +
                "COALESCE(SUM(CASE WHEN a.result = 1 THEN 1 ELSE 0 END), 0) AS NormalCount, " +  // 正常
                "COALESCE(SUM(CASE WHEN a.result = 4 THEN 1 ELSE 0 END), 0) AS LateCount, " +  // 迟到
                "COALESCE(SUM(CASE WHEN a.result = 5 THEN 1 ELSE 0 END), 0) AS AbsentCount, " +  // 旷工
                "COALESCE(SUM(CASE WHEN a.result = 6 THEN 1 ELSE 0 END), 0) AS LeaveCount, " +  // 请假
                "COALESCE(SUM(CASE WHEN a.review_by IS NULL THEN 1 ELSE 0 END), 0) AS PendingReviewCount " +
                "FROM t_attendance a JOIN t_employee e ON e.id = a.employee_id " +
                "WHERE e.del_flag = 0 AND a.work_date >= @from AND a.work_date <= @to";
            var p = new DynamicParameters();
            p.Add("from", from.ToString("yyyy-MM-dd"));
            p.Add("to", to.ToString("yyyy-MM-dd"));
            if (deptId.HasValue) { sql += " AND e.dept_id = @deptId"; p.Add("deptId", deptId.Value); }
            AttendanceAggRow agg = connection.QueryFirstOrDefault<AttendanceAggRow>(sql, p) ?? new AttendanceAggRow();
            return new AttendanceSummaryDto
            {
                Year = year,
                Month = month,
                TotalCount = agg.TotalCount,
                AttendanceRate = agg.TotalCount > 0 ? Math.Round(agg.NormalCount * 1000.0 / agg.TotalCount) / 10.0 : 0,
                LateCount = agg.LateCount,
                AbsentCount = agg.AbsentCount,
                LeaveCount = agg.LeaveCount,
                PendingReviewCount = agg.PendingReviewCount
            };
        }

        public List<AttendanceDto> ListAttendanceForExport(IDbConnection connection, int year, int month, int? deptId)
        {
            DateTime from = new DateTime(year, month, 1);
            DateTime to = from.AddMonths(1).AddDays(-1);
            string where = "WHERE a.work_date >= @from AND a.work_date <= @to";
            var p = new DynamicParameters();
            p.Add("from", from.ToString("yyyy-MM-dd"));
            p.Add("to", to.ToString("yyyy-MM-dd"));
            if (deptId.HasValue) { where += " AND e.dept_id = @deptId"; p.Add("deptId", deptId.Value); }
            return connection.Query<AttendanceDto>(
                AttendanceBaseSql + " " + where + " ORDER BY a.work_date, a.employee_id", p)
                .Select(NormalizeAttendance).ToList();
        }

        public void InsertExportLog(IDbConnection connection, string module, int format, string filePath)
        {
            try
            {
                connection.Execute(
                    "INSERT INTO t_export_log (module, format, file_path) VALUES (@module, @format, @filePath)",
                    new { module, format, filePath });
            }
            catch
            {
                // 导出日志为可选留痕，失败不影响导出
            }
        }

        // ===================== 展示字段 =====================
        private static EmployeeDto NormalizeEmployee(EmployeeDto e, IDbConnection connection)
        {
            if (e == null) { return null; }
            // emp_no 列为空则回退 YG-id 并补写（审计 22：存量兜底）
            if (string.IsNullOrWhiteSpace(e.EmpNo))
            {
                e.EmpNo = "YG-" + e.Id.ToString("000");
                if (e.Id > 0 && connection != null)
                {
                    connection.Execute(
                        "UPDATE t_employee SET emp_no = @EmpNo WHERE id = @Id AND (emp_no IS NULL OR emp_no = '')",
                        new { e.EmpNo, e.Id });
                }
            }
            e.StatusText = FormatEmployeeStatus(e.Status);
            // 需求：员工列表联系电话展示完整号码，不再对中间 4 位做 * 脱敏
            e.PhoneMask = e.Phone;
            return e;
        }

        private static string FormatEmployeeStatus(EmployeeStatus status)
        {
            switch (status)
            {
                case EmployeeStatus.Active: return "在岗";
                case EmployeeStatus.OffDuty: return "离岗";
                case EmployeeStatus.Resigned: return "离职";
                case EmployeeStatus.Vacation: return "休假";
                default: return "在岗";
            }
        }

        private static ScheduleDto NormalizeSchedule(ScheduleDto s)
        {
            if (s == null) { return null; }
            if (string.IsNullOrWhiteSpace(s.EmpNo)) { s.EmpNo = "YG-" + s.EmployeeId.ToString("000"); }
            s.PeriodText = (s.ShiftName ?? string.Empty)
                + (string.IsNullOrEmpty(s.ShiftStart) ? string.Empty : " " + s.ShiftStart + "-" + s.ShiftEnd);
            s.StatusText = s.Status == ScheduleStatus.Published ? "已发布" : (s.Status == ScheduleStatus.Cancelled ? "已取消" : "草稿");
            return s;
        }

        private static AttendanceDto NormalizeAttendance(AttendanceDto a)
        {
            if (a == null) { return null; }
            if (string.IsNullOrWhiteSpace(a.EmpNo)) { a.EmpNo = "YG-" + a.EmployeeId.ToString("000"); }
            a.WorkDateText = a.WorkDate.ToString("MM-dd");
            a.ResultText = FormatAttendanceResult(a.Result);
            a.ReviewStatusText = a.ReviewBy.HasValue ? "已确认" : "待审核";
            a.ReviewText = a.ReviewNote ?? string.Empty;
            // ShiftName 由 SQL 关联当日排班（已发布优先），不再置空
            return a;
        }

        private static string FormatAttendanceResult(AttendanceResult result)
        {
            switch (result)
            {
                case AttendanceResult.Recorded: return "已记录";
                case AttendanceResult.Normal: return "正常";
                case AttendanceResult.Abnormal: return "异常";
                case AttendanceResult.Reviewed: return "已审核";
                case AttendanceResult.Late: return "迟到";
                case AttendanceResult.Absent: return "旷工";
                case AttendanceResult.Leave: return "请假";
                default: return "异常";
            }
        }

        private static int ClampPageSize(int pageSize)
        {
            if (pageSize <= 0) { return 20; }
            return Math.Min(pageSize, MaxPageSize);
        }

        /// <summary>date×shift 实排人数聚合行。</summary>
        private class ShiftCountRow
        {
            public string WorkDate { get; set; }
            public int ShiftId { get; set; }
            public int Cnt { get; set; }
        }

        /// <summary>考勤统计聚合行。</summary>
        private class AttendanceAggRow
        {
            public int TotalCount { get; set; }
            public int NormalCount { get; set; }
            public int LateCount { get; set; }
            public int AbsentCount { get; set; }
            public int LeaveCount { get; set; }
            public int PendingReviewCount { get; set; }
        }
    }
}

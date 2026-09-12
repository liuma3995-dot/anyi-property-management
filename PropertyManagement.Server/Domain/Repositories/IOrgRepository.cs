using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Org;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>人员组织仓储（M6 D6-2，UC-ORG-001~007 + BR-ORG-02~07）。事务边界由服务层控制。</summary>
    public interface IOrgRepository
    {
        // ---------- 部门（UC-ORG-001） ----------
        List<DepartmentDto> ListDepartments(IDbConnection connection, string keyword);
        DepartmentDto GetDepartment(IDbConnection connection, int id);
        int InsertDepartment(IDbConnection connection, IDbTransaction transaction, DepartmentDto dto);
        void UpdateDepartment(IDbConnection connection, IDbTransaction transaction, DepartmentDto dto);
        void SoftDeleteDepartment(IDbConnection connection, IDbTransaction transaction, int id);
        int CountEmployeesByDept(IDbConnection connection, int deptId);

        // ---------- 岗位 ----------
        List<PositionDto> ListPositions(IDbConnection connection, int? deptId);
        int InsertPosition(IDbConnection connection, IDbTransaction transaction, PositionDto dto);
        void UpdatePosition(IDbConnection connection, IDbTransaction transaction, PositionDto dto);
        void DeletePosition(IDbConnection connection, IDbTransaction transaction, int id);
        int CountEmployeesByPosition(IDbConnection connection, int positionId);

        // ---------- 员工（UC-ORG-002/005/006，BR-ORG-02） ----------
        EmployeeDto GetEmployee(IDbConnection connection, int id);
        PageResult<EmployeeDto> QueryEmployees(IDbConnection connection, EmployeeQueryRequest query, out int total);
        int InsertEmployee(IDbConnection connection, IDbTransaction transaction, EmployeeDto dto);
        void UpdateEmployee(IDbConnection connection, IDbTransaction transaction, EmployeeDto dto);
        void SoftDeleteEmployee(IDbConnection connection, IDbTransaction transaction, int id);

        /// <summary>离职（BR-ORG-02）：仅置 status=Resigned，保留 del_flag=0（档案保留）。</summary>
        void ResignEmployee(IDbConnection connection, IDbTransaction transaction, int id);

        /// <summary>按 emp_no 停用员工登录账号（t_user.status=2），返回停用条数。</summary>
        int DisableUserAccountsByEmployee(IDbConnection connection, IDbTransaction transaction, int employeeId);

        /// <summary>
        /// 停用员工电话簿条目（UC-TEL-005 联动）：优先按 employee_id 关联；
        /// 无匹配时按 姓名+entry_type=2 兜底。返回停用条数。
        /// </summary>
        int DisablePhoneEntriesByEmployee(IDbConnection connection, IDbTransaction transaction, int employeeId, string employeeName);

        /// <summary>新建员工同事务开通登录账号（username=emp_no，首登强制改密），返回 t_user.id（0=已存在未创建）。</summary>
        int InsertUserAccount(IDbConnection connection, IDbTransaction transaction, string userName, string passwordHash);

        /// <summary>按用户名取 t_user.id（审核留痕 ReviewBy 用；不存在返回 null）。</summary>
        int? GetUserIdByUserName(IDbConnection connection, string userName);

        List<EmployeeDto> ListOnDutyEmployees(IDbConnection connection, int? deptId);
        List<EmployeeStatusLogDto> ListEmployeeStatusLogs(IDbConnection connection, int employeeId);
        void InsertEmployeeStatusLog(IDbConnection connection, IDbTransaction transaction, int employeeId, EmployeeStatus status);

        // ---------- 班次（UC-ORG-003） ----------
        List<ShiftDto> ListShifts(IDbConnection connection);
        int InsertShift(IDbConnection connection, IDbTransaction transaction, ShiftDto dto);
        void UpdateShift(IDbConnection connection, IDbTransaction transaction, ShiftDto dto);
        void DeleteShift(IDbConnection connection, IDbTransaction transaction, int id);
        int CountSchedulesByShift(IDbConnection connection, int shiftId);

        // ---------- 排班（UC-ORG-003，BR-ORG-03/04） ----------
        List<ScheduleDto> QuerySchedules(IDbConnection connection, DateTime fromDate, DateTime toDate, int? employeeId);
        List<ScheduleDto> ListSchedulesByDateRange(IDbConnection connection, DateTime fromDate, DateTime toDate);
        int InsertSchedule(IDbConnection connection, IDbTransaction transaction, ScheduleDto dto);
        void UpdateSchedule(IDbConnection connection, IDbTransaction transaction, ScheduleDto dto);
        void SoftDeleteSchedule(IDbConnection connection, IDbTransaction transaction, int id);
        ScheduleDto GetSchedule(IDbConnection connection, int id);
        List<ScheduleDto> ListSchedulesInRangeFiltered(IDbConnection connection, DateTime fromDate, DateTime toDate, List<int> shiftIds);
        /// <summary>按日期区间软删排班（shiftIds null/空=全部），返回删除条数（批量删除排班）。</summary>
        int SoftDeleteSchedulesInRange(IDbConnection connection, IDbTransaction transaction, DateTime fromDate, DateTime toDate, List<int> shiftIds);
        void InsertScheduleConflictLog(IDbConnection connection, IDbTransaction transaction, ScheduleConflictLogDto dto);
        List<ScheduleConflictLogDto> ListScheduleConflicts(IDbConnection connection, DateTime fromDate, DateTime toDate);

        /// <summary>清理区间内排班的冲突/缺员日志（重发布时避免重复写，P2-2.8）。</summary>
        void ClearScheduleConflictLogs(IDbConnection connection, IDbTransaction transaction, DateTime fromDate, DateTime toDate);

        /// <summary>缺员缺口检测（BR-ORG-04）：date×shift 统计草稿+已发布排班数 vs t_shift.min_required，含推荐补班人选。</summary>
        List<UnderstaffedSlotDto> DetectUnderstaffedSlots(IDbConnection connection, DateTime fromDate, DateTime toDate);

        // ---------- 排班模板（排班表模块：由保存排班后的数据提取） ----------
        List<ScheduleTemplateDto> ListScheduleTemplates(IDbConnection connection);
        ScheduleTemplateDto GetScheduleTemplateItems(IDbConnection connection, int templateId);
        int InsertScheduleTemplate(IDbConnection connection, IDbTransaction transaction, ScheduleTemplateDto dto);
        void InsertScheduleTemplateItem(IDbConnection connection, IDbTransaction transaction, ScheduleTemplateItemDto item);
        void SoftDeleteScheduleTemplate(IDbConnection connection, IDbTransaction transaction, int id);

        // ---------- 考勤（UC-ORG-004，BR-ORG-05） ----------
        PageResult<AttendanceDto> QueryAttendance(IDbConnection connection, AttendanceQueryRequest query, out int total);
        AttendanceDto GetAttendance(IDbConnection connection, int id);
        AttendanceDto GetAttendanceByEmployeeDate(IDbConnection connection, int employeeId, DateTime workDate);
        int InsertAttendance(IDbConnection connection, IDbTransaction transaction, AttendanceDto dto);
        void UpdateAttendance(IDbConnection connection, IDbTransaction transaction, AttendanceDto dto);
        /// <summary>排班→考勤同步：为周期内生效排班补齐考勤草稿（BR-ORG-05 闭环）。</summary>
        void SyncAttendanceFromSchedules(IDbConnection connection, IDbTransaction transaction, DateTime fromDate, DateTime toDate);
        /// <summary>无显式事务的排班→考勤同步（查询前补齐用）。</summary>
        void SyncAttendanceFromSchedules(IDbConnection connection, DateTime fromDate, DateTime toDate);
        /// <summary>删除某员工某日考勤（排班删除时联动清空）。</summary>
        void DeleteAttendanceByEmployeeDate(IDbConnection connection, IDbTransaction transaction, int employeeId, DateTime workDate);

        /// <summary>员工当日已发布排班（含班次时间窗，考勤自动判定依据）；无则返回 null。</summary>
        ScheduleDto GetPublishedScheduleForEmployeeDate(IDbConnection connection, int employeeId, DateTime workDate);

        /// <summary>考勤统计（PG-ORG-03 统计卡）。</summary>
        AttendanceSummaryDto SummarizeAttendance(IDbConnection connection, int year, int month, int? deptId);

        /// <summary>考勤月报导出数据（不分页）。</summary>
        List<AttendanceDto> ListAttendanceForExport(IDbConnection connection, int year, int month, int? deptId);

        /// <summary>写导出日志（可选留痕，失败不抛出）。</summary>
        void InsertExportLog(IDbConnection connection, string module, int format, string filePath);
    }
}

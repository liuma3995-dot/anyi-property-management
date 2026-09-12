using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Org
{
    /// <summary>部门（t_department，UC-ORG-001）。</summary>
    public class DepartmentDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int? ParentId { get; set; }
        public int Status { get; set; }
    }

    /// <summary>岗位（t_position）。</summary>
    public class PositionDto
    {
        public int Id { get; set; }
        public int DeptId { get; set; }
        public string Name { get; set; }
    }

    /// <summary>员工（t_employee，UC-ORG-002，离职保留档案 BR-ORG-02）。</summary>
    public class EmployeeDto
    {
        public int Id { get; set; }
        public int DeptId { get; set; }
        public int PositionId { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public DateTime HireDate { get; set; }
        public EmployeeStatus Status { get; set; }
        public string EmpNo { get; set; }
        public string DeptName { get; set; }
        public string PositionName { get; set; }
        public string StatusText { get; set; }
        public string PhoneMask { get; set; }

        /// <summary>初始密码（仅创建员工自动开通账号时返回一次，不落库）。</summary>
        public string InitialPassword { get; set; }
    }

    /// <summary>在岗状态变更记录（t_employee_status_log，UC-ORG-005，只追加）。</summary>
    public class EmployeeStatusLogDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public EmployeeStatus Status { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>登录账号（t_user，UC-ORG-007 账号维护）。</summary>
    public class UserAccountDto
    {
        public int Id { get; set; }
        public string UserName { get; set; }
        public UserStatus Status { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    /// <summary>班次（t_shift；MinRequired=需求人数，BR-ORG-04 缺员判定）。</summary>
    public class ShiftDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public int? MinRequired { get; set; }
    }

    /// <summary>排班（t_schedule，UC-ORG-003，BR-ORG-03 冲突检查）。</summary>
    public class ScheduleDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime WorkDate { get; set; }
        public ScheduleStatus Status { get; set; }
        public string EmpNo { get; set; }
        public string EmpName { get; set; }
        public string ShiftName { get; set; }
        public string ShiftStart { get; set; }
        public string ShiftEnd { get; set; }
        public string PeriodText { get; set; }
        public string StatusText { get; set; }

        /// <summary>同人同日双班冲突标记（网格 ⚠，BR-ORG-03）。</summary>
        public bool HasConflict { get; set; }

        /// <summary>所属日期×班次缺员标记（网格红标，BR-ORG-04）。</summary>
        public bool IsUnderstaffed { get; set; }
    }

    /// <summary>排班冲突/缺口记录（t_schedule_conflict_log，发布时生成）。</summary>
    public class ScheduleConflictLogDto
    {
        public int Id { get; set; }
        public int ScheduleId { get; set; }
        public string ConflictType { get; set; }
        public string Detail { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>考勤记录（t_attendance，UC-ORG-004，BR-ORG-05 异常审核）。</summary>
    public class AttendanceDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string CheckIn { get; set; }
        public string CheckOut { get; set; }
        public AttendanceResult Result { get; set; }
        public int? ReviewBy { get; set; }
        public string ReviewNote { get; set; }
        public string EmpNo { get; set; }
        public string EmpName { get; set; }
        public string DeptName { get; set; }
        public string ShiftName { get; set; }
        public string WorkDateText { get; set; }
        public string ResultText { get; set; }
        public string ReviewText { get; set; }
        public string ReviewStatusText { get; set; }

        /// <summary>异常类型（迟到/缺卡，t_attendance.abnormal_type）。</summary>
        public string AbnormalType { get; set; }

        /// <summary>审核时间留痕（t_attendance.review_at）。</summary>
        public DateTime? ReviewAt { get; set; }
    }

    /// <summary>考勤统计卡（PG-ORG-03：出勤率/迟到/缺卡/待审核补卡）。</summary>
    public class AttendanceSummaryDto
    {
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>本月出勤率（正常/已审核闭环记录数 ÷ 总记录数，百分制保留 1 位小数）。</summary>
        public double AttendanceRate { get; set; }

        /// <summary>迟到人次（含待审核与已确认）。</summary>
        public int LateCount { get; set; }

        /// <summary>旷工人次（未到岗）。</summary>
        public int AbsentCount { get; set; }

        /// <summary>请假日次（休假留痕）。</summary>
        public int LeaveCount { get; set; }

        /// <summary>待审核补卡数（需主管处理）。</summary>
        public int PendingReviewCount { get; set; }

        /// <summary>当月考勤记录总数。</summary>
        public int TotalCount { get; set; }
    }

    /// <summary>缺员缺口（BR-ORG-04：date×shift 需求 vs 实排 + 推荐补班人选）。</summary>
    public class UnderstaffedSlotDto
    {
        public DateTime Date { get; set; }
        public int ShiftId { get; set; }
        public string ShiftName { get; set; }

        /// <summary>班次需求人数（t_shift.min_required）。</summary>
        public int Required { get; set; }

        /// <summary>实排人数（草稿+已发布）。</summary>
        public int Actual { get; set; }

        /// <summary>缺口 = Required - Actual。</summary>
        public int Gap { get; set; }

        /// <summary>推荐补班人选：当日未排班且在岗的员工前 3 名。</summary>
        public List<EmployeeDto> Candidates { get; set; }
    }

    /// <summary>排班计划输出（含冲突清单与缺员缺口）。</summary>
    public class SchedulePlanDto
    {
        public List<ScheduleDto> Schedules { get; set; }
        public List<ScheduleConflictLogDto> Conflicts { get; set; }

        /// <summary>缺员缺口列表（BR-ORG-04，含推荐补班人选）。</summary>
        public List<UnderstaffedSlotDto> Understaffed { get; set; }
    }

    /// <summary>排班模板明细行：员工×周期内第 dayOffset 天×班次。</summary>
    public class ScheduleTemplateItemDto
    {
        public int TemplateId { get; set; }
        public int EmployeeId { get; set; }
        public string EmpNo { get; set; }
        public string EmpName { get; set; }
        public int ShiftId { get; set; }
        public string ShiftName { get; set; }

        /// <summary>周期内偏移天数（0=模板周期开始日）。</summary>
        public int DayOffset { get; set; }
    }

    /// <summary>排班模板（由保存排班后的数据提取，用于一键套用生成排班表）。</summary>
    public class ScheduleTemplateDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int ItemCount { get; set; }
        public string CreatedAtText { get; set; }
        public List<ScheduleTemplateItemDto> Items { get; set; }
    }
}

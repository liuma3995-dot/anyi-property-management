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
    }

    /// <summary>在岗状态变更记录（t_employee_status_log，UC-ORG-005，只追加）。</summary>
    public class EmployeeStatusLogDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public EmployeeStatus Status { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>班次（t_shift）。</summary>
    public class ShiftDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }

    /// <summary>排班（t_schedule，UC-ORG-003，BR-ORG-03 冲突检查）。</summary>
    public class ScheduleDto
    {
        public int Id { get; set; }
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime WorkDate { get; set; }
        public ScheduleStatus Status { get; set; }
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
    }

    /// <summary>排班计划输出（含冲突清单）。</summary>
    public class SchedulePlanDto
    {
        public List<ScheduleDto> Schedules { get; set; }
        public List<ScheduleConflictLogDto> Conflicts { get; set; }
    }
}

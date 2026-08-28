using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Org
{
    public class DepartmentRequest
    {
        public string Name { get; set; }
        public int? ParentId { get; set; }
    }

    public class PositionRequest
    {
        public int DeptId { get; set; }
        public string Name { get; set; }
    }

    public class EmployeeRequest
    {
        public int DeptId { get; set; }
        public int PositionId { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
        public DateTime HireDate { get; set; }
    }

    public class ShiftRequest
    {
        public string Name { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }

    public class ScheduleRequest
    {
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime WorkDate { get; set; }
    }

    /// <summary>排班生成请求（批量，按员工/日期区间）。</summary>
    public class ScheduleGenerateRequest
    {
        public List<ScheduleRequest> Items { get; set; }
    }

    /// <summary>排班发布请求（发布前冲突检查，BR-ORG-03）。</summary>
    public class SchedulePublishRequest
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
    }

    /// <summary>考勤登记请求（签到/签退，UC-ORG-004）。</summary>
    public class AttendanceRequest
    {
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string CheckIn { get; set; }
        public string CheckOut { get; set; }
    }

    /// <summary>考勤异常审核请求（BR-ORG-05）。</summary>
    public class AttendanceReviewRequest
    {
        public string ReviewNote { get; set; }
    }

    /// <summary>员工在岗状态变更请求（UC-ORG-005：离岗/返岗/离职）。</summary>
    public class EmployeeStatusRequest
    {
        public EmployeeStatus Status { get; set; }
    }

    /// <summary>员工查询条件（UC-ORG-006，分页）。</summary>
    public class EmployeeQueryRequest : PageRequest
    {
        public int? DeptId { get; set; }
        public EmployeeStatus? Status { get; set; }
    }

    /// <summary>登录账号请求（UC-ORG-007，唯一系统管理员）。</summary>
    public class UserAccountRequest
    {
        public string UserName { get; set; }
        public string Password { get; set; }
        public UserStatus Status { get; set; }
    }

    /// <summary>账号密码重置请求（P-02 锁定后管理员重置）。</summary>
    public class ResetPasswordRequest
    {
        public string NewPassword { get; set; }
    }
}

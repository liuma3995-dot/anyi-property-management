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

        /// <summary>部门状态（0启用 1停用，CHG-ORG-02 部门禁删可停用）；null=不修改（更新时保留原值）。</summary>
        public int? Status { get; set; }
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

        /// <summary>班次需求人数（BR-ORG-04 缺员判定）；null=不修改。</summary>
        public int? MinRequired { get; set; }
    }

    public class ScheduleRequest
    {
        public int EmployeeId { get; set; }
        public int ShiftId { get; set; }
        public DateTime WorkDate { get; set; }
    }

    /// <summary>
    /// 排班生成请求：
    /// 1) Items 非空 → 按手工排班项批量生成草稿（原行为）；
    /// 2) Items 为空 → 自动排班（CHG-ORG-03）：在 [FromDate,ToDate]（默认本周一~周日）内
    ///    按各班次 MinRequired 从在岗员工顺序轮流分配生成草稿，跳过当日已有排班的员工。
    /// </summary>
    public class ScheduleGenerateRequest
    {
        public List<ScheduleRequest> Items { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        /// <summary>自动排班时仅生成该部门员工（null=全部在岗员工）。</summary>
        public int? DeptId { get; set; }

        /// <summary>自动排班时仅使用这些班次（null/空=全部可排班次）；手工排班（Items 非空）时忽略。</summary>
        public List<int> ShiftIds { get; set; }
    }

    /// <summary>保存排班模板请求：从指定周期已保存的排班提取模板数据。</summary>
    public class ScheduleTemplateSaveRequest
    {
        public string Name { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
    }

    /// <summary>
    /// 排班发布请求（BR-ORG-03/04）：
    /// 同人同日双班冲突 → 一律阻断（HTTP 409 + 冲突明细，事务回滚）；
    /// 缺员缺口 → 默认阻断并列出缺口；Force=true 时「强制标记发布」：允许发布并写
    /// t_schedule_conflict_log(conflict_type='缺员') 标记缺口（BR-ORG-04）。
    /// </summary>
    public class SchedulePublishRequest
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        /// <summary>强制标记发布：缺员允许发布但必须标记缺口；双班冲突仍不允许。</summary>
        public bool Force { get; set; }
    }

    /// <summary>批量删除排班请求（工具栏「批量删除排班」）：按日期区间删除，可限定班次。</summary>
    public class ScheduleBatchDeleteRequest
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        /// <summary>要删除的班次Id（null/空=删除周期内全部排班）。</summary>
        public List<int> ShiftIds { get; set; }
    }

    /// <summary>考勤登记请求（签到/签退，UC-ORG-004）。</summary>
    public class AttendanceRequest
    {
        public int EmployeeId { get; set; }
        public DateTime WorkDate { get; set; }
        public string CheckIn { get; set; }
        public string CheckOut { get; set; }
    }

    /// <summary>考勤查询条件（UC-ORG-004，按日期/员工/部门/结果）。</summary>
    public class AttendanceQueryRequest : PageRequest
    {
        public int? EmployeeId { get; set; }
        public DateTime? WorkDateFrom { get; set; }
        public DateTime? WorkDateTo { get; set; }
        public AttendanceResult? Result { get; set; }

        /// <summary>部门筛选（CHG-ORG-04）。</summary>
        public int? DeptId { get; set; }
    }

    /// <summary>考勤异常审核请求（BR-ORG-05：通过/驳回，驳回需填写意见）。</summary>
    public class AttendanceReviewRequest
    {
        /// <summary>true=通过（回写打卡并置正常闭环）；false=驳回（保持异常）；null=通过（兼容旧客户端「通过审核」语义）。</summary>
        public bool? IsApproved { get; set; }
        /// <summary>审核选择的状态标识（正常/迟到/旷工/请假）；null=默认按「正常」处理。</summary>
        public AttendanceResult? Result { get; set; }
        public string ReviewNote { get; set; }
        public string CheckIn { get; set; }
        public string CheckOut { get; set; }
    }

    /// <summary>批量审核考勤请求（页面工具栏「批量审核」）：对选中的异常/待补卡记录一键通过闭环。</summary>
    public class AttendanceBatchReviewRequest
    {
        public List<int> Ids { get; set; }

        /// <summary>true=通过（置正常闭环）；false=驳回（意见必填）。批量审核默认通过。</summary>
        public bool IsApproved { get; set; }
        public string ReviewNote { get; set; }
    }

    /// <summary>员工在岗状态变更请求（UC-ORG-005：离岗/返岗/离职）。</summary>
    public class EmployeeStatusRequest
    {
        public EmployeeStatus Status { get; set; }
    }

    /// <summary>员工查询条件（UC-ORG-006，分页；支持部门/岗位/在岗状态筛选）。</summary>
    public class EmployeeQueryRequest : PageRequest
    {
        public int? DeptId { get; set; }
        public EmployeeStatus? Status { get; set; }

        /// <summary>岗位筛选（CHG-ORG-01）。</summary>
        public int? PositionId { get; set; }
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

using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Emergency
{
    /// <summary>应急场景（t_emergency_scene，UC-EMG-001）。</summary>
    public class EmergencySceneDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        /// <summary>场景标识图标（对应客户端资源键，如 Icon.Flame；每个场景选用不同图标区分）。</summary>
        public string IconKey { get; set; }
        public int Status { get; set; }
        public int StepCount { get; set; }
        public string StatusText { get; set; }
        /// <summary>步骤可解析时限合计（分钟；无可解析时限为 null，PG-EMG-01 场景卡「N 步 · M 分钟」）。</summary>
        public int? StepMinutes { get; set; }
        /// <summary>场景卡时限文案（如「6 步 · 31 分钟」；无可解析时限时仅「N 步」）。</summary>
        public string TimeLimitText { get; set; }
    }

    /// <summary>处置步骤（t_emergency_step，UC-EMG-002，版本化 BR-EMG-05）。</summary>
    public class EmergencyStepDto
    {
        public int Id { get; set; }
        public int SceneId { get; set; }
        public int StepNo { get; set; }
        public string Content { get; set; }
        public string VersionNo { get; set; }
        public int Status { get; set; }
        public string Role { get; set; }
        public string TimeLimit { get; set; }
        public string Action { get; set; }
        public string StatusText { get; set; }
    }

    /// <summary>应急事件（t_emergency_event，UC-EMG-003，BR-EMG-01）。</summary>
    public class EmergencyEventDto
    {
        public int Id { get; set; }
        public int SceneId { get; set; }
        public DateTime EventTime { get; set; }
        public string Location { get; set; }
        public string Description { get; set; }
        public EmergencyEventStatus Status { get; set; }
        public string StepVersion { get; set; }
        public bool RecordPending { get; set; }
        public string EventNo { get; set; }
        public string SceneName { get; set; }
        public string StatusText { get; set; }
        public string LevelText { get; set; }
        public int Level { get; set; }
        public string EventTimeText { get; set; }
        public string MainPerson { get; set; }
        public string ElapsedText { get; set; }
        public string DetailLocation { get; set; }
        /// <summary>结案处置结果与物资消耗（BR-EMG-02 必填留档）。</summary>
        public string CloseSummary { get; set; }
        /// <summary>结案时间（P-03 复盘时限/本月已结案口径）。</summary>
        public DateTime? ClosedAt { get; set; }
        /// <summary>发起落库时间（撤销 60 秒窗口/今日新增口径）。</summary>
        public DateTime? CreatedAt { get; set; }
        /// <summary>处置超时（进行中且已用时超级别阈值：Ⅰ 级 15 分钟、Ⅱ 级 30 分钟、Ⅲ 级 60 分钟）。</summary>
        public bool IsOverdue { get; set; }
    }

    /// <summary>应急指派（t_emergency_assign，UC-EMG-004）。</summary>
    public class EmergencyAssignDto
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public EmergencyAssignType AssignType { get; set; }
        public DateTime AssignedAt { get; set; }
        /// <summary>岗位名称（BR-EMG-03 展示口径）。</summary>
        public string PositionName { get; set; }
        /// <summary>脱敏电话（138****2233，原型口径）。</summary>
        public string Phone { get; set; }
        /// <summary>当日已发布排班班次（无排班为空）。</summary>
        public string ShiftName { get; set; }
        /// <summary>当日是否有已发布排班（在岗）。</summary>
        public bool OnDuty { get; set; }
    }

    /// <summary>处置记录（t_emergency_record，UC-EMG-005，补录留痕）。</summary>
    public class EmergencyRecordDto
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public DateTime RecordTime { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        public string Recorder { get; set; }
        /// <summary>补录标记（BR-EMG-06：归档后只能补录）。</summary>
        public bool IsSupplement { get; set; }
    }

    /// <summary>复盘改进措施项（t_emergency_review_item，PG-EMG-04）。</summary>
    public class EmergencyReviewItemDto
    {
        public int Id { get; set; }
        public int ReviewId { get; set; }
        public string Content { get; set; }
        public string Owner { get; set; }
        public DateTime? DueDate { get; set; }
        /// <summary>0 待开展 1 进行中 2 已完成。</summary>
        public int Status { get; set; }
        public int Sort { get; set; }
        public string StatusText { get; set; }
    }

    /// <summary>复盘记录（t_emergency_review，UC-EMG-007，P-03 时限 3 工作日）。</summary>
    public class EmergencyReviewDto
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public string Cause { get; set; }
        public string Measure { get; set; }
        public DateTime? FinishAt { get; set; }
        public DateTime CreatedAt { get; set; }
        /// <summary>复盘编号（FP-yyMM-序号，PG-EMG-04）。</summary>
        public string ReviewNo { get; set; }
        /// <summary>主持人（为空取当前登录用户）。</summary>
        public string HostName { get; set; }
        /// <summary>复盘日期（计划）。</summary>
        public DateTime? PlanDate { get; set; }
        /// <summary>完成度 0~100（未传时按改进项完成比例推导）。</summary>
        public int? CompletionRate { get; set; }
        /// <summary>关联事件编号（列表页「关联事件」列）。</summary>
        public string EventNo { get; set; }
        /// <summary>复盘状态（按改进项推导：待开展/进行中/已完成）。</summary>
        public string StatusText { get; set; }
        /// <summary>改进措施清单（先删后插全量保存）。</summary>
        public List<EmergencyReviewItemDto> Items { get; set; }
    }

    /// <summary>应急责任匹配候选（GET scenes/{id}/match，BR-EMG-03：角色 + 当日已发布排班 + 在岗）。</summary>
    public class EmergencyMatchDto
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; }
        public string PositionName { get; set; }
        /// <summary>脱敏电话（138****2233，原型口径）。</summary>
        public string Phone { get; set; }
        /// <summary>当日已发布排班班次（无排班为空）。</summary>
        public string ShiftName { get; set; }
        /// <summary>当日是否有已发布排班（在岗）。</summary>
        public bool OnDuty { get; set; }
        /// <summary>命中的场景步骤责任角色（匹配面板按角色分组展示）。</summary>
        public string Role { get; set; }
    }

    /// <summary>事件工作台汇总统计（GET events/stats，T6-1-7）。</summary>
    public class EmergencyEventStatsDto
    {
        /// <summary>进行中（已发起 + 处置中）。</summary>
        public int Ongoing { get; set; }
        /// <summary>进行中且处置超时数（Ⅰ 级 &gt;15 分钟、Ⅱ 级 &gt;30 分钟、Ⅲ 级 &gt;60 分钟，原型 PG-EMG-02/03）。</summary>
        public int OvertimeCount { get; set; }
        /// <summary>今日新增（按 created_at 发起落库时间口径）。</summary>
        public int TodayNew { get; set; }
        /// <summary>本月已结案（closed_at 在本月）。</summary>
        public int MonthClosed { get; set; }
        /// <summary>待复盘（已结案且无复盘、closed_at 超过 P-03 配置工作日）。</summary>
        public int ReviewPending { get; set; }
    }

    /// <summary>应急事件详情（事件 + 步骤 + 指派 + 处置 + 复盘，工作台聚合）。</summary>
    public class EmergencyEventDetailDto
    {
        public EmergencyEventDto Event { get; set; }
        public List<EmergencyStepDto> Steps { get; set; }
        public List<EmergencyAssignDto> Assignments { get; set; }
        public List<EmergencyRecordDto> Records { get; set; }
        public EmergencyReviewDto Review { get; set; }
    }

    /// <summary>应急事件状态变更记录（t_event_status_log，只追加）。</summary>
    public class EmergencyEventStatusLogDto
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public EmergencyEventStatus OldStatus { get; set; }
        public EmergencyEventStatus NewStatus { get; set; }
        public DateTime ChangedAt { get; set; }
        /// <summary>操作人（登录用户名，migration_019）。</summary>
        public string Operator { get; set; }
        /// <summary>动作类型（发起/处置/结案/复盘/撤销，migration_019）。</summary>
        public string Action { get; set; }
    }
}

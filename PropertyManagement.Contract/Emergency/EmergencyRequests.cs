using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Emergency
{
    public class EmergencySceneRequest
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string IconKey { get; set; }
        /// <summary>0 启用 1 停用（更新时 null = 保持原状态）。</summary>
        public int? Status { get; set; }
    }

    /// <summary>场景启用/停用请求（POST scenes/{id}/status，BR-EMG-01 停用不可发起）。</summary>
    public class EmergencySceneStatusRequest
    {
        public int Status { get; set; }
    }

    public class EmergencyStepRequest
    {
        public int SceneId { get; set; }
        public int StepNo { get; set; }
        public string Content { get; set; }
        public string VersionNo { get; set; }
        public string Role { get; set; }
        public string TimeLimit { get; set; }
        public string Action { get; set; }
    }

    /// <summary>发起应急事件请求（UC-EMG-003，P-07 无人匹配不阻塞）。</summary>
    public class EmergencyEventCreateRequest
    {
        public int SceneId { get; set; }
        public DateTime EventTime { get; set; }
        public string Location { get; set; }
        public string Description { get; set; }
        public string DetailLocation { get; set; }
        public int Level { get; set; }
        /// <summary>发起人（默认第一处置人，P-07）；为空取当前登录用户。</summary>
        public string InitiatorName { get; set; }
    }

    /// <summary>责任/值班匹配请求（UC-EMG-004：自动匹配 + 人工调整）。</summary>
    public class EmergencyAssignRequest
    {
        public int EventId { get; set; }
        public List<int> ResponsibleIds { get; set; }
        public List<int> DutyIds { get; set; }
    }

    /// <summary>处置记录请求（UC-EMG-005；已结案事件传 IsSupplement=true 走补录，BR-EMG-06）。</summary>
    public class EmergencyRecordRequest
    {
        public int EventId { get; set; }
        public DateTime RecordTime { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        public string Recorder { get; set; }
        /// <summary>补录标记（对已结案/已复盘事件必须为 true，落库 is_supplement=1）。</summary>
        public bool IsSupplement { get; set; }
    }

    /// <summary>结案请求（UC-EMG-006，BR-EMG-02/06）。</summary>
    public class EmergencyCloseRequest
    {
        public int EventId { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>复盘改进措施项请求（t_emergency_review_item，PG-EMG-04）。</summary>
    public class EmergencyReviewItemRequest
    {
        public string Content { get; set; }
        public string Owner { get; set; }
        public DateTime? DueDate { get; set; }
        /// <summary>0 待开展 1 进行中 2 已完成。</summary>
        public int Status { get; set; }
    }

    /// <summary>复盘请求（UC-EMG-007：问题根因 + 改进措施清单，先删后插全量保存）。</summary>
    public class EmergencyReviewRequest
    {
        public int EventId { get; set; }
        public string Cause { get; set; }
        public string Measure { get; set; }
        /// <summary>主持人（为空取当前登录用户）。</summary>
        public string HostName { get; set; }
        /// <summary>复盘日期（计划），为空取当天。</summary>
        public DateTime? PlanDate { get; set; }
        /// <summary>完成度 0~100，为空按改进项完成比例推导。</summary>
        public int? CompletionRate { get; set; }
        public List<EmergencyReviewItemRequest> Items { get; set; }
    }

    /// <summary>应急历史查询（UC-EMG-008，分页）。</summary>
    public class EmergencyEventQueryRequest : PageRequest
    {
        public EmergencyEventStatus? Status { get; set; }
        public int? SceneId { get; set; }
        public int? Level { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}

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
    }

    public class EmergencyStepRequest
    {
        public int SceneId { get; set; }
        public int StepNo { get; set; }
        public string Content { get; set; }
        public string VersionNo { get; set; }
    }

    /// <summary>发起应急事件请求（UC-EMG-003，P-07 无人匹配不阻塞）。</summary>
    public class EmergencyEventCreateRequest
    {
        public int SceneId { get; set; }
        public DateTime EventTime { get; set; }
        public string Location { get; set; }
        public string Description { get; set; }
    }

    /// <summary>责任/值班匹配请求（UC-EMG-004：自动匹配 + 人工调整）。</summary>
    public class EmergencyAssignRequest
    {
        public int EventId { get; set; }
        public List<int> ResponsibleIds { get; set; }
        public List<int> DutyIds { get; set; }
    }

    /// <summary>处置记录请求（UC-EMG-005）。</summary>
    public class EmergencyRecordRequest
    {
        public int EventId { get; set; }
        public DateTime RecordTime { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        public string Recorder { get; set; }
    }

    /// <summary>结案请求（UC-EMG-006，BR-EMG-02/06）。</summary>
    public class EmergencyCloseRequest
    {
        public int EventId { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>复盘请求（UC-EMG-007）。</summary>
    public class EmergencyReviewRequest
    {
        public int EventId { get; set; }
        public string Cause { get; set; }
        public string Measure { get; set; }
    }

    /// <summary>应急历史查询（UC-EMG-008，分页）。</summary>
    public class EmergencyEventQueryRequest : PageRequest
    {
        public EmergencyEventStatus? Status { get; set; }
        public int? SceneId { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}

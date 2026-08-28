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
        public int Status { get; set; }
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
    }
}

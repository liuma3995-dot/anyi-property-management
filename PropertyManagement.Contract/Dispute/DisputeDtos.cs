using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Dispute
{
    /// <summary>纠纷类型（t_dispute_type）。</summary>
    public class DisputeTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Status { get; set; }
    }

    /// <summary>纠纷案件（t_dispute_case，UC-DIS-001/002/004/005）。</summary>
    public class DisputeCaseDto
    {
        public int Id { get; set; }
        public int TypeId { get; set; }
        public DateTime OccurTime { get; set; }
        public string Location { get; set; }
        public string Detail { get; set; }
        public DisputeCaseStatus Status { get; set; }
        public DisputeCloseType? CloseType { get; set; }
        public int? MediatorId { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    /// <summary>纠纷当事人（t_dispute_party，业主引用或外部登记）。</summary>
    public class DisputePartyDto
    {
        public int Id { get; set; }
        public int CaseId { get; set; }
        public string PartyType { get; set; }
        public int? OwnerId { get; set; }
        public string Name { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>处理记录（t_dispute_record，UC-DIS-003/007，补录留痕 BR-DIS-04）。</summary>
    public class DisputeRecordDto
    {
        public int Id { get; set; }
        public int CaseId { get; set; }
        public DateTime RecordTime { get; set; }
        public string Content { get; set; }
        public string Recorder { get; set; }
        public bool IsSupplement { get; set; }
    }

    /// <summary>纠纷案件详情（案件 + 当事人 + 处理记录）。</summary>
    public class DisputeCaseDetailDto
    {
        public DisputeCaseDto Case { get; set; }
        public List<DisputePartyDto> Parties { get; set; }
        public List<DisputeRecordDto> Records { get; set; }
    }

    /// <summary>纠纷状态变更记录（t_dispute_status_log，只追加）。</summary>
    public class DisputeStatusLogDto
    {
        public int Id { get; set; }
        public int CaseId { get; set; }
        public DisputeCaseStatus OldStatus { get; set; }
        public DisputeCaseStatus NewStatus { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>纠纷统计（UC-DIS-006）。</summary>
    public class DisputeStatisticsDto
    {
        public int Total { get; set; }
        public int Registered { get; set; }
        public int Handling { get; set; }
        public int Closed { get; set; }
        public Dictionary<string, int> ByType { get; set; }
    }

    public class DisputeTypeRequest
    {
        public string Name { get; set; }
    }

    /// <summary>纠纷登记请求（UC-DIS-001）。</summary>
    public class DisputeCaseCreateRequest
    {
        public int TypeId { get; set; }
        public DateTime OccurTime { get; set; }
        public string Location { get; set; }
        public string Detail { get; set; }
        public int? MediatorId { get; set; }
        public List<DisputePartyDto> Parties { get; set; }
    }

    /// <summary>纠纷信息维护请求（UC-DIS-002）。</summary>
    public class DisputeCaseUpdateRequest
    {
        public int? TypeId { get; set; }
        public string Location { get; set; }
        public string Detail { get; set; }
        public int? MediatorId { get; set; }
        public List<DisputePartyDto> Parties { get; set; }
    }

    /// <summary>处理记录请求（UC-DIS-003 记录方案 / UC-DIS-007 结案后补录）。</summary>
    public class DisputeRecordRequest
    {
        public int CaseId { get; set; }
        public DateTime RecordTime { get; set; }
        public string Content { get; set; }
        public string Recorder { get; set; }
        public bool IsSupplement { get; set; }
    }

    /// <summary>纠纷进度更新请求（UC-DIS-004：已登记→处理中）。</summary>
    public class DisputeCaseStatusRequest
    {
        public DisputeCaseStatus Status { get; set; }
        public string Note { get; set; }
    }

    /// <summary>结案请求（UC-DIS-005，P-08 结案类型）。</summary>
    public class DisputeCloseRequest
    {
        public int CaseId { get; set; }
        public DisputeCloseType CloseType { get; set; }
        public string Summary { get; set; }
    }

    /// <summary>纠纷查询条件（UC-DIS-006，分页）。</summary>
    public class DisputeQueryRequest : PageRequest
    {
        public DisputeCaseStatus? Status { get; set; }
        public int? TypeId { get; set; }
        public string OwnerName { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}

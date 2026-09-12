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
        public DateTime? ExpectedAt { get; set; }
        public string CaseNo { get; set; }
        public string TypeName { get; set; }
        public string MediatorName { get; set; }
        public string StatusText { get; set; }
        public string CloseTypeText { get; set; }
        public string OccurTimeText { get; set; }
        public string PartySummary { get; set; }
        public int RecordCount { get; set; }
        public int? PropertyId { get; set; }
        public string PropertyPath { get; set; }
        /// <summary>纠纷等级：0 一般 1 较大 2 重大（PG-DIS-02）。</summary>
        public int Level { get; set; }
        public string LevelText { get; set; }
        public string ExpectedAtText { get; set; }
        /// <summary>结案报告（必填留档，结案时写入并回显）。</summary>
        public string CloseSummary { get; set; }
        /// <summary>是否超期：调解中且发生时间超过 30 天（PG-DIS-01，由服务端计算）。</summary>
        public bool IsOverdue { get; set; }
        /// <summary>创建返回的软提示（如 90 天内同类纠纷建议升级，非阻断）。</summary>
        public string WarningText { get; set; }
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
        public string PartyTypeText { get; set; }
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
        /// <summary>调解方式（BR-DIS-03 六列）。</summary>
        public string Method { get; set; }
        /// <summary>方案摘要（BR-DIS-03 六列）。</summary>
        public string PlanSummary { get; set; }
        /// <summary>当事人意见（BR-DIS-03 六列）。</summary>
        public string PartyOpinion { get; set; }
        /// <summary>结果（BR-DIS-03 六列）。</summary>
        public string Result { get; set; }
        /// <summary>补录原因（BR-DIS-04，仅补录记录有值）。</summary>
        public string SupplementReason { get; set; }
    }

    /// <summary>纠纷案件详情（案件 + 当事人 + 处理记录 + 状态时间线）。</summary>
    public class DisputeCaseDetailDto
    {
        public DisputeCaseDto Case { get; set; }
        public List<DisputePartyDto> Parties { get; set; }
        public List<DisputeRecordDto> Records { get; set; }
        /// <summary>处理进度时间线（t_dispute_status_log，PG-DIS-03）。</summary>
        public List<DisputeStatusLogDto> StatusLogs { get; set; }
        /// <summary>调解协议扫描件（t_dispute_attachment，F1）。</summary>
        public List<DisputeAttachmentDto> Attachments { get; set; }
    }

    /// <summary>调解协议扫描件（t_dispute_attachment，F1：pdf/jpg/jpeg/png，单件 ≤20MB，单案 ≤10 份）。</summary>
    public class DisputeAttachmentDto
    {
        public int Id { get; set; }
        public int CaseId { get; set; }
        /// <summary>原始文件名（展示用）。</summary>
        public string FileName { get; set; }
        /// <summary>相对存储路径（服务端内部定位物理文件，前端不展示）。</summary>
        public string StoredPath { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
        /// <summary>展示用大小文本（如 1.2 MB）。</summary>
        public string SizeText { get; set; }
        public string UploadedBy { get; set; }
        public DateTime UploadedAt { get; set; }
        /// <summary>展示用上传时间（yyyy-MM-dd HH:mm）。</summary>
        public string UploadedAtText { get; set; }
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

    /// <summary>纠纷统计（UC-DIS-006，统计口径后端计算 T6-4-6）。</summary>
    public class DisputeStatisticsDto
    {
        public int Total { get; set; }
        public int Registered { get; set; }
        public int Handling { get; set; }
        public int Closed { get; set; }
        public Dictionary<string, int> ByType { get; set; }
        /// <summary>本月新增案件数（统计卡"本月新增"）。</summary>
        public int MonthNew { get; set; }
        /// <summary>本月新增环比（本月新增 - 上月新增，可为负）。</summary>
        public int MonthNewDelta { get; set; }
        /// <summary>本月结案数（统计卡"已结案·本月"标签）。</summary>
        public int MonthClosed { get; set; }
        /// <summary>超期案件数：调解中且发生时间超过 30 天（PG-DIS-01"含超期"子行）。</summary>
        public int OverdueCount { get; set; }
        /// <summary>调解成功率：近 12 个月结案中 close_type=调解成功 的占比（0~100）。</summary>
        public double SuccessRate { get; set; }
        /// <summary>成功率口径注明（前端展示标签："近12个月"）。</summary>
        public string SuccessRateNote { get; set; }
    }

    /// <summary>调解员推荐（BR-DIS-03：引用员工档案 + 历史结案成功率）。</summary>
    public class DisputeMediatorDto
    {
        public int EmployeeId { get; set; }
        public string Name { get; set; }
        public string PositionName { get; set; }
        public string DeptName { get; set; }
        /// <summary>历史结案中调解成功占比（0~100，无结案记录为 0）。</summary>
        public double SuccessRate { get; set; }
        /// <summary>历史结案数（指定类型时仅统计该类型）。</summary>
        public int CaseCount { get; set; }
    }

    public class DisputeTypeRequest
    {
        public string Name { get; set; }
    }

    /// <summary>纠纷登记请求（UC-DIS-001）。</summary>
    public class DisputeCaseCreateRequest
    {
        /// <summary>纠纷等级：0 一般 1 较大 2 重大。</summary>
        public int Level { get; set; }
        public int TypeId { get; set; }
        public DateTime OccurTime { get; set; }
        public string Location { get; set; }
        public string Detail { get; set; }
        public int? MediatorId { get; set; }
        public int? PropertyId { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public List<DisputePartyDto> Parties { get; set; }
    }

    /// <summary>纠纷信息维护请求（UC-DIS-002）。</summary>
    public class DisputeCaseUpdateRequest
    {
        /// <summary>纠纷等级：0 一般 1 较大 2 重大。</summary>
        public int? Level { get; set; }
        public int? TypeId { get; set; }
        public string Location { get; set; }
        public string Detail { get; set; }
        public int? MediatorId { get; set; }
        public int? PropertyId { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public List<DisputePartyDto> Parties { get; set; }
    }

    /// <summary>处理记录请求（UC-DIS-003 记录方案）。</summary>
    public class DisputeRecordRequest
    {
        public int CaseId { get; set; }
        public DateTime RecordTime { get; set; }
        public string Content { get; set; }
        /// <summary>调解方式（BR-DIS-03 六列）。</summary>
        public string Method { get; set; }
        /// <summary>方案摘要（BR-DIS-03 六列；与 Content 至少填一项）。</summary>
        public string PlanSummary { get; set; }
        /// <summary>当事人意见（BR-DIS-03 六列）。</summary>
        public string PartyOpinion { get; set; }
        /// <summary>结果（BR-DIS-03 六列）。</summary>
        public string Result { get; set; }
        /// <summary>补录标记：服务端忽略该字段（防越权旁路），补录一律走 POST /cases/{id}/supplement（BR-DIS-04）。</summary>
        public bool IsSupplement { get; set; }
        public string Recorder { get; set; }
    }

    /// <summary>结案后补录请求（UC-DIS-007，BR-DIS-04：仅管理员 + 原因必填）。</summary>
    public class DisputeSupplementRequest
    {
        public string Content { get; set; }
        public string Method { get; set; }
        public string PlanSummary { get; set; }
        public string PartyOpinion { get; set; }
        public string Result { get; set; }
        /// <summary>补录原因（必填留痕）。</summary>
        public string Reason { get; set; }
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

    /// <summary>结案报告导出请求（PG-DIS-03 新增：仅已结案案件，PDF / Excel）。</summary>
    public class DisputeCloseReportRequest
    {
        /// <summary>导出格式：Pdf（默认，打印签字用）/ Excel（归档用）。</summary>
        public ExportFormat Format { get; set; }
        /// <summary>导出人展示名（服务端以登录态为准，仅作报告头兜底）。</summary>
        public string OperatorName { get; set; }
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

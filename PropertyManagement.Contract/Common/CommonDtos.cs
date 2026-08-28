using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Common
{
    /// <summary>字典类型（t_dict_type，UC-COM-004）。</summary>
    public class DictTypeDto
    {
        public int Id { get; set; }
        public string TypeCode { get; set; }
        public string TypeName { get; set; }
    }

    /// <summary>字典项（t_dict_item，被引用禁删 BR-COM-05）。</summary>
    public class DictItemDto
    {
        public int Id { get; set; }
        public string TypeCode { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public int Sort { get; set; }
        public DictItemStatus Status { get; set; }
    }

    /// <summary>系统参数（t_param，P-01~P-09）。</summary>
    public class ParamDto
    {
        public int Id { get; set; }
        public string ParamKey { get; set; }
        public string ParamValue { get; set; }
    }

    /// <summary>审计日志（t_audit_log，UC-COM-003，只追加）。</summary>
    public class AuditLogDto
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string Action { get; set; }
        public string TargetType { get; set; }
        public string TargetId { get; set; }
        public string Detail { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>备份记录（t_backup，UC-COM-005，P-09 保留 30 份）。</summary>
    public class BackupDto
    {
        public int Id { get; set; }
        public string FilePath { get; set; }
        public long Size { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>提醒（t_reminder，仪表盘 UC-COM-006）。</summary>
    public class ReminderDto
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public int TargetId { get; set; }
        public DateTime DueAt { get; set; }
        public ReminderStatus Status { get; set; }
    }

    /// <summary>导出记录（t_export_log，留痕）。</summary>
    public class ExportLogDto
    {
        public int Id { get; set; }
        public string Module { get; set; }
        public ExportFormat Format { get; set; }
        public string FilePath { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>打印记录（t_print_log，收据/报表补打留痕）。</summary>
    public class PrintLogDto
    {
        public int Id { get; set; }
        public string BizType { get; set; }
        public int BizId { get; set; }
        public int PrintCount { get; set; }
        public DateTime PrintedAt { get; set; }
    }

    /// <summary>仪表盘首页（UC-COM-006：提醒/欠费/应急/纠纷/设备统计）。</summary>
    public class DashboardDto
    {
        public int PendingReminders { get; set; }
        public decimal ArrearAmount { get; set; }
        public int ArrearCount { get; set; }
        public int HandlingEmergency { get; set; }
        public int PendingReview { get; set; }
        public int HandlingDisputes { get; set; }
        public int RepairingDevices { get; set; }
        public List<ReminderDto> RecentReminders { get; set; }
    }

    public class DictTypeRequest
    {
        public string TypeCode { get; set; }
        public string TypeName { get; set; }
    }

    public class DictItemRequest
    {
        public string TypeCode { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public int Sort { get; set; }
        public DictItemStatus Status { get; set; }
    }

    public class ParamUpdateRequest
    {
        public string ParamKey { get; set; }
        public string ParamValue { get; set; }
    }

    /// <summary>审计日志查询（UC-COM-003，分页）。</summary>
    public class AuditLogQueryRequest : PageRequest
    {
        public string Action { get; set; }
        public string TargetType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    /// <summary>手动备份请求（UC-COM-005）。</summary>
    public class BackupCreateRequest
    {
        public string Note { get; set; }
    }

    /// <summary>备份恢复请求（恢复前自动备份当前库）。</summary>
    public class BackupRestoreRequest
    {
        public int BackupId { get; set; }
    }
}

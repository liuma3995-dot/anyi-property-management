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

    /// <summary>字典项（t_dict_item，被引用禁删 BR-COM-05）。
    /// PG-COM-01 增强：显示值/修改人/修改时间（migration_019 display_value/updated_by/updated_at）。</summary>
    public class DictItemDto
    {
        public int Id { get; set; }
        public string TypeCode { get; set; }
        public string ItemCode { get; set; }
        public string ItemName { get; set; }
        public string DisplayValue { get; set; } // 显示值（默认与名称一致，业务展示用）
        public string Remark { get; set; }    // T4F-1-5：附加信息（自定义计价方式单位文本，如 张）
        public int Sort { get; set; }
        public DictItemStatus Status { get; set; }
        public string ModifiedBy { get; set; }   // 修改人（updated_by 冗余名）
        public DateTime? ModifiedAt { get; set; } // 修改时间（updated_at）
    }

    /// <summary>系统参数（t_param，P-01~P-09）。</summary>
    public class ParamDto
    {
        public int Id { get; set; }
        public string ParamKey { get; set; }
        public string ParamValue { get; set; }
    }

    /// <summary>审计日志（t_audit_log，UC-COM-003，只追加）。
    /// PG-COM-03 八列增强：操作人/角色/模块/结果/IP（migration_019 user_name/role/module/result/ip_addr）。</summary>
    public class AuditLogDto
    {
        public int Id { get; set; }
        public int? UserId { get; set; }
        public string UserName { get; set; }  // 操作人（冗余列优先，JOIN t_user 兜底）
        public string Role { get; set; }      // 角色（BR-ORG-01 唯一系统管理员）
        public string Module { get; set; }    // 模块（冗余列优先，target_type 兜底）
        public string Action { get; set; }
        public string TargetType { get; set; }
        public string TargetId { get; set; }
        public string Detail { get; set; }
        public string Result { get; set; }    // 结果：成功/失败
        public string IpAddr { get; set; }    // IP 地址
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>备份/恢复记录（t_backup，UC-COM-005，P-09 保留 30 份）。
    /// CHG-COM-02 增强：kind/backup_type/period/operator/reviewer/result/source_point/target。</summary>
    public class BackupDto
    {
        public int Id { get; set; }
        public string FilePath { get; set; }
        public long Size { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Kind { get; set; }        // backup=备份点 / restore=恢复演练记录
        public string BackupType { get; set; }  // auto=每日自动 / manual=手动
        public string Period { get; set; }      // 计划周期（daily 预留）
        public string Operator { get; set; }    // 操作人
        public string Reviewer { get; set; }    // 复核人（恢复双管理员第二人）
        public string Result { get; set; }      // 结果：成功/失败/已清理
        public string SourcePoint { get; set; } // 目标备份点（恢复时=备份文件路径）
        public string Target { get; set; }      // 目标位置
    }

    /// <summary>备份状态卡（PG-COM-02：上次自动备份/数据库大小/备份保留/待恢复演练）。</summary>
    public class BackupStatusDto
    {
        public DateTime? LastAutoBackupAt { get; set; }  // 上次自动备份时间（从未执行过时回退最近备份）
        public string LastAutoBackupResult { get; set; } // 上次自动备份结果
        public long DatabaseSize { get; set; }           // 数据库文件大小（字节）
        public string DatabaseSizeText { get; set; }     // 格式化文本（如 2.3 GB）
        public long AttachmentsSize { get; set; }        // 附件目录占用（字节，data/attachments 递归合计）
        public string AttachmentsSizeText { get; set; }  // 格式化文本（如 6.8 GB）
        public int RetainCount { get; set; }             // 保留份数（P-09 backup.retain.count）
        public bool AutoDailyEnabled { get; set; }       // 每日自动备份开关（backup.auto.daily）
        public int PendingRestoreDrill { get; set; }     // 待恢复演练数（本月无 kind=restore 记录则 1）
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

    /// <summary>仪表盘首页（UC-COM-006：提醒/欠费/应急/纠纷/设备统计）。
    /// CHG-001（2026-08-31 负责人批准）：增量追加本月应收/已收/收缴率/趋势/值班/保养到期字段。</summary>
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

        // ---- CHG-001 新增（原型 1:1 所需）----
        public decimal MonthReceivable { get; set; }
        public string ReceivableTrend { get; set; }
        public decimal MonthReceived { get; set; }
        public decimal CollectionRate { get; set; }
        public string ReceivedTrend { get; set; }
        public string OverdueTrend { get; set; }
        public int MaintenanceDue { get; set; }
        public int DutyToday { get; set; }
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
        public string DisplayValue { get; set; } // PG-COM-01：显示值（编辑传空则保留原值）
        public string Remark { get; set; }    // T4F-1-5：附加信息（自定义计价方式单位文本，如 张）
        public int Sort { get; set; }
        public DictItemStatus Status { get; set; } // 仅 /dict-items/{id}/status 端点生效；编辑保留原状态
    }

    /// <summary>字典项批量删除请求（PG-COM-01 / R12）：仅允许删除“已停用”字典项。</summary>
    public class DictItemBatchDeleteRequest
    {
        public List<int> Ids { get; set; }
    }

    /// <summary>不可删除明细（未停用的字典项）。</summary>
    public class DictItemDeleteBlockedDto
    {
        public int Id { get; set; }
        public string ItemName { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>字典项批量删除结果：blocked 非空表示整批拒绝（与到期提醒批量删除同口径）。</summary>
    public class DictItemBatchDeleteResultDto
    {
        public int Deleted { get; set; }
        public List<DictItemDeleteBlockedDto> Blocked { get; set; } = new List<DictItemDeleteBlockedDto>();
    }

    /// <summary>记录批量删除请求（审计日志 / 备份恢复记录，R13）：删除为软删留痕。</summary>
    public class RecordBatchDeleteRequest
    {
        public List<int> Ids { get; set; }
    }

    /// <summary>记录批量删除结果。</summary>
    public class RecordBatchDeleteResultDto
    {
        public int Deleted { get; set; }
    }

    /// <summary>一键清理残余数据结果（R13）：逐表统计被物理清理的软删留痕行数。</summary>
    public class PurgeSoftDeletedResultDto
    {
        public int TotalPurged { get; set; }
        public List<PurgeTableCountDto> Items { get; set; } = new List<PurgeTableCountDto>();
    }

    public class PurgeTableCountDto
    {
        public string TableName { get; set; }
        public int Count { get; set; }
    }

    public class ParamUpdateRequest
    {
        public string ParamKey { get; set; }
        public string ParamValue { get; set; }
    }

    /// <summary>审计日志查询（UC-COM-003，组合筛选+分页：时间范围/操作人/模块/动作/结果/关键字）。</summary>
    public class AuditLogQueryRequest : PageRequest
    {
        public string Operator { get; set; }  // 操作人（user_name/username 模糊）
        public string Module { get; set; }    // 模块
        public string Result { get; set; }    // 结果：成功/失败（精确）
        public string Action { get; set; }
        public string TargetType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }     // 边界含当日（服务端按 < date(@to,'+1 day') 处理）
    }

    /// <summary>审计日志导出结果（PG-COM-03：导出动作本身写 t_export_log + AUDIT_EXPORT 审计）。</summary>
    public class AuditExportDto
    {
        public string FilePath { get; set; }  // 服务端导出文件路径（exports 目录）
        public string Content { get; set; }   // CSV 文本（UTF-8 BOM）
        public int Total { get; set; }        // 导出行数
    }

    /// <summary>手动备份请求（UC-COM-005）。</summary>
    public class BackupCreateRequest
    {
        public string Note { get; set; }

        /// <summary>备份文件落盘路径（含文件名）；空=落默认备份目录（系统设置/备份与恢复 R12 支持自选保存位置）。</summary>
        public string TargetPath { get; set; }
    }

    /// <summary>数据恢复请求（BR-COM-04 高危；T6-6-3）。
    /// R12：恢复源改为「手动选择备份文件路径」（SourcePath），并移除第二管理员确认（本系统无第二管理员业务）；
    /// 流程：校验操作密码与确认文本 → 恢复前自动快照 → 覆盖恢复 → 写恢复记录与审计。</summary>
    public class BackupRestoreRequest
    {
        /// <summary>备份点 Id（按记录恢复时使用；与 SourcePath 二选一）。</summary>
        public int BackupId { get; set; }

        /// <summary>备份文件路径（手动选择恢复数据文件；优先于 BackupId）。</summary>
        public string SourcePath { get; set; }

        public string OperationPassword { get; set; }   // 操作密码（=当前登录用户密码，BCrypt 校验）
        public string ConfirmText { get; set; }         // 二次确认文本，必须输入：覆盖当前数据不可撤销
    }
}


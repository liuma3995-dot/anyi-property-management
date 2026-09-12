using System;
using System.Collections.Generic;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;

namespace PropertyManagement.Contract.Equipment
{
    /// <summary>设备类型（t_device_type，P-04 类型默认保养周期）。</summary>
    public class DeviceTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int MaintenanceCycle { get; set; }
        public int Status { get; set; }
    }

    /// <summary>
    /// 保养 / 年检登记类型（t_maintain_type）。R6：类型固定为系统内置（保养/年检），不再支持自定义增删，
    /// 历史自定义类型软删保留（IsSystem=false 仅供历史展示与迁移追溯）。
    /// </summary>
    public class MaintainTypeDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        /// <summary>0 保养（走保养端点）/ 1 年检（走年检端点），决定保存路由。</summary>
        public int Kind { get; set; }
        /// <summary>是否系统内置固定类型（true=不可删改；保养/年检）。</summary>
        public bool IsSystem { get; set; }
    }

    /// <summary>设备（t_device，UC-EQP-001/007，BR-EQP-01）。</summary>
    public class DeviceDto
    {
        public int Id { get; set; }
        public int TypeId { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public DeviceStatus Status { get; set; }
        public DateTime? EnableDate { get; set; }
        public bool DelFlag { get; set; }
        public string DeviceNo { get; set; }
        public string TypeName { get; set; }
        public string StatusText { get; set; }
        public string BrandModel { get; set; }
        public string NextMaintenanceText { get; set; }
        public DateTime? NextMaintenance { get; set; }
        /// <summary>用户自定义的下次保养日期（t_device.next_maintenance_at，R6；空=按周期派生）。</summary>
        public DateTime? NextMaintenanceOverride { get; set; }
        /// <summary>下次保养来源："自定义" / "周期派生"（R6，界面标注用）。</summary>
        public string NextMaintenanceSource { get; set; }
        /// <summary>质保到期（yyyy-MM-dd，PG-EQP-04）。</summary>
        public string WarrantyEnd { get; set; }
        /// <summary>维保合同到期（yyyy-MM-dd）。</summary>
        public string ContractEnd { get; set; }
        /// <summary>单台保养周期覆盖（天，P-04；空=按类型默认）。</summary>
        public int? MaintenanceCycleOverride { get; set; }
        /// <summary>解析后的保养周期（天）：单台覆盖 &gt; 类型默认 &gt; 30（P-04，前端登记类型选项用）。</summary>
        public int MaintenanceCycle { get; set; }
        /// <summary>未结故障单状态：-1 无未结故障单 / 0 待处理（派生展示态"故障"）/ 1 维修中（PG-EQP-01 状态标签）。</summary>
        public int OpenFaultState { get; set; }
        /// <summary>下次年检（最近一次年检 + 1 年，无记录回退投运日期，BR-EQP-02）。</summary>
        public DateTime? NextInspection { get; set; }
        /// <summary>下次年检文本（MM-dd）。</summary>
        public string NextInspectionText { get; set; }
    }

    /// <summary>设备状态变更记录（t_device_status_log，只追加，BR-EQP-01）。</summary>
    public class DeviceStatusLogDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public DeviceStatus OldStatus { get; set; }
        public DeviceStatus NewStatus { get; set; }
        public string Reason { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>保养记录（t_maintenance_record，UC-EQP-002，BR-EQP-02/03）。</summary>
    public class MaintenanceRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime MDate { get; set; }
        /// <summary>登记类型名称快照（默认按设备保养周期派生，可为自定义类型）。</summary>
        public string RecordType { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        /// <summary>费用（元，BR-EQP-06 与支出弱关联）。</summary>
        public decimal? Cost { get; set; }
        /// <summary>不合格说明（不合格时必填）。</summary>
        public string FailReason { get; set; }
        /// <summary>执行单位名称（t_vendor JOIN）。</summary>
        public string VendorName { get; set; }
        /// <summary>不合格转维修时生成的维修工单号（WX-yyMM-序号）。</summary>
        public string FaultNo { get; set; }
    }

    /// <summary>年检记录（t_inspection_record，UC-EQP-003）。</summary>
    public class InspectionRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime IDate { get; set; }
        /// <summary>登记类型名称快照（年检登记 / 自定义年检类型）。</summary>
        public string RecordType { get; set; }
        public string Result { get; set; }
        /// <summary>费用（元）。</summary>
        public decimal? Cost { get; set; }
        /// <summary>不合格说明（不合格时必填）。</summary>
        public string FailReason { get; set; }
        /// <summary>执行单位名称（t_vendor JOIN）。</summary>
        public string VendorName { get; set; }
        /// <summary>不合格转维修时生成的维修工单号（WX-yyMM-序号）。</summary>
        public string FaultNo { get; set; }
    }

    /// <summary>故障记录（t_fault_record，UC-EQP-004，event_id 弱关联应急）。</summary>
    public class FaultRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int? EventId { get; set; }
        public DateTime FTime { get; set; }
        public string Symptom { get; set; }
        public string Cause { get; set; }
        public string Handle { get; set; }
        /// <summary>故障单号（WX-yyMM-序号，PG-EQP-03）。</summary>
        public string FaultNo { get; set; }
        /// <summary>故障级别：0 一般 1 重大（重大且影响应急能力强提示转应急）。</summary>
        public int Level { get; set; }
        public string LevelText { get; set; }
        /// <summary>发现人。</summary>
        public string Reporter { get; set; }
        public string DeviceName { get; set; }
        /// <summary>关联应急事件编号（EM-yyMM-序号，弱关联可空；PG-EQP-03 右表"关联应急"列）。</summary>
        public string EventNo { get; set; }
        /// <summary>故障状态：0 待处理 1 维修中 2 已修复。</summary>
        public int Status { get; set; }
        public string StatusText { get; set; }
    }

    /// <summary>故障维修闭环处理请求（BR-EQP-04：维修完成后补录原因/处理结果并更新状态）。</summary>
    public class FaultHandleRequest
    {
        /// <summary>故障原因（可留空保持原值）。</summary>
        public string Cause { get; set; }
        /// <summary>处理结果（状态=已修复时必填）。</summary>
        public string Handle { get; set; }
        /// <summary>目标状态：0 待处理 1 维修中 2 已修复；缺省时按 Handle 是否填写推断。</summary>
        public int Status { get; set; }
    }

    /// <summary>维保单位（t_vendor，UC-EQP-005）。</summary>
    public class VendorDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>到期提醒（t_reminder，UC-EQP-006/UC-COM-006，保养/年检/质保/合同）。</summary>
    public class EquipmentReminderDto
    {
        public int ReminderId { get; set; }
        /// <summary>t_reminder.type：maint_due / inspect_due / warranty_due / contract_due。</summary>
        public string Type { get; set; }
        /// <summary>事项归类（范围页签用）：maintenance / inspection / warranty / contract。</summary>
        public string Kind { get; set; }
        /// <summary>事项中文（保养到期/年检到期/质保到期/合同到期）。</summary>
        public string ItemText { get; set; }
        public int DeviceId { get; set; }
        public string DeviceName { get; set; }
        /// <summary>设备编号（EQP-xxxx）。</summary>
        public string DeviceNo { get; set; }
        /// <summary>设备类型名称。</summary>
        public string TypeName { get; set; }
        public DateTime DueAt { get; set; }
        /// <summary>剩余天数（负=已逾期 N 天）。</summary>
        public int RemainingDays { get; set; }
        public ReminderStatus Status { get; set; }
        /// <summary>状态中文：待处理 / 逾期 / 已处理。</summary>
        public string StatusText { get; set; }
        /// <summary>已处理时间（yyyy-MM-dd HH:mm:ss 展示文本；R6 保留已处理记录用）。</summary>
        public string HandledAt { get; set; }
        /// <summary>责任班组：逾期项升级物业办，其余工程部（T6-5-7 逾期升级推送负责人）。</summary>
        public string ResponsibleTeam { get; set; }
        /// <summary>责任班组人工指派值（R7；空=按默认规则派生，界面"恢复默认"即清空该值）。</summary>
        public string ResponsibleTeamOverride { get; set; }
    }

    /// <summary>责任班组指派请求（R7；Team 传空字符串表示恢复默认规则）。</summary>
    public class ReminderTeamRequest
    {
        public string Team { get; set; }
    }

    /// <summary>到期提醒批量删除请求（R6）：整批校验，命中待处理记录则整批拒绝。</summary>
    public class ReminderBatchDeleteRequest
    {
        public List<int> Ids { get; set; }
    }

    /// <summary>到期提醒批量删除结果（R6）：Blocked 非空时 Deleted=0（整批拒绝）。</summary>
    public class ReminderBatchDeleteResultDto
    {
        public int Deleted { get; set; }
        public List<ReminderDeleteBlockedDto> Blocked { get; set; }
    }

    /// <summary>禁止删除明细（提醒仍为待处理/逾期）。</summary>
    public class ReminderDeleteBlockedDto
    {
        public int Id { get; set; }
        /// <summary>设备展示（EQP-xxxx 设备名）。</summary>
        public string Device { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>设备自定义类型记录（t_device_custom_record，R6 登记类型解耦）。</summary>
    public class DeviceCustomRecordDto
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        /// <summary>自定义类型名称（管理员输入，如"清洁保养"）。</summary>
        public string TypeName { get; set; }
        public DateTime RDate { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        public decimal? Cost { get; set; }
        public int? VendorId { get; set; }
        /// <summary>执行方名称（t_vendor 回读）。</summary>
        public string VendorName { get; set; }
        /// <summary>记录人。</summary>
        public string Operator { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>自定义类型记录新增/编辑请求。</summary>
    public class DeviceCustomRecordRequest
    {
        public int DeviceId { get; set; }
        public string TypeName { get; set; }
        public DateTime RDate { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        public decimal? Cost { get; set; }
        public int? VendorId { get; set; }
        public string Operator { get; set; }
    }

    /// <summary>到期提醒统计（PG-EQP-04 统计卡，后端计算）。</summary>
    public class ReminderSummaryDto
    {
        /// <summary>30 天内到期（不含逾期）。</summary>
        public int Within30 { get; set; }
        /// <summary>7 天内到期（不含逾期）。</summary>
        public int Within7 { get; set; }
        /// <summary>本月已处理（本月处置计数）。</summary>
        public int MonthHandled { get; set; }
        /// <summary>逾期未处理。</summary>
        public int OverdueUnhandled { get; set; }
    }

    /// <summary>设备台账统计卡（PG-EQP-01，后端计算；"故障"=存在未结故障单的派生展示态）。</summary>
    public class DeviceSummaryDto
    {
        public int Total { get; set; }
        public int InUse { get; set; }
        /// <summary>维修中（含存在未结故障单的设备数标注）。</summary>
        public int Repairing { get; set; }
        /// <summary>存在未结故障单（status 0/1）的设备台数。</summary>
        public int FaultCount { get; set; }
        public int DisabledOrScrapped { get; set; }
        /// <summary>设备大类数（去重 type_id）。</summary>
        public int TypeCount { get; set; }
    }

    /// <summary>设备台账导出结果（CSV 文本 + 导出留痕）。</summary>
    public class EquipmentExportResultDto
    {
        public int Id { get; set; }
        public string Module { get; set; }
        public string FileName { get; set; }
        /// <summary>导出行数。</summary>
        public int Total { get; set; }
        /// <summary>导出文件格式（设备台账导出为 Excel，前端按 Id 调通用下载端点另存）。</summary>
        public string Format { get; set; }
        public DateTime ExportedAt { get; set; }
    }

    public class DeviceTypeRequest
    {
        public string Name { get; set; }
        /// <summary>P-04 类型默认保养周期（天）。</summary>
        public int MaintenanceCycle { get; set; }
        /// <summary>0 启用 1 停用（仅更新分支生效，缺省保持原值）。</summary>
        public int? Status { get; set; }
    }

    /// <summary>设备登记请求（UC-EQP-001）。</summary>
    public class DeviceRequest
    {
        public int TypeId { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public DateTime? EnableDate { get; set; }
        public string BrandModel { get; set; }
        /// <summary>质保到期（PG-EQP-04）。</summary>
        public DateTime? WarrantyEnd { get; set; }
        /// <summary>维保合同到期（PG-EQP-04）。</summary>
        public DateTime? ContractEnd { get; set; }
        /// <summary>单台保养周期覆盖（天，P-04；空=按类型默认）。</summary>
        public int? MaintenanceCycleOverride { get; set; }
        /// <summary>用户自定义下次保养日期（R6；空=保持原值）。</summary>
        public DateTime? NextMaintenanceOverride { get; set; }
        /// <summary>是否清空自定义下次保养日期（R6；true=恢复按周期派生，优先级高于 NextMaintenanceOverride）。</summary>
        public bool ClearNextMaintenance { get; set; }
    }

    /// <summary>设备状态变更请求（BR-EQP-01：维修/停用/报废/启用）。</summary>
    public class DeviceStatusRequest
    {
        public DeviceStatus Status { get; set; }
        public string Reason { get; set; }
    }

    public class MaintenanceRecordRequest
    {
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime MDate { get; set; }
        /// <summary>登记类型名称（PG-EQP-02 下拉所选，缺省按设备周期派生）。</summary>
        public string RecordType { get; set; }
        public string Content { get; set; }
        public string Result { get; set; }
        /// <summary>费用（元）。</summary>
        public decimal? Cost { get; set; }
        /// <summary>不合格说明（检测结果不合格时必填，BR-EQP-03）。</summary>
        public string FailReason { get; set; }
    }

    public class InspectionRecordRequest
    {
        public int DeviceId { get; set; }
        public int? VendorId { get; set; }
        public DateTime IDate { get; set; }
        /// <summary>登记类型名称（缺省"年检登记"）。</summary>
        public string RecordType { get; set; }
        public string Result { get; set; }
        /// <summary>费用（元）。</summary>
        public decimal? Cost { get; set; }
        /// <summary>不合格说明（检测结果不合格时必填，BR-EQP-03）。</summary>
        public string FailReason { get; set; }
    }

    public class FaultRecordRequest
    {
        public int DeviceId { get; set; }
        public int? EventId { get; set; }
        public DateTime FTime { get; set; }
        public string Symptom { get; set; }
        public string Cause { get; set; }
        public string Handle { get; set; }
        /// <summary>故障级别：0 一般 1 重大。</summary>
        public int Level { get; set; }
        public string Reporter { get; set; }
    }

    public class VendorRequest
    {
        public string Name { get; set; }
        public string Contact { get; set; }
        public string Phone { get; set; }
    }

    /// <summary>设备查询条件（UC-EQP-007，分页）。</summary>
    public class DeviceQueryRequest : PageRequest
    {
        public int? TypeId { get; set; }
        public DeviceStatus? Status { get; set; }
        /// <summary>位置模糊筛选。</summary>
        public string Location { get; set; }
        /// <summary>多状态筛选（如"停用+报废"传 [2,3]），与 Status 二选一。</summary>
        public List<int> StatusIn { get; set; }
        /// <summary>待处理故障单筛选（PG-EQP-01 状态筛选项"故障"＝派生展示态：存在 status 0 待接单故障单）。</summary>
        public bool? HasPendingFault { get; set; }
    }
}

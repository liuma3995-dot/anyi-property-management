using System;
using System.Collections.Generic;
using System.Data;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Equipment;

namespace PropertyManagement.Server.Domain.Repositories
{
    /// <summary>设备资产台账仓储（M6 D6-5，UC-EQP-001~007 + BR-EQP-01~06）。</summary>
    public interface IEquipmentRepository
    {
        List<DeviceTypeDto> ListDeviceTypes(IDbConnection connection);
        DeviceTypeDto GetDeviceType(IDbConnection connection, int id);
        int InsertDeviceType(IDbConnection connection, IDbTransaction transaction, DeviceTypeDto dto);
        void UpdateDeviceType(IDbConnection connection, IDbTransaction transaction, DeviceTypeDto dto);
        void SoftDeleteDeviceType(IDbConnection connection, IDbTransaction transaction, int id);
        /// <summary>在册设备数（设备类型删除保护：被引用则拒绝删除）。</summary>
        int CountDevicesByType(IDbConnection connection, int typeId);

        // ---------- 保养/年检登记类型（t_maintain_type，R6 起固定为系统内置，不再支持增删） ----------
        /// <summary>系统内置固定登记类型（保养/年检）；includeDisabled=true 时附带历史停用类型。</summary>
        List<MaintainTypeDto> ListMaintainTypes(IDbConnection connection, bool includeDisabled);
        MaintainTypeDto GetMaintainType(IDbConnection connection, int id);

        DeviceDto GetDevice(IDbConnection connection, int id);
        PageResult<DeviceDto> QueryDevices(IDbConnection connection, DeviceQueryRequest query, out int total);
        List<DeviceDto> ListDevices(IDbConnection connection, int? typeId);
        int InsertDevice(IDbConnection connection, IDbTransaction transaction, DeviceDto dto);
        void UpdateDevice(IDbConnection connection, IDbTransaction transaction, DeviceDto dto);
        /// <summary>写入/清空自定义下次保养日期（R6；nextMaintenanceAt=null 表示恢复按周期派生）。</summary>
        void UpdateDeviceNextMaintenance(IDbConnection connection, IDbTransaction transaction, int id, string nextMaintenanceAt);
        void SoftDeleteDevice(IDbConnection connection, IDbTransaction transaction, int id);

        /// <summary>
        /// 设备删除引用校验（v1.1.0 R1）：保养/年检/故障/自定义记录/状态变更日志/支出引用的总数。
        /// &gt; 0 表示设备仍有在用历史（或财务引用），不可删除。
        /// </summary>
        int CountDeviceReferences(IDbConnection connection, int deviceId);

        /// <summary>
        /// 设备删除级联（v1.1.0 R1）：软删该设备的到期提醒及其催办/处置流水（派生数据，可重算），
        /// 避免留痕父行被物理清理后残留孤儿提醒。返回软删行数。
        /// </summary>
        int SoftDeleteDeviceReminders(IDbConnection connection, IDbTransaction transaction, int deviceId);

        /// <summary>
        /// 设备删除级联（v1.1.0 R1 / migration_036）：软删该设备的状态变更日志（含登记行）。
        /// 状态日志是设备过程留痕，随设备删除一并软删并由「一键清理残余数据」回收。返回软删行数。
        /// </summary>
        int SoftDeleteDeviceStatusLogs(IDbConnection connection, IDbTransaction transaction, int deviceId);
        void InsertDeviceStatusLog(IDbConnection connection, IDbTransaction transaction, int deviceId, int oldStatus, int newStatus, string reason);
        List<DeviceStatusLogDto> ListDeviceStatusLogs(IDbConnection connection, int deviceId);
        DeviceSummaryDto GetDeviceSummary(IDbConnection connection);

        List<MaintenanceRecordDto> ListMaintenance(IDbConnection connection, int deviceId);
        int InsertMaintenance(IDbConnection connection, IDbTransaction transaction, MaintenanceRecordDto dto);
        List<InspectionRecordDto> ListInspection(IDbConnection connection, int deviceId);
        int InsertInspection(IDbConnection connection, IDbTransaction transaction, InspectionRecordDto dto);

        List<FaultRecordDto> ListFaults(IDbConnection connection, int deviceId);
        List<FaultRecordDto> ListRecentFaults(IDbConnection connection, int limit);
        FaultRecordDto GetFault(IDbConnection connection, int id);
        int InsertFault(IDbConnection connection, IDbTransaction transaction, FaultRecordDto dto);
        void SetFaultNo(IDbConnection connection, IDbTransaction transaction, int id, string faultNo);
        void UpdateFaultHandle(IDbConnection connection, IDbTransaction transaction, int id, string cause, string handle, int status);

        List<VendorDto> ListVendors(IDbConnection connection);
        VendorDto GetVendor(IDbConnection connection, int id);
        int InsertVendor(IDbConnection connection, IDbTransaction transaction, VendorDto dto);
        void UpdateVendor(IDbConnection connection, IDbTransaction transaction, VendorDto dto);
        void SoftDeleteVendor(IDbConnection connection, IDbTransaction transaction, int id);
        /// <summary>
        /// 维保单位被引用条数（BR-EQP-06，M7 BUG-001 修复）：保养记录 + 年检记录 + 自定义类型记录 + 支出关联
        /// （t_expense_object_rel.object_type=2，且支出未软删）。已软删的自定义记录不计数。
        /// </summary>
        int CountVendorReferences(IDbConnection connection, int vendorId);

        // ---------- 到期提醒（PG-EQP-04，t_reminder 物化 + 催办子记录） ----------
        List<ReminderCandidateRow> ListMaintenanceDueCandidates(IDbConnection connection);
        /// <summary>单个设备的保养到期候选行（R8 设备恢复在用后重新物化提醒用）。</summary>
        ReminderCandidateRow GetMaintenanceCandidate(IDbConnection connection, int deviceId);
        List<ReminderCandidateRow> ListInspectionDueCandidates(IDbConnection connection);
        List<ReminderCandidateRow> ListWarrantyDueCandidates(IDbConnection connection);
        List<ReminderCandidateRow> ListContractDueCandidates(IDbConnection connection);
        /// <summary>取同 type+target_id 的待处理提醒（不存在返回 null）。</summary>
        ReminderRow FindActiveReminder(IDbConnection connection, IDbTransaction transaction, string type, int targetId);
        /// <summary>
        /// 取同 type+target_id 最近一条已处理提醒的到期日（yyyy-MM-dd 文本，无则返回 null）。
        /// 用于"本期已处置"判定：处置后不再重复物化同一周期的提醒，直到保养/年检登记使到期日顺延。
        /// </summary>
        string FindLastHandledReminderDueAt(IDbConnection connection, string type, int targetId);
        void InsertReminder(IDbConnection connection, IDbTransaction transaction, string type, int targetId, string dueAt);
        void UpdateReminderDueAt(IDbConnection connection, IDbTransaction transaction, int id, string dueAt);
        /// <summary>提醒列表（R6）：status=-1 全部（含已处理）/0 待处理/1 已处理；已软删记录不返回。</summary>
        List<EquipmentReminderDto> ListReminders(IDbConnection connection, string type, int status);
        EquipmentReminderDto GetReminder(IDbConnection connection, int id);
        /// <summary>处置完成：status=1 + 处置时间（R6）。</summary>
        void HandleReminder(IDbConnection connection, IDbTransaction transaction, int id, string handledAt);
        /// <summary>批量删除用：按 id 取提醒行（含状态与设备展示，R6）。</summary>
        List<ReminderRow> GetRemindersByIds(IDbConnection connection, IEnumerable<int> ids);
        /// <summary>批量软删提醒（R6；仅允许已处理记录，规则由服务层拦截）。</summary>
        void SoftDeleteReminders(IDbConnection connection, IDbTransaction transaction, IEnumerable<int> ids);
        /// <summary>指派责任班组（R7；team 为空 → 清空指派，恢复默认规则）。</summary>
        void UpdateReminderTeam(IDbConnection connection, IDbTransaction transaction, int id, string team);
        void InsertReminderChild(IDbConnection connection, IDbTransaction transaction, string type, int targetId, string dueAt);
        int CountMonthHandled(IDbConnection connection);

        // ---------- 自定义类型记录（t_device_custom_record，R6 登记类型解耦） ----------
        List<DeviceCustomRecordDto> ListCustomRecords(IDbConnection connection, int deviceId);
        DeviceCustomRecordDto GetCustomRecord(IDbConnection connection, int id);
        int InsertCustomRecord(IDbConnection connection, IDbTransaction transaction, DeviceCustomRecordDto dto);
        void UpdateCustomRecord(IDbConnection connection, IDbTransaction transaction, DeviceCustomRecordDto dto);
        void SoftDeleteCustomRecord(IDbConnection connection, IDbTransaction transaction, int id);
        /// <summary>自定义类型名称候选（历史记录去重，R6 新增记录下拉用）。</summary>
        List<string> ListCustomRecordTypeNames(IDbConnection connection);

        /// <summary>导出留痕（t_export_log，module='equipment'）。</summary>
        int InsertExportLog(IDbConnection connection, IDbTransaction transaction, string module, int format, string filePath);
    }

    /// <summary>提醒物化候选行（动态推导的下次到期，未落库）。</summary>
    public class ReminderCandidateRow
    {
        public int DeviceId { get; set; }
        public string DeviceName { get; set; }
        /// <summary>保养周期（override>type>30，仅保养候选用）。</summary>
        public int Cycle { get; set; }
        /// <summary>投运日期（yyyy-MM-dd 文本；SQLite 文本列映射到可空 DateTime 会抛 InvalidCastException，统一文本承载）。</summary>
        public string EnableDate { get; set; }
        /// <summary>最近一次保养 / 年检日期（yyyy-MM-dd 文本，同上）。</summary>
        public string LastDate { get; set; }
        /// <summary>质保/合同到期原值（仅这两类候选用）。</summary>
        public string DueDate { get; set; }
        /// <summary>自定义下次保养日期（t_device.next_maintenance_at，R6；仅保养候选，空=按周期派生）。</summary>
        public string NextMaintenanceOverride { get; set; }
    }

    /// <summary>t_reminder 原始行。</summary>
    public class ReminderRow
    {
        public int Id { get; set; }
        public string DueAt { get; set; }
        /// <summary>0 待处理 / 1 已处理（R6 批量删除规则判定用）。</summary>
        public int Status { get; set; }
        /// <summary>关联设备展示（EQP-xxxx + 名称，R6 拒绝删除明细用）。</summary>
        public string DeviceName { get; set; }
    }

}

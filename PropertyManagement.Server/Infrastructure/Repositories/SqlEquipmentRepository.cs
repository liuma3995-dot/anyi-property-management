using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;

namespace PropertyManagement.Server.Infrastructure.Repositories
{
    /// <summary>设备资产台账仓储 SQLite 实现（M6 D6-5，Dapper）。</summary>
    public class SqlEquipmentRepository : IEquipmentRepository
    {
        private const string ReminderTypes = "'maint_due','inspect_due','warranty_due','contract_due'";
        private const string UrgeChildType = "urge";     // 催办流水（target_id=提醒 id）
        private const string HandledChildType = "handled"; // 处置流水（本月已处理计数用）

        // ===================== 设备类型（t_device_type，P-04/BR-EQP-05） =====================
        public List<DeviceTypeDto> ListDeviceTypes(IDbConnection connection)
        {
            return connection.Query<DeviceTypeDto>(
                "SELECT id, name, CAST(COALESCE(maintenance_cycle,0) AS INTEGER) AS MaintenanceCycle, status FROM t_device_type WHERE del_flag = 0 AND status = 0 ORDER BY id").ToList();
        }

        public DeviceTypeDto GetDeviceType(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<DeviceTypeDto>(
                "SELECT id, name, CAST(COALESCE(maintenance_cycle,0) AS INTEGER) AS MaintenanceCycle, status FROM t_device_type WHERE id = @id AND del_flag = 0", new { id });
        }

        public int InsertDeviceType(IDbConnection connection, IDbTransaction transaction, DeviceTypeDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_device_type (name, maintenance_cycle, status, del_flag) VALUES (@Name, @MaintenanceCycle, @Status, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.MaintenanceCycle, dto.Status }, transaction);
        }

        public void UpdateDeviceType(IDbConnection connection, IDbTransaction transaction, DeviceTypeDto dto)
        {
            connection.Execute(
                "UPDATE t_device_type SET name = @Name, maintenance_cycle = @MaintenanceCycle, status = @Status, " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.Name, dto.MaintenanceCycle, dto.Status }, transaction);
        }

        public void SoftDeleteDeviceType(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_device_type SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id",
                new { id }, transaction);
        }

        public int CountDevicesByType(IDbConnection connection, int typeId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_device WHERE type_id = @typeId AND del_flag = 0", new { typeId });
        }

        // ===================== 保养/年检登记类型（t_maintain_type，PG-EQP-02） =====================
        /// <summary>R6：类型固定为系统内置（保养/年检）；includeDisabled=true 时附带历史停用类型（仅只读展示）。</summary>
        public List<MaintainTypeDto> ListMaintainTypes(IDbConnection connection, bool includeDisabled)
        {
            return connection.Query<MaintainTypeDto>(
                "SELECT id, name, kind AS Kind, is_system AS IsSystem FROM t_maintain_type WHERE " +
                (includeDisabled ? "1 = 1" : "del_flag = 0") +
                " ORDER BY is_system DESC, sort, id").ToList();
        }

        public MaintainTypeDto GetMaintainType(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<MaintainTypeDto>(
                "SELECT id, name, kind AS Kind, is_system AS IsSystem FROM t_maintain_type WHERE id = @id AND del_flag = 0", new { id });
        }

        // ===================== 设备台账（t_device，BR-EQP-01） =====================
        private const string DeviceBaseSql =
            "SELECT d.id, d.type_id AS TypeId, d.name, d.location, d.status, d.enable_date AS EnableDate, d.del_flag AS DelFlag, " +
            "COALESCE(d.brand_model,'') AS BrandModel, COALESCE(t.name,'') AS TypeName, " +
            "COALESCE(d.warranty_end,'') AS WarrantyEnd, COALESCE(d.contract_end,'') AS ContractEnd, " +
            "d.maintenance_cycle_override AS MaintenanceCycleOverride, d.next_maintenance_at AS NextMaintenanceOverride " +
            "FROM t_device d LEFT JOIN t_device_type t ON t.id = d.type_id";

        public DeviceDto GetDevice(IDbConnection connection, int id)
        {
            var dto = connection.QueryFirstOrDefault<DeviceDto>(
                DeviceBaseSql + " WHERE d.id = @id AND d.del_flag = 0", new { id });
            return NormalizeDevices(connection, new[] { dto }).FirstOrDefault();
        }

        public PageResult<DeviceDto> QueryDevices(IDbConnection connection, DeviceQueryRequest query, out int total)
        {
            string where = "WHERE d.del_flag = 0";
            var p = new DynamicParameters();
            if (query.TypeId.HasValue) { where += " AND d.type_id = @typeId"; p.Add("typeId", query.TypeId.Value); }
            if (query.Status.HasValue) { where += " AND d.status = @status"; p.Add("status", (int)query.Status.Value); }
            if (query.StatusIn != null && query.StatusIn.Count > 0)
            {
                where += " AND d.status IN @statusIn";
                p.Add("statusIn", query.StatusIn.Distinct().ToList());
            }
            if (!string.IsNullOrWhiteSpace(query.Location))
            {
                where += " AND COALESCE(d.location,'') LIKE @loc";
                p.Add("loc", "%" + query.Location.Trim() + "%");
            }
            if (query.HasPendingFault.HasValue)
            {
                // PG-EQP-01 状态筛选"故障"：与行内派生展示态一致（存在 status 0 待接单故障单）
                const string pendingFaultExists =
                    "EXISTS (SELECT 1 FROM t_fault_record f WHERE f.device_id = d.id AND f.status = 0)";
                where += query.HasPendingFault.Value ? " AND " + pendingFaultExists : " AND NOT " + pendingFaultExists;
            }
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                // 编号是 "EQP-0000"（NormalizeDevice 派生），补齐编号匹配 + 品牌型号（PG-EQP-01 搜索）
                where += " AND (d.name LIKE @kw OR d.location LIKE @kw OR COALESCE(d.brand_model,'') LIKE @kw " +
                         "OR ('EQP-' || printf('%04d', d.id)) LIKE @idkw OR printf('%04d', d.id) LIKE @idkw OR CAST(d.id AS TEXT) LIKE @idkw)";
                p.Add("kw", "%" + query.Keyword.Trim() + "%");
                p.Add("idkw", "%" + query.Keyword.Trim() + "%");
            }
            total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_device d " + where, p);
            int pageIndex = query.PageIndex <= 0 ? 1 : query.PageIndex;
            int pageSize = query.PageSize <= 0 ? 20 : query.PageSize;
            int offset = (pageIndex - 1) * pageSize;
            p.Add("limit", pageSize); p.Add("offset", offset);
            // 输入即检索：命中关键词的行按相关度重排（编号/名称前缀命中优先，其次其余命中，组内仍按编号升序）
            string orderBy = " ORDER BY d.id";
            if (!string.IsNullOrWhiteSpace(query.Keyword))
            {
                const string relevance =
                    " ORDER BY CASE " +
                    "WHEN ('EQP-' || printf('%04d', d.id)) LIKE @kwPrefix THEN 0 " +
                    "WHEN COALESCE(d.name,'') LIKE @kwPrefix THEN 1 " +
                    "ELSE 2 END, d.id";
                p.Add("kwPrefix", query.Keyword.Trim() + "%");
                orderBy = relevance;
            }
            var items = connection.Query<DeviceDto>(
                DeviceBaseSql + " " + where + orderBy + " LIMIT @limit OFFSET @offset", p).ToList();
            NormalizeDevices(connection, items);
            return new PageResult<DeviceDto> { PageIndex = pageIndex, PageSize = pageSize, Total = total, Items = items };
        }

        public List<DeviceDto> ListDevices(IDbConnection connection, int? typeId)
        {
            string sql = DeviceBaseSql + " WHERE d.del_flag = 0";
            var p = new DynamicParameters();
            if (typeId.HasValue) { sql += " AND d.type_id = @typeId"; p.Add("typeId", typeId.Value); }
            var items = connection.Query<DeviceDto>(sql + " ORDER BY d.id", p).ToList();
            NormalizeDevices(connection, items);
            return items;
        }

        public int InsertDevice(IDbConnection connection, IDbTransaction transaction, DeviceDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_device (type_id, name, location, status, enable_date, brand_model, warranty_end, contract_end, maintenance_cycle_override, del_flag) " +
                "VALUES (@TypeId, @Name, @Location, @Status, @EnableDate, @BrandModel, @WarrantyEnd, @ContractEnd, @MaintenanceCycleOverride, 0); SELECT last_insert_rowid();",
                new
                {
                    dto.TypeId, dto.Name, dto.Location, Status = (int)dto.Status,
                    EnableDate = dto.EnableDate?.ToString("yyyy-MM-dd"), dto.BrandModel,
                    dto.WarrantyEnd, dto.ContractEnd, dto.MaintenanceCycleOverride
                }, transaction);
        }

        public void UpdateDevice(IDbConnection connection, IDbTransaction transaction, DeviceDto dto)
        {
            connection.Execute(
                "UPDATE t_device SET type_id = @TypeId, name = @Name, location = @Location, status = @Status, " +
                "enable_date = @EnableDate, brand_model = @BrandModel, " +
                "warranty_end = COALESCE(@WarrantyEnd, warranty_end), contract_end = COALESCE(@ContractEnd, contract_end), " +
                "maintenance_cycle_override = COALESCE(@MaintenanceCycleOverride, maintenance_cycle_override), " +
                "updated_at = datetime('now','localtime') WHERE id = @Id AND del_flag = 0",
                new
                {
                    dto.Id, dto.TypeId, dto.Name, dto.Location, Status = (int)dto.Status,
                    EnableDate = dto.EnableDate?.ToString("yyyy-MM-dd"), dto.BrandModel,
                    dto.WarrantyEnd, dto.ContractEnd, dto.MaintenanceCycleOverride
                }, transaction);
        }

        /// <summary>R6：写入/清空自定义下次保养日期（nextMaintenanceAt=null → 置空，恢复按周期派生）。</summary>
        public void UpdateDeviceNextMaintenance(IDbConnection connection, IDbTransaction transaction, int id, string nextMaintenanceAt)
        {
            connection.Execute(
                "UPDATE t_device SET next_maintenance_at = @nextMaintenanceAt, updated_at = datetime('now','localtime') " +
                "WHERE id = @id AND del_flag = 0",
                new { id, nextMaintenanceAt }, transaction);
        }

        public void SoftDeleteDevice(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_device SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id AND del_flag = 0", new { id }, transaction);
        }

        public void InsertDeviceStatusLog(IDbConnection connection, IDbTransaction transaction, int deviceId, int oldStatus, int newStatus, string reason)
        {
            connection.Execute(
                "INSERT INTO t_device_status_log (device_id, old_status, new_status, reason) VALUES (@deviceId, @oldStatus, @newStatus, @reason)",
                new { deviceId, oldStatus, newStatus, reason }, transaction);
        }

        public List<DeviceStatusLogDto> ListDeviceStatusLogs(IDbConnection connection, int deviceId)
        {
            return connection.Query<DeviceStatusLogDto>(
                "SELECT id, device_id AS DeviceId, old_status AS OldStatus, new_status AS NewStatus, reason, changed_at AS ChangedAt " +
                "FROM t_device_status_log WHERE device_id = @deviceId AND del_flag = 0 ORDER BY id DESC", new { deviceId }).ToList();
        }

        public DeviceSummaryDto GetDeviceSummary(IDbConnection connection)
        {
            var dto = new DeviceSummaryDto();
            dto.Total = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_device WHERE del_flag = 0");
            dto.InUse = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_device WHERE del_flag = 0 AND status = 0");
            dto.Repairing = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_device WHERE del_flag = 0 AND status = 1");
            dto.DisabledOrScrapped = connection.ExecuteScalar<int>("SELECT COUNT(1) FROM t_device WHERE del_flag = 0 AND status IN (2,3)");
            dto.TypeCount = connection.ExecuteScalar<int>("SELECT COUNT(DISTINCT type_id) FROM t_device WHERE del_flag = 0");
            // "故障" = 存在未结故障单（status 0/1）的派生展示态（PG-EQP-01 统计卡子标注）
            dto.FaultCount = connection.ExecuteScalar<int>(
                "SELECT COUNT(DISTINCT f.device_id) FROM t_fault_record f " +
                "JOIN t_device d ON d.id = f.device_id AND d.del_flag = 0 WHERE f.status IN (0,1)");
            return dto;
        }

        // ===================== 保养记录（t_maintenance_record，BR-EQP-02/03） =====================
        public List<MaintenanceRecordDto> ListMaintenance(IDbConnection connection, int deviceId)
        {
            // 软删设备记录防泄漏：JOIN 校验 del_flag = 0
            return connection.Query<MaintenanceRecordDto>(
                "SELECT m.id, m.device_id AS DeviceId, m.vendor_id AS VendorId, m.m_date AS MDate, m.content, m.result, " +
                "COALESCE(m.record_type,'') AS RecordType, m.cost AS Cost, COALESCE(m.fail_reason,'') AS FailReason, " +
                "COALESCE(v.name,'') AS VendorName " +
                "FROM t_maintenance_record m " +
                "JOIN t_device d ON d.id = m.device_id AND d.del_flag = 0 " +
                "LEFT JOIN t_vendor v ON v.id = m.vendor_id " +
                "WHERE m.device_id = @deviceId ORDER BY m.m_date DESC, m.id DESC", new { deviceId }).ToList();
        }

        public int InsertMaintenance(IDbConnection connection, IDbTransaction transaction, MaintenanceRecordDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_maintenance_record (device_id, vendor_id, m_date, content, result, cost, fail_reason, record_type) " +
                "VALUES (@DeviceId, @VendorId, @MDate, @Content, @Result, @Cost, @FailReason, @RecordType); SELECT last_insert_rowid();",
                new { dto.DeviceId, dto.VendorId, MDate = dto.MDate.ToString("yyyy-MM-dd"), dto.Content, dto.Result, dto.Cost, dto.FailReason, dto.RecordType }, transaction);
        }

        // ===================== 年检记录（t_inspection_record，BR-EQP-02/03） =====================
        public List<InspectionRecordDto> ListInspection(IDbConnection connection, int deviceId)
        {
            return connection.Query<InspectionRecordDto>(
                "SELECT i.id, i.device_id AS DeviceId, i.vendor_id AS VendorId, i.i_date AS IDate, i.result, " +
                "COALESCE(i.record_type,'') AS RecordType, i.cost AS Cost, COALESCE(i.fail_reason,'') AS FailReason, " +
                "COALESCE(v.name,'') AS VendorName " +
                "FROM t_inspection_record i " +
                "JOIN t_device d ON d.id = i.device_id AND d.del_flag = 0 " +
                "LEFT JOIN t_vendor v ON v.id = i.vendor_id " +
                "WHERE i.device_id = @deviceId ORDER BY i.i_date DESC, i.id DESC", new { deviceId }).ToList();
        }

        public int InsertInspection(IDbConnection connection, IDbTransaction transaction, InspectionRecordDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_inspection_record (device_id, vendor_id, i_date, result, cost, fail_reason, record_type) " +
                "VALUES (@DeviceId, @VendorId, @IDate, @Result, @Cost, @FailReason, @RecordType); SELECT last_insert_rowid();",
                new { dto.DeviceId, dto.VendorId, IDate = dto.IDate.ToString("yyyy-MM-dd"), dto.Result, dto.Cost, dto.FailReason, dto.RecordType }, transaction);
        }

        // ===================== 故障记录（t_fault_record，BR-EQP-04） =====================
        private const string FaultBaseSql =
            "SELECT f.id, f.device_id AS DeviceId, f.event_id AS EventId, f.f_time AS FTime, f.symptom, f.cause, f.handle, " +
            "f.fault_no AS FaultNo, f.level, f.reporter AS Reporter, f.status AS Status, COALESCE(d.name,'') AS DeviceName, " +
            "COALESCE(e.event_no,'') AS EventNo " +
            "FROM t_fault_record f LEFT JOIN t_device d ON d.id = f.device_id " +
            "LEFT JOIN t_emergency_event e ON e.id = f.event_id";

        public List<FaultRecordDto> ListFaults(IDbConnection connection, int deviceId)
        {
            return connection.Query<FaultRecordDto>(
                FaultBaseSql + " WHERE f.device_id = @deviceId AND d.del_flag = 0 ORDER BY f.f_time DESC, f.id DESC", new { deviceId })
                .Select(NormalizeFault).ToList();
        }

        public List<FaultRecordDto> ListRecentFaults(IDbConnection connection, int limit)
        {
            return connection.Query<FaultRecordDto>(
                FaultBaseSql + " WHERE d.del_flag = 0 ORDER BY f.f_time DESC, f.id DESC LIMIT @limit", new { limit })
                .Select(NormalizeFault).ToList();
        }

        public FaultRecordDto GetFault(IDbConnection connection, int id)
        {
            // 软删设备的故障单不可见（防泄漏，与 ListFaults 口径一致）
            return NormalizeFault(connection.QueryFirstOrDefault<FaultRecordDto>(FaultBaseSql + " WHERE f.id = @id AND d.del_flag = 0", new { id }));
        }

        public int InsertFault(IDbConnection connection, IDbTransaction transaction, FaultRecordDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_fault_record (device_id, event_id, f_time, symptom, cause, handle, level, reporter, status) " +
                "VALUES (@DeviceId, @EventId, @FTime, @Symptom, @Cause, @Handle, @Level, @Reporter, @Status); SELECT last_insert_rowid();",
                new { dto.DeviceId, dto.EventId, FTime = dto.FTime, dto.Symptom, dto.Cause, dto.Handle, dto.Level, dto.Reporter, dto.Status }, transaction);
        }

        public void SetFaultNo(IDbConnection connection, IDbTransaction transaction, int id, string faultNo)
        {
            connection.Execute(
                "UPDATE t_fault_record SET fault_no = @faultNo WHERE id = @id",
                new { id, faultNo }, transaction);
        }

        public void UpdateFaultHandle(IDbConnection connection, IDbTransaction transaction, int id, string cause, string handle, int status)
        {
            connection.Execute(
                "UPDATE t_fault_record SET " +
                "cause = CASE WHEN @cause IS NULL OR @cause = '' THEN cause ELSE @cause END, " +
                "handle = @handle, status = @status WHERE id = @id",
                new { id, cause, handle, status }, transaction);
        }

        private static FaultRecordDto NormalizeFault(FaultRecordDto dto)
        {
            if (dto == null) return null;
            if (string.IsNullOrEmpty(dto.FaultNo)) dto.FaultNo = "WX-" + dto.FTime.ToString("yyMM") + "-" + dto.Id;
            dto.LevelText = dto.Level == 1 ? "重大" : "一般";
            // 状态：0 待处理 1 维修中 2 已修复；历史行（无状态列时期）按 Handle 兜底
            if (dto.Status == 1) dto.StatusText = "维修中";
            else if (dto.Status == 2) dto.StatusText = "已修复";
            else dto.StatusText = string.IsNullOrWhiteSpace(dto.Handle) ? "待接单" : "已处理";
            return dto;
        }

        // ===================== 维保单位（t_vendor，BR-EQP-06） =====================
        public List<VendorDto> ListVendors(IDbConnection connection)
        {
            return connection.Query<VendorDto>(
                "SELECT id, name, contact, phone FROM t_vendor WHERE del_flag = 0 ORDER BY id").ToList();
        }

        public VendorDto GetVendor(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<VendorDto>(
                "SELECT id, name, contact, phone FROM t_vendor WHERE id = @id AND del_flag = 0", new { id });
        }

        public int InsertVendor(IDbConnection connection, IDbTransaction transaction, VendorDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_vendor (name, contact, phone, del_flag) VALUES (@Name, @Contact, @Phone, 0); SELECT last_insert_rowid();",
                new { dto.Name, dto.Contact, dto.Phone }, transaction);
        }

        public void UpdateVendor(IDbConnection connection, IDbTransaction transaction, VendorDto dto)
        {
            connection.Execute(
                "UPDATE t_vendor SET name = @Name, contact = @Contact, phone = @Phone, updated_at = datetime('now','localtime') " +
                "WHERE id = @Id AND del_flag = 0", new { dto.Id, dto.Name, dto.Contact, dto.Phone }, transaction);
        }

        public void SoftDeleteVendor(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_vendor SET del_flag = 1, updated_at = datetime('now','localtime') WHERE id = @id",
                new { id }, transaction);
        }

        /// <summary>BR-EQP-06 引用计数（M7 BUG-001）：保养 + 年检 + 自定义记录 + 未软删支出关联。</summary>
        public int CountVendorReferences(IDbConnection connection, int vendorId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT " +
                "(SELECT COUNT(1) FROM t_maintenance_record WHERE vendor_id = @vendorId) + " +
                "(SELECT COUNT(1) FROM t_inspection_record  WHERE vendor_id = @vendorId) + " +
                "(SELECT COUNT(1) FROM t_device_custom_record WHERE vendor_id = @vendorId AND del_flag = 0) + " +
                "(SELECT COUNT(1) FROM t_expense_object_rel r JOIN t_expense e ON e.id = r.expense_id " +
                "  WHERE r.object_type = 2 AND r.object_id = @vendorId AND e.del_flag = 0)",
                new { vendorId });
        }

        /// <summary>
        /// 设备删除引用校验（v1.1.0 R1）：保养/年检/故障/自定义记录（在用）+ 支出引用（在用支出）。
        /// 口径同 <see cref="CountVendorReferences"/>：软删的支出不计引用，历史业务记录计入引用。
        /// 说明：状态变更日志**不计入**阻塞项（设备登记时自动写入 1 行，正常设备必有），
        /// 改为随设备删除由 <see cref="SoftDeleteDeviceStatusLogs"/> 级联软删留痕。
        /// </summary>
        public int CountDeviceReferences(IDbConnection connection, int deviceId)
        {
            return connection.ExecuteScalar<int>(
                "SELECT " +
                "(SELECT COUNT(1) FROM t_maintenance_record WHERE device_id = @deviceId) + " +
                "(SELECT COUNT(1) FROM t_inspection_record  WHERE device_id = @deviceId) + " +
                "(SELECT COUNT(1) FROM t_fault_record       WHERE device_id = @deviceId) + " +
                "(SELECT COUNT(1) FROM t_device_custom_record WHERE device_id = @deviceId AND del_flag = 0) + " +
                "(SELECT COUNT(1) FROM t_expense_object_rel r JOIN t_expense e ON e.id = r.expense_id " +
                "  WHERE r.object_type = 1 AND r.object_id = @deviceId AND e.del_flag = 0)",
                new { deviceId });
        }

        /// <summary>
        /// 设备删除级联（v1.1.0 R1）：软删该设备的四类到期提醒 + 其催办/处置流水
        /// （流水 type='urge'/'handled' 且 target_id = 提醒 id）。返回软删行数。
        /// </summary>
        public int SoftDeleteDeviceReminders(IDbConnection connection, IDbTransaction transaction, int deviceId)
        {
            return connection.Execute(
                "UPDATE t_reminder SET del_flag = 1 WHERE del_flag = 0 AND (" +
                "  (type IN (" + ReminderTypes + ") AND target_id = @deviceId) OR " +
                "  (type IN ('urge','handled') AND target_id IN " +
                "     (SELECT id FROM t_reminder WHERE type IN (" + ReminderTypes + ") AND target_id = @deviceId)))",
                new { deviceId }, transaction);
        }

        /// <summary>设备删除级联（v1.1.0 R1 / migration_036）：软删该设备的状态变更日志。返回软删行数。</summary>
        public int SoftDeleteDeviceStatusLogs(IDbConnection connection, IDbTransaction transaction, int deviceId)
        {
            return connection.Execute(
                "UPDATE t_device_status_log SET del_flag = 1 WHERE device_id = @deviceId AND del_flag = 0",
                new { deviceId }, transaction);
        }

        // ===================== 到期提醒（PG-EQP-04，t_reminder 物化） =====================
        // 催办/处置流水复用 t_reminder（不加列）：type='urge'/'handled'，target_id=提醒 id，due_at=操作时间。

        public List<ReminderCandidateRow> ListMaintenanceDueCandidates(IDbConnection connection)
        {
            // 周期优先级：单台 override > 类型默认 > 30（P-04）
            return connection.Query<ReminderCandidateRow>(
                "SELECT d.id AS DeviceId, d.name AS DeviceName, " +
                "COALESCE(d.maintenance_cycle_override, CAST(t.maintenance_cycle AS INTEGER), 0) AS Cycle, " +
                "d.enable_date AS EnableDate, " +
                "d.next_maintenance_at AS NextMaintenanceOverride, " +
                "(SELECT MAX(m.m_date) FROM t_maintenance_record m WHERE m.device_id = d.id) AS LastDate " +
                "FROM t_device d LEFT JOIN t_device_type t ON t.id = d.type_id " +
                "WHERE d.del_flag = 0 AND d.status = 0").ToList();
        }

        /// <summary>单设备保养到期候选（R8：设备恢复「在用」后按当前周期重新物化提醒）。</summary>
        public ReminderCandidateRow GetMaintenanceCandidate(IDbConnection connection, int deviceId)
        {
            return connection.QueryFirstOrDefault<ReminderCandidateRow>(
                "SELECT d.id AS DeviceId, d.name AS DeviceName, " +
                "COALESCE(d.maintenance_cycle_override, CAST(t.maintenance_cycle AS INTEGER), 0) AS Cycle, " +
                "d.enable_date AS EnableDate, d.next_maintenance_at AS NextMaintenanceOverride, " +
                "(SELECT MAX(m.m_date) FROM t_maintenance_record m WHERE m.device_id = d.id) AS LastDate " +
                "FROM t_device d LEFT JOIN t_device_type t ON t.id = d.type_id " +
                "WHERE d.id = @deviceId AND d.del_flag = 0", new { deviceId });
        }

        public List<ReminderCandidateRow> ListInspectionDueCandidates(IDbConnection connection)
        {
            // BR-EQP-02 年检按年度：MAX(i_date)+1 年，无记录回退投运日期
            // 年检为合规项：在用/维修中/停用设备均需提醒（原型 PG-EQP-04 行-5「老扶梯（已封存）」年检逾期），报废除外
            return connection.Query<ReminderCandidateRow>(
                "SELECT d.id AS DeviceId, d.name AS DeviceName, d.enable_date AS EnableDate, " +
                "(SELECT MAX(i.i_date) FROM t_inspection_record i WHERE i.device_id = d.id) AS LastDate " +
                "FROM t_device d WHERE d.del_flag = 0 AND d.status <> 3").ToList();
        }

        public List<ReminderCandidateRow> ListWarrantyDueCandidates(IDbConnection connection)
        {
            return connection.Query<ReminderCandidateRow>(
                "SELECT d.id AS DeviceId, d.name AS DeviceName, d.warranty_end AS DueDate FROM t_device d " +
                "WHERE d.del_flag = 0 AND d.status IN (0,1,2) AND d.warranty_end IS NOT NULL AND TRIM(d.warranty_end) <> ''").ToList();
        }

        public List<ReminderCandidateRow> ListContractDueCandidates(IDbConnection connection)
        {
            return connection.Query<ReminderCandidateRow>(
                "SELECT d.id AS DeviceId, d.name AS DeviceName, d.contract_end AS DueDate FROM t_device d " +
                "WHERE d.del_flag = 0 AND d.status IN (0,1,2) AND d.contract_end IS NOT NULL AND TRIM(d.contract_end) <> ''").ToList();
        }

        public ReminderRow FindActiveReminder(IDbConnection connection, IDbTransaction transaction, string type, int targetId)
        {
            return connection.QueryFirstOrDefault<ReminderRow>(
                "SELECT id, due_at AS DueAt, status AS Status FROM t_reminder " +
                "WHERE type = @type AND target_id = @targetId AND status = 0 AND del_flag = 0 ORDER BY id DESC LIMIT 1",
                new { type, targetId }, transaction);
        }

        public string FindLastHandledReminderDueAt(IDbConnection connection, string type, int targetId)
        {
            // 只取本类型（maint_due/inspect_due/...）的已处理行；催办/处置流水 type='urge'/'handled' 不参与判定。
            // R7 修复：不排除 del_flag=1 —— 已处理记录被批量删除（软删）后仍是"本期已处置"的证据，
            // 否则物化会按推导出的到期日重新生成同一周期提醒，表现为"删除后又自动恢复"。
            return connection.ExecuteScalar<string>(
                "SELECT due_at FROM t_reminder WHERE type = @type AND target_id = @targetId AND status = 1 ORDER BY id DESC LIMIT 1",
                new { type, targetId });
        }

        public void InsertReminder(IDbConnection connection, IDbTransaction transaction, string type, int targetId, string dueAt)
        {
            connection.Execute(
                "INSERT INTO t_reminder (type, target_id, due_at, status) VALUES (@type, @targetId, @dueAt, 0)",
                new { type, targetId, dueAt }, transaction);
        }

        public void UpdateReminderDueAt(IDbConnection connection, IDbTransaction transaction, int id, string dueAt)
        {
            connection.Execute(
                "UPDATE t_reminder SET due_at = @dueAt WHERE id = @id", new { id, dueAt }, transaction);
        }

        public void HandleReminder(IDbConnection connection, IDbTransaction transaction, int id, string handledAt)
        {
            connection.Execute(
                "UPDATE t_reminder SET status = 1, handled_at = @handledAt WHERE id = @id",
                new { id, handledAt }, transaction);
        }

        /// <summary>R6：批量删除用行（含状态与设备展示）。</summary>
        public List<ReminderRow> GetRemindersByIds(IDbConnection connection, IEnumerable<int> ids)
        {
            var list = (ids ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (list.Count == 0) return new List<ReminderRow>();
            return connection.Query<ReminderRow>(
                "SELECT r.id, r.due_at AS DueAt, r.status AS Status, " +
                "printf('EQP-%04d', r.target_id) || ' ' || COALESCE(d.name,'') AS DeviceName " +
                "FROM t_reminder r LEFT JOIN t_device d ON d.id = r.target_id " +
                "WHERE r.id IN @ids AND r.type IN (" + ReminderTypes + ") AND r.del_flag = 0",
                new { ids = list }).ToList();
        }

        /// <summary>R6：批量软删（仅已处理记录，规则在服务层拦截）。</summary>
        public void SoftDeleteReminders(IDbConnection connection, IDbTransaction transaction, IEnumerable<int> ids)
        {
            var list = (ids ?? Enumerable.Empty<int>()).Distinct().ToList();
            if (list.Count == 0) return;
            connection.Execute(
                "UPDATE t_reminder SET del_flag = 1 WHERE id IN @ids", new { ids = list }, transaction);
        }

        /// <summary>催办/处置流水（status 直接置 1：流水非待办，避免污染 DashboardService 的 status=0 待办统计）。</summary>
        public void InsertReminderChild(IDbConnection connection, IDbTransaction transaction, string type, int targetId, string dueAt)
        {
            connection.Execute(
                "INSERT INTO t_reminder (type, target_id, due_at, status) VALUES (@type, @targetId, @dueAt, 1)",
                new { type, targetId, dueAt }, transaction);
        }

        public int CountMonthHandled(IDbConnection connection)
        {
            return connection.ExecuteScalar<int>(
                "SELECT COUNT(1) FROM t_reminder WHERE type = '" + HandledChildType + "' " +
                "AND strftime('%Y-%m', created_at) = strftime('%Y-%m', 'now', 'localtime')");
        }

        public int InsertExportLog(IDbConnection connection, IDbTransaction transaction, string module, int format, string filePath)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_export_log (module, format, file_path) VALUES (@module, @format, @filePath); SELECT last_insert_rowid();",
                new { module, format, filePath }, transaction);
        }

        public EquipmentReminderDto GetReminder(IDbConnection connection, int id)
        {
            return NormalizeReminder(connection.QueryFirstOrDefault<EquipmentReminderDto>(
                "SELECT r.id AS ReminderId, r.type AS Type, r.target_id AS DeviceId, r.due_at AS DueAt, r.status AS Status, " +
                "COALESCE(r.handled_at,'') AS HandledAt, " +
                ResponsibleTeamSql + ", " +
                "COALESCE(d.name,'') AS DeviceName, COALESCE(t.name,'') AS TypeName " +
                "FROM t_reminder r LEFT JOIN t_device d ON d.id = r.target_id LEFT JOIN t_device_type t ON t.id = d.type_id " +
                "WHERE r.id = @id AND r.type IN (" + ReminderTypes + ") AND r.del_flag = 0 AND d.del_flag = 0", new { id }));
        }

        /// <summary>R6：列表含已处理记录（status=-1 全部 / 0 待处理 / 1 已处理），软删记录不返回。</summary>
        public List<EquipmentReminderDto> ListReminders(IDbConnection connection, string type, int status)
        {
            string sql =
                "SELECT r.id AS ReminderId, r.type AS Type, r.target_id AS DeviceId, r.due_at AS DueAt, r.status AS Status, " +
                "COALESCE(r.handled_at,'') AS HandledAt, " +
                ResponsibleTeamSql + ", " +
                "COALESCE(d.name,'') AS DeviceName, COALESCE(t.name,'') AS TypeName " +
                "FROM t_reminder r LEFT JOIN t_device d ON d.id = r.target_id LEFT JOIN t_device_type t ON t.id = d.type_id " +
                "WHERE r.del_flag = 0 AND r.type IN (" + ReminderTypes + ") AND d.del_flag = 0";
            if (status == 0 || status == 1) sql += " AND r.status = @status";
            else sql += " AND r.status IN (0,1)";
            if (!string.IsNullOrWhiteSpace(type)) sql += " AND r.type = @type";
            // 待处理在前，已处理在后；同组按到期日
            var list = connection.Query<EquipmentReminderDto>(sql + " ORDER BY r.status, r.due_at, r.id",
                    new { type = string.IsNullOrWhiteSpace(type) ? null : type, status })
                .Select(NormalizeReminder).Where(x => x != null).ToList();
            return list;
        }

        /// <summary>
        /// 责任班组口径（R7）：人工指派值优先；未指派时按默认规则——逾期→物业办，其余→工程部。
        /// 同时回传指派原值（空=默认）供界面区分"自定义/默认"并支持恢复默认。
        /// </summary>
        private const string ResponsibleTeamSql =
            "CASE WHEN TRIM(COALESCE(r.responsible_team,'')) <> '' THEN TRIM(r.responsible_team) " +
            "WHEN date(r.due_at) < date('now','localtime') THEN '物业办' ELSE '工程部' END AS ResponsibleTeam, " +
            "TRIM(COALESCE(r.responsible_team,'')) AS ResponsibleTeamOverride";

        /// <summary>R7：指派责任班组（team 为空 → 清空指派，恢复默认规则）。</summary>
        public void UpdateReminderTeam(IDbConnection connection, IDbTransaction transaction, int id, string team)
        {
            connection.Execute(
                "UPDATE t_reminder SET responsible_team = @team WHERE id = @id",
                new { id, team = string.IsNullOrWhiteSpace(team) ? null : team.Trim() }, transaction);
        }

        private static EquipmentReminderDto NormalizeReminder(EquipmentReminderDto dto)
        {
            if (dto == null) return null;
            switch (dto.Type)
            {
                case "maint_due": dto.Kind = "maintenance"; dto.ItemText = "保养到期"; break;
                case "inspect_due": dto.Kind = "inspection"; dto.ItemText = "年检到期"; break;
                case "warranty_due": dto.Kind = "warranty"; dto.ItemText = "质保到期"; break;
                case "contract_due": dto.Kind = "contract"; dto.ItemText = "合同到期"; break;
                default: return dto; // 未知类型原样返回
            }
            dto.DeviceNo = "EQP-" + dto.DeviceId.ToString("0000");
            dto.RemainingDays = dto.DueAt.Date.Subtract(DateTime.Today).Days;
            // R6：提醒进度（计划 30/7/1 天 + 人工催办）随进度栏与一键催办一并下线，DTO 不再返回
            if (DateTime.TryParse(dto.HandledAt, out DateTime handled))
                dto.HandledAt = handled.ToString("yyyy-MM-dd HH:mm");
            else dto.HandledAt = string.Empty;
            // 责任班组由 SQL 统一给出（R7：人工指派优先，未指派按"逾期→物业办 / 其余→工程部"）
            if (dto.Status == ReminderStatus.Processed) dto.StatusText = "已处理";
            else if (dto.RemainingDays < 0) dto.StatusText = "逾期";
            else dto.StatusText = dto.RemainingDays <= 30 ? "待处理" : "正常";
            return dto;
        }

        // ===================== 自定义类型记录（t_device_custom_record，R6 登记类型解耦） =====================
        private const string CustomRecordBaseSql =
            "SELECT c.id, c.device_id AS DeviceId, c.type_name AS TypeName, c.r_date AS RDate, " +
            "COALESCE(c.content,'') AS Content, COALESCE(c.result,'') AS Result, c.cost AS Cost, " +
            "c.vendor_id AS VendorId, COALESCE(v.name,'') AS VendorName, COALESCE(c.operator,'') AS Operator, " +
            "c.created_at AS CreatedAt " +
            "FROM t_device_custom_record c LEFT JOIN t_vendor v ON v.id = c.vendor_id";

        public List<DeviceCustomRecordDto> ListCustomRecords(IDbConnection connection, int deviceId)
        {
            return connection.Query<DeviceCustomRecordDto>(
                CustomRecordBaseSql + " WHERE c.device_id = @deviceId AND c.del_flag = 0 ORDER BY c.r_date DESC, c.id DESC",
                new { deviceId }).ToList();
        }

        public DeviceCustomRecordDto GetCustomRecord(IDbConnection connection, int id)
        {
            return connection.QueryFirstOrDefault<DeviceCustomRecordDto>(
                CustomRecordBaseSql + " WHERE c.id = @id AND c.del_flag = 0", new { id });
        }

        public int InsertCustomRecord(IDbConnection connection, IDbTransaction transaction, DeviceCustomRecordDto dto)
        {
            return connection.ExecuteScalar<int>(
                "INSERT INTO t_device_custom_record (device_id, type_name, r_date, content, result, cost, vendor_id, operator, del_flag) " +
                "VALUES (@DeviceId, @TypeName, @RDate, @Content, @Result, @Cost, @VendorId, @Operator, 0); SELECT last_insert_rowid();",
                new { dto.DeviceId, dto.TypeName, RDate = dto.RDate.ToString("yyyy-MM-dd"), dto.Content, dto.Result, dto.Cost, dto.VendorId, dto.Operator },
                transaction);
        }

        public void UpdateCustomRecord(IDbConnection connection, IDbTransaction transaction, DeviceCustomRecordDto dto)
        {
            connection.Execute(
                "UPDATE t_device_custom_record SET type_name = @TypeName, r_date = @RDate, content = @Content, " +
                "result = @Result, cost = @Cost, vendor_id = @VendorId, operator = @Operator WHERE id = @Id AND del_flag = 0",
                new { dto.Id, dto.TypeName, RDate = dto.RDate.ToString("yyyy-MM-dd"), dto.Content, dto.Result, dto.Cost, dto.VendorId, dto.Operator },
                transaction);
        }

        public void SoftDeleteCustomRecord(IDbConnection connection, IDbTransaction transaction, int id)
        {
            connection.Execute(
                "UPDATE t_device_custom_record SET del_flag = 1 WHERE id = @id", new { id }, transaction);
        }

        /// <summary>自定义类型名称候选：历史自定义登记类型（已停用）+ 已录入的自定义记录类型，去重排序。</summary>
        public List<string> ListCustomRecordTypeNames(IDbConnection connection)
        {
            return connection.Query<string>(
                "SELECT name FROM t_maintain_type WHERE is_system = 0 AND TRIM(COALESCE(name,'')) <> '' " +
                "UNION " +
                "SELECT type_name FROM t_device_custom_record WHERE del_flag = 0 AND TRIM(COALESCE(type_name,'')) <> '' " +
                "ORDER BY 1").ToList();
        }

        // ===================== 设备行归一化（批量，消除 N+1） =====================
        private static List<DeviceDto> NormalizeDevices(IDbConnection connection, IEnumerable<DeviceDto> devices)
        {
            var list = (devices ?? Enumerable.Empty<DeviceDto>()).Where(d => d != null).ToList();
            if (list.Count == 0) return list;

            var cycleMap = new Dictionary<int, int>();
            foreach (var row in connection.Query<TypeCycleRow>(
                "SELECT id, CAST(COALESCE(maintenance_cycle,0) AS INTEGER) AS Cycle FROM t_device_type WHERE del_flag = 0"))
                cycleMap[row.Id] = row.Cycle;

            var ids = list.Select(d => d.Id).Distinct().ToList();
            var lastMap = new Dictionary<int, DateTime>();
            var lastInspMap = new Dictionary<int, DateTime>();
            var faultMap = new Dictionary<int, int>();
            // 分批 IN 查询（500/批）：导出全量时规避 SQLite 绑定变量数上限
            for (int i = 0; i < ids.Count; i += 500)
            {
                var batch = ids.Skip(i).Take(500).ToList();
                foreach (var row in connection.Query<LastMaintRow>(
                    "SELECT device_id AS DeviceId, MAX(m_date) AS LastDate FROM t_maintenance_record WHERE device_id IN @ids GROUP BY device_id",
                    new { ids = batch }))
                    lastMap[row.DeviceId] = row.LastDate;
                foreach (var row in connection.Query<LastMaintRow>(
                    "SELECT device_id AS DeviceId, MAX(i_date) AS LastDate FROM t_inspection_record WHERE device_id IN @ids GROUP BY device_id",
                    new { ids = batch }))
                    lastInspMap[row.DeviceId] = row.LastDate;
                // 未结故障单最紧急状态：0 待处理 → 派生展示态"故障"；1 维修中 → 沿用"维修中"
                foreach (var row in connection.Query<OpenFaultRow>(
                    "SELECT device_id AS DeviceId, MIN(status) AS MinStatus FROM t_fault_record " +
                    "WHERE device_id IN @ids AND status IN (0,1) GROUP BY device_id",
                    new { ids = batch }))
                    faultMap[row.DeviceId] = row.MinStatus;
            }

            foreach (var d in list)
            {
                d.DeviceNo = "EQP-" + d.Id.ToString("0000");
                d.OpenFaultState = faultMap.TryGetValue(d.Id, out int fs) ? fs : -1;
                d.StatusText = d.Status == DeviceStatus.InUse ? "在用"
                    : (d.Status == DeviceStatus.Repairing ? "维修中"
                    : (d.Status == DeviceStatus.Disabled ? "停用" : "报废"));
                // 原型行-5（EQP-0128 状态"故障"）：存在待处理故障单的在用/维修中设备派生为"故障"
                if (d.OpenFaultState == 0 && d.Status != DeviceStatus.Disabled && d.Status != DeviceStatus.Scrapped)
                    d.StatusText = "故障";
                int cycle = d.MaintenanceCycleOverride ?? (cycleMap.TryGetValue(d.TypeId, out int c) && c > 0 ? c : 30);
                if (cycle <= 0) cycle = 30;
                d.MaintenanceCycle = cycle;
                DateTime last = lastMap.TryGetValue(d.Id, out DateTime l) ? l : DateTime.MinValue;
                DateTime baseDate = last > DateTime.MinValue ? last : (d.EnableDate ?? DateTime.Today);
                // R6：自定义下次保养优先；非在用设备下次保养返回 null（前端显示"—"）
                if (d.Status == DeviceStatus.InUse && d.NextMaintenanceOverride.HasValue)
                {
                    d.NextMaintenance = d.NextMaintenanceOverride;
                    d.NextMaintenanceSource = "自定义";
                }
                else
                {
                    d.NextMaintenance = d.Status == DeviceStatus.InUse ? (DateTime?)baseDate.AddDays(cycle) : null;
                    d.NextMaintenanceSource = d.NextMaintenance.HasValue ? "周期派生" : null;
                }
                d.NextMaintenanceText = d.NextMaintenance.HasValue ? d.NextMaintenance.Value.ToString("MM-dd") : null;
                // BR-EQP-02：年检按年度，下次年检 = 最近一次年检 + 1 年（无记录回退投运日期）
                DateTime lastInsp = lastInspMap.TryGetValue(d.Id, out DateTime li) ? li : DateTime.MinValue;
                DateTime inspBase = lastInsp > DateTime.MinValue ? lastInsp : (d.EnableDate ?? DateTime.Today);
                d.NextInspection = inspBase.AddYears(1);
                d.NextInspectionText = d.NextInspection.Value.ToString("MM-dd");
            }
            return list;
        }

        private class TypeCycleRow
        {
            public int Id { get; set; }
            public int Cycle { get; set; }
        }

        private class LastMaintRow
        {
            public int DeviceId { get; set; }
            public DateTime LastDate { get; set; }
        }

        private class OpenFaultRow
        {
            public int DeviceId { get; set; }
            public int MinStatus { get; set; }
        }
    }
}

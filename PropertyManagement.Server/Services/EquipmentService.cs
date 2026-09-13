using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Domain.Repositories;
using PropertyManagement.Server.Infrastructure.Data;
using PropertyManagement.Server.Infrastructure.Repositories;

namespace PropertyManagement.Server.Services
{
    /// <summary>
    /// 设备资产台账服务（M6 D6-5，UC-EQP-001~007）。
    /// 业务规则：BR-EQP-01（状态机流转+留痕）、BR-EQP-02（保养/年检周期提醒）、
    /// BR-EQP-03（不合格转维修+生成工单）、BR-EQP-04（故障闭环）、BR-EQP-05（类型可扩展）、BR-EQP-06（维保单位复用）、P-04（周期 override>type>30）。
    /// </summary>
    public class EquipmentService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IEquipmentRepository _repo;
        private readonly AuditService _audit;

        public EquipmentService()
            : this(new SqliteConnectionFactory(), new SqlEquipmentRepository(), new AuditService())
        {
        }

        public EquipmentService(IDbConnectionFactory connectionFactory, IEquipmentRepository repo)
            : this(connectionFactory, repo, new AuditService())
        {
        }

        public EquipmentService(IDbConnectionFactory connectionFactory, IEquipmentRepository repo, AuditService audit)
        {
            _connectionFactory = connectionFactory;
            _repo = repo;
            _audit = audit;
        }

        // ===================== 设备类型（BR-EQP-05，T6-5-1 含 PUT） =====================
        public List<DeviceTypeDto> ListDeviceTypes() =>
            WithConnection(c => _repo.ListDeviceTypes(c));

        public DeviceTypeDto SaveDeviceType(int id, DeviceTypeRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("设备类型名称不能为空");
                if (request.MaintenanceCycle <= 0) throw ApiException.ValidationFailed("保养周期必须大于 0 天（P-04）");
                string name = request.Name.Trim();
                // 类别下拉自定义新增：同名类别去重（避免下拉出现重复候选；编辑时排除自身）
                if (_repo.ListDeviceTypes(c).Any(t => t.Id != id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                    throw ApiException.ValidationFailed("设备类别已存在：" + name);
                var dto = new DeviceTypeDto { Name = name, MaintenanceCycle = request.MaintenanceCycle, Status = 0 };
                if (id > 0)
                {
                    var existing = _repo.GetDeviceType(c, id) ?? throw ApiException.NotFound("设备类型不存在");
                    dto.Id = id;
                    dto.Status = request.Status.HasValue ? (request.Status.Value == 0 ? 0 : 1) : existing.Status;
                    _repo.UpdateDeviceType(c, tx, dto);
                    return dto;
                }
                dto.Id = _repo.InsertDeviceType(c, tx, dto);
                return dto;
            });

        /// <summary>删除设备类型（软删）：被在册设备引用时拒绝，提示先转移设备，避免设备失去类别。</summary>
        public void DeleteDeviceType(int id) =>
            WithTransaction((c, tx) =>
            {
                var existing = _repo.GetDeviceType(c, id) ?? throw ApiException.NotFound("设备类型不存在");
                int used = _repo.CountDevicesByType(c, id);
                if (used > 0)
                    throw ApiException.Conflict("该类别下仍有 " + used + " 台在册设备（如「" + existing.Name + "」），请先转移或删除设备后再删除类别");
                _repo.SoftDeleteDeviceType(c, tx, id);
            });

        // ===================== 保养/年检登记类型（PG-EQP-02 自定义新增/删除） =====================
        /// <summary>
        /// 登记类型字典（R6）：类型固定为系统内置 保养/年检，不再支持自定义新增/删除；
        /// includeDisabled=true 时附带历史停用类型（仅只读展示，历史 record_type 文本快照不受影响）。
        /// </summary>
        public List<MaintainTypeDto> ListMaintainTypes(bool includeDisabled = false) =>
            WithConnection(c => _repo.ListMaintainTypes(c, includeDisabled));

        // ===================== 设备台账（BR-EQP-01） =====================
        public PageResult<DeviceDto> QueryDevices(DeviceQueryRequest query) =>
            WithConnection(c => _repo.QueryDevices(c, query ?? new DeviceQueryRequest { PageIndex = 1, PageSize = 20 }, out int total));

        public DeviceDto GetDevice(int id) =>
            WithConnection(c => _repo.GetDevice(c, id) ?? throw ApiException.NotFound("设备不存在"));

        public DeviceSummaryDto GetDeviceSummary() =>
            WithConnection(c => _repo.GetDeviceSummary(c));

        public List<DeviceStatusLogDto> ListDeviceStatusLogs(int deviceId) =>
            WithConnection(c => _repo.ListDeviceStatusLogs(c, deviceId));

        public DeviceDto SaveDevice(int id, DeviceRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                if (request.TypeId <= 0) throw ApiException.ValidationFailed("请选择设备类型");
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("设备名称不能为空");
                if (id > 0)
                {
                    var existing = _repo.GetDevice(c, id) ?? throw ApiException.NotFound("设备不存在");
                    // R6：下次保养自定义仅在用设备可设置（停用/维修中/报废设备不参与保养计划）
                    if (request.NextMaintenanceOverride.HasValue && existing.Status != DeviceStatus.InUse)
                        throw ApiException.Conflict("仅在用设备可设置下次保养时间（当前：" + StatusText(existing.Status) + "）");
                    var dto = new DeviceDto
                    {
                        Id = id, TypeId = request.TypeId, Name = request.Name.Trim(), Location = request.Location ?? string.Empty,
                        Status = existing.Status, EnableDate = request.EnableDate, BrandModel = request.BrandModel ?? string.Empty,
                        WarrantyEnd = request.WarrantyEnd?.ToString("yyyy-MM-dd") ?? existing.WarrantyEnd,
                        ContractEnd = request.ContractEnd?.ToString("yyyy-MM-dd") ?? existing.ContractEnd,
                        MaintenanceCycleOverride = request.MaintenanceCycleOverride ?? existing.MaintenanceCycleOverride
                    };
                    _repo.UpdateDevice(c, tx, dto);
                    ApplyNextMaintenanceOverride(c, tx, id, request);
                    return _repo.GetDevice(c, id);
                }
                var created = new DeviceDto
                {
                    TypeId = request.TypeId, Name = request.Name.Trim(), Location = request.Location ?? string.Empty,
                    Status = DeviceStatus.InUse, EnableDate = request.EnableDate, BrandModel = request.BrandModel ?? string.Empty,
                    WarrantyEnd = request.WarrantyEnd?.ToString("yyyy-MM-dd"),
                    ContractEnd = request.ContractEnd?.ToString("yyyy-MM-dd"),
                    MaintenanceCycleOverride = request.MaintenanceCycleOverride
                };
                created.Id = _repo.InsertDevice(c, tx, created);
                ApplyNextMaintenanceOverride(c, tx, created.Id, request);
                _repo.InsertDeviceStatusLog(c, tx, created.Id, -1, (int)DeviceStatus.InUse, "登记");
                return _repo.GetDevice(c, created.Id);
            });

        /// <summary>
        /// R6 下次保养自定义（D1）：ClearNextMaintenance=true → 清空自定义值（恢复按周期派生）；
        /// 否则 NextMaintenanceOverride 有值才写入（空=保持原值，避免"编辑设备"表单误清自定义日期）。
        /// </summary>
        private void ApplyNextMaintenanceOverride(IDbConnection c, IDbTransaction tx, int deviceId, DeviceRequest request)
        {
            if (request.ClearNextMaintenance)
            {
                _repo.UpdateDeviceNextMaintenance(c, tx, deviceId, null);
                return;
            }
            if (request.NextMaintenanceOverride.HasValue)
                _repo.UpdateDeviceNextMaintenance(c, tx, deviceId, request.NextMaintenanceOverride.Value.ToString("yyyy-MM-dd"));
        }

        public void DeleteDevice(int id) =>
            WithTransaction((c, tx) => _repo.SoftDeleteDevice(c, tx, id));

        /// <summary>BR-EQP-01 合法迁移表：报废为终态。</summary>
        private static readonly Dictionary<DeviceStatus, DeviceStatus[]> AllowedTransitions = new Dictionary<DeviceStatus, DeviceStatus[]>
        {
            { DeviceStatus.InUse, new[] { DeviceStatus.Repairing, DeviceStatus.Disabled, DeviceStatus.Scrapped } },
            { DeviceStatus.Repairing, new[] { DeviceStatus.InUse, DeviceStatus.Scrapped } },
            { DeviceStatus.Disabled, new[] { DeviceStatus.InUse, DeviceStatus.Scrapped } },
            { DeviceStatus.Scrapped, new DeviceStatus[0] }
        };

        public DeviceDto ChangeDeviceStatus(int id, DeviceStatusRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                var existing = _repo.GetDevice(c, id) ?? throw ApiException.NotFound("设备不存在");
                int oldStatus = (int)existing.Status; // 留痕记录真实旧状态
                DeviceStatus newStatus = request.Status;
                if (!AllowedTransitions.TryGetValue(existing.Status, out DeviceStatus[] allowed) || !allowed.Contains(newStatus))
                {
                    if (newStatus == existing.Status) throw ApiException.Conflict("设备已处于该状态，无需变更");
                    throw ApiException.Conflict("不允许的状态变更：" + StatusText(existing.Status) + " → " + StatusText(newStatus) + "（BR-EQP-01）");
                }
                if ((newStatus == DeviceStatus.Disabled || newStatus == DeviceStatus.Scrapped) && string.IsNullOrWhiteSpace(request.Reason))
                    throw ApiException.ValidationFailed(newStatus == DeviceStatus.Scrapped ? "报废必须填写原因" : "停用必须填写原因");
                existing.Status = newStatus;
                _repo.UpdateDevice(c, tx, existing);
                _repo.InsertDeviceStatusLog(c, tx, id, oldStatus, (int)newStatus, request.Reason ?? string.Empty);
                // R8：停用/报废 → 关闭"保养到期"待处理提醒（保养计划不再适用；年检为合规项保持不变）
                if (newStatus == DeviceStatus.Disabled || newStatus == DeviceStatus.Scrapped)
                    CloseMaintReminderForInactive(c, tx, existing);
                // R8：恢复"在用" → 若保养仍在 30 天窗口内且此前已闭环，则重新物化待处理提醒
                if (newStatus == DeviceStatus.InUse)
                    ReopenMaintReminderIfDue(c, tx, existing);
                return _repo.GetDevice(c, id);
            });

        /// <summary>R8：设备停用/报废后闭环其"保养到期"待处理提醒（写 handled 流水，保留可追溯）。</summary>
        private void CloseMaintReminderForInactive(IDbConnection c, IDbTransaction tx, DeviceDto dev)
        {
            var active = _repo.FindActiveReminder(c, tx, "maint_due", dev.Id);
            if (active == null) return;
            _repo.HandleReminder(c, tx, active.Id, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _repo.InsertReminderChild(c, tx, "handled", active.Id, DateTime.Now.ToString("s"));
        }

        /// <summary>
        /// R8：设备恢复"在用"后，按当前保养周期重新物化"保养到期"提醒（30 天窗口内且当前无待处理提醒时）。
        /// 覆盖停用期间被闭环、以及"本期已处置不再重复物化"被抑制的场景。
        /// </summary>
        private void ReopenMaintReminderIfDue(IDbConnection c, IDbTransaction tx, DeviceDto dev)
        {
            if (_repo.FindActiveReminder(c, tx, "maint_due", dev.Id) != null) return;
            var row = _repo.GetMaintenanceCandidate(c, dev.Id);
            if (row == null) return;
            int cycle = row.Cycle > 0 ? row.Cycle : 30;
            DateTime due = ParseDate(row.NextMaintenanceOverride)
                           ?? (ParseDate(row.LastDate) ?? ParseDate(row.EnableDate) ?? DateTime.Today).AddDays(cycle);
            if (due > DateTime.Today.AddDays(30)) return; // 与提醒物化窗口一致：仅近 30 天内到期才提醒
            _repo.InsertReminder(c, tx, "maint_due", dev.Id, due.ToString("yyyy-MM-dd"));
        }

        private static string StatusText(DeviceStatus status)
        {
            switch (status)
            {
                case DeviceStatus.InUse: return "在用";
                case DeviceStatus.Repairing: return "维修中";
                case DeviceStatus.Disabled: return "停用";
                default: return "报废";
            }
        }

        // ===================== 保养登记（BR-EQP-02/03） =====================
        public List<MaintenanceRecordDto> ListMaintenance(int deviceId) =>
            WithConnection(c => _repo.ListMaintenance(c, deviceId));

        public MaintenanceRecordDto AddMaintenance(MaintenanceRecordRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                var dev = _repo.GetDevice(c, request.DeviceId) ?? throw ApiException.NotFound("设备不存在");
                var dto = new MaintenanceRecordDto
                {
                    DeviceId = request.DeviceId, VendorId = request.VendorId,
                    MDate = request.MDate == default ? DateTime.Today : request.MDate,
                    // R6：登记类型固定为系统两类，缺省即系统「保养」（不再按周期派生出多套类型名称）
                    RecordType = string.IsNullOrWhiteSpace(request.RecordType) ? "保养" : request.RecordType.Trim(),
                    Content = request.Content ?? string.Empty, Result = request.Result ?? string.Empty,
                    Cost = request.Cost, FailReason = (request.FailReason ?? string.Empty).Trim()
                };
                bool unqualified = IsNotQualified(dto.Result);
                if (unqualified && string.IsNullOrEmpty(dto.FailReason))
                    throw ApiException.ValidationFailed("检测结果不合格时必须填写不合格说明（BR-EQP-03）");
                dto.Id = _repo.InsertMaintenance(c, tx, dto);
                if (unqualified) dto.FaultNo = ToRepairingWithFault(c, tx, dev, "保养不合格：" + dto.FailReason, "保养不合格转维修");
                else CloseDueReminder(c, tx, "maint_due", dev, dto.MDate, dev.MaintenanceCycle);
                // R6（D1）：保养登记后按"登记日期 + 周期"覆盖自定义下次保养日期（来源回到周期派生）
                _repo.UpdateDeviceNextMaintenance(c, tx, dev.Id, null);
                // 合格：自定义日期已清空，下次保养由 NormalizeDevice 按 MAX(m_date)+周期 动态推导
                return dto;
            });

        // ===================== 年检登记（BR-EQP-02/03） =====================
        public List<InspectionRecordDto> ListInspection(int deviceId) =>
            WithConnection(c => _repo.ListInspection(c, deviceId));

        public InspectionRecordDto AddInspection(InspectionRecordRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                var dev = _repo.GetDevice(c, request.DeviceId) ?? throw ApiException.NotFound("设备不存在");
                var dto = new InspectionRecordDto
                {
                    DeviceId = request.DeviceId, VendorId = request.VendorId,
                    IDate = request.IDate == default ? DateTime.Today : request.IDate,
                    RecordType = string.IsNullOrWhiteSpace(request.RecordType) ? "年检" : request.RecordType.Trim(),
                    Result = request.Result ?? string.Empty,
                    Cost = request.Cost, FailReason = (request.FailReason ?? string.Empty).Trim()
                };
                bool unqualified = IsNotQualified(dto.Result);
                if (unqualified && string.IsNullOrEmpty(dto.FailReason))
                    throw ApiException.ValidationFailed("检测结果不合格时必须填写不合格说明（BR-EQP-03）");
                dto.Id = _repo.InsertInspection(c, tx, dto);
                if (unqualified) dto.FaultNo = ToRepairingWithFault(c, tx, dev, "年检不合格：" + dto.FailReason, "年检不合格转维修");
                else CloseDueReminder(c, tx, "inspect_due", dev, dto.IDate, 365);
                return dto;
            });

        /// <summary>Excel 列宽近似换算：以「0」字符宽度为单位，中文/全角按 2 个字符宽度计（导出列宽兜底用）。</summary>
        private static double VisualWidth(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            double width = 0;
            foreach (char ch in text) width += ch > 0x2E80 ? 2 : 1;
            return width;
        }

        /// <summary>不合格 → 设备转维修中（仅 InUse 可转，写真实旧状态）+ 生成维修工单（t_fault_record，BR-EQP-03/T6-5-3）。</summary>
        private string ToRepairingWithFault(IDbConnection c, IDbTransaction tx, DeviceDto dev, string symptom, string logReason)
        {
            SyncDeviceToRepairing(c, tx, dev, logReason);
            return CreateFaultCore(c, tx, dev.Id, symptom, 0);
        }

        /// <summary>设备转维修中（仅在用可转；已维修中/停用/报废保持原状态），并写状态留痕（BR-EQP-01）。</summary>
        private void SyncDeviceToRepairing(IDbConnection c, IDbTransaction tx, DeviceDto dev, string logReason)
        {
            if (dev == null || dev.Status != DeviceStatus.InUse)
            {
                return;
            }
            dev.Status = DeviceStatus.Repairing;
            _repo.UpdateDevice(c, tx, dev);
            _repo.InsertDeviceStatusLog(c, tx, dev.Id, (int)DeviceStatus.InUse, (int)DeviceStatus.Repairing, logReason);
        }

        /// <summary>
        /// 保养/年检登记完成后的跨模块闭环（BR-EQP-02，R5）：
        /// 把该设备当前的到期提醒置为「已处理」（写 handled 流水，计入"本月已处理"），
        /// 下一次读取提醒时按新记录顺延出下一周期提醒（t_device 无 next_* 列，到期日始终由登记记录动态推导）。
        /// 仅当本次登记确实把周期推到将来（下次到期日 &gt; 今天）才关闭，避免补录历史记录误关逾期提醒。
        /// </summary>
        private void CloseDueReminder(IDbConnection c, IDbTransaction tx, string type, DeviceDto dev, DateTime recordDate, int cycleDays)
        {
            if (dev == null) return;
            var active = _repo.FindActiveReminder(c, tx, type, dev.Id);
            if (active == null) return; // 本期没有待处理提醒，无需关闭（下次读取时按新记录物化）
            DateTime nextDue = type == "inspect_due"
                ? recordDate.Date.AddYears(1)
                : recordDate.Date.AddDays(cycleDays > 0 ? cycleDays : 30);
            if (nextDue <= DateTime.Today) return; // 补录历史记录：周期未推入将来，保留原提醒（可能仍逾期）
            _repo.HandleReminder(c, tx, active.Id, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            // 处置流水（type='handled'）＝"本月已处理"计数来源，与人工处置口径一致
            _repo.InsertReminderChild(c, tx, "handled", active.Id, DateTime.Now.ToString("s"));
        }

        // ===================== 故障（BR-EQP-04） =====================
        public List<FaultRecordDto> ListFaults(int? deviceId) =>
            WithConnection(c => deviceId.HasValue
                ? _repo.ListFaults(c, deviceId.Value)
                : _repo.ListRecentFaults(c, 20)); // 最近故障表（PG-EQP-03）

        public FaultRecordDto AddFault(FaultRecordRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                if (_repo.GetDevice(c, request.DeviceId) == null) throw ApiException.NotFound("设备不存在");
                if (string.IsNullOrWhiteSpace(request.Symptom)) throw ApiException.ValidationFailed("故障现象不能为空（BR-EQP-04）");
                var dto = new FaultRecordDto
                {
                    DeviceId = request.DeviceId, EventId = request.EventId,
                    FTime = request.FTime == default ? DateTime.Now : request.FTime,
                    Symptom = request.Symptom.Trim(),
                    Cause = (request.Cause ?? string.Empty).Trim(),
                    Handle = (request.Handle ?? string.Empty).Trim(),
                    Level = request.Level, Reporter = request.Reporter ?? string.Empty, Status = 0
                };
                dto.Id = _repo.InsertFault(c, tx, dto);
                // 故障单号：WX-yyMM-序号（PG-EQP-03）
                dto.FaultNo = "WX-" + dto.FTime.ToString("yyMM") + "-" + dto.Id.ToString("D2");
                _repo.SetFaultNo(c, tx, dto.Id, dto.FaultNo);
                var dev = _repo.GetDevice(c, request.DeviceId);
                if (dev.Status == DeviceStatus.InUse)
                {
                    dev.Status = DeviceStatus.Repairing;
                    _repo.UpdateDevice(c, tx, dev);
                    _repo.InsertDeviceStatusLog(c, tx, request.DeviceId, (int)DeviceStatus.InUse, (int)DeviceStatus.Repairing, "故障未修复");
                }
                // 回读归一化结果（StatusText/LevelText/EventNo 由仓储统一填充，避免新增接口返回空展示文案）
                return _repo.GetFault(c, dto.Id);
            });

        /// <summary>故障闭环：补录原因/处理结果并更新状态（T6-5-5，BR-EQP-04 完整化）。</summary>
        public FaultRecordDto HandleFault(int id, FaultHandleRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                var fault = _repo.GetFault(c, id) ?? throw ApiException.NotFound("故障单不存在");
                if (fault.Status == 2) throw ApiException.Conflict("故障单已修复，无需重复处理");
                int status = request.Status;
                if (status < 0 || status > 2) throw ApiException.ValidationFailed("故障状态不合法（0 待处理 1 维修中 2 已修复）");
                string handle = (request.Handle ?? string.Empty).Trim();
                if (status == 0) status = string.IsNullOrEmpty(handle) ? 1 : 2; // 未显式指定时按处理结果推断
                if (status == 2 && string.IsNullOrEmpty(handle))
                    throw ApiException.ValidationFailed("处理结果不能为空（BR-EQP-04）");
                _repo.UpdateFaultHandle(c, tx, id, (request.Cause ?? string.Empty).Trim(), handle, status);
                // R5 闭环：故障状态与设备台账状态同步（维修中 ↔ 在用），设备列表随即可见（BR-EQP-01/04）
                var dev = _repo.GetDevice(c, fault.DeviceId);
                if (dev != null)
                {
                    if (status == 1) SyncDeviceToRepairing(c, tx, dev, "故障受理：" + fault.FaultNo);
                    else if (status == 2) RestoreDeviceAfterRepair(c, tx, dev, fault);
                }
                return _repo.GetFault(c, id);
            });

        /// <summary>最后一张未闭环故障单修复后设备回到「在用」（停用/报废设备不动；仍有其它未闭环故障则保持维修中）。</summary>
        private void RestoreDeviceAfterRepair(IDbConnection c, IDbTransaction tx, DeviceDto dev, FaultRecordDto fault)
        {
            if (dev.Status != DeviceStatus.Repairing) return;
            bool otherOpen = _repo.ListFaults(c, dev.Id).Any(f => f.Id != fault.Id && f.Status < 2);
            if (otherOpen) return;
            dev.Status = DeviceStatus.InUse;
            _repo.UpdateDevice(c, tx, dev);
            _repo.InsertDeviceStatusLog(c, tx, dev.Id, (int)DeviceStatus.Repairing, (int)DeviceStatus.InUse,
                "故障已修复：" + (string.IsNullOrEmpty(fault.FaultNo) ? fault.Id.ToString() : fault.FaultNo));
        }

        /// <summary>创建故障单并回填 WX 编号（供报修/不合格转维修复用）。</summary>
        private string CreateFaultCore(IDbConnection c, IDbTransaction tx, int deviceId, string symptom, int level)
        {
            var fault = new FaultRecordDto
            {
                DeviceId = deviceId, FTime = DateTime.Now, Symptom = symptom,
                Cause = string.Empty, Handle = string.Empty, Level = level, Reporter = string.Empty, Status = 0
            };
            fault.Id = _repo.InsertFault(c, tx, fault);
            fault.FaultNo = "WX-" + fault.FTime.ToString("yyMM") + "-" + fault.Id.ToString("D2");
            _repo.SetFaultNo(c, tx, fault.Id, fault.FaultNo);
            return fault.FaultNo;
        }

        /// <summary>
        /// 生成维修工单（T6-5-5，PG-EQP-03，R5）：以 Excel 派工单形式落盘（ClosedXML，可打印/可线下回填）
        /// 并写 t_export_log(module='equipment') 留痕，前端按返回 Id 走通用下载端点另存到本机「文档」。
        /// 业务联动：「待接单」故障单签发工单即推进为「维修中」，设备状态同步为维修中（BR-EQP-01/04），
        /// 线下维修完成后在故障登记页确认「维修完成」→ 故障单已修复 + 设备回到在用，形成闭环。
        /// </summary>
        public EquipmentExportResultDto GenerateFaultWorkOrder(int faultId)
        {
            return WithTransaction((c, tx) =>
            {
                var fault = _repo.GetFault(c, faultId) ?? throw ApiException.NotFound("故障单不存在");
                if (fault.Status == 2) throw ApiException.Conflict("故障单已修复，无需生成维修工单");
                var dev = _repo.GetDevice(c, fault.DeviceId) ?? throw ApiException.NotFound("设备不存在");
                if (fault.Status == 0)
                {
                    // 签发工单即受理：待接单 → 维修中（t_fault_record.status）
                    fault.Status = 1;
                    _repo.UpdateFaultHandle(c, tx, faultId, fault.Cause, fault.Handle ?? string.Empty, 1);
                }
                SyncDeviceToRepairing(c, tx, dev, "生成维修工单：" + fault.FaultNo);

                string fileName = "维修工单_" + (string.IsNullOrEmpty(fault.FaultNo) ? faultId.ToString("D2") : fault.FaultNo)
                                  + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx";
                string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);
                using (var workbook = new XLWorkbook())
                {
                    var sheet = workbook.Worksheets.Add("维修工单");
                    sheet.Cell(1, 1).Value = "设备维修工单";
                    sheet.Range(1, 1, 1, 2).Merge();
                    sheet.Cell(1, 1).Style.Font.Bold = true;
                    sheet.Cell(1, 1).Style.Font.FontSize = 16;
                    sheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    sheet.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    sheet.Row(1).Height = 28;

                    string[][] rows =
                    {
                        new[] { "工单号", fault.FaultNo ?? string.Empty },
                        new[] { "设备编号", dev.DeviceNo ?? ("EQP-" + dev.Id.ToString("0000")) },
                        new[] { "设备名称", dev.Name ?? string.Empty },
                        new[] { "设备类别", dev.TypeName ?? string.Empty },
                        new[] { "安装位置", dev.Location ?? string.Empty },
                        new[] { "设备状态", dev.StatusText ?? string.Empty },
                        new[] { "故障级别", string.IsNullOrEmpty(fault.LevelText) ? (fault.Level == 1 ? "重大" : "一般") : fault.LevelText },
                        new[] { "报修时间", fault.FTime.ToString("yyyy-MM-dd HH:mm") },
                        new[] { "发现人", fault.Reporter ?? string.Empty },
                        new[] { "故障现象", fault.Symptom ?? string.Empty },
                        new[] { "故障原因", fault.Cause ?? string.Empty },
                        new[] { "维修单位（线下填写）", string.Empty },
                        new[] { "维修负责人 / 联系电话", string.Empty },
                        new[] { "维修要求", "按设备台账维保要求处理，完成后回填处理结果并在系统内确认维修状态。" },
                        new[] { "处理结果（线下填写）", string.Empty },
                        new[] { "更换配件 / 维修费用（元）", string.Empty },
                        new[] { "验收人 / 验收日期", string.Empty },
                        new[] { "工单状态", fault.Status == 2 ? "已修复" : "维修中" },
                        new[] { "生成时间", DateTime.Now.ToString("yyyy-MM-dd HH:mm") }
                    };
                    int r = 3;
                    foreach (var row in rows)
                    {
                        var label = sheet.Cell(r, 1);
                        label.Value = row[0];
                        label.Style.Font.Bold = true;
                        label.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        var value = sheet.Cell(r, 2);
                        value.Value = row[1];
                        value.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                        value.Style.Alignment.WrapText = true;
                        if (row[0].StartsWith("故障现象") || row[0].StartsWith("维修要求")) sheet.Row(r).Height = 34;
                        r++;
                    }
                    // 说明行（合并 A/B）
                    sheet.Cell(r + 1, 1).Value = "说明：本工单由系统生成（维修工单 = 故障单号）；线下维修完成后请回到「设备台账 / 故障登记」在最近故障列表中确认「维修完成」，系统据此同步设备状态。";
                    sheet.Range(r + 1, 1, r + 1, 2).Merge();
                    sheet.Cell(r + 1, 1).Style.Font.FontSize = 10;
                    sheet.Cell(r + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#5B6472");
                    sheet.Cell(r + 1, 1).Style.Alignment.WrapText = true;
                    sheet.Row(r + 1).Height = 30;

                    sheet.Column(1).Width = Math.Max(18, VisualWidth("更换配件 / 维修费用（元）") + 4);
                    sheet.Column(2).Width = 56;
                    sheet.SheetView.FreezeRows(2);
                    workbook.SaveAs(filePath);
                }

                int logId = _repo.InsertExportLog(c, tx, "equipment", (int)ExportFormat.Excel, filePath);
                return new EquipmentExportResultDto
                {
                    Id = logId, Module = "equipment", FileName = fileName,
                    Total = 1, Format = "xlsx", ExportedAt = DateTime.Now
                };
            });
        }

        // ===================== 维保单位（BR-EQP-06，T6-5-6 含 PUT） =====================
        public List<VendorDto> ListVendors() =>
            WithConnection(c => _repo.ListVendors(c));

        public VendorDto SaveVendor(int id, VendorRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                if (string.IsNullOrWhiteSpace(request.Name)) throw ApiException.ValidationFailed("维保单位名称不能为空");
                var dto = new VendorDto { Name = request.Name.Trim(), Contact = request.Contact ?? string.Empty, Phone = request.Phone ?? string.Empty };
                if (id > 0)
                {
                    var existing = _repo.GetVendor(c, id) ?? throw ApiException.NotFound("维保单位不存在");
                    dto.Id = existing.Id;
                    _repo.UpdateVendor(c, tx, dto);
                    return dto;
                }
                dto.Id = _repo.InsertVendor(c, tx, dto);
                return dto;
            });

        /// <summary>
        /// 删除维保单位（软删）：BR-EQP-06 强制引用校验——已被保养/年检/自定义记录或支出引用的单位不可删除
        /// （M7 T7-9-7 修复，BUG-001）；未被引用时软删，历史记录的 vendor_id 仍指向该行，执行方名称不丢失。
        /// </summary>
        public void DeleteVendor(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetVendor(c, id) == null) throw ApiException.NotFound("维保单位不存在");
                int referenced = _repo.CountVendorReferences(c, id);
                if (referenced > 0)
                    throw ApiException.Conflict(
                        "该维保单位已被 " + referenced + " 条保养/年检/自定义记录或支出引用，不可删除（BR-EQP-06）；" +
                        "如不再合作请先解除相关记录的关联");
                _repo.SoftDeleteVendor(c, tx, id);
            });

        // ===================== 到期提醒（PG-EQP-04，t_reminder 物化，T6-5-7 / R6 记录化） =====================
        /// <summary>
        /// 提醒列表（R6）：status=-1 全部（含已处理，默认）/0 待处理/1 已处理；
        /// days 窗口对已处理记录不生效（历史记录始终可见，便于追溯与批量删除）。
        /// </summary>
        public List<EquipmentReminderDto> QueryReminders(int days, string type, int status = -1) =>
            WithConnection(c =>
            {
                int window = days <= 0 ? 30 : days;
                MaterializeReminders(c, window);
                var list = _repo.ListReminders(c, NormalizeReminderType(type), status);
                return list.Where(x => x.Status == ReminderStatus.Processed || x.RemainingDays <= window).ToList();
            });

        public ReminderSummaryDto GetReminderSummary() =>
            WithConnection(c =>
            {
                MaterializeReminders(c, 30);
                var list = _repo.ListReminders(c, null, 0); // 统计卡只算未处理
                var summary = new ReminderSummaryDto
                {
                    Within30 = list.Count(x => x.RemainingDays >= 0 && x.RemainingDays <= 30),
                    Within7 = list.Count(x => x.RemainingDays >= 0 && x.RemainingDays <= 7),
                    OverdueUnhandled = list.Count(x => x.RemainingDays < 0)
                };
                summary.MonthHandled = _repo.CountMonthHandled(c);
                return summary;
            });

        /// <summary>处置完成（R6）：status=1 + 处置时间 handled_at + 处置流水（type='handled'，本月已处理计数来源）；记录保留在列表中。</summary>
        public EquipmentReminderDto HandleReminder(int id) =>
            WithTransaction((c, tx) =>
            {
                var reminder = _repo.GetReminder(c, id) ?? throw ApiException.NotFound("提醒不存在");
                if (reminder.Status == ReminderStatus.Processed) throw ApiException.ValidationFailed("提醒已处理，请勿重复操作");
                _repo.HandleReminder(c, tx, id, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _repo.InsertReminderChild(c, tx, "handled", id, DateTime.Now.ToString("s"));
                return _repo.GetReminder(c, id);
            });

        /// <summary>
        /// 批量删除提醒记录（R6，D2/D2b）：仅允许删除「已处理」记录；命中待处理/逾期时**整批拒绝**并返回明细。
        /// 删除为软删（t_reminder.del_flag=1）并写审计，历史催办/处置流水保留。
        /// </summary>
        public ReminderBatchDeleteResultDto BatchDeleteReminders(ReminderBatchDeleteRequest request)
        {
            var ids = (request == null || request.Ids == null ? new List<int>() : request.Ids).Distinct().Where(x => x > 0).ToList();
            if (ids.Count == 0) throw ApiException.ValidationFailed("请选择要删除的提醒记录");
            var result = WithTransaction((c, tx) =>
            {
                var rows = _repo.GetRemindersByIds(c, ids);
                var blocked = rows.Where(r => r.Status != (int)ReminderStatus.Processed)
                    .Select(r => new ReminderDeleteBlockedDto
                    {
                        Id = r.Id, Device = r.DeviceName,
                        Reason = "该提醒仍为待处理/逾期，请先【标记已处理】后再删除"
                    }).ToList();
                if (blocked.Count > 0)
                    return new ReminderBatchDeleteResultDto { Deleted = 0, Blocked = blocked }; // 整批拒绝（D2b）
                var existing = rows.Select(r => r.Id).ToList();
                if (existing.Count == 0) throw ApiException.NotFound("提醒记录不存在或已删除");
                _repo.SoftDeleteReminders(c, tx, existing);
                return new ReminderBatchDeleteResultDto { Deleted = existing.Count, Blocked = new List<ReminderDeleteBlockedDto>() };
            });
            // 审计在业务事务提交后单独写入（AuditService 自建连接 + 事务，事务内写会与 SQLite 写锁冲突）
            if (result.Deleted > 0)
            {
                _audit.Write("EQP_REMINDER_DELETE", "t_reminder", string.Join(",", ids),
                    "到期提醒记录批量删除（软删，" + result.Deleted + " 条；仅已处理记录可删）", module: "设备台账");
            }
            return result;
        }

        /// <summary>
        /// 指派责任班组（R7）：管理员可为单条提醒灵活调配部门；team 传空 → 恢复默认规则（逾期→物业办 / 其余→工程部）。
        /// 指派写入 t_reminder.responsible_team，物化与列表读取时人工值优先。
        /// </summary>
        public EquipmentReminderDto SaveReminderTeam(int id, ReminderTeamRequest request)
        {
            string team = request == null || request.Team == null ? string.Empty : request.Team.Trim();
            if (team.Length > 20) throw ApiException.ValidationFailed("责任班组名称过长（≤20 字）");
            var dto = WithTransaction((c, tx) =>
            {
                var reminder = _repo.GetReminder(c, id) ?? throw ApiException.NotFound("提醒不存在");
                _repo.UpdateReminderTeam(c, tx, id, team);
                return _repo.GetReminder(c, id);
            });
            _audit.Write("EQP_REMINDER_TEAM", "t_reminder", id.ToString(),
                string.IsNullOrEmpty(team) ? "责任班组恢复默认规则" : "责任班组指派为：" + team, module: "设备台账");
            return dto;
        }

        /// <summary>物化四类到期提醒到 t_reminder：同 type+target_id 且 status=0 已存在则更新 due_at（登记新记录后到期日顺延），否则在窗口内插入。</summary>
        private void MaterializeReminders(IDbConnection c, int days)
        {
            DateTime today = DateTime.Today;
            DateTime horizon = today.AddDays(days);
            foreach (var row in _repo.ListMaintenanceDueCandidates(c))
            {
                int cycle = row.Cycle > 0 ? row.Cycle : 30;
                // R6：自定义下次保养日期优先；空则按"最近保养/投运 + 周期"派生
                DateTime due = ParseDate(row.NextMaintenanceOverride)
                               ?? (ParseDate(row.LastDate) ?? ParseDate(row.EnableDate) ?? today).AddDays(cycle);
                UpsertReminder(c, null, "maint_due", row.DeviceId, due, today, horizon);
            }
            foreach (var row in _repo.ListInspectionDueCandidates(c))
            {
                // BR-EQP-02：年检按年度
                DateTime due = (ParseDate(row.LastDate) ?? ParseDate(row.EnableDate) ?? today).AddYears(1);
                UpsertReminder(c, null, "inspect_due", row.DeviceId, due, today, horizon);
            }
            foreach (var row in _repo.ListWarrantyDueCandidates(c))
            {
                if (DateTime.TryParse(row.DueDate, out DateTime due))
                    UpsertReminder(c, null, "warranty_due", row.DeviceId, due, today, horizon);
            }
            foreach (var row in _repo.ListContractDueCandidates(c))
            {
                if (DateTime.TryParse(row.DueDate, out DateTime due))
                    UpsertReminder(c, null, "contract_due", row.DeviceId, due, today, horizon);
            }
        }

        private void UpsertReminder(IDbConnection c, IDbTransaction tx, string type, int deviceId, DateTime due, DateTime today, DateTime horizon)
        {
            var existing = _repo.FindActiveReminder(c, tx, type, deviceId);
            if (existing != null)
            {
                // 已存在待处理提醒：一律同步最新 due_at（新增保养/年检记录后到期日自动顺延，逾期状态随之解除）
                if (!DateTime.TryParse(existing.DueAt, out DateTime oldDue) || oldDue.Date != due.Date)
                    _repo.UpdateReminderDueAt(c, tx, existing.Id, due.ToString("yyyy-MM-dd"));
                return;
            }
            // R5 闭环：本期已处置（人工「处置」或保养/年检登记自动关闭）不再重复物化同一周期，
            // 直到推导出的到期日随新登记顺延（due 变化）才重新生成下一周期提醒。
            DateTime? handledDue = ParseDate(_repo.FindLastHandledReminderDueAt(c, type, deviceId));
            if (handledDue.HasValue && handledDue.Value.Date == due.Date) return;
            if (due <= horizon) _repo.InsertReminder(c, tx, type, deviceId, due.ToString("yyyy-MM-dd"));
        }

        /// <summary>SQLite 文本日期解析（yyyy-MM-dd / yyyy-MM-dd HH:mm:ss），失败返回 null。</summary>
        private static DateTime? ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            DateTime value;
            return DateTime.TryParse(text.Trim(), out value) ? value : (DateTime?)null;
        }

        // ===================== 自定义类型记录（t_device_custom_record，R6 登记类型解耦，BR-EQP-07） =====================
        public List<DeviceCustomRecordDto> ListCustomRecords(int deviceId) =>
            WithConnection(c =>
            {
                if (_repo.GetDevice(c, deviceId) == null) throw ApiException.NotFound("设备不存在");
                return _repo.ListCustomRecords(c, deviceId);
            });

        /// <summary>自定义类型名称候选（历史自定义登记类型 + 已录入记录类型，去重）。</summary>
        public List<string> ListCustomRecordTypes() =>
            WithConnection(c => _repo.ListCustomRecordTypeNames(c));

        public DeviceCustomRecordDto SaveCustomRecord(int id, DeviceCustomRecordRequest request) =>
            WithTransaction((c, tx) =>
            {
                if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
                if (string.IsNullOrWhiteSpace(request.TypeName)) throw ApiException.ValidationFailed("自定义类型名称不能为空");
                if (request.Cost.HasValue && request.Cost.Value < 0) throw ApiException.ValidationFailed("费用不能为负数");
                var dto = new DeviceCustomRecordDto
                {
                    DeviceId = request.DeviceId,
                    TypeName = request.TypeName.Trim(),
                    RDate = request.RDate == default ? DateTime.Today : request.RDate,
                    Content = (request.Content ?? string.Empty).Trim(),
                    Result = (request.Result ?? string.Empty).Trim(),
                    Cost = request.Cost, VendorId = request.VendorId,
                    Operator = (request.Operator ?? string.Empty).Trim()
                };
                if (id > 0)
                {
                    var existing = _repo.GetCustomRecord(c, id) ?? throw ApiException.NotFound("自定义类型记录不存在");
                    dto.Id = id;
                    dto.DeviceId = existing.DeviceId; // 记录不可跨设备搬迁
                    _repo.UpdateCustomRecord(c, tx, dto);
                    return _repo.GetCustomRecord(c, id);
                }
                if (_repo.GetDevice(c, dto.DeviceId) == null) throw ApiException.NotFound("设备不存在");
                dto.Id = _repo.InsertCustomRecord(c, tx, dto);
                return _repo.GetCustomRecord(c, dto.Id);
            });

        public void DeleteCustomRecord(int id) =>
            WithTransaction((c, tx) =>
            {
                if (_repo.GetCustomRecord(c, id) == null) throw ApiException.NotFound("自定义类型记录不存在");
                _repo.SoftDeleteCustomRecord(c, tx, id);
            });

        private static string NormalizeReminderType(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;
            switch (type.Trim().ToLowerInvariant())
            {
                case "all": return null;
                case "maintenance": case "maint": return "maint_due";
                case "inspection": case "inspect": return "inspect_due";
                case "warranty": return "warranty_due";
                case "contract": return "contract_due";
                default: return type.Trim();
            }
        }

        // ===================== 导出（T6-5-2，仿 BaseInfoController exports + t_export_log 留痕） =====================
        /// <summary>
        /// 设备台账导出（T6-5-2）：生成 Excel（ClosedXML），列宽按内容自适应 + 最小宽度兜底，
        /// 避免导出后单元格内容被遮挡；文件落 DbConfig.ExportDirectory 并写 t_export_log(module='equipment') 留痕，
        /// 前端按返回的 Id 调用通用下载端点另存到本机「文档」目录。
        /// </summary>
        public EquipmentExportResultDto ExportDevices(DeviceQueryRequest filter)
        {
            var query = filter ?? new DeviceQueryRequest();
            query.PageIndex = 1;
            query.PageSize = 100000; // 导出全量（分页仅用于列表展示）
            var page = WithConnection(c => _repo.QueryDevices(c, query, out int total));
            var items = page.Items ?? new List<DeviceDto>();

            string fileName = "设备台账_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx";
            string filePath = Path.Combine(DbConfig.ExportDirectory, fileName);

            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("设备台账");
                string[] headers = { "设备编号", "设备名称", "设备类别", "位置", "品牌型号", "投运日期", "状态", "下次保养", "质保到期", "合同到期", "保养周期(天)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var head = sheet.Cell(1, i + 1);
                    head.Value = headers[i];
                    head.Style.Font.Bold = true;
                    head.Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F7FB");
                    head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                }
                sheet.Row(1).Height = 22;

                int r = 2;
                var contentUnits = new double[headers.Length];
                foreach (var d in items)
                {
                    string[] values =
                    {
                        d.DeviceNo ?? string.Empty,
                        d.Name ?? string.Empty,
                        d.TypeName ?? string.Empty,
                        d.Location ?? string.Empty,
                        d.BrandModel ?? string.Empty,
                        d.EnableDate.HasValue ? d.EnableDate.Value.ToString("yyyy-MM-dd") : string.Empty,
                        d.StatusText ?? string.Empty,
                        d.NextMaintenance.HasValue ? d.NextMaintenance.Value.ToString("yyyy-MM-dd") : string.Empty,
                        d.WarrantyEnd ?? string.Empty,
                        d.ContractEnd ?? string.Empty,
                        d.MaintenanceCycleOverride.HasValue ? d.MaintenanceCycleOverride.Value.ToString() : "类型默认"
                    };
                    for (int c = 0; c < values.Length; c++)
                    {
                        sheet.Cell(r, c + 1).Value = values[c];
                        double units = VisualWidth(values[c]);
                        if (units > contentUnits[c]) contentUnits[c] = units;
                    }
                    r++;
                }

                // 列宽：内容自适应 + 中文按 2 字符宽度的保守兜底（原 16 字符固定宽 / 纯自适应都会遮挡中文内容）
                for (int c = 1; c <= headers.Length; c++)
                {
                    sheet.Column(c).AdjustToContents();
                    double minWidth = Math.Max(VisualWidth(headers[c - 1]) + 6, contentUnits[c - 1] + 4);
                    if (sheet.Column(c).Width < minWidth) sheet.Column(c).Width = minWidth;
                }
                sheet.Columns().Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                sheet.SheetView.FreezeRows(1); // 表头冻结，滚动时列名不丢失
                workbook.SaveAs(filePath);
            }

            int logId = WithTransaction((c, tx) => _repo.InsertExportLog(c, tx, "equipment", (int)ExportFormat.Excel, filePath));
            return new EquipmentExportResultDto
            {
                Id = logId, Module = "equipment", FileName = fileName,
                Total = items.Count, Format = "xlsx", ExportedAt = DateTime.Now
            };
        }

        /// <summary>不合格判定：精确匹配"不合格"（Result=="不合格"，杜绝 Contains("否") 把"合格（否决项：无）"误判）。</summary>
        private static bool IsNotQualified(string result)
        {
            return result != null && result.Trim() == "不合格";
        }

        private TResult WithConnection<TResult>(Func<IDbConnection, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            {
                return action(connection);
            }
        }

        private TResult WithTransaction<TResult>(Func<IDbConnection, IDbTransaction, TResult> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                var result = action(connection, transaction);
                transaction.Commit();
                return result;
            }
        }

        private void WithTransaction(Action<IDbConnection, IDbTransaction> action)
        {
            using (IDbConnection connection = _connectionFactory.OpenConnection())
            using (IDbTransaction transaction = connection.BeginTransaction())
            {
                action(connection, transaction);
                transaction.Commit();
            }
        }
    }
}

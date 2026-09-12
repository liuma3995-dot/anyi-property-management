using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Equipment;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>设备资产台账端点（M6 D6-5，UC-EQP-001~007 + BR-EQP-01~06）。</summary>
    [RoutePrefix("api/v1/equipment")]
    public class EquipmentController : ApiController
    {
        private readonly EquipmentService _service;

        public EquipmentController()
        {
            _service = new EquipmentService();
        }

        // ---------- 设备类型（T6-5-1：GET/POST/PUT） ----------
        [HttpGet] [Route("types")]
        public ApiResponse<List<DeviceTypeDto>> ListTypes() => ApiResponse<List<DeviceTypeDto>>.Ok(_service.ListDeviceTypes());

        [HttpPost] [Route("types")]
        public ApiResponse<DeviceTypeDto> CreateType(DeviceTypeRequest request) =>
            ApiResponse<DeviceTypeDto>.Ok(_service.SaveDeviceType(0, request));

        [HttpPut] [Route("types/{id:int}")]
        public ApiResponse<DeviceTypeDto> UpdateType(int id, DeviceTypeRequest request) =>
            ApiResponse<DeviceTypeDto>.Ok(_service.SaveDeviceType(id, request));

        [HttpDelete] [Route("types/{id:int}")]
        public ApiResponse<object> DeleteType(int id) { _service.DeleteDeviceType(id); return ApiResponse<object>.Ok(null); }

        // ---------- 保养/年检登记类型（PG-EQP-02 登记类型下拉：自定义新增 / 删除） ----------
        [HttpGet] [Route("maintain-types")]
        public ApiResponse<List<MaintainTypeDto>> ListMaintainTypes(bool includeDisabled = false) =>
            ApiResponse<List<MaintainTypeDto>>.Ok(_service.ListMaintainTypes(includeDisabled));

        // ---------- 设备台账（T6-5-2 + 统计/导出） ----------
        [HttpGet] [Route("devices")]
        public ApiResponse<PageResult<DeviceDto>> QueryDevices([FromUri] DeviceQueryRequest request) =>
            ApiResponse<PageResult<DeviceDto>>.Ok(_service.QueryDevices(request ?? new DeviceQueryRequest()));

        [HttpGet] [Route("devices/summary")]
        public ApiResponse<DeviceSummaryDto> GetDeviceSummary() => ApiResponse<DeviceSummaryDto>.Ok(_service.GetDeviceSummary());

        [HttpGet] [Route("devices/{id:int}")]
        public ApiResponse<DeviceDto> GetDevice(int id) => ApiResponse<DeviceDto>.Ok(_service.GetDevice(id));

        [HttpPost] [Route("devices")]
        public ApiResponse<DeviceDto> CreateDevice(DeviceRequest request) => ApiResponse<DeviceDto>.Ok(_service.SaveDevice(0, request));

        [HttpPut] [Route("devices/{id:int}")]
        public ApiResponse<DeviceDto> UpdateDevice(int id, DeviceRequest request) => ApiResponse<DeviceDto>.Ok(_service.SaveDevice(id, request));

        [HttpDelete] [Route("devices/{id:int}")]
        public ApiResponse<object> DeleteDevice(int id) { _service.DeleteDevice(id); return ApiResponse<object>.Ok(null); }

        /// <summary>状态变更留痕（BR-EQP-01，设备详情浮层用）。</summary>
        [HttpGet] [Route("devices/{id:int}/status-logs")]
        public ApiResponse<List<DeviceStatusLogDto>> ListStatusLogs(int id)
        {
            _service.GetDevice(id); // 404 兜底：软删/不存在设备不返回留痕
            return ApiResponse<List<DeviceStatusLogDto>>.Ok(_service.ListDeviceStatusLogs(id));
        }

        [HttpPost] [Route("devices/{id:int}/status")]
        public ApiResponse<DeviceDto> ChangeStatus(int id, DeviceStatusRequest request) =>
            ApiResponse<DeviceDto>.Ok(_service.ChangeDeviceStatus(id, request));

        /// <summary>设备台账导出：CSV 文本 + t_export_log(module='equipment') 留痕（T6-5-2）。</summary>
        [HttpPost] [Route("devices/exports")]
        public ApiResponse<EquipmentExportResultDto> ExportDevices(DeviceQueryRequest request) =>
            ApiResponse<EquipmentExportResultDto>.Ok(_service.ExportDevices(request));

        // ---------- 保养/年检（T6-5-3/4） ----------
        [HttpGet] [Route("devices/{id:int}/maintenance")]
        public ApiResponse<List<MaintenanceRecordDto>> ListMaintenance(int id) => ApiResponse<List<MaintenanceRecordDto>>.Ok(_service.ListMaintenance(id));

        [HttpPost] [Route("devices/{id:int}/maintenance")]
        public ApiResponse<MaintenanceRecordDto> AddMaintenance(int id, MaintenanceRecordRequest request)
        {
            if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
            request.DeviceId = id;
            return ApiResponse<MaintenanceRecordDto>.Ok(_service.AddMaintenance(request));
        }

        [HttpGet] [Route("devices/{id:int}/inspection")]
        public ApiResponse<List<InspectionRecordDto>> ListInspection(int id) => ApiResponse<List<InspectionRecordDto>>.Ok(_service.ListInspection(id));

        [HttpPost] [Route("devices/{id:int}/inspection")]
        public ApiResponse<InspectionRecordDto> AddInspection(int id, InspectionRecordRequest request)
        {
            if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
            request.DeviceId = id;
            return ApiResponse<InspectionRecordDto>.Ok(_service.AddInspection(request));
        }

        // ---------- 故障（T6-5-5，全局列表 + 闭环） ----------
        [HttpGet] [Route("devices/{id:int}/faults")]
        public ApiResponse<List<FaultRecordDto>> ListFaults(int id) => ApiResponse<List<FaultRecordDto>>.Ok(_service.ListFaults(id));

        [HttpPost] [Route("devices/{id:int}/faults")]
        public ApiResponse<FaultRecordDto> AddFault(int id, FaultRecordRequest request)
        {
            if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
            request.DeviceId = id;
            return ApiResponse<FaultRecordDto>.Ok(_service.AddFault(request));
        }

        /// <summary>全局最近故障（PG-EQP-03 右侧"进行中/最近故障"表）。</summary>
        [HttpGet] [Route("faults")]
        public ApiResponse<List<FaultRecordDto>> ListRecentFaults() => ApiResponse<List<FaultRecordDto>>.Ok(_service.ListFaults(null));

        /// <summary>故障闭环：补录原因/处理结果并更新状态（0 待处理 1 维修中 2 已修复）。</summary>
        [HttpPut] [Route("faults/{id:int}/handle")]
        public ApiResponse<FaultRecordDto> HandleFault(int id, FaultHandleRequest request) =>
            ApiResponse<FaultRecordDto>.Ok(_service.HandleFault(id, request));

        /// <summary>
        /// 生成维修工单（R5，T6-5-5）：Excel 派工单落盘 + t_export_log 留痕（前端按 Id 走通用下载端点另存），
        /// 同时把「待接单」故障推进为「维修中」并同步设备状态。
        /// </summary>
        [HttpPost] [Route("faults/{id:int}/work-order")]
        public ApiResponse<EquipmentExportResultDto> GenerateWorkOrder(int id) =>
            ApiResponse<EquipmentExportResultDto>.Ok(_service.GenerateFaultWorkOrder(id));

        // ---------- 维保单位（T6-5-6：GET/POST/PUT） ----------
        [HttpGet] [Route("vendors")]
        public ApiResponse<List<VendorDto>> ListVendors() => ApiResponse<List<VendorDto>>.Ok(_service.ListVendors());

        [HttpPost] [Route("vendors")]
        public ApiResponse<VendorDto> CreateVendor(VendorRequest request) => ApiResponse<VendorDto>.Ok(_service.SaveVendor(0, request));

        [HttpPut] [Route("vendors/{id:int}")]
        public ApiResponse<VendorDto> UpdateVendor(int id, VendorRequest request) => ApiResponse<VendorDto>.Ok(_service.SaveVendor(id, request));

        [HttpDelete] [Route("vendors/{id:int}")]
        public ApiResponse<object> DeleteVendor(int id) { _service.DeleteVendor(id); return ApiResponse<object>.Ok(null); }

        // ---------- 到期提醒（T6-5-7：物化 + 催办 + 处置 + 统计 + 范围筛选） ----------
        /// <summary>提醒列表：status=-1 全部（含已处理，默认）/0 待处理/1 已处理（R6）。</summary>
        [HttpGet] [Route("reminders")]
        public ApiResponse<List<EquipmentReminderDto>> QueryReminders(int days = 30, string type = null, int status = -1) =>
            ApiResponse<List<EquipmentReminderDto>>.Ok(_service.QueryReminders(days, type, status));

        [HttpGet] [Route("reminders/summary")]
        public ApiResponse<ReminderSummaryDto> GetReminderSummary() => ApiResponse<ReminderSummaryDto>.Ok(_service.GetReminderSummary());

        /// <summary>处置完成（R6）：状态置已处理 + 记录处置时间，记录保留在列表中（不再"移除"）。</summary>
        [HttpPost] [Route("reminders/{id:int}/handle")]
        public ApiResponse<EquipmentReminderDto> HandleReminder(int id) => ApiResponse<EquipmentReminderDto>.Ok(_service.HandleReminder(id));

        /// <summary>批量删除提醒记录（R6）：仅已处理可删；命中待处理/逾期整批拒绝并返回明细。</summary>
        [HttpPost] [Route("reminders/batch-delete")]
        public ApiResponse<ReminderBatchDeleteResultDto> BatchDeleteReminders(ReminderBatchDeleteRequest request) =>
            ApiResponse<ReminderBatchDeleteResultDto>.Ok(_service.BatchDeleteReminders(request));

        /// <summary>责任班组指派（R7）：按行自定义调配部门；team 传空 = 恢复默认规则。</summary>
        [HttpPost] [Route("reminders/{id:int}/team")]
        public ApiResponse<EquipmentReminderDto> SaveReminderTeam(int id, ReminderTeamRequest request) =>
            ApiResponse<EquipmentReminderDto>.Ok(_service.SaveReminderTeam(id, request));

        // ---------- 自定义类型记录（R6 登记类型解耦：设备查看浮层"自定义类型记录"页签） ----------
        /// <summary>自定义类型名称候选（历史自定义登记类型 + 已录入记录类型，去重）。</summary>
        [HttpGet] [Route("custom-record-types")]
        public ApiResponse<List<string>> ListCustomRecordTypes() =>
            ApiResponse<List<string>>.Ok(_service.ListCustomRecordTypes());

        [HttpGet] [Route("devices/{id:int}/custom-records")]
        public ApiResponse<List<DeviceCustomRecordDto>> ListCustomRecords(int id) =>
            ApiResponse<List<DeviceCustomRecordDto>>.Ok(_service.ListCustomRecords(id));

        [HttpPost] [Route("devices/{id:int}/custom-records")]
        public ApiResponse<DeviceCustomRecordDto> CreateCustomRecord(int id, DeviceCustomRecordRequest request)
        {
            if (request == null) throw ApiException.ValidationFailed("请求体不能为空");
            request.DeviceId = id;
            return ApiResponse<DeviceCustomRecordDto>.Ok(_service.SaveCustomRecord(0, request));
        }

        [HttpPut] [Route("custom-records/{id:int}")]
        public ApiResponse<DeviceCustomRecordDto> UpdateCustomRecord(int id, DeviceCustomRecordRequest request) =>
            ApiResponse<DeviceCustomRecordDto>.Ok(_service.SaveCustomRecord(id, request));

        [HttpDelete] [Route("custom-records/{id:int}")]
        public ApiResponse<object> DeleteCustomRecord(int id) { _service.DeleteCustomRecord(id); return ApiResponse<object>.Ok(null); }
    }
}

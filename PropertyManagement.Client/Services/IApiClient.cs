using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Contract.Health;
using PropertyManagement.Contract.Org;
using PropertyManagement.Contract.PhoneBook;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Emergency;
using PropertyManagement.Contract.Equipment;

namespace PropertyManagement.Client.Services
{
    /// <summary>API 调用异常：Code 为契约错误码（ErrorCode）。</summary>
    public class ApiClientException : Exception
    {
        public int Code { get; }

        public ApiClientException(int code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>前端 API 客户端契约（M3：登录/会话/仪表盘；业务端点随切片扩展）。</summary>
    public interface IApiClient
    {
        bool IsMock { get; }

        Task<HealthResponse> GetHealthAsync();

        Task<LoginResult> LoginAsync(LoginRequest request);

        Task LogoutAsync();

        Task ChangePasswordAsync(ChangePasswordRequest request);

        Task<DashboardDto> GetDashboardAsync();

        /// <summary>
        /// 仪表盘统计：period = `yyyy-MM`（按月）或 `yyyy`（按年，CHG-v1.2.0-26）；空 = 当前月。
        /// 返回体 <c>Annual</c> 告知实际生效粒度。
        /// </summary>
        Task<DashboardDto> GetDashboardAsync(string period);

        // ==================== R17 顶部栏与仪表盘交互 ====================

        /// <summary>待办中心（顶部铃铛通知中心，与仪表盘待办同一数据源）。</summary>
        Task<TodoCenterDto> GetTodosAsync(int limit = 20);

        /// <summary>顶栏全局搜索（房产/业主/设备/电话/员工/纠纷，按模块分组）。</summary>
        Task<GlobalSearchResultDto> SearchAsync(string keyword);

        /// <summary>管理员个人信息（顶栏下拉 → 个人信息设置）。</summary>
        Task<UserProfileDto> GetProfileAsync();

        Task<UserProfileDto> UpdateProfileAsync(UserProfileRequest request);

        // ==================== M4 财务收费（PG-FIN-01~08） ====================
        Task<List<ChargeItemDto>> GetChargeItemsAsync(string keyword = null, string category = null);
        Task<List<DictItemDto>> GetDictItemsAsync(string typeCode);
        Task<DictItemDto> CreateDictItemAsync(string typeCode, DictItemRequest request);
        Task<ChargeItemDto> CreateChargeItemAsync(ChargeItemRequest request);
        Task<ChargeItemDto> UpdateChargeItemAsync(int id, ChargeItemRequest request);
        Task DeleteChargeItemAsync(int id);

        // ---------- CHG-v1.1.2-26：收费项目「价目表 + 计量变量」 ----------
        Task<List<ChargeStandardDto>> GetChargeStandardsAsync(string keyword = null, string category = null, bool includeDisabled = true);
        Task<ChargeStandardDto> GetChargeStandardAsync(int id);
        Task<ChargeStandardDto> CreateChargeStandardAsync(ChargeStandardRequest request);
        Task<ChargeStandardDto> UpdateChargeStandardAsync(int id, ChargeStandardRequest request);
        Task DeleteChargeStandardAsync(int id);
        Task<ChargeStandardDto> ToggleChargeStandardAsync(int id, int status);
        Task<ChargeStandardSpecDto> CreateChargeSpecAsync(int standardId, ChargeStandardSpecRequest request);
        Task<ChargeStandardSpecDto> UpdateChargeSpecAsync(int id, ChargeStandardSpecRequest request);
        Task DeleteChargeSpecAsync(int id);
        Task ToggleChargeSpecAsync(int id, int status);
        Task<List<ChargeVariableDto>> GetChargeVariablesAsync(string keyword = null, bool includeDisabled = true);
        Task<ChargeVariableDto> CreateChargeVariableAsync(ChargeVariableRequest request);
        Task<ChargeVariableDto> UpdateChargeVariableAsync(int id, ChargeVariableRequest request);
        Task DeleteChargeVariableAsync(int id);
        Task ToggleChargeVariableAsync(int id, int status);
        /// <summary>出账试算（只读）：按价目表规格自动匹配并试算金额。</summary>
        Task<BillPreviewResult> PreviewBillsAsync(BillPreviewRequest request);
        /// <summary>CHG-v1.1.2-33：收费项目清单导出（PDF / Excel），返回导出日志（含文件路径）。</summary>
        Task<ReportLogDto> ExportChargeItemsAsync(ChargeItemExportRequest request);
        Task<List<BillingCycleDto>> GetCyclesAsync();
        Task<BillingCycleDto> CreateCycleAsync(BillingCycleRequest request);
        /// <summary>CHG-v1.1.0-17：删除自定义计费周期（内置周期或被账单引用的周期由服务端拦截）。</summary>
        Task DeleteCycleAsync(int id);
        Task<BillGenerateLogDto> GenerateBillsAsync(BillGenerateRequest request);

        /// <summary>CHG-v1.1.0-10：生成账单「缴费对象」候选查询（只读，供选择器使用）。</summary>
        Task<BillObjectQueryResult> QueryBillObjectsAsync(BillObjectQueryRequest request);

        /// <summary>CHG-v1.1.0-10：草稿批次既有缴费对象（批次编辑回填）。</summary>
        Task<BillObjectSelectionDto> GetBatchBillObjectsAsync(int batchId);
        Task<BillGenerateLogDto> PublishBillsAsync(BillPublishRequest request);
        Task<BillGenerateLogDto> RetryFailuresAsync(int batchId);
        Task<List<BillBatchDto>> GetGenerateLogsAsync();
        Task DeleteBillBatchAsync(int batchId);
        Task<List<BillFailureDto>> GetFailuresAsync(int batchId);
        Task<PageResult<BillListItemDto>> QueryBillsAsync(BillQueryRequest request);
        Task<PageResult<ArrearDto>> QueryArrearsAsync(BillQueryRequest request);
        Task RecordRemindAsync(ArrearRemindRequest request);
        /// <summary>单张账单删除（口径同批次删除：下游记录同步不再显示）。欠费台账已改用「移出台账」。</summary>
        Task DeleteArrearAsync(int billId);

        /// <summary>CHG-v1.1.2-03：移出台账（只影响欠费台账可见性，可恢复）。</summary>
        Task<int> DismissArrearsAsync(ArrearDismissRequest request);

        /// <summary>CHG-v1.1.2-03：已移出台账的记录。</summary>
        Task<List<ArrearDismissDto>> QueryDismissedArrearsAsync();

        /// <summary>CHG-v1.1.2-03：恢复台账。</summary>
        Task<int> RestoreArrearsAsync(ArrearDismissRequest request);
        Task<PaymentStatisticsDto> GetPaymentStatisticsAsync();
        Task<PaymentDto> CreatePaymentAsync(PaymentCreateRequest request);

        /// <summary>CHG-v1.1.0-12：统一收款（多账单一次收款，共享流水号）。</summary>
        Task<PaymentBatchResultDto> CreateBatchPaymentAsync(PaymentBatchCreateRequest request);
        Task<PaymentDto> GetPaymentAsync(int id);
        // CHG-v1.1.0-15：收据号前后端下线 —— 原「收据查询 / 收据打印」客户端接口一并移除，
        // 收款凭据改由 ExportReceiptTemplateAsync（收据打印模板）承载。
        Task<PreDepositDto> GetPreDepositAsync(int ownerId);
        Task<RefundAdjustmentDto> CreateRefundAsync(RefundAdjustmentRequest request);

        /// <summary>CHG-v1.1.0-13：批量退款/减免/调整（多张账单各登记一条记录）。</summary>
        Task<RefundBatchResultDto> CreateRefundBatchAsync(RefundAdjustmentRequest request);
        Task<PageResult<RefundAdjustmentDto>> QueryRefundsAsync(PageRequest request);
        Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync();

        /// <summary>新增支出分类（登记支出表单内「＋新增分类」）。</summary>
        Task<ExpenseCategoryDto> CreateExpenseCategoryAsync(ExpenseCategoryRequest request);

        /// <summary>删除支出分类（被支出记录引用时服务端拒绝并提示改用停用）。</summary>
        Task DeleteExpenseCategoryAsync(int id);
        Task<ExpenseDto> CreateExpenseAsync(ExpenseCreateRequest request);
        Task<PageResult<ExpenseDto>> QueryExpensesAsync(PageRequest request);
        Task DeleteExpenseAsync(int id);

        /// <summary>支出记录批量删除（v1.1.0-⑤）：软删留痕（BR-FIN-10）。</summary>
        Task<RecordBatchDeleteResultDto> BatchDeleteExpensesAsync(RecordBatchDeleteRequest request);
        Task<PageResult<LedgerEntryDto>> GetLedgerAsync(LedgerQueryRequest request);

        /// <summary>CHG-v1.1.2-05：收支明细流水导出（Excel/PDF）。</summary>
        Task<ReportLogDto> ExportLedgerAsync(LedgerExportRequest request);

        /// <summary>
        /// CHG-v1.2.0-25：支出登记明细导出（PDF）。
        /// 请求体带页面当前筛选条件（关键字 / 类别 / 状态），返回导出留痕后经 DownloadReportFileAsync 下载。
        /// </summary>
        Task<ReportLogDto> ExportExpensesAsync(ExpenseExportRequest request);

        Task<FinancialReportDto> GetFinancialReportAsync(FinancialReportQueryRequest request);
        Task<ReportLogDto> ExportReportAsync(ReportExportRequest request);

        /// <summary>
        /// CHG-v1.3.1-05：财务报表「报表与导出留痕」清单
        /// （报表留痕 + 导出留痕 + 未被任何留痕引用的孤立生成文件；用于年度/季度/月度清算与缓存清理）。
        /// </summary>
        Task<List<ExportTraceDto>> ListExportTracesAsync();

        /// <summary>CHG-v1.3.1-05：清理所选留痕 —— 留痕软删 + 物理删除服务端生成文件（不涉及账目）。</summary>
        Task<ExportTraceDeleteResultDto> DeleteExportTracesAsync(ExportTraceDeleteRequest request);

        /// <summary>CHG-v1.1.0-14：导出收据打印模板（含逐项收款明细）。</summary>
        Task<ReportLogDto> ExportReceiptTemplateAsync(ReceiptTemplateRequest request);

        /// <summary>
        /// CHG-v1.1.2-41：导出「退款/减免/调整单据」PDF（按单据主键，服务端回查金额与账单口径）。
        /// </summary>
        Task<ReportLogDto> ExportRefundRecordAsync(RefundRecordExportRequest request);

        /// <summary>
        /// CHG-v1.2.0-13：业主档案导出 PDF（本年度缴费概况 + 账单明细 + 收款明细）。
        /// 年度为空 = 当前年度；返回导出留痕记录，再由 DownloadReportFileAsync 下载。
        /// </summary>
        Task<ReportLogDto> ExportOwnerProfilePdfAsync(int ownerId, int? year);

        /// <summary>
        /// CHG-v1.2.0-17：业主档案导出 PDF —— **全部业主**（汇总表 + 逐户缴费概况与缴费明细）。
        /// 年度为空 = 当前年度；返回导出留痕记录，再由 DownloadReportFileAsync 下载。
        /// </summary>
        Task<ReportLogDto> ExportAllOwnerProfilesPdfAsync(int? year);

        /// <summary>
        /// CHG-v1.2.0-31：收款登记「应缴明细」批量删除已结清记录（归档语义）。
        /// 只从应缴明细移除，账单与收款/退款/财报/流水/业主档案数据不变；
        /// 未结清记录由服务端逐条拒绝并在结果里回报原因。
        /// </summary>
        Task<BillArchiveResultDto> ArchiveSettledBillsAsync(SettledBillArchiveRequest request);

        /// <summary>
        /// CHG-v1.2.0-32：收款登记「应缴明细」导出 PDF（按缴费对象，含已结清记录）。
        /// 返回导出留痕记录，再由 DownloadReportFileAsync 下载。
        /// </summary>
        Task<ReportLogDto> ExportArrearDetailsPdfAsync(ArrearDetailExportRequest request);

        // ==================== M5 基础信息与导入（PG-INF-01~05） ====================
        Task<List<CommunityDto>> GetCommunitiesAsync(string keyword = null);
        Task<List<BuildingDto>> GetBuildingsAsync(int? communityId = null);
        Task<List<UnitDto>> GetUnitsAsync(int? buildingId = null);
        Task<BuildingDto> CreateBuildingAsync(BuildingRequest request);
        Task<UnitDto> CreateUnitAsync(UnitRequest request);
        Task DeleteBuildingAsync(int id);
        Task DeleteUnitAsync(int id);
        Task<PageResult<PropertyDto>> QueryPropertiesAsync(BaseInfoQueryRequest request);
        Task<PropertyDto> CreatePropertyAsync(PropertyRequest request);
        Task<PropertyDto> UpdatePropertyAsync(int id, PropertyRequest request);
        Task DeletePropertyAsync(int id);
        Task<PageResult<OwnerDto>> QueryOwnersAsync(BaseInfoQueryRequest request);
        Task<OwnerDto> GetOwnerAsync(int id);
        Task<List<BaseChangeLogDto>> GetOwnerChangeLogsAsync(int id);
        Task<List<OwnerPropertyRelationDto>> GetOwnerRelationsAsync(int id);
        Task<OwnerDto> CreateOwnerAsync(OwnerRequest request);
        Task<OwnerDto> UpdateOwnerAsync(int id, OwnerRequest request);
        Task DeleteOwnerAsync(int id);
        Task<PageResult<OwnerPropertyRelationDto>> QueryRelationsAsync(BaseInfoQueryRequest request);
        Task<OwnerPropertyRelationDto> CreateRelationAsync(OwnerPropertyRelationRequest request);
        Task<OwnerPropertyRelationDto> UpdateRelationAsync(int id, OwnerPropertyRelationRequest request);
        Task ReleaseRelationAsync(int id, string reason);
        Task<PageResult<ParkingSpaceDto>> QueryParkingsAsync(BaseInfoQueryRequest request);
        Task<ParkingSpaceDto> CreateParkingAsync(ParkingSpaceRequest request);
        Task<ParkingSpaceDto> UpdateParkingAsync(int id, ParkingSpaceRequest request);
        Task DeleteParkingAsync(int id);
        Task<byte[]> DownloadBaseInfoTemplateAsync(ImportModule module);
        Task<ImportResultDto> ImportAsync(ImportRequest request);
        Task<List<ImportLogDto>> GetImportLogsAsync();

        /// <summary>导入批次记录批量删除（v1.1.0-⑤）：软删留痕。</summary>
        Task<RecordBatchDeleteResultDto> BatchDeleteImportLogsAsync(RecordBatchDeleteRequest request);
        Task<byte[]> DownloadImportErrorsAsync(int id);

        /// <summary>导入回执（v1.2.0 CHG-v1.2.0-01）：逐行 新增/覆盖/失败，无论有无失败行都可下载。</summary>
        Task<byte[]> DownloadImportReceiptAsync(int id);
        Task<ExportLogDto> ExportAsync(BaseInfoExportRequest request);
        Task DownloadExportFileAsync(int id, string savePath);
        Task<string> GetParamAsync(string key);
        Task SetParamAsync(string key, string value);

        // ==================== M6 人员组织（PG-ORG-01~03） ====================
        Task<List<DepartmentDto>> GetDepartmentsAsync(string keyword = null);
        Task<DepartmentDto> CreateDepartmentAsync(DepartmentRequest request);
        Task<DepartmentDto> UpdateDepartmentAsync(int id, DepartmentRequest request);
        Task DeleteDepartmentAsync(int id);
        Task<List<PositionDto>> GetPositionsAsync(int? deptId = null);
        Task<PositionDto> CreatePositionAsync(PositionRequest request);
        Task<PositionDto> UpdatePositionAsync(int id, PositionRequest request);
        Task DeletePositionAsync(int id);
        Task<PageResult<EmployeeDto>> QueryEmployeesAsync(EmployeeQueryRequest request);
        Task<EmployeeDto> GetEmployeeAsync(int id);
        Task<EmployeeDto> CreateEmployeeAsync(EmployeeRequest request);
        Task<EmployeeDto> UpdateEmployeeAsync(int id, EmployeeRequest request);
        Task DeleteEmployeeAsync(int id);
        Task<EmployeeDto> ChangeEmployeeStatusAsync(int id, EmployeeStatusRequest request);
        Task<EmployeeDto> ResignEmployeeAsync(int id);
        Task<List<EmployeeStatusLogDto>> GetEmployeeStatusLogsAsync(int id);
        Task<List<ShiftDto>> GetShiftsAsync();
        Task<ShiftDto> CreateShiftAsync(ShiftRequest request);
        Task<ShiftDto> UpdateShiftAsync(int id, ShiftRequest request);
        Task DeleteShiftAsync(int id);
        Task<List<ScheduleDto>> QuerySchedulesAsync(DateTime from, DateTime to, int? employeeId = null);
        Task<SchedulePlanDto> GenerateSchedulesAsync(ScheduleGenerateRequest request);
        Task<SchedulePlanDto> PublishSchedulesAsync(SchedulePublishRequest request);
        Task DeleteScheduleAsync(int id);
        Task<int> DeleteSchedulesBatchAsync(ScheduleBatchDeleteRequest request);
        Task<List<ScheduleConflictLogDto>> GetScheduleConflictsAsync(DateTime from, DateTime to);
        Task<List<ScheduleTemplateDto>> GetScheduleTemplatesAsync();
        Task<ScheduleTemplateDto> GetScheduleTemplateAsync(int id);
        Task<ScheduleTemplateDto> SaveScheduleTemplateAsync(ScheduleTemplateSaveRequest request);
        Task DeleteScheduleTemplateAsync(int id);
        Task<PageResult<AttendanceDto>> QueryAttendanceAsync(AttendanceQueryRequest request);
        Task<AttendanceDto> RecordAttendanceAsync(AttendanceRequest request);
        Task<AttendanceSummaryDto> GetAttendanceSummaryAsync(int? year = null, int? month = null, int? deptId = null);
        Task<string> ExportAttendanceCsvAsync(int? year = null, int? month = null, int? deptId = null);
        Task<AttendanceDto> ReviewAttendanceAsync(int id, AttendanceReviewRequest request);
        Task<int> BatchReviewAttendanceAsync(AttendanceBatchReviewRequest request);

        // ==================== M6 便民电话簿（PG-TEL-01/02） ====================
        Task<List<PhoneCategoryDto>> GetPhoneCategoriesAsync();
        Task<PhoneCategoryDto> CreatePhoneCategoryAsync(PhoneCategoryRequest request);
        Task DeletePhoneCategoryAsync(int id);
        Task<List<PhoneTypeDto>> GetPhoneTypesAsync();
        Task<PhoneTypeDto> CreatePhoneTypeAsync(PhoneTypeRequest request);
        Task<PhoneTypeDto> UpdatePhoneTypeAsync(int id, PhoneTypeRequest request);
        Task DeletePhoneTypeAsync(int id);
        Task<PageResult<PhoneEntryDto>> QueryPhoneEntriesAsync(PhoneEntryQueryRequest request);
        Task<PhoneEntryDto> CreatePhoneEntryAsync(PhoneEntryRequest request);
        Task<PhoneEntryDto> UpdatePhoneEntryAsync(int id, PhoneEntryRequest request);
        Task<PhoneEntryDto> SetPhoneEntryStatusAsync(int id, PhoneEntryStatus status);
        Task<PhoneEntryBatchStatusResultDto> BatchDisablePhoneEntriesAsync(PhoneEntryBatchStatusRequest request);
        Task<PhoneEntryDto> SetPhoneEntryTopAsync(int id, bool isTop);
        Task<EmployeeSyncResultDto> SyncEmployeePhoneEntriesAsync(int categoryId);

        // ==================== M6 纠纷调解（PG-DIS-01~03） ====================
        Task<List<DisputeTypeDto>> GetDisputeTypesAsync();
        Task<DisputeTypeDto> CreateDisputeTypeAsync(DisputeTypeRequest request);
        Task DeleteDisputeTypeAsync(int id);
        Task<PageResult<DisputeCaseDto>> QueryDisputesAsync(DisputeQueryRequest request);
        Task<DisputeCaseDetailDto> GetDisputeAsync(int id);
        Task<DisputeCaseDto> CreateDisputeAsync(DisputeCaseCreateRequest request);
        Task<DisputeCaseDto> UpdateDisputeAsync(int id, DisputeCaseUpdateRequest request);
        Task<DisputeRecordDto> AddDisputeRecordAsync(int caseId, DisputeRecordRequest request);
        Task DeleteDisputeAsync(int id);
        Task<DisputeCaseDto> UpdateDisputeStatusAsync(int id, DisputeCaseStatusRequest request);
        Task<DisputeCaseDto> CloseDisputeAsync(int id, DisputeCloseRequest request);
        Task<DisputeRecordDto> SupplementDisputeCaseAsync(int caseId, DisputeSupplementRequest request);
        Task<List<DisputeMediatorDto>> GetMediatorRecommendationsAsync(int? typeId = null, int? propertyId = null);
        Task<DisputeStatisticsDto> GetDisputeStatisticsAsync();
        /// <summary>导出结案报告（F2，仅已结案）：返回导出留痕（含文件路径），文件由 /reports/files/{id} 取回。</summary>
        Task<ReportLogDto> ExportDisputeCloseReportAsync(int caseId, ExportFormat format);
        /// <summary>按导出留痕 id 下载报表文件到本机指定路径。</summary>
        Task DownloadReportFileAsync(int logId, string savePath);
        /// <summary>调解协议扫描件清单（F1）。</summary>
        Task<List<DisputeAttachmentDto>> GetDisputeAttachmentsAsync(int caseId);
        /// <summary>上传扫描件（multipart；pdf/jpg/jpeg/png，≤20MB，单案 ≤10 份）。</summary>
        Task<DisputeAttachmentDto> UploadDisputeAttachmentAsync(int caseId, string filePath);
        /// <summary>下载扫描件到本机指定路径。</summary>
        Task DownloadDisputeAttachmentAsync(int caseId, int attachmentId, string savePath);
        /// <summary>删除扫描件（仅管理员，连物理文件一并删除）。</summary>
        Task DeleteDisputeAttachmentAsync(int caseId, int attachmentId);

        // ==================== M6 应急处置（PG-EMG-01~04） ====================
        Task<List<EmergencySceneDto>> GetEmergencyScenesAsync(string keyword = null);
        Task<EmergencySceneDto> CreateEmergencySceneAsync(EmergencySceneRequest request);
        Task<EmergencySceneDto> UpdateEmergencySceneAsync(int id, EmergencySceneRequest request);
        Task DeleteEmergencySceneAsync(int id);
        Task<List<EmergencyStepDto>> GetEmergencyStepsAsync(int sceneId);
        Task<EmergencyStepDto> CreateEmergencyStepAsync(int sceneId, EmergencyStepRequest request);
        Task<EmergencyStepDto> UpdateEmergencyStepAsync(int id, EmergencyStepRequest request);
        Task DeleteEmergencyStepAsync(int id);
        Task<PageResult<EmergencyEventDto>> QueryEmergencyEventsAsync(EmergencyEventQueryRequest request);
        Task<EmergencyEventDetailDto> GetEmergencyEventAsync(int id);
        Task<EmergencyEventDetailDto> CreateEmergencyEventAsync(EmergencyEventCreateRequest request);
        Task<EmergencyEventDetailDto> AssignEmergencyAsync(int id, EmergencyAssignRequest request);
        Task<EmergencyRecordDto> AddEmergencyRecordAsync(int id, EmergencyRecordRequest request);
        Task<EmergencyEventDetailDto> CloseEmergencyAsync(int id, EmergencyCloseRequest request);
        Task<List<EmergencyMatchDto>> GetEmergencyMatchesAsync(int sceneId);
        Task<EmergencySceneDto> SetEmergencySceneStatusAsync(int id, EmergencySceneStatusRequest request);
        Task ReorderEmergencyStepsAsync(int sceneId, System.Collections.Generic.List<int> orderedIds);
        Task<EmergencyEventStatsDto> GetEmergencyEventStatsAsync();
        Task CancelEmergencyEventAsync(int id);
        Task<PageResult<EmergencyReviewDto>> QueryEmergencyReviewsAsync(PageRequest request);
        Task<EmergencyReviewDto> GetEmergencyReviewAsync(int eventId);
        Task<EmergencyReviewDto> ReviewEmergencyAsync(int id, EmergencyReviewRequest request);

        // ==================== M6 设备台账（PG-EQP-01~04） ====================
        Task<List<DeviceTypeDto>> GetDeviceTypesAsync();
        Task<DeviceTypeDto> CreateDeviceTypeAsync(DeviceTypeRequest request);
        Task DeleteDeviceTypeAsync(int id);
        /// <summary>R6：登记类型固定为系统内置 保养/年检（无自定义增删）；includeDisabled=true 附带历史停用类型。</summary>
        Task<List<MaintainTypeDto>> GetMaintainTypesAsync(bool includeDisabled = false);
        Task<PageResult<DeviceDto>> QueryDevicesAsync(DeviceQueryRequest request);
        Task<DeviceDto> GetDeviceAsync(int id);
        Task<DeviceDto> CreateDeviceAsync(DeviceRequest request);
        Task<DeviceDto> UpdateDeviceAsync(int id, DeviceRequest request);
        Task DeleteDeviceAsync(int id);
        Task<DeviceDto> ChangeDeviceStatusAsync(int id, DeviceStatusRequest request);
        /// <summary>设备状态变更留痕（R8，详情浮层「状态留痕」页签；含首次登记行 oldStatus=-1）。</summary>
        Task<List<DeviceStatusLogDto>> GetDeviceStatusLogsAsync(int deviceId);
        Task<List<MaintenanceRecordDto>> GetMaintenanceAsync(int deviceId);
        Task<MaintenanceRecordDto> AddMaintenanceAsync(int deviceId, MaintenanceRecordRequest request);
        Task<List<InspectionRecordDto>> GetInspectionAsync(int deviceId);
        Task<InspectionRecordDto> AddInspectionAsync(int deviceId, InspectionRecordRequest request);
        Task<List<FaultRecordDto>> GetFaultsAsync(int deviceId);
        Task<FaultRecordDto> AddFaultAsync(int deviceId, FaultRecordRequest request);
        Task<List<VendorDto>> GetVendorsAsync();
        Task<VendorDto> CreateVendorAsync(VendorRequest request);
        Task DeleteVendorAsync(int id);
        /// <summary>R6：status=-1 全部（含已处理，默认）/0 待处理/1 已处理。</summary>
        Task<List<EquipmentReminderDto>> GetEquipmentRemindersAsync(int days = 30, string type = null, int status = -1);
        Task<ReminderSummaryDto> GetEquipmentReminderSummaryAsync();
        Task<EquipmentReminderDto> HandleEquipmentReminderAsync(int reminderId);
        /// <summary>R7：指派责任班组（team 传空 = 恢复默认规则：逾期→物业办 / 其余→工程部）。</summary>
        Task<EquipmentReminderDto> SaveEquipmentReminderTeamAsync(int reminderId, ReminderTeamRequest request);
        /// <summary>R6：批量删除提醒记录（仅已处理可删；命中待处理整批拒绝并返回明细）。</summary>
        Task<ReminderBatchDeleteResultDto> BatchDeleteEquipmentRemindersAsync(ReminderBatchDeleteRequest request);
        /// <summary>R6：设备自定义类型记录（登记类型解耦）。</summary>
        Task<List<DeviceCustomRecordDto>> GetDeviceCustomRecordsAsync(int deviceId);
        Task<DeviceCustomRecordDto> CreateDeviceCustomRecordAsync(int deviceId, DeviceCustomRecordRequest request);
        Task<DeviceCustomRecordDto> UpdateDeviceCustomRecordAsync(int recordId, DeviceCustomRecordRequest request);
        Task DeleteDeviceCustomRecordAsync(int recordId);
        /// <summary>自定义类型名称候选（历史自定义登记类型 + 已录入记录类型，去重）。</summary>
        Task<List<string>> GetCustomRecordTypesAsync();
        Task<DeviceSummaryDto> GetDeviceSummaryAsync();
        Task<EquipmentExportResultDto> ExportDevicesAsync(DeviceQueryRequest request);
        Task<DeviceTypeDto> UpdateDeviceTypeAsync(int id, DeviceTypeRequest request);
        Task<VendorDto> UpdateVendorAsync(int id, VendorRequest request);
        Task<List<FaultRecordDto>> GetAllFaultsAsync();
        Task<FaultRecordDto> HandleFaultAsync(int faultId, FaultHandleRequest request);
        /// <summary>
        /// 生成维修工单（R5）：以 Excel 派工单形式落盘（导出留痕），故障单转维修中并同步设备状态；
        /// 返回结果后按 Id 调 DownloadExportFileAsync 另存到本机。
        /// </summary>
        Task<EquipmentExportResultDto> GenerateFaultWorkOrderAsync(int faultId);

        // ==================== M6 系统设置（PG-COM-01~04） ====================
        Task<List<DictTypeDto>> GetSystemDictTypesAsync();
        Task<DictTypeDto> CreateSystemDictTypeAsync(DictTypeRequest request);
        Task<List<DictItemDto>> GetSystemDictItemsAsync(string typeCode, bool includeDisabled = false, int? status = null);
        Task<DictItemDto> UpdateDictItemAsync(int id, DictItemRequest request);
        Task<DictItemDto> SetDictItemStatusAsync(int id, DictItemStatus status);
        /// <summary>R12：批量删除字典项（仅已停用项可删；命中未停用项整批拒绝并返回 blocked 明细）。</summary>
        Task<DictItemBatchDeleteResultDto> BatchDeleteDictItemsAsync(DictItemBatchDeleteRequest request);
        /// <summary>R13：审计日志批量删除（软删留痕）。</summary>
        Task<RecordBatchDeleteResultDto> BatchDeleteAuditLogsAsync(RecordBatchDeleteRequest request);
        /// <summary>R13：备份/恢复记录批量删除（软删留痕）。</summary>
        Task<RecordBatchDeleteResultDto> BatchDeleteBackupRecordsAsync(RecordBatchDeleteRequest request);
        /// <summary>R13：一键清理残余数据（物理删除全库软删留痕行）。</summary>
        Task<PurgeSoftDeletedResultDto> PurgeSoftDeletedAsync();

        Task<List<ParamDto>> GetSystemParamsAsync();
        Task SetSystemParamAsync(string key, string value);
        Task<List<BackupDto>> GetSystemBackupsAsync();
        /// <summary>手动备份；targetPath 非空时按用户选择的保存路径落盘（R12）。</summary>
        Task<BackupDto> RunSystemBackupAsync(string note, string targetPath = null);
        Task<BackupDto> RestoreSystemBackupAsync(BackupRestoreRequest request);
        Task<BackupStatusDto> GetSystemBackupStatusAsync();
        Task<AuditExportDto> ExportAuditLogsAsync(AuditLogQueryRequest request);
        Task<PageResult<AuditLogDto>> QueryAuditLogsAsync(AuditLogQueryRequest request);
    }
}

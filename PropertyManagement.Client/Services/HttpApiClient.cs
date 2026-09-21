using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
    /// <summary>真实 HTTP 客户端：统一信封解包、token 注入、错误码映射（契约 v0.1 + M4 财务端点）。</summary>
    public class HttpApiClient : IApiClient
    {
        public const string DefaultBaseAddress = "http://127.0.0.1:5210/api/v1/";

        private readonly HttpClient _http;

        public bool IsMock => false;

        public HttpApiClient(string baseAddress)
        {
            // BaseAddress 必须以 "/" 结尾，否则相对路径会被 URI 解析吞掉最后一段
            string normalized = string.IsNullOrEmpty(baseAddress) || baseAddress.EndsWith("/")
                ? baseAddress
                : baseAddress + "/";
            _http = new HttpClient
            {
                BaseAddress = new Uri(normalized),
                Timeout = TimeSpan.FromSeconds(20)
            };
        }

        public Task<HealthResponse> GetHealthAsync()
        {
            return GetAsync<HealthResponse>("health");
        }

        public Task<LoginResult> LoginAsync(LoginRequest request)
        {
            return PostAsync<LoginRequest, LoginResult>("auth/login", request);
        }

        public Task LogoutAsync()
        {
            return PostAsync<object, object>("auth/logout", null);
        }

        public Task ChangePasswordAsync(ChangePasswordRequest request)
        {
            return PostAsync<ChangePasswordRequest, object>("auth/change-password", request);
        }

        public Task<DashboardDto> GetDashboardAsync()
        {
            return GetAsync<DashboardDto>("common/dashboard");
        }

        // ==================== R17 顶部栏与仪表盘交互 ====================

        /// <summary>仪表盘统计（period = yyyy-MM，空则当前月）。</summary>
        public Task<DashboardDto> GetDashboardAsync(string period)
        {
            return GetAsync<DashboardDto>("common/dashboard" + Query(new { period }));
        }

        public Task<TodoCenterDto> GetTodosAsync(int limit = 20)
        {
            return GetAsync<TodoCenterDto>("common/todos" + Query(new { limit }));
        }

        public Task<GlobalSearchResultDto> SearchAsync(string keyword)
        {
            return GetAsync<GlobalSearchResultDto>("common/search" + Query(new { keyword }));
        }

        public Task<UserProfileDto> GetProfileAsync()
        {
            return GetAsync<UserProfileDto>("auth/profile");
        }

        public Task<UserProfileDto> UpdateProfileAsync(UserProfileRequest request)
        {
            return PutAsync<UserProfileRequest, UserProfileDto>("auth/profile", request);
        }

        // ==================== M4 财务收费 ====================

        public Task<List<ChargeItemDto>> GetChargeItemsAsync(string keyword = null, string category = null)
        {
            return GetAsync<List<ChargeItemDto>>("billing/charge-items" + Query(new { keyword, category }));
        }

        public Task<List<DictItemDto>> GetDictItemsAsync(string typeCode)
        {
            return GetAsync<List<DictItemDto>>("dicts/" + Uri.EscapeDataString(typeCode));
        }

        public Task<DictItemDto> CreateDictItemAsync(string typeCode, DictItemRequest request)
        {
            // PG-COM-01：走 system 端点以支持 显示值/排序（DictItemRequest）
            return PostAsync<DictItemRequest, DictItemDto>("system/dict-types/" + Uri.EscapeDataString(typeCode) + "/items", request);
        }

        public Task<ChargeItemDto> CreateChargeItemAsync(ChargeItemRequest request)
        {
            return PostAsync<ChargeItemRequest, ChargeItemDto>("billing/charge-items", request);
        }

        public Task<ChargeItemDto> UpdateChargeItemAsync(int id, ChargeItemRequest request)
        {
            return PutAsync<ChargeItemRequest, ChargeItemDto>("billing/charge-items/" + id, request);
        }

        public Task DeleteChargeItemAsync(int id)
        {
            return DeleteAsync<object>("billing/charge-items/" + id);
        }

        // ---------- CHG-v1.1.2-26：价目表与计量变量 ----------

        public Task<List<ChargeStandardDto>> GetChargeStandardsAsync(string keyword = null, string category = null, bool includeDisabled = true)
        {
            return GetAsync<List<ChargeStandardDto>>("billing/charge-standards" + Query(new { keyword, category, includeDisabled }));
        }

        public Task<ChargeStandardDto> GetChargeStandardAsync(int id)
        {
            return GetAsync<ChargeStandardDto>("billing/charge-standards/" + id);
        }

        public Task<ChargeStandardDto> CreateChargeStandardAsync(ChargeStandardRequest request)
        {
            return PostAsync<ChargeStandardRequest, ChargeStandardDto>("billing/charge-standards", request);
        }

        public Task<ChargeStandardDto> UpdateChargeStandardAsync(int id, ChargeStandardRequest request)
        {
            return PutAsync<ChargeStandardRequest, ChargeStandardDto>("billing/charge-standards/" + id, request);
        }

        public Task DeleteChargeStandardAsync(int id)
        {
            return DeleteAsync<object>("billing/charge-standards/" + id);
        }

        public Task<ChargeStandardDto> ToggleChargeStandardAsync(int id, int status)
        {
            return PostAsync<ChargeStandardStatusRequest, ChargeStandardDto>(
                "billing/charge-standards/" + id + "/status", new ChargeStandardStatusRequest { Status = status });
        }

        public Task<ChargeStandardSpecDto> CreateChargeSpecAsync(int standardId, ChargeStandardSpecRequest request)
        {
            return PostAsync<ChargeStandardSpecRequest, ChargeStandardSpecDto>(
                "billing/charge-standards/" + standardId + "/specs", request);
        }

        public Task<ChargeStandardSpecDto> UpdateChargeSpecAsync(int id, ChargeStandardSpecRequest request)
        {
            return PutAsync<ChargeStandardSpecRequest, ChargeStandardSpecDto>("billing/charge-specs/" + id, request);
        }

        public Task DeleteChargeSpecAsync(int id)
        {
            return DeleteAsync<object>("billing/charge-specs/" + id);
        }

        public Task ToggleChargeSpecAsync(int id, int status)
        {
            return PostAsync<ChargeStandardStatusRequest, object>(
                "billing/charge-specs/" + id + "/status", new ChargeStandardStatusRequest { Status = status });
        }

        public Task<List<ChargeVariableDto>> GetChargeVariablesAsync(string keyword = null, bool includeDisabled = true)
        {
            return GetAsync<List<ChargeVariableDto>>("billing/charge-variables" + Query(new { keyword, includeDisabled }));
        }

        public Task<ChargeVariableDto> CreateChargeVariableAsync(ChargeVariableRequest request)
        {
            return PostAsync<ChargeVariableRequest, ChargeVariableDto>("billing/charge-variables", request);
        }

        public Task<ChargeVariableDto> UpdateChargeVariableAsync(int id, ChargeVariableRequest request)
        {
            return PutAsync<ChargeVariableRequest, ChargeVariableDto>("billing/charge-variables/" + id, request);
        }

        public Task DeleteChargeVariableAsync(int id)
        {
            return DeleteAsync<object>("billing/charge-variables/" + id);
        }

        public Task ToggleChargeVariableAsync(int id, int status)
        {
            return PostAsync<ChargeStandardStatusRequest, object>(
                "billing/charge-variables/" + id + "/status", new ChargeStandardStatusRequest { Status = status });
        }

        public Task<BillPreviewResult> PreviewBillsAsync(BillPreviewRequest request)
        {
            return PostAsync<BillPreviewRequest, BillPreviewResult>("billing/bills/preview", request);
        }

        public Task<ReportLogDto> ExportChargeItemsAsync(ChargeItemExportRequest request)
        {
            return PostAsync<ChargeItemExportRequest, ReportLogDto>("reports/charge-items/export", request);
        }

        public Task<BillingCycleDto> CreateCycleAsync(BillingCycleRequest request)
        {
            return PostAsync<BillingCycleRequest, BillingCycleDto>("billing/cycles", request);
        }
        public Task<List<BillingCycleDto>> GetCyclesAsync()
        {
            return GetAsync<List<BillingCycleDto>>("billing/cycles");
        }

        public Task DeleteCycleAsync(int id)
        {
            return DeleteAsync<object>("billing/cycles/" + id);
        }

        public Task<BillGenerateLogDto> GenerateBillsAsync(BillGenerateRequest request)
        {
            return PostAsync<BillGenerateRequest, BillGenerateLogDto>("billing/bills/generate", request);
        }

        public Task<BillObjectQueryResult> QueryBillObjectsAsync(BillObjectQueryRequest request)
        {
            // CHG-v1.1.0-10：缴费对象候选查询（kind=property|parking & keyword）
            return GetAsync<BillObjectQueryResult>("billing/bill-objects" + Query(new
            {
                kind = request == null ? null : request.Kind,
                keyword = request == null ? null : request.Keyword
            }));
        }

        public Task<BillObjectSelectionDto> GetBatchBillObjectsAsync(int batchId)
        {
            return GetAsync<BillObjectSelectionDto>("billing/bills/generate-logs/" + batchId + "/objects");
        }

        public Task<BillGenerateLogDto> PublishBillsAsync(BillPublishRequest request)
        {
            return PostAsync<BillPublishRequest, BillGenerateLogDto>("billing/bills/publish", request);
        }

        public Task<BillGenerateLogDto> RetryFailuresAsync(int batchId)
        {
            return PostAsync<BillRetryRequest, BillGenerateLogDto>(
                "billing/bills/generate-logs/" + batchId + "/retry", new BillRetryRequest { BatchId = batchId });
        }

        public Task DeleteBillBatchAsync(int batchId)
        {
            return PostAsync<BillBatchDeleteRequest, object>(
                "billing/bills/generate-logs/delete", new BillBatchDeleteRequest { BatchId = batchId });
        }
        public Task<List<BillBatchDto>> GetGenerateLogsAsync()
        {
            return GetAsync<List<BillBatchDto>>("billing/bills/generate-logs");
        }

        public Task<List<BillFailureDto>> GetFailuresAsync(int batchId)
        {
            return GetAsync<List<BillFailureDto>>("billing/bills/generate-logs/" + batchId + "/failures");
        }

        public Task<PageResult<BillListItemDto>> QueryBillsAsync(BillQueryRequest request)
        {
            return GetAsync<PageResult<BillListItemDto>>("billing/bills" + Query(request));
        }

        public Task<PageResult<ArrearDto>> QueryArrearsAsync(BillQueryRequest request)
        {
            return GetAsync<PageResult<ArrearDto>>("billing/bills/arrears" + Query(request));
        }

        public Task RecordRemindAsync(ArrearRemindRequest request)
        {
            return PostAsync<ArrearRemindRequest, object>("billing/bills/remind", request);
        }

        public Task DeleteArrearAsync(int billId)
        {
            return DeleteAsync<object>("billing/bills/" + billId);
        }

        // ---------- 欠费台账「移出台账」（CHG-v1.1.2-03） ----------
        public Task<int> DismissArrearsAsync(ArrearDismissRequest request)
        {
            return PostAsync<ArrearDismissRequest, int>("billing/bills/arrears/dismiss", request);
        }

        public Task<List<ArrearDismissDto>> QueryDismissedArrearsAsync()
        {
            return GetAsync<List<ArrearDismissDto>>("billing/bills/arrears/dismissed");
        }

        public Task<int> RestoreArrearsAsync(ArrearDismissRequest request)
        {
            return PostAsync<ArrearDismissRequest, int>("billing/bills/arrears/restore", request);
        }

        public Task<PaymentStatisticsDto> GetPaymentStatisticsAsync()
        {
            return GetAsync<PaymentStatisticsDto>("billing/statistics/payment");
        }

        public Task<PaymentDto> CreatePaymentAsync(PaymentCreateRequest request)
        {
            return PostAsync<PaymentCreateRequest, PaymentDto>("payments", request);
        }

        /// <summary>CHG-v1.1.0-12：统一收款（多账单一次收款）。</summary>
        public Task<PaymentBatchResultDto> CreateBatchPaymentAsync(PaymentBatchCreateRequest request)
        {
            return PostAsync<PaymentBatchCreateRequest, PaymentBatchResultDto>("payments/batch", request);
        }

        public Task<PaymentDto> GetPaymentAsync(int id)
        {
            return GetAsync<PaymentDto>("payments/" + id);
        }

        // CHG-v1.1.0-15：收据号前后端下线（原收据查询/打印接口已移除）

        public Task<PreDepositDto> GetPreDepositAsync(int ownerId)
        {
            return GetAsync<PreDepositDto>("payments/pre-deposits/" + ownerId);
        }

        public Task<RefundAdjustmentDto> CreateRefundAsync(RefundAdjustmentRequest request)
        {
            return PostAsync<RefundAdjustmentRequest, RefundAdjustmentDto>("payments/refunds", request);
        }

        /// <summary>CHG-v1.1.0-13：批量退款/减免/调整。</summary>
        public Task<RefundBatchResultDto> CreateRefundBatchAsync(RefundAdjustmentRequest request)
        {
            return PostAsync<RefundAdjustmentRequest, RefundBatchResultDto>("payments/refunds/batch", request);
        }

        public Task<PageResult<RefundAdjustmentDto>> QueryRefundsAsync(PageRequest request)
        {
            return GetAsync<PageResult<RefundAdjustmentDto>>("payments/refunds" + Query(request));
        }

        public Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync()
        {
            return GetAsync<List<ExpenseCategoryDto>>("expenses/categories");
        }

        public Task<ExpenseCategoryDto> CreateExpenseCategoryAsync(ExpenseCategoryRequest request)
        {
            return PostAsync<ExpenseCategoryRequest, ExpenseCategoryDto>("expenses/categories", request);
        }

        public Task DeleteExpenseCategoryAsync(int id)
        {
            return DeleteAsync<object>("expenses/categories/" + id);
        }

        public Task<ExpenseDto> CreateExpenseAsync(ExpenseCreateRequest request)
        {
            return PostAsync<ExpenseCreateRequest, ExpenseDto>("expenses", request);
        }

        public Task<PageResult<ExpenseDto>> QueryExpensesAsync(PageRequest request)
        {
            return GetAsync<PageResult<ExpenseDto>>("expenses" + Query(request));
        }

        public Task DeleteExpenseAsync(int id)
        {
            return DeleteAsync<object>("expenses/" + id);
        }

        public Task<RecordBatchDeleteResultDto> BatchDeleteExpensesAsync(RecordBatchDeleteRequest request)
        {
            return PostAsync<RecordBatchDeleteRequest, RecordBatchDeleteResultDto>(
                "expenses/batch-delete", request ?? new RecordBatchDeleteRequest());
        }

        public Task<PageResult<LedgerEntryDto>> GetLedgerAsync(LedgerQueryRequest request)
        {
            return GetAsync<PageResult<LedgerEntryDto>>("reports/ledger" + Query(request));
        }

        public Task<FinancialReportDto> GetFinancialReportAsync(FinancialReportQueryRequest request)
        {
            return GetAsync<FinancialReportDto>("reports/financial" + Query(request));
        }

        public Task<ReportLogDto> ExportReportAsync(ReportExportRequest request)
        {
            return PostAsync<ReportExportRequest, ReportLogDto>("reports/export", request);
        }

        /// <summary>CHG-v1.1.2-05：收支明细流水导出（Excel/PDF）。</summary>
        public Task<ReportLogDto> ExportLedgerAsync(LedgerExportRequest request)
        {
            return PostAsync<LedgerExportRequest, ReportLogDto>("reports/ledger/export", request);
        }

        /// <summary>CHG-v1.1.0-14：导出收据打印模板。</summary>
        public Task<ReportLogDto> ExportReceiptTemplateAsync(ReceiptTemplateRequest request)
        {
            return PostAsync<ReceiptTemplateRequest, ReportLogDto>("reports/receipt-template", request);
        }

        /// <summary>CHG-v1.1.2-41：导出退款/减免/调整单据 PDF（留档/审计追溯）。</summary>
        public Task<ReportLogDto> ExportRefundRecordAsync(RefundRecordExportRequest request)
        {
            return PostAsync<RefundRecordExportRequest, ReportLogDto>("reports/refund-record", request);
        }

        // ==================== M5 基础信息与导入 ====================

        public Task<List<CommunityDto>> GetCommunitiesAsync(string keyword = null)
        {
            return GetAsync<List<CommunityDto>>("baseinfo/communities" + Query(new { keyword }));
        }

        public Task<List<BuildingDto>> GetBuildingsAsync(int? communityId = null)
        {
            return GetAsync<List<BuildingDto>>("baseinfo/buildings" + Query(new { communityId }));
        }

        public Task<List<UnitDto>> GetUnitsAsync(int? buildingId = null)
        {
            return GetAsync<List<UnitDto>>("baseinfo/units" + Query(new { buildingId }));
        }

        public Task<BuildingDto> CreateBuildingAsync(BuildingRequest request)
        {
            return PostAsync<BuildingRequest, BuildingDto>("baseinfo/buildings", request);
        }

        public Task<UnitDto> CreateUnitAsync(UnitRequest request)
        {
            return PostAsync<UnitRequest, UnitDto>("baseinfo/units", request);
        }

        public Task DeleteBuildingAsync(int id)
        {
            return DeleteAsync<object>("baseinfo/buildings/" + id);
        }

        public Task DeleteUnitAsync(int id)
        {
            return DeleteAsync<object>("baseinfo/units/" + id);
        }

        public Task<PageResult<PropertyDto>> QueryPropertiesAsync(BaseInfoQueryRequest request)
        {
            return GetAsync<PageResult<PropertyDto>>("baseinfo/properties" + Query(request));
        }

        public Task<PropertyDto> CreatePropertyAsync(PropertyRequest request)
        {
            return PostAsync<PropertyRequest, PropertyDto>("baseinfo/properties", request);
        }

        public Task<PropertyDto> UpdatePropertyAsync(int id, PropertyRequest request)
        {
            return PutAsync<PropertyRequest, PropertyDto>("baseinfo/properties/" + id, request);
        }

        public Task DeletePropertyAsync(int id)
        {
            return DeleteAsync<object>("baseinfo/properties/" + id);
        }

        public Task<PageResult<OwnerDto>> QueryOwnersAsync(BaseInfoQueryRequest request)
        {
            return GetAsync<PageResult<OwnerDto>>("baseinfo/owners" + Query(request));
        }

        public Task<OwnerDto> GetOwnerAsync(int id)
        {
            return GetAsync<OwnerDto>("baseinfo/owners/" + id);
        }

        public Task<List<BaseChangeLogDto>> GetOwnerChangeLogsAsync(int id)
        {
            return GetAsync<List<BaseChangeLogDto>>("baseinfo/owners/" + id + "/change-logs");
        }

        public Task<List<OwnerPropertyRelationDto>> GetOwnerRelationsAsync(int id)
        {
            return GetAsync<List<OwnerPropertyRelationDto>>("baseinfo/owners/" + id + "/relations");
        }

        public Task<OwnerDto> CreateOwnerAsync(OwnerRequest request)
        {
            return PostAsync<OwnerRequest, OwnerDto>("baseinfo/owners", request);
        }

        public Task<OwnerDto> UpdateOwnerAsync(int id, OwnerRequest request)
        {
            return PutAsync<OwnerRequest, OwnerDto>("baseinfo/owners/" + id, request);
        }

        public Task DeleteOwnerAsync(int id)
        {
            return DeleteAsync<object>("baseinfo/owners/" + id);
        }

        public Task<PageResult<OwnerPropertyRelationDto>> QueryRelationsAsync(BaseInfoQueryRequest request)
        {
            return GetAsync<PageResult<OwnerPropertyRelationDto>>("baseinfo/owner-property-relations" + Query(request));
        }

        public Task<OwnerPropertyRelationDto> CreateRelationAsync(OwnerPropertyRelationRequest request)
        {
            return PostAsync<OwnerPropertyRelationRequest, OwnerPropertyRelationDto>("baseinfo/owner-property-relations", request);
        }

        public Task<OwnerPropertyRelationDto> UpdateRelationAsync(int id, OwnerPropertyRelationRequest request)
        {
            return PutAsync<OwnerPropertyRelationRequest, OwnerPropertyRelationDto>("baseinfo/owner-property-relations/" + id, request);
        }

        public Task ReleaseRelationAsync(int id, string reason)
        {
            return PostAsync<ReleaseRelationRequest, object>(
                "baseinfo/owner-property-relations/" + id + "/release", new ReleaseRelationRequest { Reason = reason });
        }

        public Task<PageResult<ParkingSpaceDto>> QueryParkingsAsync(BaseInfoQueryRequest request)
        {
            return GetAsync<PageResult<ParkingSpaceDto>>("baseinfo/parking-spaces" + Query(request));
        }

        public Task<ParkingSpaceDto> CreateParkingAsync(ParkingSpaceRequest request)
        {
            return PostAsync<ParkingSpaceRequest, ParkingSpaceDto>("baseinfo/parking-spaces", request);
        }

        public Task<ParkingSpaceDto> UpdateParkingAsync(int id, ParkingSpaceRequest request)
        {
            return PutAsync<ParkingSpaceRequest, ParkingSpaceDto>("baseinfo/parking-spaces/" + id, request);
        }

        public Task DeleteParkingAsync(int id)
        {
            return DeleteAsync<object>("baseinfo/parking-spaces/" + id);
        }

        public Task<byte[]> DownloadBaseInfoTemplateAsync(ImportModule module)
        {
            return GetRawBytesAsync("baseinfo/imports/template" + Query(new { module }));
        }

        public Task<ImportResultDto> ImportAsync(ImportRequest request)
        {
            return PostAsync<ImportRequest, ImportResultDto>("baseinfo/imports", request);
        }

        public Task<List<ImportLogDto>> GetImportLogsAsync()
        {
            return GetAsync<List<ImportLogDto>>("baseinfo/imports");
        }

        public Task<RecordBatchDeleteResultDto> BatchDeleteImportLogsAsync(RecordBatchDeleteRequest request)
        {
            return PostAsync<RecordBatchDeleteRequest, RecordBatchDeleteResultDto>(
                "baseinfo/imports/batch-delete", request ?? new RecordBatchDeleteRequest());
        }

        public Task<byte[]> DownloadImportErrorsAsync(int id)
        {
            return GetRawBytesAsync("baseinfo/imports/" + id + "/errors");
        }

        public Task<ExportLogDto> ExportAsync(BaseInfoExportRequest request)
        {
            return PostAsync<BaseInfoExportRequest, ExportLogDto>("baseinfo/exports", request);
        }

        public async Task DownloadExportFileAsync(int id, string savePath)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, "baseinfo/exports/" + id + "/file"))
            {
                AddToken(req);
                HttpResponseMessage resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ApiClientException(ErrorCode.InternalError, "文件下载失败（HTTP " + (int)resp.StatusCode + "）");
                }
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                System.IO.File.WriteAllBytes(savePath, bytes);
            }
        }

        public Task<string> GetParamAsync(string key)
        {
            return GetAsync<string>("baseinfo/params/" + Uri.EscapeDataString(key));
        }

        public Task SetParamAsync(string key, string value)
        {
            return PutAsync<ParamValueRequest, object>(
                "baseinfo/params/" + Uri.EscapeDataString(key), new ParamValueRequest { Value = value });
        }

        // ==================== M6 人员组织（PG-ORG-01~03） ====================
        public Task<List<DepartmentDto>> GetDepartmentsAsync(string keyword = null)
        {
            return GetAsync<List<DepartmentDto>>("org/departments" + Query(new { keyword }));
        }

        public Task<DepartmentDto> CreateDepartmentAsync(DepartmentRequest request)
        {
            return PostAsync<DepartmentRequest, DepartmentDto>("org/departments", request);
        }

        public Task<DepartmentDto> UpdateDepartmentAsync(int id, DepartmentRequest request)
        {
            return PutAsync<DepartmentRequest, DepartmentDto>("org/departments/" + id, request);
        }

        public Task DeleteDepartmentAsync(int id)
        {
            return DeleteAsync<object>("org/departments/" + id);
        }

        public Task<List<PositionDto>> GetPositionsAsync(int? deptId = null)
        {
            return GetAsync<List<PositionDto>>("org/positions" + Query(new { deptId }));
        }

        public Task<PositionDto> CreatePositionAsync(PositionRequest request)
        {
            return PostAsync<PositionRequest, PositionDto>("org/positions", request);
        }

        public Task<PositionDto> UpdatePositionAsync(int id, PositionRequest request)
        {
            return PutAsync<PositionRequest, PositionDto>("org/positions/" + id, request);
        }

        public Task DeletePositionAsync(int id)
        {
            return DeleteAsync<object>("org/positions/" + id);
        }

        public Task<PageResult<EmployeeDto>> QueryEmployeesAsync(EmployeeQueryRequest request)
        {
            return GetAsync<PageResult<EmployeeDto>>("org/employees" + Query(request));
        }

        public Task<EmployeeDto> GetEmployeeAsync(int id)
        {
            return GetAsync<EmployeeDto>("org/employees/" + id);
        }

        public Task<EmployeeDto> CreateEmployeeAsync(EmployeeRequest request)
        {
            return PostAsync<EmployeeRequest, EmployeeDto>("org/employees", request);
        }

        public Task<EmployeeDto> UpdateEmployeeAsync(int id, EmployeeRequest request)
        {
            return PutAsync<EmployeeRequest, EmployeeDto>("org/employees/" + id, request);
        }

        public Task DeleteEmployeeAsync(int id)
        {
            return DeleteAsync<object>("org/employees/" + id);
        }

        public Task<EmployeeDto> ChangeEmployeeStatusAsync(int id, EmployeeStatusRequest request)
        {
            return PostAsync<EmployeeStatusRequest, EmployeeDto>("org/employees/" + id + "/status", request);
        }

        public Task<EmployeeDto> ResignEmployeeAsync(int id)
        {
            return PostAsync<object, EmployeeDto>("org/employees/" + id + "/resign", null);
        }

        public Task<List<EmployeeStatusLogDto>> GetEmployeeStatusLogsAsync(int id)
        {
            return GetAsync<List<EmployeeStatusLogDto>>("org/employees/" + id + "/status-logs");
        }

        public Task<List<ShiftDto>> GetShiftsAsync()
        {
            return GetAsync<List<ShiftDto>>("org/shifts");
        }

        public Task<ShiftDto> CreateShiftAsync(ShiftRequest request)
        {
            return PostAsync<ShiftRequest, ShiftDto>("org/shifts", request);
        }

        public Task<ShiftDto> UpdateShiftAsync(int id, ShiftRequest request)
        {
            return PutAsync<ShiftRequest, ShiftDto>("org/shifts/" + id, request);
        }

        public Task DeleteShiftAsync(int id)
        {
            return DeleteAsync<object>("org/shifts/" + id);
        }

        public Task<List<ScheduleDto>> QuerySchedulesAsync(DateTime from, DateTime to, int? employeeId = null)
        {
            return GetAsync<List<ScheduleDto>>("org/schedules" + Query(new { from, to, employeeId }));
        }

        public Task<SchedulePlanDto> GenerateSchedulesAsync(ScheduleGenerateRequest request)
        {
            return PostAsync<ScheduleGenerateRequest, SchedulePlanDto>("org/schedules/generate", request);
        }

        public Task<SchedulePlanDto> PublishSchedulesAsync(SchedulePublishRequest request)
        {
            return PostAsync<SchedulePublishRequest, SchedulePlanDto>("org/schedules/publish", request);
        }

        public Task DeleteScheduleAsync(int id)
        {
            return DeleteAsync<object>("org/schedules/" + id);
        }

        public Task<int> DeleteSchedulesBatchAsync(ScheduleBatchDeleteRequest request)
        {
            return PostAsync<ScheduleBatchDeleteRequest, int>("org/schedules/batch-delete", request);
        }

        public Task<List<ScheduleConflictLogDto>> GetScheduleConflictsAsync(DateTime from, DateTime to)
        {
            return GetAsync<List<ScheduleConflictLogDto>>("org/schedules/conflicts" + Query(new { from, to }));
        }

        public Task<List<ScheduleTemplateDto>> GetScheduleTemplatesAsync()
        {
            return GetAsync<List<ScheduleTemplateDto>>("org/schedule-templates");
        }

        public Task<ScheduleTemplateDto> GetScheduleTemplateAsync(int id)
        {
            return GetAsync<ScheduleTemplateDto>("org/schedule-templates/" + id);
        }

        public Task<ScheduleTemplateDto> SaveScheduleTemplateAsync(ScheduleTemplateSaveRequest request)
        {
            return PostAsync<ScheduleTemplateSaveRequest, ScheduleTemplateDto>("org/schedule-templates", request);
        }

        public Task DeleteScheduleTemplateAsync(int id)
        {
            return DeleteAsync<object>("org/schedule-templates/" + id);
        }

        public Task<PageResult<AttendanceDto>> QueryAttendanceAsync(AttendanceQueryRequest request)
        {
            return GetAsync<PageResult<AttendanceDto>>("org/attendance" + Query(request));
        }

        public Task<AttendanceDto> RecordAttendanceAsync(AttendanceRequest request)
        {
            return PostAsync<AttendanceRequest, AttendanceDto>("org/attendance", request);
        }

        public Task<AttendanceSummaryDto> GetAttendanceSummaryAsync(int? year = null, int? month = null, int? deptId = null)
        {
            return GetAsync<AttendanceSummaryDto>("org/attendance/summary" + Query(new { year, month, deptId }));
        }

        public async Task<string> ExportAttendanceCsvAsync(int? year = null, int? month = null, int? deptId = null)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, "org/attendance/export" + Query(new { year, month, deptId })))
            {
                AddToken(req);
                HttpResponseMessage resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ApiClientException(ErrorCode.InternalError, "考勤月报导出失败（HTTP " + (int)resp.StatusCode + "）");
                }
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                return Encoding.UTF8.GetString(bytes);
            }
        }

        public Task<AttendanceDto> ReviewAttendanceAsync(int id, AttendanceReviewRequest request)
        {
            return PostAsync<AttendanceReviewRequest, AttendanceDto>("org/attendance/" + id + "/review", request);
        }

        public Task<int> BatchReviewAttendanceAsync(AttendanceBatchReviewRequest request)
        {
            return PostAsync<AttendanceBatchReviewRequest, int>("org/attendance/batch-review", request);
        }

        // ==================== M6 便民电话簿（PG-TEL-01/02） ====================
        public Task<List<PhoneCategoryDto>> GetPhoneCategoriesAsync()
        {
            return GetAsync<List<PhoneCategoryDto>>("phonebook/categories");
        }

        public Task<PhoneCategoryDto> CreatePhoneCategoryAsync(PhoneCategoryRequest request)
        {
            return PostAsync<PhoneCategoryRequest, PhoneCategoryDto>("phonebook/categories", request);
        }

        public Task DeletePhoneCategoryAsync(int id)
        {
            return PostAsync<object, object>("phonebook/categories/" + id + "/delete", null);
        }

        public Task<List<PhoneTypeDto>> GetPhoneTypesAsync()
        {
            return GetAsync<List<PhoneTypeDto>>("phonebook/types");
        }

        public Task<PhoneTypeDto> CreatePhoneTypeAsync(PhoneTypeRequest request)
        {
            return PostAsync<PhoneTypeRequest, PhoneTypeDto>("phonebook/types", request);
        }

        public Task<PhoneTypeDto> UpdatePhoneTypeAsync(int id, PhoneTypeRequest request)
        {
            return PutAsync<PhoneTypeRequest, PhoneTypeDto>("phonebook/types/" + id, request);
        }

        public Task DeletePhoneTypeAsync(int id)
        {
            return PostAsync<object, object>("phonebook/types/" + id + "/delete", null);
        }

        public Task<PageResult<PhoneEntryDto>> QueryPhoneEntriesAsync(PhoneEntryQueryRequest request)
        {
            return GetAsync<PageResult<PhoneEntryDto>>("phonebook/entries" + Query(request));
        }

        public Task<PhoneEntryDto> CreatePhoneEntryAsync(PhoneEntryRequest request)
        {
            return PostAsync<PhoneEntryRequest, PhoneEntryDto>("phonebook/entries", request);
        }

        public Task<PhoneEntryDto> UpdatePhoneEntryAsync(int id, PhoneEntryRequest request)
        {
            return PutAsync<PhoneEntryRequest, PhoneEntryDto>("phonebook/entries/" + id, request);
        }

        public Task<PhoneEntryDto> SetPhoneEntryStatusAsync(int id, PhoneEntryStatus status)
        {
            return PostAsync<PhoneEntryStatusRequest, PhoneEntryDto>(
                "phonebook/entries/" + id + "/status", new PhoneEntryStatusRequest { Status = status });
        }

        public Task<PhoneEntryBatchStatusResultDto> BatchDisablePhoneEntriesAsync(PhoneEntryBatchStatusRequest request)
        {
            return PostAsync<PhoneEntryBatchStatusRequest, PhoneEntryBatchStatusResultDto>("phonebook/entries/batch-disable", request);
        }

        public Task<PhoneEntryDto> SetPhoneEntryTopAsync(int id, bool isTop)
        {
            return PostAsync<PhoneEntryTopRequest, PhoneEntryDto>(
                "phonebook/entries/" + id + "/top" + Query(new { isTop }), new PhoneEntryTopRequest { IsTop = isTop });
        }

        public Task<EmployeeSyncResultDto> SyncEmployeePhoneEntriesAsync(int categoryId)
        {
            return PostAsync<EmployeeSyncRequest, EmployeeSyncResultDto>(
                "phonebook/sync-employees", new EmployeeSyncRequest { CategoryId = categoryId });
        }

        // ==================== M6 纠纷调解（PG-DIS-01~03） ====================
        public Task<List<DisputeTypeDto>> GetDisputeTypesAsync()
        {
            return GetAsync<List<DisputeTypeDto>>("dispute/types");
        }

        public Task<DisputeTypeDto> CreateDisputeTypeAsync(DisputeTypeRequest request)
        {
            return PostAsync<DisputeTypeRequest, DisputeTypeDto>("dispute/types", request);
        }

        public Task DeleteDisputeTypeAsync(int id)
        {
            return PostAsync<object, object>("dispute/types/" + id + "/delete", null);
        }

        public Task<PageResult<DisputeCaseDto>> QueryDisputesAsync(DisputeQueryRequest request)
        {
            return GetAsync<PageResult<DisputeCaseDto>>("dispute/cases" + Query(request));
        }

        public Task<DisputeCaseDetailDto> GetDisputeAsync(int id)
        {
            return GetAsync<DisputeCaseDetailDto>("dispute/cases/" + id);
        }

        public Task<DisputeCaseDto> CreateDisputeAsync(DisputeCaseCreateRequest request)
        {
            return PostAsync<DisputeCaseCreateRequest, DisputeCaseDto>("dispute/cases", request);
        }

        public Task<DisputeCaseDto> UpdateDisputeAsync(int id, DisputeCaseUpdateRequest request)
        {
            return PutAsync<DisputeCaseUpdateRequest, DisputeCaseDto>("dispute/cases/" + id, request);
        }

        public Task<DisputeRecordDto> AddDisputeRecordAsync(int caseId, DisputeRecordRequest request)
        {
            return PostAsync<DisputeRecordRequest, DisputeRecordDto>("dispute/cases/" + caseId + "/records", request);
        }

        public Task DeleteDisputeAsync(int id)
        {
            return PostAsync<object, object>("dispute/cases/" + id + "/delete", null);
        }

        public Task<DisputeCaseDto> UpdateDisputeStatusAsync(int id, DisputeCaseStatusRequest request)
        {
            return PostAsync<DisputeCaseStatusRequest, DisputeCaseDto>("dispute/cases/" + id + "/status", request);
        }

        public Task<DisputeCaseDto> CloseDisputeAsync(int id, DisputeCloseRequest request)
        {
            return PostAsync<DisputeCloseRequest, DisputeCaseDto>("dispute/cases/" + id + "/close", request);
        }

        public Task<DisputeRecordDto> SupplementDisputeCaseAsync(int caseId, DisputeSupplementRequest request)
        {
            return PostAsync<DisputeSupplementRequest, DisputeRecordDto>("dispute/cases/" + caseId + "/supplement", request);
        }

        public Task<List<DisputeMediatorDto>> GetMediatorRecommendationsAsync(int? typeId = null, int? propertyId = null)
        {
            return GetAsync<List<DisputeMediatorDto>>("dispute/mediators/recommend" + Query(new { typeId, propertyId }));
        }

        public Task<DisputeStatisticsDto> GetDisputeStatisticsAsync()
        {
            return GetAsync<DisputeStatisticsDto>("dispute/statistics");
        }

        public Task<ReportLogDto> ExportDisputeCloseReportAsync(int caseId, ExportFormat format)
        {
            return PostAsync<DisputeCloseReportRequest, ReportLogDto>(
                "dispute/cases/" + caseId + "/report", new DisputeCloseReportRequest { Format = format });
        }

        public async Task DownloadReportFileAsync(int logId, string savePath)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, "reports/files/" + logId))
            {
                AddToken(req);
                HttpResponseMessage resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ApiClientException(ErrorCode.InternalError, "报表文件下载失败（HTTP " + (int)resp.StatusCode + "）");
                }
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                System.IO.File.WriteAllBytes(savePath, bytes);
            }
        }

        public Task<List<DisputeAttachmentDto>> GetDisputeAttachmentsAsync(int caseId)
        {
            return GetAsync<List<DisputeAttachmentDto>>("dispute/cases/" + caseId + "/attachments");
        }

        public async Task<DisputeAttachmentDto> UploadDisputeAttachmentAsync(int caseId, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
            {
                throw new ApiClientException(ErrorCode.ValidationFailed, "请选择要上传的扫描件文件");
            }

            using (var content = new MultipartFormDataContent())
            using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(GuessUploadContentType(filePath));
                content.Add(fileContent, "file", System.IO.Path.GetFileName(filePath));

                using (var req = new HttpRequestMessage(HttpMethod.Post, "dispute/cases/" + caseId + "/attachments"))
                {
                    req.Content = content;
                    AddToken(req);
                    return await SendAsync<DisputeAttachmentDto>(req);
                }
            }
        }

        public async Task DownloadDisputeAttachmentAsync(int caseId, int attachmentId, string savePath)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get,
                "dispute/cases/" + caseId + "/attachments/" + attachmentId + "/download"))
            {
                AddToken(req);
                HttpResponseMessage resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ApiClientException(ErrorCode.InternalError, "扫描件下载失败（HTTP " + (int)resp.StatusCode + "）");
                }
                byte[] bytes = await resp.Content.ReadAsByteArrayAsync();
                System.IO.File.WriteAllBytes(savePath, bytes);
            }
        }

        public Task DeleteDisputeAttachmentAsync(int caseId, int attachmentId)
        {
            return DeleteAsync<object>("dispute/cases/" + caseId + "/attachments/" + attachmentId);
        }

        /// <summary>按扩展名推断上传 MIME（服务端另有白名单校验兜底）。</summary>
        private static string GuessUploadContentType(string filePath)
        {
            switch ((System.IO.Path.GetExtension(filePath) ?? string.Empty).ToLowerInvariant())
            {
                case ".pdf": return "application/pdf";
                case ".png": return "image/png";
                default: return "image/jpeg";
            }
        }

        // ==================== M6 应急处置（PG-EMG-01~04） ====================
        public Task<List<EmergencySceneDto>> GetEmergencyScenesAsync(string keyword = null)
        {
            return GetAsync<List<EmergencySceneDto>>("emergency/scenes" + Query(new { keyword }));
        }

        public Task<EmergencySceneDto> CreateEmergencySceneAsync(EmergencySceneRequest request)
        {
            return PostAsync<EmergencySceneRequest, EmergencySceneDto>("emergency/scenes", request);
        }

        public Task<EmergencySceneDto> UpdateEmergencySceneAsync(int id, EmergencySceneRequest request)
        {
            return PutAsync<EmergencySceneRequest, EmergencySceneDto>("emergency/scenes/" + id, request);
        }

        public Task DeleteEmergencySceneAsync(int id)
        {
            return DeleteAsync<object>("emergency/scenes/" + id);
        }

        public Task<List<EmergencyStepDto>> GetEmergencyStepsAsync(int sceneId)
        {
            return GetAsync<List<EmergencyStepDto>>("emergency/scenes/" + sceneId + "/steps");
        }

        public Task<EmergencyStepDto> CreateEmergencyStepAsync(int sceneId, EmergencyStepRequest request)
        {
            return PostAsync<EmergencyStepRequest, EmergencyStepDto>("emergency/scenes/" + sceneId + "/steps", request);
        }

        public Task<EmergencyStepDto> UpdateEmergencyStepAsync(int id, EmergencyStepRequest request)
        {
            return PutAsync<EmergencyStepRequest, EmergencyStepDto>("emergency/steps/" + id, request);
        }

        public Task DeleteEmergencyStepAsync(int id)
        {
            return DeleteAsync<object>("emergency/steps/" + id);
        }

        public Task<PageResult<EmergencyEventDto>> QueryEmergencyEventsAsync(EmergencyEventQueryRequest request)
        {
            return GetAsync<PageResult<EmergencyEventDto>>("emergency/events" + Query(request));
        }

        public Task<EmergencyEventDetailDto> GetEmergencyEventAsync(int id)
        {
            return GetAsync<EmergencyEventDetailDto>("emergency/events/" + id);
        }

        public Task<EmergencyEventDetailDto> CreateEmergencyEventAsync(EmergencyEventCreateRequest request)
        {
            return PostAsync<EmergencyEventCreateRequest, EmergencyEventDetailDto>("emergency/events", request);
        }

        public Task<EmergencyEventDetailDto> AssignEmergencyAsync(int id, EmergencyAssignRequest request)
        {
            return PostAsync<EmergencyAssignRequest, EmergencyEventDetailDto>("emergency/events/" + id + "/assign", request);
        }

        public Task<EmergencyRecordDto> AddEmergencyRecordAsync(int id, EmergencyRecordRequest request)
        {
            return PostAsync<EmergencyRecordRequest, EmergencyRecordDto>("emergency/events/" + id + "/records", request);
        }

        public Task<EmergencyEventDetailDto> CloseEmergencyAsync(int id, EmergencyCloseRequest request)
        {
            return PostAsync<EmergencyCloseRequest, EmergencyEventDetailDto>("emergency/events/" + id + "/close", request);
        }

        public Task<List<EmergencyMatchDto>> GetEmergencyMatchesAsync(int sceneId)
        {
            return GetAsync<List<EmergencyMatchDto>>("emergency/scenes/" + sceneId + "/match");
        }

        public Task<EmergencySceneDto> SetEmergencySceneStatusAsync(int id, EmergencySceneStatusRequest request)
        {
            return PostAsync<EmergencySceneStatusRequest, EmergencySceneDto>("emergency/scenes/" + id + "/status", request);
        }

        public Task ReorderEmergencyStepsAsync(int sceneId, List<int> orderedIds)
        {
            return PostAsync<List<int>, object>("emergency/scenes/" + sceneId + "/steps/reorder", orderedIds);
        }

        public Task<EmergencyEventStatsDto> GetEmergencyEventStatsAsync()
        {
            return GetAsync<EmergencyEventStatsDto>("emergency/events/stats");
        }

        public Task CancelEmergencyEventAsync(int id)
        {
            return PostAsync<object, object>("emergency/events/" + id + "/cancel", null);
        }

        public Task<PageResult<EmergencyReviewDto>> QueryEmergencyReviewsAsync(PageRequest request)
        {
            return GetAsync<PageResult<EmergencyReviewDto>>("emergency/reviews" + Query(request ?? new PageRequest()));
        }

        public Task<EmergencyReviewDto> GetEmergencyReviewAsync(int eventId)
        {
            return GetAsync<EmergencyReviewDto>("emergency/reviews/" + eventId);
        }

        public Task<EmergencyReviewDto> ReviewEmergencyAsync(int id, EmergencyReviewRequest request)
        {
            return PostAsync<EmergencyReviewRequest, EmergencyReviewDto>("emergency/events/" + id + "/review", request);
        }

        // ==================== M6 设备台账（PG-EQP-01~04） ====================
        public Task<List<DeviceTypeDto>> GetDeviceTypesAsync()
        {
            return GetAsync<List<DeviceTypeDto>>("equipment/types");
        }

        public Task<DeviceTypeDto> CreateDeviceTypeAsync(DeviceTypeRequest request)
        {
            return PostAsync<DeviceTypeRequest, DeviceTypeDto>("equipment/types", request);
        }

        public Task DeleteDeviceTypeAsync(int id)
        {
            return DeleteAsync<object>("equipment/types/" + id);
        }

        public Task<List<MaintainTypeDto>> GetMaintainTypesAsync(bool includeDisabled = false)
        {
            return GetAsync<List<MaintainTypeDto>>("equipment/maintain-types" + Query(new { includeDisabled }));
        }

        public Task<PageResult<DeviceDto>> QueryDevicesAsync(DeviceQueryRequest request)
        {
            return GetAsync<PageResult<DeviceDto>>("equipment/devices" + Query(request));
        }

        public Task<DeviceDto> GetDeviceAsync(int id)
        {
            return GetAsync<DeviceDto>("equipment/devices/" + id);
        }

        public Task<DeviceDto> CreateDeviceAsync(DeviceRequest request)
        {
            return PostAsync<DeviceRequest, DeviceDto>("equipment/devices", request);
        }

        public Task<DeviceDto> UpdateDeviceAsync(int id, DeviceRequest request)
        {
            return PutAsync<DeviceRequest, DeviceDto>("equipment/devices/" + id, request);
        }

        public Task DeleteDeviceAsync(int id)
        {
            return DeleteAsync<object>("equipment/devices/" + id);
        }

        public Task<DeviceDto> ChangeDeviceStatusAsync(int id, DeviceStatusRequest request)
        {
            return PostAsync<DeviceStatusRequest, DeviceDto>("equipment/devices/" + id + "/status", request);
        }

        public Task<List<DeviceStatusLogDto>> GetDeviceStatusLogsAsync(int deviceId)
        {
            return GetAsync<List<DeviceStatusLogDto>>("equipment/devices/" + deviceId + "/status-logs");
        }

        public Task<List<MaintenanceRecordDto>> GetMaintenanceAsync(int deviceId)
        {
            return GetAsync<List<MaintenanceRecordDto>>("equipment/devices/" + deviceId + "/maintenance");
        }

        public Task<MaintenanceRecordDto> AddMaintenanceAsync(int deviceId, MaintenanceRecordRequest request)
        {
            return PostAsync<MaintenanceRecordRequest, MaintenanceRecordDto>("equipment/devices/" + deviceId + "/maintenance", request);
        }

        public Task<List<InspectionRecordDto>> GetInspectionAsync(int deviceId)
        {
            return GetAsync<List<InspectionRecordDto>>("equipment/devices/" + deviceId + "/inspection");
        }

        public Task<InspectionRecordDto> AddInspectionAsync(int deviceId, InspectionRecordRequest request)
        {
            return PostAsync<InspectionRecordRequest, InspectionRecordDto>("equipment/devices/" + deviceId + "/inspection", request);
        }

        public Task<List<FaultRecordDto>> GetFaultsAsync(int deviceId)
        {
            return GetAsync<List<FaultRecordDto>>("equipment/devices/" + deviceId + "/faults");
        }

        public Task<FaultRecordDto> AddFaultAsync(int deviceId, FaultRecordRequest request)
        {
            return PostAsync<FaultRecordRequest, FaultRecordDto>("equipment/devices/" + deviceId + "/faults", request);
        }

        public Task<List<VendorDto>> GetVendorsAsync()
        {
            return GetAsync<List<VendorDto>>("equipment/vendors");
        }

        public Task<VendorDto> CreateVendorAsync(VendorRequest request)
        {
            return PostAsync<VendorRequest, VendorDto>("equipment/vendors", request);
        }

        public Task DeleteVendorAsync(int id)
        {
            return DeleteAsync<object>("equipment/vendors/" + id);
        }

        public Task<List<EquipmentReminderDto>> GetEquipmentRemindersAsync(int days = 30, string type = null, int status = -1)
        {
            return GetAsync<List<EquipmentReminderDto>>("equipment/reminders" + Query(new { days, type, status }));
        }

        public Task<ReminderSummaryDto> GetEquipmentReminderSummaryAsync()
        {
            return GetAsync<ReminderSummaryDto>("equipment/reminders/summary");
        }

        public Task<EquipmentReminderDto> HandleEquipmentReminderAsync(int reminderId)
        {
            return PostAsync<object, EquipmentReminderDto>("equipment/reminders/" + reminderId + "/handle", null);
        }

        public Task<EquipmentReminderDto> SaveEquipmentReminderTeamAsync(int reminderId, ReminderTeamRequest request)
        {
            return PostAsync<ReminderTeamRequest, EquipmentReminderDto>("equipment/reminders/" + reminderId + "/team", request);
        }

        public Task<ReminderBatchDeleteResultDto> BatchDeleteEquipmentRemindersAsync(ReminderBatchDeleteRequest request)
        {
            return PostAsync<ReminderBatchDeleteRequest, ReminderBatchDeleteResultDto>("equipment/reminders/batch-delete", request);
        }

        public Task<List<DeviceCustomRecordDto>> GetDeviceCustomRecordsAsync(int deviceId)
        {
            return GetAsync<List<DeviceCustomRecordDto>>("equipment/devices/" + deviceId + "/custom-records");
        }

        public Task<DeviceCustomRecordDto> CreateDeviceCustomRecordAsync(int deviceId, DeviceCustomRecordRequest request)
        {
            return PostAsync<DeviceCustomRecordRequest, DeviceCustomRecordDto>("equipment/devices/" + deviceId + "/custom-records", request);
        }

        public Task<DeviceCustomRecordDto> UpdateDeviceCustomRecordAsync(int recordId, DeviceCustomRecordRequest request)
        {
            return PutAsync<DeviceCustomRecordRequest, DeviceCustomRecordDto>("equipment/custom-records/" + recordId, request);
        }

        public Task DeleteDeviceCustomRecordAsync(int recordId)
        {
            return DeleteAsync<object>("equipment/custom-records/" + recordId);
        }

        public Task<List<string>> GetCustomRecordTypesAsync()
        {
            return GetAsync<List<string>>("equipment/custom-record-types");
        }

        public Task<DeviceSummaryDto> GetDeviceSummaryAsync()
        {
            return GetAsync<DeviceSummaryDto>("equipment/devices/summary");
        }

        public Task<EquipmentExportResultDto> ExportDevicesAsync(DeviceQueryRequest request)
        {
            return PostAsync<DeviceQueryRequest, EquipmentExportResultDto>("equipment/devices/exports", request ?? new DeviceQueryRequest());
        }

        public Task<DeviceTypeDto> UpdateDeviceTypeAsync(int id, DeviceTypeRequest request)
        {
            return PutAsync<DeviceTypeRequest, DeviceTypeDto>("equipment/types/" + id, request);
        }

        public Task<VendorDto> UpdateVendorAsync(int id, VendorRequest request)
        {
            return PutAsync<VendorRequest, VendorDto>("equipment/vendors/" + id, request);
        }

        public Task<List<FaultRecordDto>> GetAllFaultsAsync()
        {
            return GetAsync<List<FaultRecordDto>>("equipment/faults");
        }

        public Task<FaultRecordDto> HandleFaultAsync(int faultId, FaultHandleRequest request)
        {
            return PutAsync<FaultHandleRequest, FaultRecordDto>("equipment/faults/" + faultId + "/handle", request);
        }

        public Task<EquipmentExportResultDto> GenerateFaultWorkOrderAsync(int faultId)
        {
            return PostAsync<object, EquipmentExportResultDto>("equipment/faults/" + faultId + "/work-order", null);
        }

        // ==================== M6 系统设置（PG-COM-01~04） ====================
        public Task<List<DictTypeDto>> GetSystemDictTypesAsync()
        {
            return GetAsync<List<DictTypeDto>>("system/dict-types");
        }

        public Task<DictTypeDto> CreateSystemDictTypeAsync(DictTypeRequest request)
        {
            return PostAsync<DictTypeRequest, DictTypeDto>("system/dict-types", request);
        }

        public Task<List<DictItemDto>> GetSystemDictItemsAsync(string typeCode, bool includeDisabled = false, int? status = null)
        {
            return GetAsync<List<DictItemDto>>("system/dict-types/" + Uri.EscapeDataString(typeCode) + "/items" + Query(new { includeDisabled, status }));
        }

        public Task<DictItemDto> UpdateDictItemAsync(int id, DictItemRequest request)
        {
            return PutAsync<DictItemRequest, DictItemDto>("system/dict-items/" + id, request);
        }

        public Task<DictItemDto> SetDictItemStatusAsync(int id, DictItemStatus status)
        {
            return PostAsync<DictItemRequest, DictItemDto>("system/dict-items/" + id + "/status", new DictItemRequest { Status = status });
        }

        public Task<DictItemBatchDeleteResultDto> BatchDeleteDictItemsAsync(DictItemBatchDeleteRequest request)
        {
            return PostAsync<DictItemBatchDeleteRequest, DictItemBatchDeleteResultDto>(
                "system/dict-items/batch-delete", request ?? new DictItemBatchDeleteRequest());
        }

        public Task<RecordBatchDeleteResultDto> BatchDeleteAuditLogsAsync(RecordBatchDeleteRequest request)
        {
            return PostAsync<RecordBatchDeleteRequest, RecordBatchDeleteResultDto>(
                "system/audit-logs/batch-delete", request ?? new RecordBatchDeleteRequest());
        }

        public Task<RecordBatchDeleteResultDto> BatchDeleteBackupRecordsAsync(RecordBatchDeleteRequest request)
        {
            return PostAsync<RecordBatchDeleteRequest, RecordBatchDeleteResultDto>(
                "system/backups/batch-delete", request ?? new RecordBatchDeleteRequest());
        }

        public Task<PurgeSoftDeletedResultDto> PurgeSoftDeletedAsync()
        {
            return PostAsync<object, PurgeSoftDeletedResultDto>("system/cleanup/soft-deleted", new { });
        }

        public Task<List<ParamDto>> GetSystemParamsAsync()
        {
            return GetAsync<List<ParamDto>>("system/params");
        }

        public Task SetSystemParamAsync(string key, string value)
        {
            return PutAsync<ParamValueRequest, object>("system/params/" + Uri.EscapeDataString(key), new ParamValueRequest { Value = value });
        }

        public Task<List<BackupDto>> GetSystemBackupsAsync()
        {
            return GetAsync<List<BackupDto>>("system/backups");
        }

        public Task<BackupDto> RunSystemBackupAsync(string note)
        {
            return RunSystemBackupAsync(note, null);
        }

        public Task<BackupDto> RunSystemBackupAsync(string note, string targetPath)
        {
            return PostAsync<BackupCreateRequest, BackupDto>("system/backups/run",
                new BackupCreateRequest { Note = note, TargetPath = targetPath });
        }

        public Task<BackupDto> RestoreSystemBackupAsync(BackupRestoreRequest request)
        {
            int id = request != null ? request.BackupId : 0;
            return PostAsync<BackupRestoreRequest, BackupDto>("system/backups/" + id + "/restore", request ?? new BackupRestoreRequest { BackupId = id });
        }

        public Task<BackupStatusDto> GetSystemBackupStatusAsync()
        {
            return GetAsync<BackupStatusDto>("system/backups/status");
        }

        public Task<AuditExportDto> ExportAuditLogsAsync(AuditLogQueryRequest request)
        {
            return GetAsync<AuditExportDto>("system/audit-logs/export" + Query(request ?? new AuditLogQueryRequest()));
        }

        public Task<PageResult<AuditLogDto>> QueryAuditLogsAsync(AuditLogQueryRequest request)
        {
            return GetAsync<PageResult<AuditLogDto>>("system/audit-logs" + Query(request));
        }

        // ==================== 基础 HTTP 设施 ====================

        private async Task<T> GetAsync<T>(string url)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                AddToken(req);
                return await SendAsync<T>(req);
            }
        }

        private async Task<byte[]> GetRawBytesAsync(string url)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            {
                AddToken(req);
                HttpResponseMessage resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new ApiClientException(ErrorCode.InternalError, "文件下载失败（HTTP " + (int)resp.StatusCode + "）");
                }
                return await resp.Content.ReadAsByteArrayAsync();
            }
        }

        private async Task<TResult> PostAsync<TBody, TResult>(string url, TBody body)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, url))
            {
                if (body != null)
                {
                    req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                }
                AddToken(req);
                return await SendAsync<TResult>(req);
            }
        }

        private async Task<TResult> PutAsync<TBody, TResult>(string url, TBody body)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Put, url))
            {
                if (body != null)
                {
                    req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
                }
                AddToken(req);
                return await SendAsync<TResult>(req);
            }
        }

        private async Task<T> DeleteAsync<T>(string url)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Delete, url))
            {
                AddToken(req);
                return await SendAsync<T>(req);
            }
        }

        private void AddToken(HttpRequestMessage req)
        {
            var session = SessionManager.Instance.Current;
            if (session != null && !string.IsNullOrEmpty(session.Token))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
            }
        }

        /// <summary>把查询对象序列化为查询串（驼峰、忽略 null；bool 输出 true/false）。</summary>
        private static string Query(object query)
        {
            if (query == null)
            {
                return string.Empty;
            }
            var parts = new List<string>();
            foreach (PropertyInfo prop in query.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead)
                {
                    continue;
                }
                object value = prop.GetValue(query, null);
                if (value == null)
                {
                    continue;
                }
                string sValue = value as string;
                if (sValue != null && sValue.Length == 0)
                {
                    continue;
                }
                string name = Char.ToLowerInvariant(prop.Name[0]) + prop.Name.Substring(1);
                string text;
                if (value is DateTime)
                {
                    text = ((DateTime)value).ToString("yyyy-MM-dd");
                }
                else if (value is bool)
                {
                    text = (bool)value ? "true" : "false";
                }
                else if (value is Enum)
                {
                    text = Convert.ToInt32(value).ToString();
                }
                else
                {
                    text = value.ToString();
                }
                parts.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(text));
            }
            return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
        }

        private async Task<T> SendAsync<T>(HttpRequestMessage req)
        {
            HttpResponseMessage resp = await _http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();

            // CHG-v1.1.2-14：后端与客户端版本错配的兜底提示。
            // 现象：新客户端打到旧后端（如已安装的 1.1.1 实例）时，新端点返回 HTTP 404，
            // 用户只看到「服务器响应异常（HTTP 404）」无从判断。这里统一翻译为可行动的中文提示。
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new ApiClientException(ErrorCode.InternalError,
                    "后端服务版本过旧，不支持该功能（HTTP 404）：请关闭旧的后端服务并启动与本客户端同版本的后端后再试");
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                if (resp.IsSuccessStatusCode) return default(T);
                HandleUnauthorized(req, resp,
                    resp.StatusCode == System.Net.HttpStatusCode.Unauthorized ? ErrorCode.Unauthorized : ErrorCode.InternalError,
                    "服务响应异常（HTTP " + (int)resp.StatusCode + "）");
                throw new ApiClientException(ErrorCode.InternalError, "服务响应异常（HTTP " + (int)resp.StatusCode + "）");
            }

            ApiResponse<T> envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<ApiResponse<T>>(json);
            }
            catch (JsonException)
            {
                // 服务端业务错误信封为 ApiResponse&lt;object&gt;，data=null 在 T 为值类型（如 bool）时无法反序列化，
                // 导致向前端暴露英文 "Error converting value {null}..."。此处退化为 object 信封提取中文业务错误。
                if (!resp.IsSuccessStatusCode)
                {
                    ApiResponse<object> err = null;
                    try { err = JsonConvert.DeserializeObject<ApiResponse<object>>(json); } catch (JsonException) { }
                    if (err != null)
                    {
                        HandleUnauthorized(req, resp, err.Code, err.Message);
                        throw new ApiClientException(err.Code, string.IsNullOrEmpty(err.Message) ? "请求处理失败（HTTP " + (int)resp.StatusCode + "）" : err.Message);
                    }
                }
                throw new ApiClientException(ErrorCode.InternalError, "服务响应异常（HTTP " + (int)resp.StatusCode + "）");
            }

            if (envelope == null || envelope.Code != ErrorCode.Success)
            {
                int code = envelope == null ? ErrorCode.InternalError : envelope.Code;
                string message = envelope == null
                    ? "服务响应异常（HTTP " + (int)resp.StatusCode + "）"
                    : (string.IsNullOrEmpty(envelope.Message) ? "请求处理失败（HTTP " + (int)resp.StatusCode + "）" : envelope.Message);
                HandleUnauthorized(req, resp, code, message);
                throw new ApiClientException(code, message);
            }
            return envelope.Data;
        }

        /// <summary>
        /// 401 自愈（M7 修复）：token 失效时清空本地会话并回落登录页。
        /// 排除 auth/login —— 登录失败同样返回 40100，属业务错误而非会话失效。
        /// </summary>
        private static void HandleUnauthorized(HttpRequestMessage req, HttpResponseMessage resp, int code, string message)
        {
            bool unauthorized = code == ErrorCode.Unauthorized ||
                                resp.StatusCode == System.Net.HttpStatusCode.Unauthorized;
            if (!unauthorized)
            {
                return;
            }

            string path = req.RequestUri == null ? string.Empty : req.RequestUri.AbsolutePath;
            if (path.EndsWith("auth/login", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SessionManager.Instance.NotifyExpired(
                // 说明：结尾统一由 MainWindow 追加「，请重新登录。」，此处不重复拼接（避免「…请重新登录，请重新登录。」）
                string.IsNullOrWhiteSpace(message) ? "登录状态已失效" : ("登录状态已失效：" + message));
        }
    }
}

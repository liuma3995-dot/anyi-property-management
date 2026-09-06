using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PropertyManagement.Contract.Auth;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Contract.Health;

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

        // ==================== M4 财务收费（PG-FIN-01~08） ====================
        Task<List<ChargeItemDto>> GetChargeItemsAsync(string keyword = null, string category = null);
        Task<List<DictItemDto>> GetDictItemsAsync(string typeCode);
        Task<DictItemDto> CreateDictItemAsync(string typeCode, DictItemCreateRequest request);
        Task<ChargeItemDto> CreateChargeItemAsync(ChargeItemRequest request);
        Task<ChargeItemDto> UpdateChargeItemAsync(int id, ChargeItemRequest request);
        Task DeleteChargeItemAsync(int id);
        Task<List<BillingCycleDto>> GetCyclesAsync();
        Task<BillingCycleDto> CreateCycleAsync(BillingCycleRequest request);
        Task<BillGenerateLogDto> GenerateBillsAsync(BillGenerateRequest request);
        Task<BillGenerateLogDto> PublishBillsAsync(BillPublishRequest request);
        Task<BillGenerateLogDto> RetryFailuresAsync(int batchId);
        Task<List<BillBatchDto>> GetGenerateLogsAsync();
        Task DeleteBillBatchAsync(int batchId);
        Task<List<BillFailureDto>> GetFailuresAsync(int batchId);
        Task<PageResult<BillListItemDto>> QueryBillsAsync(BillQueryRequest request);
        Task<PageResult<ArrearDto>> QueryArrearsAsync(BillQueryRequest request);
        Task RecordRemindAsync(ArrearRemindRequest request);
        Task DeleteArrearAsync(int billId);
        Task<PaymentStatisticsDto> GetPaymentStatisticsAsync();
        Task<PaymentDto> CreatePaymentAsync(PaymentCreateRequest request);
        Task<PaymentDto> GetPaymentAsync(int id);
        Task<ReceiptDto> GetReceiptByPaymentAsync(int paymentId);
        Task<ReceiptDto> PrintReceiptAsync(int receiptId);
        Task<PreDepositDto> GetPreDepositAsync(int ownerId);
        Task<RefundAdjustmentDto> CreateRefundAsync(RefundAdjustmentRequest request);
        Task<PageResult<RefundAdjustmentDto>> QueryRefundsAsync(PageRequest request);
        Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync();
        Task<ExpenseDto> CreateExpenseAsync(ExpenseCreateRequest request);
        Task<PageResult<ExpenseDto>> QueryExpensesAsync(PageRequest request);
        Task DeleteExpenseAsync(int id);
        Task<PageResult<LedgerEntryDto>> GetLedgerAsync(LedgerQueryRequest request);
        Task<FinancialReportDto> GetFinancialReportAsync(FinancialReportQueryRequest request);
        Task<ReportLogDto> ExportReportAsync(ReportExportRequest request);

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
        Task<byte[]> DownloadImportErrorsAsync(int id);
        Task<ExportLogDto> ExportAsync(BaseInfoExportRequest request);
        Task DownloadExportFileAsync(int id, string savePath);
        Task<string> GetParamAsync(string key);
        Task SetParamAsync(string key, string value);
    }
}

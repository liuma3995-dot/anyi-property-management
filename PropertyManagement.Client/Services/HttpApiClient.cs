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

        // ==================== M4 财务收费 ====================

        public Task<List<ChargeItemDto>> GetChargeItemsAsync(string keyword = null, string category = null)
        {
            return GetAsync<List<ChargeItemDto>>("billing/charge-items" + Query(new { keyword, category }));
        }

        public Task<List<DictItemDto>> GetDictItemsAsync(string typeCode)
        {
            return GetAsync<List<DictItemDto>>("dicts/" + Uri.EscapeDataString(typeCode));
        }

        public Task<DictItemDto> CreateDictItemAsync(string typeCode, DictItemCreateRequest request)
        {
            return PostAsync<DictItemCreateRequest, DictItemDto>("dicts/" + Uri.EscapeDataString(typeCode) + "/items", request);
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

        public Task<BillingCycleDto> CreateCycleAsync(BillingCycleRequest request)
        {
            return PostAsync<BillingCycleRequest, BillingCycleDto>("billing/cycles", request);
        }
        public Task<List<BillingCycleDto>> GetCyclesAsync()
        {
            return GetAsync<List<BillingCycleDto>>("billing/cycles");
        }

        public Task<BillGenerateLogDto> GenerateBillsAsync(BillGenerateRequest request)
        {
            return PostAsync<BillGenerateRequest, BillGenerateLogDto>("billing/bills/generate", request);
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

        public Task<PaymentStatisticsDto> GetPaymentStatisticsAsync()
        {
            return GetAsync<PaymentStatisticsDto>("billing/statistics/payment");
        }

        public Task<PaymentDto> CreatePaymentAsync(PaymentCreateRequest request)
        {
            return PostAsync<PaymentCreateRequest, PaymentDto>("payments", request);
        }

        public Task<PaymentDto> GetPaymentAsync(int id)
        {
            return GetAsync<PaymentDto>("payments/" + id);
        }

        public Task<ReceiptDto> GetReceiptByPaymentAsync(int paymentId)
        {
            return GetAsync<ReceiptDto>("payments/receipts/" + paymentId);
        }

        public Task<ReceiptDto> PrintReceiptAsync(int receiptId)
        {
            return PostAsync<ReceiptPrintRequest, ReceiptDto>(
                "payments/receipts/" + receiptId + "/print", new ReceiptPrintRequest { ReceiptId = receiptId });
        }

        public Task<PreDepositDto> GetPreDepositAsync(int ownerId)
        {
            return GetAsync<PreDepositDto>("payments/pre-deposits/" + ownerId);
        }

        public Task<RefundAdjustmentDto> CreateRefundAsync(RefundAdjustmentRequest request)
        {
            return PostAsync<RefundAdjustmentRequest, RefundAdjustmentDto>("payments/refunds", request);
        }

        public Task<PageResult<RefundAdjustmentDto>> QueryRefundsAsync(PageRequest request)
        {
            return GetAsync<PageResult<RefundAdjustmentDto>>("payments/refunds" + Query(request));
        }

        public Task<List<ExpenseCategoryDto>> GetExpenseCategoriesAsync()
        {
            return GetAsync<List<ExpenseCategoryDto>>("expenses/categories");
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

            if (string.IsNullOrWhiteSpace(json) && resp.IsSuccessStatusCode)
            {
                return default(T);
            }

            var envelope = JsonConvert.DeserializeObject<ApiResponse<T>>(json);
            if (envelope == null || envelope.Code != ErrorCode.Success)
            {
                int code = envelope == null ? ErrorCode.InternalError : envelope.Code;
                string message = envelope == null
                    ? "服务响应异常（HTTP " + (int)resp.StatusCode + "）"
                    : envelope.Message;
                throw new ApiClientException(code, message);
            }
            return envelope.Data;
        }
    }
}

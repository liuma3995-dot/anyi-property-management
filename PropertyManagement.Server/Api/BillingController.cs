using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>财务-账单端点（D4-1/D4-5，UC-FIN-001/002/007/008，FL-FIN-01）。</summary>
    [RoutePrefix("api/v1/billing")]
    public class BillingController : ApiController
    {
        private readonly BillingService _billing;

        public BillingController()
        {
            _billing = new BillingService();
        }

        // ---------- 收费项目 ----------
        [HttpGet]
        [Route("charge-items")]
        public ApiResponse<List<ChargeItemDto>> ListChargeItems(string keyword = null, string category = null)
        {
            return ApiResponse<List<ChargeItemDto>>.Ok(_billing.ListChargeItems(keyword, category));
        }

        [HttpPost]
        [Route("charge-items")]
        public ApiResponse<ChargeItemDto> CreateChargeItem(ChargeItemRequest request)
        {
            return ApiResponse<ChargeItemDto>.Ok(_billing.CreateChargeItem(request));
        }

        [HttpPut]
        [Route("charge-items/{id:int}")]
        public ApiResponse<ChargeItemDto> UpdateChargeItem(int id, ChargeItemRequest request)
        {
            return ApiResponse<ChargeItemDto>.Ok(_billing.UpdateChargeItem(id, request));
        }

        [HttpDelete]
        [Route("charge-items/{id:int}")]
        public ApiResponse<object> DeleteChargeItem(int id)
        {
            _billing.DeleteChargeItem(id);
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 计费周期 ----------
        [HttpGet]
        [Route("cycles")]
        public ApiResponse<List<BillingCycleDto>> ListCycles()
        {
            return ApiResponse<List<BillingCycleDto>>.Ok(_billing.ListCycles());
        }

        [HttpPost]
        [Route("cycles")]
        public ApiResponse<BillingCycleDto> CreateCycle(BillingCycleRequest request)
        {
            return ApiResponse<BillingCycleDto>.Ok(_billing.CreateCycle(request));
        }

        [HttpPut]
        [Route("cycles/{id:int}")]
        public ApiResponse<BillingCycleDto> UpdateCycle(int id, BillingCycleRequest request)
        {
            return ApiResponse<BillingCycleDto>.Ok(_billing.UpdateCycle(id, request));
        }

        [HttpDelete]
        [Route("cycles/{id:int}")]
        public ApiResponse<object> DeleteCycle(int id)
        {
            _billing.DeleteCycle(id);
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 账单生成/发布/失败重推 ----------
        [HttpPost]
        [Route("bills/generate")]
        public ApiResponse<BillGenerateLogDto> GenerateBill(BillGenerateRequest request)
        {
            return ApiResponse<BillGenerateLogDto>.Ok(_billing.GenerateBill(request));
        }

        [HttpPost]
        [Route("bills/publish")]
        public ApiResponse<BillGenerateLogDto> PublishBills(BillPublishRequest request)
        {
            return ApiResponse<BillGenerateLogDto>.Ok(_billing.PublishBills(request));
        }

        [HttpPost]
        [Route("bills/generate-logs/{id:int}/retry")]
        public ApiResponse<BillGenerateLogDto> RetryFailures(int id, BillRetryRequest request)
        {
            request = request ?? new BillRetryRequest { BatchId = id };
            request.BatchId = id;
            return ApiResponse<BillGenerateLogDto>.Ok(_billing.RetryFailures(request));
        }

        [HttpGet]
        /// <summary>账单生成批次列表（CHG-M4-10：PG-FIN-02 批次工作台）。</summary>
        [Route("bills/generate-logs")]
        public ApiResponse<List<BillBatchDto>> ListGenerateLogs()
        {
            return ApiResponse<List<BillBatchDto>>.Ok(_billing.QueryGenerateLogs());
        }

        [HttpPost]
        [Route("bills/generate-logs/delete")]
        public ApiResponse<object> DeleteBatch(BillBatchDeleteRequest request)
        {
            if (request == null || request.BatchId <= 0)
            {
                throw ApiException.BadRequest("批次不能为空");
            }
            _billing.DeleteBillBatch(request.BatchId);
            return ApiResponse<object>.Ok(null);
        }

        [Route("bills/generate-logs/{id:int}")]
        public ApiResponse<BillGenerateLogDto> GetGenerateLog(int id)
        {
            return ApiResponse<BillGenerateLogDto>.Ok(_billing.GetGenerateLog(id));
        }

        [HttpGet]
        [Route("bills/generate-logs/{id:int}/failures")]
        public ApiResponse<List<BillFailureDto>> ListFailures(int id)
        {
            return ApiResponse<List<BillFailureDto>>.Ok(_billing.ListFailures(id));
        }

        [HttpGet]
        [Route("bills/generate-logs/{id:int}/drafts")]
        public ApiResponse<List<BillListItemDto>> ListDrafts(int id)
        {
            return ApiResponse<List<BillListItemDto>>.Ok(_billing.ListDrafts(id));
        }

        [HttpGet]
        [Route("bills/generate-logs/{id:int}/published")]
        public ApiResponse<List<BillListItemDto>> ListPublished(int id)
        {
            return ApiResponse<List<BillListItemDto>>.Ok(_billing.ListPublished(id));
        }

        // ---------- 账单列表/统计/欠费 ----------
        [HttpGet]
        [Route("bills")]
        public ApiResponse<PageResult<BillListItemDto>> QueryBills([FromUri] BillQueryRequest request)
        {
            return ApiResponse<PageResult<BillListItemDto>>.Ok(_billing.QueryBills(request));
        }

        [HttpGet]
        [Route("bills/arrears")]
        public ApiResponse<PageResult<ArrearDto>> QueryArrears([FromUri] BillQueryRequest request)
        {
            return ApiResponse<PageResult<ArrearDto>>.Ok(_billing.QueryArrears(request));
        }

        [HttpPost]
        [Route("bills/remind")]
        public ApiResponse<object> RecordRemind(ArrearRemindRequest request)
        {
            _billing.RecordRemind(request);
            return ApiResponse<object>.Ok(null);
        }

        [HttpDelete]
        [Route("bills/{id:int}")]
        public ApiResponse<object> DeleteBill(int id)
        {
            _billing.DeleteArrearBill(id);
            return ApiResponse<object>.Ok(null);
        }

        [HttpGet]
        [Route("statistics/payment")]
        public ApiResponse<PaymentStatisticsDto> GetPaymentStatistics()
        {
            return ApiResponse<PaymentStatisticsDto>.Ok(_billing.GetPaymentStatistics());
        }
    }
}

using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>财务-收款/收据/退款端点（D4-2/D4-3，UC-FIN-003/004/011，P-06）。</summary>
    [RoutePrefix("api/v1/payments")]
    public class PaymentsController : ApiController
    {
        private readonly PaymentService _payments;

        public PaymentsController()
        {
            _payments = new PaymentService();
        }

        [HttpPost]
        [Route("")]
        public ApiResponse<PaymentDto> CreatePayment(PaymentCreateRequest request)
        {
            return ApiResponse<PaymentDto>.Ok(_payments.CreatePayment(request, GetUsername(), GetIp()));
        }

        /// <summary>统一收款（CHG-v1.1.0-12）：对同一缴费对象下的多张账单一次性收款，共享收款流水号。</summary>
        [HttpPost]
        [Route("batch")]
        public ApiResponse<PaymentBatchResultDto> CreateBatchPayment(PaymentBatchCreateRequest request)
        {
            return ApiResponse<PaymentBatchResultDto>.Ok(_payments.CreateBatchPayment(request, GetUsername(), GetIp()));
        }

        [HttpGet]
        [Route("")]
        public ApiResponse<PageResult<PaymentDto>> QueryPayments([FromUri] PageRequest request)
        {
            return ApiResponse<PageResult<PaymentDto>>.Ok(_payments.QueryPayments(request));
        }

        [HttpGet]
        [Route("pre-deposits/{ownerId:int}")]
        public ApiResponse<PreDepositDto> GetPreDeposit(int ownerId)
        {
            return ApiResponse<PreDepositDto>.Ok(_payments.GetPreDeposit(ownerId));
        }

        [HttpPost]
        [Route("pre-deposits/refund")]
        public ApiResponse<PreDepositDto> RefundPreDeposit(PreDepositRefundRequest request)
        {
            return ApiResponse<PreDepositDto>.Ok(_payments.RefundPreDeposit(request, GetUsername(), GetIp()));
        }

        [HttpPost]
        [Route("refunds")]
        public ApiResponse<RefundAdjustmentDto> CreateRefund(RefundAdjustmentRequest request)
        {
            return ApiResponse<RefundAdjustmentDto>.Ok(_payments.CreateRefund(request, GetUsername(), GetIp()));
        }

        /// <summary>批量退款/减免/调整（CHG-v1.1.0-13）：多张账单各登记一条记录（每张金额口径）。</summary>
        [HttpPost]
        [Route("refunds/batch")]
        public ApiResponse<RefundBatchResultDto> CreateRefundBatch(RefundAdjustmentRequest request)
        {
            return ApiResponse<RefundBatchResultDto>.Ok(_payments.CreateRefundBatch(request, GetUsername(), GetIp()));
        }

        [HttpGet]
        [Route("refunds")]
        public ApiResponse<PageResult<RefundAdjustmentDto>> QueryRefunds([FromUri] PageRequest request)
        {
            return ApiResponse<PageResult<RefundAdjustmentDto>>.Ok(_payments.QueryRefunds(request));
        }

        /// <summary>
        /// 删除退款/减免/调整记录（CHG-v1.4.0-01 初版；CHG-v1.5.0-01 放开为全部记录可删）。
        /// 软删留痕；调整（冲正/补收）删除 → 报表/流水移除金额并同步回退关联账单实缴；退款/减免只删记录。
        /// </summary>
        [HttpPost]
        [Route("refunds/delete")]
        public ApiResponse<RefundAdjustmentDto> DeleteRefund(RefundDeleteRequest request)
        {
            return ApiResponse<RefundAdjustmentDto>.Ok(_payments.DeleteRefund(request, GetUsername(), GetIp()));
        }

        /// <summary>
        /// 批量删除退款/减免/调整记录（CHG-v1.5.0-01，负责人 2026-10-11 裁定）：
        /// 支持勾选/全选后一次删除；口径与单条一致（调整回退账单，退款/减免只删记录）。
        /// </summary>
        [HttpPost]
        [Route("refunds/delete-batch")]
        public ApiResponse<RefundBatchDeleteResultDto> DeleteRefundBatch(RefundDeleteBatchRequest request)
        {
            return ApiResponse<RefundBatchDeleteResultDto>.Ok(_payments.DeleteRefundBatch(request, GetUsername(), GetIp()));
        }

        [HttpGet]
        [Route("{id:int}")]
        public ApiResponse<PaymentDto> GetPayment(int id)
        {
            return ApiResponse<PaymentDto>.Ok(_payments.GetPayment(id));
        }

        // CHG-v1.1.0-15：收据号前后端下线 —— 原「收据查询 / 收据打印·补打」端点已移除，
        // 收款凭据统一由「导出打印收据预览模板」（/reports/receipt-template）承载。

        /// <summary>操作人（BR-ORG-01 审计八列）：从鉴权中间件写入的 OWIN 环境读取。</summary>
        private string GetUsername()
        {
            object value;
            if (Request.Properties.TryGetValue("MS_OwinContext", out value))
            {
                var owinContext = value as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Get<string>(AuthMiddleware.UsernameEnvKey) ?? string.Empty;
                }
            }
            return string.Empty;
        }

        /// <summary>客户端 IP（BR-ORG-01 审计八列）。</summary>
        private string GetIp()
        {
            object value;
            if (Request.Properties.TryGetValue("MS_OwinContext", out value))
            {
                var owinContext = value as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Request.RemoteIpAddress;
                }
            }
            return null;
        }
    }
}

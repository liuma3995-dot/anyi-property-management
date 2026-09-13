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

        [HttpGet]
        [Route("refunds")]
        public ApiResponse<PageResult<RefundAdjustmentDto>> QueryRefunds([FromUri] PageRequest request)
        {
            return ApiResponse<PageResult<RefundAdjustmentDto>>.Ok(_payments.QueryRefunds(request));
        }

        [HttpGet]
        [Route("{id:int}")]
        public ApiResponse<PaymentDto> GetPayment(int id)
        {
            return ApiResponse<PaymentDto>.Ok(_payments.GetPayment(id));
        }

        [HttpGet]
        [Route("receipts/{id:int}")]
        public ApiResponse<ReceiptDto> GetReceipt(int id)
        {
            return ApiResponse<ReceiptDto>.Ok(_payments.GetReceipt(id));
        }

        [HttpPost]
        [Route("receipts/{id:int}/print")]
        public ApiResponse<ReceiptDto> PrintReceipt(int id, ReceiptPrintRequest request)
        {
            return ApiResponse<ReceiptDto>.Ok(
                _payments.PrintReceipt(new ReceiptPrintRequest { ReceiptId = id }, GetUsername(), GetIp()));
        }

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

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>财务-报表/流水端点（D4-6，UC-FIN-009/010/012）。</summary>
    [RoutePrefix("api/v1/reports")]
    public class ReportsController : ApiController
    {
        private readonly ReportService _reports;

        public ReportsController()
        {
            _reports = new ReportService();
        }

        [HttpGet]
        [Route("ledger")]
        public ApiResponse<PageResult<LedgerEntryDto>> QueryLedger([FromUri] LedgerQueryRequest request)
        {
            return ApiResponse<PageResult<LedgerEntryDto>>.Ok(_reports.QueryLedger(request));
        }

        [HttpGet]
        [Route("financial")]
        public ApiResponse<FinancialReportDto> GetFinancialReport([FromUri] FinancialReportQueryRequest request)
        {
            return ApiResponse<FinancialReportDto>.Ok(_reports.GetFinancialReport(request));
        }

        [HttpPost]
        [Route("export")]
        public ApiResponse<ReportLogDto> ExportReport(ReportExportRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportReport(request));
        }

        /// <summary>收支明细流水导出（CHG-v1.1.2-05，Excel/PDF，按当前筛选条件导出明细）。</summary>
        [HttpPost]
        [Route("ledger/export")]
        public ApiResponse<ReportLogDto> ExportLedger(LedgerExportRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportLedger(request));
        }

        /// <summary>收费项目清单导出（CHG-v1.1.2-33，PDF / Excel，按当前筛选条件导出）。</summary>
        [HttpPost]
        [Route("charge-items/export")]
        public ApiResponse<ReportLogDto> ExportChargeItems(ChargeItemExportRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportChargeItems(request));
        }

        /// <summary>导出收据打印模板（CHG-v1.1.0-14：收据号下线，模板含逐项收款明细）。</summary>
        [HttpPost]
        [Route("receipt-template")]
        public ApiResponse<ReportLogDto> ExportReceiptTemplate(ReceiptTemplateRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportReceiptTemplate(request));
        }

        /// <summary>
        /// 导出退款/减免/调整单据 PDF（CHG-v1.1.2-41）：提交后留档、审计追溯用，
        /// 金额与账单口径由服务端按单据主键回查，导出动作写 t_report_log 与审计日志。
        /// </summary>
        [HttpPost]
        [Route("refund-record")]
        public ApiResponse<ReportLogDto> ExportRefundRecord(RefundRecordExportRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportRefundRecord(request, GetUsername(), GetIp()));
        }

        [HttpGet]
        [Route("files/{logId:int}")]
        public HttpResponseMessage DownloadFile(int logId)
        {
            ReportLogDto log = _reports.GetReportLog(logId);
            if (string.IsNullOrWhiteSpace(log.FilePath) || !File.Exists(log.FilePath))
            {
                throw ApiException.NotFound("导出文件不存在或已被清理");
            }

            string fileName = Path.GetFileName(log.FilePath);
            string contentType = log.Format == ExportFormat.Excel
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "application/pdf";

            HttpResponseMessage response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StreamContent(new FileStream(log.FilePath, FileMode.Open, FileAccess.Read));
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            response.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileName = fileName };
            return response;
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

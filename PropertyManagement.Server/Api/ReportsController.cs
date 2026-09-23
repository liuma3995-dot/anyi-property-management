using System;
using System.Collections.Generic;
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
        private readonly OwnerProfileReportService _ownerProfile;

        public ReportsController()
        {
            _reports = new ReportService();
            _ownerProfile = new OwnerProfileReportService();
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

        /// <summary>
        /// CHG-v1.3.1-05：财务报表「报表与导出留痕」清单（报表留痕 + 导出留痕 + 未被引用的孤立生成文件）。
        /// </summary>
        [HttpGet]
        [Route("export-traces")]
        public ApiResponse<List<ExportTraceDto>> ListExportTraces()
        {
            return ApiResponse<List<ExportTraceDto>>.Ok(_reports.ListExportTraces());
        }

        /// <summary>
        /// CHG-v1.3.1-05：清理所选留痕 —— 留痕软删 + 物理删除服务端生成文件（不涉及任何账目）。
        /// </summary>
        [HttpPost]
        [Route("export-traces/delete")]
        public ApiResponse<ExportTraceDeleteResultDto> DeleteExportTraces(ExportTraceDeleteRequest request)
        {
            return ApiResponse<ExportTraceDeleteResultDto>.Ok(
                _reports.DeleteExportTraces(request, GetUsername(), GetIp()));
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

        /// <summary>
        /// 支出登记明细导出（CHG-v1.2.0-25，PDF）：按页面当前筛选条件（关键字 / 类别 / 状态）导出明细，
        /// 文件写 t_report_log 留痕后经 /reports/files/{id} 下载。
        /// </summary>
        [HttpPost]
        [Route("expenses/export")]
        public ApiResponse<ReportLogDto> ExportExpenses(ExpenseExportRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportExpenses(request));
        }

        /// <summary>
        /// 收款登记「应缴明细」导出 PDF（CHG-v1.2.0-32）：口径 = 当前所选缴费对象的全部应缴明细
        /// （含已结清记录），供「清理（删除）已结清记录」之前先归档留存；
        /// 文件写 t_report_log（report_type = arrear_detail）后经 /reports/files/{id} 下载。
        /// </summary>
        [HttpPost]
        [Route("arrear-details/export")]
        public ApiResponse<ReportLogDto> ExportArrearDetails(ArrearDetailExportRequest request)
        {
            return ApiResponse<ReportLogDto>.Ok(_reports.ExportArrearDetails(request, GetUsername()));
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

        /// <summary>
        /// 业主档案导出 PDF（CHG-v1.2.0-13）：本年度缴费概况 + 账单明细 + 收款明细。
        /// 与「导出 Excel（业主档案表格）」并存，互不影响；文件写 t_report_log 留痕后经 /reports/files/{id} 下载。
        /// </summary>
        [HttpPost]
        [Route("owners/{id:int}/profile-pdf")]
        public ApiResponse<ReportLogDto> ExportOwnerProfile(int id, OwnerProfileExportRequest request)
        {
            int? year = request == null ? null : request.Year;
            return ApiResponse<ReportLogDto>.Ok(_ownerProfile.Export(id, year, GetUsername()));
        }

        /// <summary>
        /// 业主档案导出 PDF —— **全部业主**（CHG-v1.2.0-17）：汇总表 + 逐户概况/账单/收款明细。
        /// 请求体可带 year（默认当前年度）。
        /// </summary>
        [HttpPost]
        [Route("owners/profile-pdf-all")]
        public ApiResponse<ReportLogDto> ExportAllOwnerProfiles(OwnerProfileExportRequest request)
        {
            int? year = request == null ? null : request.Year;
            return ApiResponse<ReportLogDto>.Ok(_ownerProfile.ExportAll(year, GetUsername()));
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

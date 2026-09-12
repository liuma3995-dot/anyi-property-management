using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Owin;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Dispute;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>民事纠纷调解端点（M6 D6-4，UC-DIS-001~007 + BR-DIS-01~05）。</summary>
    [RoutePrefix("api/v1/dispute")]
    public class DisputeController : ApiController
    {
        private readonly DisputeService _service;
        private readonly DisputeCloseReportService _closeReports;
        private readonly DisputeAttachmentService _attachments;

        public DisputeController()
        {
            _service = new DisputeService();
            _closeReports = new DisputeCloseReportService();
            _attachments = new DisputeAttachmentService();
        }

        [HttpGet] [Route("types")]
        public ApiResponse<List<DisputeTypeDto>> ListTypes() =>
            ApiResponse<List<DisputeTypeDto>>.Ok(_service.ListTypes());

        [HttpPost] [Route("types")]
        public ApiResponse<DisputeTypeDto> CreateType(DisputeTypeRequest request) =>
            ApiResponse<DisputeTypeDto>.Ok(_service.SaveType(request));

        [HttpPost] [Route("types/{id:int}/delete")]
        public ApiResponse<object> DeleteType(int id)
        {
            _service.DeleteType(id);
            return ApiResponse<object>.Ok(null);
        }

        [HttpGet] [Route("cases")]
        public ApiResponse<PageResult<DisputeCaseDto>> QueryCases([FromUri] DisputeQueryRequest request) =>
            ApiResponse<PageResult<DisputeCaseDto>>.Ok(_service.QueryCases(request ?? new DisputeQueryRequest()));

        /// <summary>案件详情：任意状态可查（PG-DIS-03 处理页侧栏直进/跨页传参的后端配合）。</summary>
        [HttpGet] [Route("cases/{id:int}")]
        public ApiResponse<DisputeCaseDetailDto> GetCase(int id) => ApiResponse<DisputeCaseDetailDto>.Ok(_service.GetCase(id));

        [HttpPost] [Route("cases")]
        public ApiResponse<DisputeCaseDto> CreateCase(DisputeCaseCreateRequest request) =>
            ApiResponse<DisputeCaseDto>.Ok(_service.CreateCase(request));

        [HttpPost] [Route("cases/{id:int}/delete")]
        public ApiResponse<object> DeleteCase(int id)
        {
            _service.DeleteCase(id);
            return ApiResponse<object>.Ok(null);
        }

        [HttpPut] [Route("cases/{id:int}")]
        public ApiResponse<DisputeCaseDto> UpdateCase(int id, DisputeCaseUpdateRequest request) =>
            ApiResponse<DisputeCaseDto>.Ok(_service.UpdateCase(id, request));

        [HttpPost] [Route("cases/{id:int}/records")]
        public ApiResponse<DisputeRecordDto> AddRecord(int id, DisputeRecordRequest request)
        {
            if (request != null) request.CaseId = id;
            return ApiResponse<DisputeRecordDto>.Ok(_service.AddRecord(request ?? new DisputeRecordRequest { CaseId = id }));
        }

        /// <summary>结案后补录（UC-DIS-007 / BR-DIS-04）：仅管理员，补录原因必填。</summary>
        [HttpPost] [Route("cases/{id:int}/supplement")]
        public ApiResponse<DisputeRecordDto> Supplement(int id, DisputeSupplementRequest request) =>
            ApiResponse<DisputeRecordDto>.Ok(_service.SupplementRecord(id, request, GetCurrentUsername()));

        /// <summary>调解员推荐（BR-DIS-03）：GET mediators/recommend?typeId=&amp;propertyId=。</summary>
        [HttpGet] [Route("mediators/recommend")]
        public ApiResponse<List<DisputeMediatorDto>> RecommendMediators(int? typeId = null, int? propertyId = null) =>
            ApiResponse<List<DisputeMediatorDto>>.Ok(_service.RecommendMediators(typeId, propertyId));

        [HttpPost] [Route("cases/{id:int}/status")]
        public ApiResponse<DisputeCaseDto> UpdateStatus(int id, DisputeCaseStatusRequest request) =>
            ApiResponse<DisputeCaseDto>.Ok(_service.UpdateStatus(id, request ?? new DisputeCaseStatusRequest()));

        [HttpPost] [Route("cases/{id:int}/close")]
        public ApiResponse<DisputeCaseDto> CloseCase(int id, DisputeCloseRequest request)
        {
            if (request != null) request.CaseId = id;
            return ApiResponse<DisputeCaseDto>.Ok(_service.CloseCase(id, request ?? new DisputeCloseRequest { CaseId = id }));
        }

        /// <summary>导出结案报告（F2）：仅已结案案件，PDF / Excel，落 exports 目录并写 t_report_log。</summary>
        [HttpPost] [Route("cases/{id:int}/report")]
        public ApiResponse<ReportLogDto> ExportCloseReport(int id, DisputeCloseReportRequest request)
        {
            ExportFormat format = request == null ? ExportFormat.Pdf : request.Format;
            string operatorName = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(operatorName) && request != null)
            {
                operatorName = request.OperatorName;
            }
            return ApiResponse<ReportLogDto>.Ok(_closeReports.Export(id, format, operatorName));
        }

        // ---------- F1：调解协议扫描件（上传 / 列表 / 下载 / 删除） ----------

        [HttpGet] [Route("cases/{id:int}/attachments")]
        public ApiResponse<List<DisputeAttachmentDto>> ListAttachments(int id) =>
            ApiResponse<List<DisputeAttachmentDto>>.Ok(_attachments.List(id));

        /// <summary>上传扫描件（multipart/form-data，字段名 file）：pdf/jpg/jpeg/png，≤20MB，单案 ≤10 份。</summary>
        [HttpPost] [Route("cases/{id:int}/attachments")]
        public async Task<ApiResponse<DisputeAttachmentDto>> UploadAttachment(int id)
        {
            if (!Request.Content.IsMimeMultipartContent())
            {
                throw ApiException.BadRequest("上传内容必须为 multipart/form-data");
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "pm_dispute_upload_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var provider = new MultipartFormDataStreamProvider(tempDirectory);
                await Request.Content.ReadAsMultipartAsync(provider);
                if (provider.FileData.Count == 0)
                {
                    throw ApiException.ValidationFailed("未接收到上传文件");
                }

                MultipartFileData file = provider.FileData[0];
                string originalName = file.Headers.ContentDisposition == null
                    ? "扫描件"
                    : (file.Headers.ContentDisposition.FileName ?? "扫描件").Trim('"');
                string contentType = file.Headers.ContentType == null ? null : file.Headers.ContentType.MediaType;
                byte[] bytes = File.ReadAllBytes(file.LocalFileName);

                return ApiResponse<DisputeAttachmentDto>.Ok(
                    _attachments.Upload(id, originalName, contentType, bytes, GetCurrentUsername()));
            }
            finally
            {
                try { Directory.Delete(tempDirectory, true); }
                catch { /* 临时目录清理失败不影响业务 */ }
            }
        }

        [HttpGet] [Route("cases/{id:int}/attachments/{aid:int}/download")]
        public HttpResponseMessage DownloadAttachment(int id, int aid)
        {
            DisputeAttachmentDto dto = _attachments.Locate(id, aid);
            HttpResponseMessage response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StreamContent(new FileStream(dto.StoredPath, FileMode.Open, FileAccess.Read));
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(dto.ContentType) ? "application/octet-stream" : dto.ContentType);
            response.Content.Headers.ContentDisposition =
                new ContentDispositionHeaderValue("attachment") { FileName = dto.FileName };
            return response;
        }

        /// <summary>删除扫描件：仅管理员，软删记录并连物理文件一并删除。</summary>
        [HttpDelete] [Route("cases/{id:int}/attachments/{aid:int}")]
        public ApiResponse<object> DeleteAttachment(int id, int aid)
        {
            _attachments.Delete(id, aid, GetCurrentUsername());
            return ApiResponse<object>.Ok(null);
        }

        [HttpGet] [Route("statistics")]
        public ApiResponse<DisputeStatisticsDto> Statistics() =>
            ApiResponse<DisputeStatisticsDto>.Ok(_service.Statistics());

        /// <summary>从 WebApi 注入的 OWIN 环境读取鉴权中间件写入的用户名（参照 AuthController）。</summary>
        private string GetCurrentUsername()
        {
            object owinContextValue;
            if (Request.Properties.TryGetValue("MS_OwinContext", out owinContextValue))
            {
                var owinContext = owinContextValue as IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Get<string>(AuthMiddleware.UsernameEnvKey) ?? string.Empty;
                }
            }

            return string.Empty;
        }
    }
}

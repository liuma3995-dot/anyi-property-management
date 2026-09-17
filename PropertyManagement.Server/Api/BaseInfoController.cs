using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Http;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>基础信息端点（M5 D5-1~D5-4，UC-INF-001~007 + BR-INF-01~05）。</summary>
    [RoutePrefix("api/v1/baseinfo")]
    public class BaseInfoController : ApiController
    {
        private readonly BaseInfoService _service;

        public BaseInfoController()
        {
            _service = new BaseInfoService();
        }

        // ---------- 小区（UC-INF-001） ----------
        [HttpGet] [Route("communities")]
        public ApiResponse<List<CommunityDto>> ListCommunities(string keyword = null) =>
            ApiResponse<List<CommunityDto>>.Ok(_service.ListCommunities(keyword));

        [HttpGet] [Route("communities/{id:int}")]
        public ApiResponse<CommunityDto> GetCommunity(int id) =>
            ApiResponse<CommunityDto>.Ok(_service.GetCommunity(id));

        [HttpPost] [Route("communities")]
        public ApiResponse<CommunityDto> CreateCommunity(CommunityRequest request) =>
            ApiResponse<CommunityDto>.Ok(_service.SaveCommunity(0, request));

        [HttpPut] [Route("communities/{id:int}")]
        public ApiResponse<CommunityDto> UpdateCommunity(int id, CommunityRequest request) =>
            ApiResponse<CommunityDto>.Ok(_service.SaveCommunity(id, request));

        [HttpDelete] [Route("communities/{id:int}")]
        public ApiResponse<object> DeleteCommunity(int id) { _service.DeleteCommunity(id); return ApiResponse<object>.Ok(null); }

        // ---------- 楼栋（UC-INF-001） ----------
        [HttpGet] [Route("buildings")]
        public ApiResponse<List<BuildingDto>> ListBuildings(int? communityId = null, string keyword = null) =>
            ApiResponse<List<BuildingDto>>.Ok(_service.ListBuildings(communityId, keyword));

        [HttpGet] [Route("buildings/{id:int}")]
        public ApiResponse<BuildingDto> GetBuilding(int id) => ApiResponse<BuildingDto>.Ok(_service.GetBuilding(id));

        [HttpPost] [Route("buildings")]
        public ApiResponse<BuildingDto> CreateBuilding(BuildingRequest request) =>
            ApiResponse<BuildingDto>.Ok(_service.SaveBuilding(0, request));

        [HttpPut] [Route("buildings/{id:int}")]
        public ApiResponse<BuildingDto> UpdateBuilding(int id, BuildingRequest request) =>
            ApiResponse<BuildingDto>.Ok(_service.SaveBuilding(id, request));

        [HttpDelete] [Route("buildings/{id:int}")]
        public ApiResponse<object> DeleteBuilding(int id) { _service.DeleteBuilding(id); return ApiResponse<object>.Ok(null); }

        // ---------- 单元（UC-INF-001） ----------
        [HttpGet] [Route("units")]
        public ApiResponse<List<UnitDto>> ListUnits(int? buildingId = null, string keyword = null) =>
            ApiResponse<List<UnitDto>>.Ok(_service.ListUnits(buildingId, keyword));

        [HttpGet] [Route("units/{id:int}")]
        public ApiResponse<UnitDto> GetUnit(int id) => ApiResponse<UnitDto>.Ok(_service.GetUnit(id));

        [HttpPost] [Route("units")]
        public ApiResponse<UnitDto> CreateUnit(UnitRequest request) =>
            ApiResponse<UnitDto>.Ok(_service.SaveUnit(0, request));

        [HttpPut] [Route("units/{id:int}")]
        public ApiResponse<UnitDto> UpdateUnit(int id, UnitRequest request) =>
            ApiResponse<UnitDto>.Ok(_service.SaveUnit(id, request));

        [HttpDelete] [Route("units/{id:int}")]
        public ApiResponse<object> DeleteUnit(int id) { _service.DeleteUnit(id); return ApiResponse<object>.Ok(null); }

        // ---------- 房产（UC-INF-002，BR-INF-01） ----------
        [HttpGet] [Route("properties")]
        public ApiResponse<PageResult<PropertyDto>> QueryProperties([FromUri] BaseInfoQueryRequest request) =>
            ApiResponse<PageResult<PropertyDto>>.Ok(_service.QueryProperties(request ?? new BaseInfoQueryRequest()));

        [HttpGet] [Route("properties/{id:int}")]
        public ApiResponse<PropertyDto> GetProperty(int id) => ApiResponse<PropertyDto>.Ok(_service.GetProperty(id));

        [HttpPost] [Route("properties")]
        public ApiResponse<PropertyDto> CreateProperty(PropertyRequest request) =>
            ApiResponse<PropertyDto>.Ok(_service.SaveProperty(0, request));

        [HttpPut] [Route("properties/{id:int}")]
        public ApiResponse<PropertyDto> UpdateProperty(int id, PropertyRequest request) =>
            ApiResponse<PropertyDto>.Ok(_service.SaveProperty(id, request));

        [HttpDelete] [Route("properties/{id:int}")]
        public ApiResponse<object> DeleteProperty(int id) { _service.DeleteProperty(id); return ApiResponse<object>.Ok(null); }

        // ---------- 业主（UC-INF-003，BR-INF-04） ----------
        [HttpGet] [Route("owners")]
        public ApiResponse<PageResult<OwnerDto>> QueryOwners([FromUri] BaseInfoQueryRequest request) =>
            ApiResponse<PageResult<OwnerDto>>.Ok(_service.QueryOwners(request ?? new BaseInfoQueryRequest()));

        [HttpGet] [Route("owners/{id:int}")]
        public ApiResponse<OwnerDto> GetOwner(int id) => ApiResponse<OwnerDto>.Ok(_service.GetOwner(id));

        [HttpGet] [Route("owners/{id:int}/change-logs")]
        public ApiResponse<List<BaseChangeLogDto>> GetOwnerChangeLogs(int id) =>
            ApiResponse<List<BaseChangeLogDto>>.Ok(_service.GetChangeLogs((int)BaseChangeObjectType.Owner, id));

        [HttpGet] [Route("owners/{id:int}/relations")]
        public ApiResponse<List<OwnerPropertyRelationDto>> ListOwnerRelations(int id) =>
            ApiResponse<List<OwnerPropertyRelationDto>>.Ok(_service.ListRelationsByOwner(id));

        [HttpPost] [Route("owners")]
        public ApiResponse<OwnerDto> CreateOwner(OwnerRequest request) =>
            ApiResponse<OwnerDto>.Ok(_service.SaveOwner(0, request));

        [HttpPut] [Route("owners/{id:int}")]
        public ApiResponse<OwnerDto> UpdateOwner(int id, OwnerRequest request) =>
            ApiResponse<OwnerDto>.Ok(_service.SaveOwner(id, request));

        [HttpDelete] [Route("owners/{id:int}")]
        public ApiResponse<object> DeleteOwner(int id) { _service.DeleteOwner(id); return ApiResponse<object>.Ok(null); }

        // ---------- 业主-房产关系（UC-INF-004，BR-INF-02） ----------
        [HttpGet] [Route("owner-property-relations")]
        public ApiResponse<PageResult<OwnerPropertyRelationDto>> QueryRelations([FromUri] BaseInfoQueryRequest request) =>
            ApiResponse<PageResult<OwnerPropertyRelationDto>>.Ok(_service.QueryRelations(request ?? new BaseInfoQueryRequest()));

        [HttpGet] [Route("owner-property-relations/{id:int}")]
        public ApiResponse<OwnerPropertyRelationDto> GetRelation(int id) =>
            ApiResponse<OwnerPropertyRelationDto>.Ok(_service.GetRelation(id));

        [HttpPost] [Route("owner-property-relations")]
        public ApiResponse<OwnerPropertyRelationDto> CreateRelation(OwnerPropertyRelationRequest request) =>
            ApiResponse<OwnerPropertyRelationDto>.Ok(_service.SaveRelation(0, request));

        [HttpPut] [Route("owner-property-relations/{id:int}")]
        public ApiResponse<OwnerPropertyRelationDto> UpdateRelation(int id, OwnerPropertyRelationRequest request) =>
            ApiResponse<OwnerPropertyRelationDto>.Ok(_service.SaveRelation(id, request));

        [HttpPost] [Route("owner-property-relations/{id:int}/release")]
        public ApiResponse<object> ReleaseRelation(int id, ReleaseRelationRequest request)
        {
            _service.ReleaseRelation(id, request == null ? null : request.Reason);
            return ApiResponse<object>.Ok(null);
        }

        [HttpDelete] [Route("owner-property-relations/{id:int}")]
        public ApiResponse<object> DeleteRelation(int id)
        {
            _service.ReleaseRelation(id, null);
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 车位（UC-INF-005，BR-INF-03） ----------
        [HttpGet] [Route("parking-spaces")]
        public ApiResponse<PageResult<ParkingSpaceDto>> QueryParkings([FromUri] BaseInfoQueryRequest request) =>
            ApiResponse<PageResult<ParkingSpaceDto>>.Ok(_service.QueryParkings(request ?? new BaseInfoQueryRequest()));

        [HttpGet] [Route("parking-spaces/{id:int}")]
        public ApiResponse<ParkingSpaceDto> GetParking(int id) => ApiResponse<ParkingSpaceDto>.Ok(_service.GetParking(id));

        [HttpPost] [Route("parking-spaces")]
        public ApiResponse<ParkingSpaceDto> CreateParking(ParkingSpaceRequest request) =>
            ApiResponse<ParkingSpaceDto>.Ok(_service.SaveParking(0, request));

        [HttpPut] [Route("parking-spaces/{id:int}")]
        public ApiResponse<ParkingSpaceDto> UpdateParking(int id, ParkingSpaceRequest request) =>
            ApiResponse<ParkingSpaceDto>.Ok(_service.SaveParking(id, request));

        [HttpDelete] [Route("parking-spaces/{id:int}")]
        public ApiResponse<object> DeleteParking(int id) { _service.DeleteParking(id); return ApiResponse<object>.Ok(null); }

        // ---------- 导入（UC-INF-006，BR-INF-05） ----------
        [HttpGet] [Route("imports/template")]
        public HttpResponseMessage DownloadTemplate(ImportModule module)
        {
            byte[] bytes = _service.BuildTemplate(module);
            return FileResponse(bytes, "导入模板_" + module + "_" + DateTime.Now.ToString("yyyyMMdd") + ".xlsx");
        }

        [HttpPost] [Route("imports")]
        public ApiResponse<ImportResultDto> Import(ImportRequest request) =>
            ApiResponse<ImportResultDto>.Ok(_service.Import(request));

        [HttpGet] [Route("imports")]
        public ApiResponse<List<ImportLogDto>> ListImports() =>
            ApiResponse<List<ImportLogDto>>.Ok(_service.ListImportLogs());

        [HttpGet] [Route("imports/{id:int}")]
        public ApiResponse<ImportLogDto> GetImport(int id) => ApiResponse<ImportLogDto>.Ok(_service.GetImportLog(id));

        [HttpGet] [Route("imports/{id:int}/errors")]
        public HttpResponseMessage DownloadImportErrors(int id)
        {
            byte[] bytes = _service.BuildImportErrorsExcel(id);
            return FileResponse(bytes, "导入错误_" + id + ".xlsx");
        }

        /// <summary>导入批次记录批量删除（v1.1.0-⑤）：软删留痕，记录不再出现在批次列表。</summary>
        [HttpPost] [Route("imports/batch-delete")]
        public ApiResponse<RecordBatchDeleteResultDto> BatchDeleteImports(RecordBatchDeleteRequest request) =>
            ApiResponse<RecordBatchDeleteResultDto>.Ok(_service.BatchDeleteImportLogs(request, GetUsername(), GetIp()));

        // ---------- 导出（UC-INF-007） ----------
        [HttpPost] [Route("exports")]
        public ApiResponse<ExportLogDto> Export(BaseInfoExportRequest request) =>
            ApiResponse<ExportLogDto>.Ok(_service.Export(request));

        // ---------- 业务参数（P4-3：车位统计卡说明等，落库 t_param） ----------
        [HttpGet] [Route("params/{key}")]
        public ApiResponse<string> GetParam(string key) =>
            ApiResponse<string>.Ok(_service.GetParam(key));

        [HttpPut] [Route("params/{key}")]
        public ApiResponse<object> SetParam(string key, ParamValueRequest request)
        {
            _service.SetParam(key, request == null ? string.Empty : request.Value);
            return ApiResponse<object>.Ok(null);
        }

        [HttpGet] [Route("exports/{id:int}/file")]
        public HttpResponseMessage DownloadExport(int id)
        {
            ExportLogDto log = _service.GetExportLog(id);
            if (string.IsNullOrWhiteSpace(log.FilePath) || !File.Exists(log.FilePath))
                throw ApiException.NotFound("导出文件不存在或已被清理");
            return FileResponse(File.ReadAllBytes(log.FilePath), Path.GetFileName(log.FilePath));
        }

        private HttpResponseMessage FileResponse(byte[] bytes, string fileName)
        {
            HttpResponseMessage response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(bytes);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
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

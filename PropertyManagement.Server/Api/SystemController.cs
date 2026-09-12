using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.BaseInfo;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Enums;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>系统设置端点（M6 D6-6，UC-COM-003/004/005，BR-COM-01/04/05）。
    /// 操作人/IP 从 OWIN 环境读取后传入服务层（PG-COM-03 审计留痕根因项）。</summary>
    [RoutePrefix("api/v1/system")]
    public class SystemController : ApiController
    {
        private readonly CommonService _common;
        private readonly DictService _dicts;

        public SystemController()
        {
            _common = new CommonService();
            _dicts = new DictService();
        }

        // ---------- 字典（UC-COM-004，BR-COM-05，PG-COM-01） ----------
        [HttpGet] [Route("dict-types")]
        public ApiResponse<List<DictTypeDto>> ListDictTypes() => ApiResponse<List<DictTypeDto>>.Ok(_dicts.ListTypes());

        [HttpPost] [Route("dict-types")]
        public ApiResponse<DictTypeDto> CreateDictType(DictTypeRequest request) =>
            ApiResponse<DictTypeDto>.Ok(_dicts.CreateType(request, GetUsername(), GetIp()));

        /// <summary>PG-COM-01：管理端默认返回全部状态（includeDisabled=true，含停用可恢复）；
        /// status=0/1 可按状态过滤；业务下拉请走 /dicts/{code}（仅启用）。</summary>
        [HttpGet] [Route("dict-types/{typeCode}/items")]
        public ApiResponse<List<DictItemDto>> ListDictItems(string typeCode, bool includeDisabled = true, int? status = null) =>
            ApiResponse<List<DictItemDto>>.Ok(_dicts.ListItems(typeCode, includeDisabled, status));

        [HttpPost] [Route("dict-types/{typeCode}/items")]
        public ApiResponse<DictItemDto> CreateDictItem(string typeCode, DictItemRequest request) =>
            ApiResponse<DictItemDto>.Ok(_dicts.CreateItem(typeCode, request, GetUsername(), GetIp()));

        [HttpPut] [Route("dict-items/{id:int}")]
        public ApiResponse<DictItemDto> UpdateDictItem(int id, DictItemRequest request) =>
            ApiResponse<DictItemDto>.Ok(_dicts.UpdateItem(id, request, GetUsername(), GetIp()));

        [HttpPost] [Route("dict-items/{id:int}/status")]
        public ApiResponse<DictItemDto> SetDictItemStatus(int id, DictItemRequest request) =>
            ApiResponse<DictItemDto>.Ok(_dicts.SetItemStatus(
                id, request != null ? request.Status : DictItemStatus.Enabled, GetUsername(), GetIp()));

        /// <summary>PG-COM-01 / R12：批量删除字典项（仅已停用项可删；命中未停用项整批拒绝并返回 blocked 明细）。</summary>
        [HttpPost] [Route("dict-items/batch-delete")]
        public ApiResponse<DictItemBatchDeleteResultDto> BatchDeleteDictItems(DictItemBatchDeleteRequest request) =>
            ApiResponse<DictItemBatchDeleteResultDto>.Ok(_dicts.BatchDeleteItems(request, GetUsername(), GetIp()));

        // ---------- 参数（P-01~P-09，白名单 + 审计值掩码） ----------
        [HttpGet] [Route("params")]
        public ApiResponse<List<ParamDto>> ListParams() => ApiResponse<List<ParamDto>>.Ok(_common.ListParams());

        [HttpGet] [Route("params/{key}")]
        public ApiResponse<string> GetParam(string key) => ApiResponse<string>.Ok(_common.GetParam(key));

        [HttpPut] [Route("params/{key}")]
        public ApiResponse<object> SetParam(string key, ParamValueRequest request)
        {
            _common.SetParam(key, request == null ? string.Empty : request.Value, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 备份与恢复（UC-COM-005，BR-COM-04，PG-COM-02） ----------
        [HttpGet] [Route("backups")]
        public ApiResponse<List<BackupDto>> ListBackups() => ApiResponse<List<BackupDto>>.Ok(_common.ListBackups());

        /// <summary>PG-COM-02 状态卡：上次自动备份/数据库大小/保留份数/待恢复演练。</summary>
        [HttpGet] [Route("backups/status")]
        public ApiResponse<BackupStatusDto> GetBackupStatus() =>
            ApiResponse<BackupStatusDto>.Ok(_common.GetBackupStatus());

        [HttpPost] [Route("backups/run")]
        public ApiResponse<BackupDto> RunBackup(BackupCreateRequest request) =>
            ApiResponse<BackupDto>.Ok(_common.RunBackup(request == null ? "" : request.Note, GetUsername(), GetIp(),
                "manual", request == null ? null : request.TargetPath));

        /// <summary>BR-COM-04 高危恢复：操作密码 + 确认文本；R12 起支持按「手动选择的备份文件路径」恢复，并移除第二管理员确认。</summary>
        [HttpPost] [Route("backups/{id:int}/restore")]
        public ApiResponse<BackupDto> RestoreBackup(int id, BackupRestoreRequest request) =>
            ApiResponse<BackupDto>.Ok(_common.RestoreBackup(id, request, GetUsername(), GetIp()));

        // ---------- 审计日志（UC-COM-003，BR-COM-01，PG-COM-03 八列） ----------
        [HttpGet] [Route("audit-logs")]
        public ApiResponse<PageResult<AuditLogDto>> QueryAuditLogs([FromUri] AuditLogQueryRequest request) =>
            ApiResponse<PageResult<AuditLogDto>>.Ok(_common.QueryAuditLogs(request ?? new AuditLogQueryRequest()));

        /// <summary>T6-6-9：按当前筛选导出 CSV；导出动作写 t_export_log(audit) + AUDIT_EXPORT 审计。</summary>
        [HttpGet] [Route("audit-logs/export")]
        public ApiResponse<AuditExportDto> ExportAuditLogs([FromUri] AuditLogQueryRequest request) =>
            ApiResponse<AuditExportDto>.Ok(_common.ExportAuditLogs(request ?? new AuditLogQueryRequest(), GetUsername(), GetIp()));

        /// <summary>R13：审计日志批量删除（软删留痕；删除动作自身写审计）。</summary>
        [HttpPost] [Route("audit-logs/batch-delete")]
        public ApiResponse<RecordBatchDeleteResultDto> BatchDeleteAuditLogs(RecordBatchDeleteRequest request) =>
            ApiResponse<RecordBatchDeleteResultDto>.Ok(_common.BatchDeleteAuditLogs(request, GetUsername(), GetIp()));

        // ---------- 残余数据清理（R13） ----------

        /// <summary>R13：备份/恢复记录批量删除（软删留痕）。</summary>
        [HttpPost] [Route("backups/batch-delete")]
        public ApiResponse<RecordBatchDeleteResultDto> BatchDeleteBackupRecords(RecordBatchDeleteRequest request) =>
            ApiResponse<RecordBatchDeleteResultDto>.Ok(_common.BatchDeleteBackupRecords(request, GetUsername(), GetIp()));

        /// <summary>R13：一键清理残余数据（物理删除全库软删留痕行，不动在用数据）。</summary>
        [HttpPost] [Route("cleanup/soft-deleted")]
        public ApiResponse<PurgeSoftDeletedResultDto> PurgeSoftDeleted() =>
            ApiResponse<PurgeSoftDeletedResultDto>.Ok(_common.PurgeSoftDeleted(GetUsername(), GetIp()));

        // ---------- OWIN 环境读取 ----------
        private string GetUsername()
        {
            object owinContextValue;
            if (Request.Properties.TryGetValue("MS_OwinContext", out owinContextValue))
            {
                var owinContext = owinContextValue as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Get<string>(AuthMiddleware.UsernameEnvKey) ?? string.Empty;
                }
            }
            return string.Empty;
        }

        private string GetIp()
        {
            object owinContextValue;
            if (Request.Properties.TryGetValue("MS_OwinContext", out owinContextValue))
            {
                var owinContext = owinContextValue as Microsoft.Owin.IOwinContext;
                if (owinContext != null)
                {
                    return owinContext.Request.RemoteIpAddress;
                }
            }
            return null;
        }
    }
}

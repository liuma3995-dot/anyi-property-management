using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>轻量字典端点（T4F-1-6：收费项目类别/计价方式/计费周期 读取与自定义新增）。
    /// 列表仅返回启用项（业务下拉）；管理端全量走 /system/dict-types/{code}/items。</summary>
    [RoutePrefix("api/v1/dicts")]
    public class DictController : ApiController
    {
        private readonly DictService _dicts;

        public DictController()
        {
            _dicts = new DictService();
        }

        [HttpGet]
        [Route("{typeCode}")]
        public ApiResponse<List<DictItemDto>> ListItems(string typeCode)
        {
            return ApiResponse<List<DictItemDto>>.Ok(_dicts.ListItems(typeCode));
        }

        [HttpPost]
        [Route("{typeCode}/items")]
        public ApiResponse<DictItemDto> CreateItem(string typeCode, DictItemCreateRequest request)
        {
            return ApiResponse<DictItemDto>.Ok(_dicts.CreateItem(typeCode, request, GetUsername(), GetIp()));
        }

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

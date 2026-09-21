using System.Collections.Generic;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Finance;
using PropertyManagement.Server.Api.Middleware;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>
    /// 收费项目「价目表 + 计量变量」端点（CHG-v1.1.2-26，UC-FIN-001）。
    /// 授权口径：与其它财务端点一致（鉴权中间件统一拦截）。
    /// </summary>
    [RoutePrefix("api/v1/billing")]
    public class ChargeStandardController : ApiController
    {
        private readonly ChargeStandardService _service;

        public ChargeStandardController()
        {
            _service = new ChargeStandardService();
        }

        // ---------- 收费标准（价目表主体） ----------
        [HttpGet]
        [Route("charge-standards")]
        public ApiResponse<List<ChargeStandardDto>> ListStandards(string keyword = null, string category = null,
            bool includeDisabled = true)
        {
            return ApiResponse<List<ChargeStandardDto>>.Ok(_service.ListStandards(keyword, category, includeDisabled));
        }

        [HttpGet]
        [Route("charge-standards/{id:int}")]
        public ApiResponse<ChargeStandardDto> GetStandard(int id)
        {
            return ApiResponse<ChargeStandardDto>.Ok(_service.GetStandard(id));
        }

        [HttpPost]
        [Route("charge-standards")]
        public ApiResponse<ChargeStandardDto> CreateStandard(ChargeStandardRequest request)
        {
            return ApiResponse<ChargeStandardDto>.Ok(_service.CreateStandard(request, GetUsername(), GetIp()));
        }

        [HttpPut]
        [Route("charge-standards/{id:int}")]
        public ApiResponse<ChargeStandardDto> UpdateStandard(int id, ChargeStandardRequest request)
        {
            return ApiResponse<ChargeStandardDto>.Ok(_service.UpdateStandard(id, request, GetUsername(), GetIp()));
        }

        [HttpDelete]
        [Route("charge-standards/{id:int}")]
        public ApiResponse<object> DeleteStandard(int id)
        {
            _service.DeleteStandard(id, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        [HttpPost]
        [Route("charge-standards/{id:int}/status")]
        public ApiResponse<ChargeStandardDto> ToggleStandard(int id, ChargeStandardStatusRequest request)
        {
            return ApiResponse<ChargeStandardDto>.Ok(
                _service.ToggleStandardStatus(id, request == null ? 0 : request.Status, GetUsername(), GetIp()));
        }

        // ---------- 规格明细（价目表条目） ----------
        [HttpGet]
        [Route("charge-standards/{id:int}/specs")]
        public ApiResponse<List<ChargeStandardSpecDto>> ListSpecs(int id, bool includeDisabled = true)
        {
            return ApiResponse<List<ChargeStandardSpecDto>>.Ok(_service.ListSpecs(id, includeDisabled));
        }

        [HttpPost]
        [Route("charge-standards/{id:int}/specs")]
        public ApiResponse<ChargeStandardSpecDto> CreateSpec(int id, ChargeStandardSpecRequest request)
        {
            return ApiResponse<ChargeStandardSpecDto>.Ok(_service.CreateSpec(id, request, GetUsername(), GetIp()));
        }

        [HttpPut]
        [Route("charge-specs/{id:int}")]
        public ApiResponse<ChargeStandardSpecDto> UpdateSpec(int id, ChargeStandardSpecRequest request)
        {
            return ApiResponse<ChargeStandardSpecDto>.Ok(_service.UpdateSpec(id, request, GetUsername(), GetIp()));
        }

        [HttpDelete]
        [Route("charge-specs/{id:int}")]
        public ApiResponse<object> DeleteSpec(int id)
        {
            _service.DeleteSpec(id, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        [HttpPost]
        [Route("charge-specs/{id:int}/status")]
        public ApiResponse<object> ToggleSpec(int id, ChargeStandardStatusRequest request)
        {
            _service.ToggleSpecStatus(id, request == null ? 0 : request.Status, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        // ---------- 计量变量（定义侧：参数 / 字典维护） ----------
        [HttpGet]
        [Route("charge-variables")]
        public ApiResponse<List<ChargeVariableDto>> ListVariables(string keyword = null, bool includeDisabled = true)
        {
            return ApiResponse<List<ChargeVariableDto>>.Ok(_service.ListVariables(keyword, includeDisabled));
        }

        [HttpPost]
        [Route("charge-variables")]
        public ApiResponse<ChargeVariableDto> CreateVariable(ChargeVariableRequest request)
        {
            return ApiResponse<ChargeVariableDto>.Ok(_service.CreateVariable(request, GetUsername(), GetIp()));
        }

        [HttpPut]
        [Route("charge-variables/{id:int}")]
        public ApiResponse<ChargeVariableDto> UpdateVariable(int id, ChargeVariableRequest request)
        {
            return ApiResponse<ChargeVariableDto>.Ok(_service.UpdateVariable(id, request, GetUsername(), GetIp()));
        }

        [HttpDelete]
        [Route("charge-variables/{id:int}")]
        public ApiResponse<object> DeleteVariable(int id)
        {
            _service.DeleteVariable(id, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        [HttpPost]
        [Route("charge-variables/{id:int}/status")]
        public ApiResponse<object> ToggleVariable(int id, ChargeStandardStatusRequest request)
        {
            _service.ToggleVariableStatus(id, request == null ? 0 : request.Status, GetUsername(), GetIp());
            return ApiResponse<object>.Ok(null);
        }

        /// <summary>操作人（审计八列）：从鉴权中间件写入的 OWIN 环境读取。</summary>
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

        /// <summary>客户端 IP（审计八列）。</summary>
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

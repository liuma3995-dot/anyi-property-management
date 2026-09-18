using System;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Server.Api
{
    /// <summary>健康检查（骨架期用于前后端连通验证，不鉴权）。</summary>
    public class HealthController : ApiController
    {
        [HttpGet]
        [Route("api/v1/health")]
        public ApiResponse<HealthResponse> Get()
        {
            var data = new HealthResponse
            {
                Service = "PropertyManagement.Server",
                // v1.1.1 版本元数据同源治理：原硬编码 "0.2.0-m2"（M2 骨架期残留）改为同源读取
                Version = AppVersionInfo.Display,
                Status = "ok",
                ServerTime = DateTime.Now
            };

            return ApiResponse<HealthResponse>.Ok(data);
        }
    }
}

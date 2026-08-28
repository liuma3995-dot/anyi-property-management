using System;
using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Contract.Health;

namespace PropertyManagement.Server.Api
{
    /// <summary>
    /// 健康检查（骨架期用于前后端连通验证，不鉴权）。
    /// </summary>
    public class HealthController : ApiController
    {
        [HttpGet]
        [Route("api/v1/health")]
        public ApiResponse<HealthResponse> Get()
        {
            var data = new HealthResponse
            {
                Service = "PropertyManagement.Server",
                Version = "0.1.0-skeleton",
                Status = "ok",
                ServerTime = DateTime.Now
            };

            return ApiResponse<HealthResponse>.Ok(data);
        }
    }
}

using System.Web.Http;
using PropertyManagement.Contract.Common;
using PropertyManagement.Server.Services;

namespace PropertyManagement.Server.Api
{
    /// <summary>公共端点（M2 提供仪表盘骨架；字典/参数/审计查询在 M6 系统设置切片补全）。</summary>
    [RoutePrefix("api/v1/common")]
    public class CommonController : ApiController
    {
        private readonly DashboardService _dashboardService;

        public CommonController()
        {
            _dashboardService = new DashboardService();
        }

        [HttpGet]
        [Route("dashboard")]
        public ApiResponse<DashboardDto> Dashboard()
        {
            DashboardDto data = _dashboardService.GetDashboard();
            return ApiResponse<DashboardDto>.Ok(data);
        }
    }
}

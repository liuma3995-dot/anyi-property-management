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
        private readonly TodoService _todoService;
        private readonly GlobalSearchService _searchService;

        public CommonController()
        {
            _dashboardService = new DashboardService();
            _todoService = new TodoService();
            _searchService = new GlobalSearchService();
        }

        /// <param name="period">统计月份 yyyy-MM（R17 仪表盘月份选择器；空=当前月）。</param>
        [HttpGet]
        [Route("dashboard")]
        public ApiResponse<DashboardDto> Dashboard(string period = null)
        {
            DashboardDto data = _dashboardService.GetDashboard(period);
            return ApiResponse<DashboardDto>.Ok(data);
        }

        /// <summary>待办中心（R17 顶部铃铛）：跨模块只读聚合，含总数与分类计数。</summary>
        [HttpGet]
        [Route("todos")]
        public ApiResponse<TodoCenterDto> Todos(int limit = 20)
        {
            return ApiResponse<TodoCenterDto>.Ok(_todoService.Query(limit));
        }

        /// <summary>顶栏全局搜索（R17）：按模块分组返回，命中项带跳转目标与关键词。</summary>
        [HttpGet]
        [Route("search")]
        public ApiResponse<GlobalSearchResultDto> Search(string keyword = null)
        {
            return ApiResponse<GlobalSearchResultDto>.Ok(_searchService.Search(keyword));
        }
    }
}
